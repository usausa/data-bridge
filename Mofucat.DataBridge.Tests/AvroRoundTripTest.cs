namespace Mofucat.DataBridge;

using System.Globalization;
using System.IO;

using Avro;
using Avro.File;
using Avro.Generic;

using Microsoft.Data.Sqlite;

#pragma warning disable IDE0230
public class AvroRoundTripTest
{
    //--------------------------------------------------------------------------------
    // Helper
    //--------------------------------------------------------------------------------

    private static RecordSchema BuildSchema(string name, (string Name, Schema.Type AvroType, bool AllowNull)[] columns)
    {
        var fields = new List<Field>();
        for (var i = 0; i < columns.Length; i++)
        {
            var (colName, avroType, allowNull) = columns[i];
            Schema fieldSchema = PrimitiveSchema.Create(avroType);
            if (allowNull)
            {
                fieldSchema = UnionSchema.Create([fieldSchema, PrimitiveSchema.Create(Schema.Type.Null)]);
            }

            fields.Add(new Field(fieldSchema, colName, i));
        }

        return RecordSchema.Create(name, fields);
    }

    private static MemoryStream WriteAvro(RecordSchema schema, IEnumerable<GenericRecord> records)
    {
        var buffer = new MemoryStream();
        var writer = new GenericDatumWriter<GenericRecord>(schema);
        using var fileWriter = DataFileWriter<GenericRecord>.OpenWriter(writer, buffer, Codec.CreateCodec(Codec.Type.Null));
        foreach (var record in records)
        {
            fileWriter.Append(record);
        }

        fileWriter.Flush();
        return new MemoryStream(buffer.ToArray());
    }

    //--------------------------------------------------------------------------------
    // Major types
    //--------------------------------------------------------------------------------

    [Fact]
    public void TestMajorTypes()
    {
        var columns = new[]
        {
            ("BoolField", Schema.Type.Boolean, false),
            ("IntField", Schema.Type.Int, false),
            ("LongField", Schema.Type.Long, false),
            ("FloatField", Schema.Type.Float, false),
            ("DoubleField", Schema.Type.Double, false),
            ("StringField", Schema.Type.String, false),
            ("BytesField", Schema.Type.Bytes, false)
        };
        var schema = BuildSchema("Test", columns);

        var record = new GenericRecord(schema);
        record.Add("BoolField", true);
        record.Add("IntField", 42);
        record.Add("LongField", 12345678901L);
        record.Add("FloatField", 3.14f);
        record.Add("DoubleField", 2.71828);
        record.Add("StringField", "hello");
        record.Add("BytesField", new byte[] { 0x01, 0x02, 0x03 });

        using var stream = WriteAvro(schema, [record]);
        using var reader = new AvroDataReader(stream);

        Assert.Equal(7, reader.FieldCount);
        Assert.True(reader.Read());

        Assert.True(reader.GetBoolean(0));
        Assert.Equal(42, reader.GetInt32(1));
        Assert.Equal(12345678901L, reader.GetInt64(2));
        Assert.Equal(3.14f, reader.GetFloat(3));
        Assert.Equal(2.71828, reader.GetDouble(4));
        Assert.Equal("hello", reader.GetString(5));

        var bytes = new byte[3];
        reader.GetBytes(6, 0, bytes, 0, 3);
        Assert.Equal(new byte[] { 0x01, 0x02, 0x03 }, bytes);

        Assert.False(reader.Read());
    }

    //--------------------------------------------------------------------------------
    // Null-containing rows
    //--------------------------------------------------------------------------------

    [Fact]
    public void TestNullableValues()
    {
        var columns = new[]
        {
            ("IntField", Schema.Type.Int, true),
            ("StringField", Schema.Type.String, true)
        };
        var schema = BuildSchema("Nullable", columns);

        var row1 = new GenericRecord(schema);
        row1.Add("IntField", 100);
        row1.Add("StringField", "value");

        var row2 = new GenericRecord(schema);
        row2.Add("IntField", null);
        row2.Add("StringField", null);

        using var stream = WriteAvro(schema, [row1, row2]);
        using var reader = new AvroDataReader(stream);

        Assert.True(reader.Read());
        Assert.False(reader.IsDBNull(0));
        Assert.False(reader.IsDBNull(1));
        Assert.Equal(100, reader.GetInt32(0));
        Assert.Equal("value", reader.GetString(1));

        Assert.True(reader.Read());
        Assert.True(reader.IsDBNull(0));
        Assert.True(reader.IsDBNull(1));
        Assert.Equal(DBNull.Value, reader.GetValue(0));
        Assert.Equal(DBNull.Value, reader.GetValue(1));

        Assert.False(reader.Read());
    }

    //--------------------------------------------------------------------------------
    // Multiple rows
    //--------------------------------------------------------------------------------

    [Fact]
    public void TestMultipleRows()
    {
        var columns = new[]
        {
            ("Id", Schema.Type.Int, false),
            ("Name", Schema.Type.String, false)
        };
        var schema = BuildSchema("Multi", columns);

        var records = Enumerable.Range(0, 100).Select(i =>
        {
            var r = new GenericRecord(schema);
            r.Add("Id", i);
            r.Add("Name", i.ToString(CultureInfo.InvariantCulture));
            return r;
        }).ToList();

        using var stream = WriteAvro(schema, records);
        using var reader = new AvroDataReader(stream);

        for (var i = 0; i < 100; i++)
        {
            Assert.True(reader.Read());
            Assert.Equal(i, reader.GetInt32(0));
            Assert.Equal(i.ToString(CultureInfo.InvariantCulture), reader.GetString(1));
        }

        Assert.False(reader.Read());
    }

