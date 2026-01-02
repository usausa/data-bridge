// ReSharper disable UseUtf8StringLiteral
// ReSharper disable UnusedAutoPropertyAccessor.Local
namespace Mofucat.DataBridge;

public class ObjectDataReaderTest
{
    [Fact]
    public void TestBasic()
    {
        var list = new List<Data>
        {
            new() { IntValue = 1, NullableIntValue = 10, StringValue = "A" },
            new() { IntValue = 2, StringValue = "B" },
            new() { IntValue = 3, NullableIntValue = 30 }
        };

        using var reader = new ObjectDataReader<Data>(list);

        // Assert
        Assert.Equal(3, reader.FieldCount);

        var intIndex = reader.GetOrdinal(nameof(Data.IntValue));
        var nullableIntIndex = reader.GetOrdinal(nameof(Data.NullableIntValue));
        var stringIndex = reader.GetOrdinal(nameof(Data.StringValue));
        Assert.Equal(0, intIndex);
        Assert.Equal(1, nullableIntIndex);
        Assert.Equal(2, stringIndex);

        // 1st
        Assert.True(reader.Read());

        Assert.False(reader.IsDBNull(nullableIntIndex));
        Assert.False(reader.IsDBNull(stringIndex));

        var values = new object[reader.FieldCount];
        reader.GetValues(values);
        Assert.Equal(1, values[intIndex]);
        Assert.Equal(10, values[nullableIntIndex]);
        Assert.Equal("A", values[stringIndex]);

        // 2nd
        Assert.True(reader.Read());

        Assert.True(reader.IsDBNull(nullableIntIndex));
        Assert.False(reader.IsDBNull(stringIndex));

        Assert.Equal(2, reader.GetValue(intIndex));
        Assert.Equal(DBNull.Value, reader.GetValue(nullableIntIndex));
        Assert.Equal("B", reader.GetValue(stringIndex));

        // 3rd
        Assert.True(reader.Read());

        Assert.False(reader.IsDBNull(nullableIntIndex));
        Assert.True(reader.IsDBNull(stringIndex));

        Assert.Equal(3, reader.GetValue(intIndex));
        Assert.Equal(30, reader.GetValue(nullableIntIndex));
        Assert.Equal(DBNull.Value, reader.GetValue(stringIndex));

        // Completed
        Assert.False(reader.Read());
    }

    [Fact]
    public void TestConvert()
    {
        var list = new List<ConvertData>
        {
            new()
            {
                BooleanValue = true,
                IntValue = 1,
                StringValue = "A",
                DateTimeValue = new DateTime(2000, 12, 31, 23, 59, 59),
                GuidValue = Guid.Empty,
                BytesValue = [0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF],
                CharsValue = ['0', '1', '2', '3', '4', '5', '6', '7', '8', '9']
            }
        };

        using var reader = new ObjectDataReader<ConvertData>(list);

        // Assert
        Assert.True(reader.Read());

        Assert.True(reader.GetBoolean(0));

        Assert.Equal(1, reader.GetByte(1));
        Assert.Equal(1, reader.GetInt16(1));
        Assert.Equal(1, reader.GetInt32(1));
        Assert.Equal(1, reader.GetInt64(1));
        Assert.Equal(1, reader.GetFloat(1));
        Assert.Equal(1, reader.GetDouble(1));
        Assert.Equal(1, reader.GetDecimal(1));

        Assert.Equal('A', reader.GetChar(2));
        Assert.Equal("A", reader.GetString(2));

        Assert.Equal(new DateTime(2000, 12, 31, 23, 59, 59), reader.GetDateTime(3));

        Assert.Equal(Guid.Empty, reader.GetGuid(4));

        var bytes = new byte[4];
        reader.GetBytes(5, 4, bytes, 0, bytes.Length);
        Assert.Equal(new byte[] { 0x44, 0x55, 0x66, 0x77 }, bytes);

        var chars = new char[4];
        reader.GetChars(6, 2, chars, 0, chars.Length);
        Assert.Equal(['2', '3', '4', '5'], chars);
    }

    private sealed class Data
    {
        public int IntValue { get; set; }

        public int? NullableIntValue { get; set; }

        public string? StringValue { get; set; }
    }

    private sealed class ConvertData
    {
        public bool BooleanValue { get; set; }

        public int IntValue { get; set; }

        public string StringValue { get; set; } = default!;

        public DateTime DateTimeValue { get; set; }

        public Guid GuidValue { get; set; }

        public byte[] BytesValue { get; set; } = default!;

        public char[] CharsValue { get; set; } = default!;
    }
}