    //--------------------------------------------------------------------------------
    // Zero rows
    //--------------------------------------------------------------------------------

    [Fact]
    public void TestEmptyData()
    {
        var columns = new (string, Schema.Type, bool)[]
        {
            ("Id", Schema.Type.Int, false)
        };
        var schema = BuildSchema("Empty", columns);

        using var stream = WriteAvro(schema, []);
        using var reader = new AvroDataReader(stream);

        Assert.Equal(1, reader.FieldCount);
        Assert.False(reader.Read());
    }

    //--------------------------------------------------------------------------------
    // ObjectDataReader → Avro write → AvroDataReader → MappingDataReader
    //--------------------------------------------------------------------------------

    [Fact]
    public void TestAvroToMappingDataReaderChain()
    {
        // Build Avro schema
        var columns = new[]
        {
            ("Id", Schema.Type.Int, false),
            ("Name", Schema.Type.String, false),
            ("Score", Schema.Type.Double, true)
        };
        var schema = BuildSchema("RowData", columns);

        // Write source data as Avro records
        var sourceRows = new[]
        {
            new RowData { Id = 1, Name = "Alice", Score = 9.5 },
            new RowData { Id = 2, Name = "Bob", Score = null },
            new RowData { Id = 3, Name = "Carol", Score = 7.0 }
        };

        var avroRecords = sourceRows.Select(row =>
        {
            var r = new GenericRecord(schema);
            r.Add("Id", row.Id);
            r.Add("Name", row.Name);
            object? score = row.Score.HasValue ? row.Score.Value : null;
            r.Add("Score", score);
            return r;
        });

        using var stream = WriteAvro(schema, avroRecords);

        // Read with AvroDataReader
        var avroOption = AvroDataReaderOption.OfDefault();
        using var avroReader = new AvroDataReader(avroOption, stream);

        // Wrap in MappingDataReader selecting all columns
        var mapOption = new MappingDataReaderOption();
        mapOption.AddColumn("Id");
        mapOption.AddColumn("Name");
        mapOption.AddColumn("Score");
        using var mappingReader = new MappingDataReader(mapOption, avroReader);

        // Verify all three rows
        for (var i = 0; i < sourceRows.Length; i++)
        {
            Assert.True(mappingReader.Read());
            var expected = sourceRows[i];
            Assert.Equal(expected.Id, mappingReader.GetInt32(0));
            Assert.Equal(expected.Name, mappingReader.GetString(1));
            if (expected.Score.HasValue)
            {
                Assert.False(mappingReader.IsDBNull(2));
                Assert.Equal(expected.Score.Value, mappingReader.GetDouble(2));
            }
            else
            {
                Assert.True(mappingReader.IsDBNull(2));
            }
        }

        Assert.False(mappingReader.Read());
    }

    //--------------------------------------------------------------------------------
    // DateTime round-trip (long → DateTime)
    //--------------------------------------------------------------------------------

    [Fact]
    public void TestDateTimeRoundTrip()
    {
        var expected = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        var ticks = expected.ToUniversalTime().Ticks;

        var columns = new (string, Schema.Type, bool)[]
        {
            ("Ts", Schema.Type.Long, false)
        };
        var schema = BuildSchema("DtTest", columns);

        var record = new GenericRecord(schema);
        record.Add("Ts", ticks);

        using var stream = WriteAvro(schema, [record]);
        var readerOption = AvroDataReaderOption.OfDefault();
        using var reader = new AvroDataReader(readerOption, stream);

        Assert.True(reader.Read());
        var dt = reader.GetDateTime(0);
        // OfDefault converts Ticks UTC to local; compare as universal ticks.
        Assert.Equal(expected.ToUniversalTime().Ticks, dt.ToUniversalTime().Ticks);
    }

    //--------------------------------------------------------------------------------
    // AvroDataExporter round-trip (database -> Avro -> readers)
    //--------------------------------------------------------------------------------

    [Fact]
    public async Task TestAvroDataExporterRoundTrip()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var con = new SqliteConnection("Data Source=:memory:");
        await con.OpenAsync(cancellationToken).ConfigureAwait(true);

        await using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE Data (Id INTEGER NOT NULL, Name TEXT, Rate REAL)";
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(true);
        }

        await using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO Data (Id, Name, Rate) VALUES (1, 'abc', 1.5), (2, NULL, 2.5)";
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(true);
        }

        var exporter = new AvroDataExporter(con) { Name = "Data" };

        var buffer = new MemoryStream();
        await exporter.ExportAsync(buffer, "SELECT Id, Name, Rate FROM Data ORDER BY Id").ConfigureAwait(true);

        // Read back through AvroDataReader wrapped in MappingDataReader to verify the full read chain
        using var avroReader = new AvroDataReader(new MemoryStream(buffer.ToArray()));

        var mapOption = new MappingDataReaderOption();
        mapOption.AddColumn("Id");
        mapOption.AddColumn("Name");
        mapOption.AddColumn("Rate");
        using var mappingReader = new MappingDataReader(mapOption, avroReader);

        Assert.True(mappingReader.Read());
        Assert.Equal(1L, mappingReader.GetInt64(0));
        Assert.Equal("abc", mappingReader.GetString(1));
        Assert.Equal(1.5, mappingReader.GetDouble(2));

        Assert.True(mappingReader.Read());
        Assert.Equal(2L, mappingReader.GetInt64(0));
        Assert.True(mappingReader.IsDBNull(1));
        Assert.Equal(2.5, mappingReader.GetDouble(2));

        Assert.False(mappingReader.Read());
    }

    private sealed class RowData
    {
        public int Id { get; set; }

        public string Name { get; set; } = default!;

        public double? Score { get; set; }
    }
}
#pragma warning restore IDE0230
