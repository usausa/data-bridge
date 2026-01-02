// ReSharper disable UseUtf8StringLiteral
namespace Mofucat.DataBridge;

using System.Globalization;

using CsvHelper;

public class MappingDataReaderTest
{
    [Fact]
    public void TestBasic()
    {
        var content =
            "Col1,Col2,Col3,Col4,Col5\n" +
            "1,,10,,true\n" +
            "2,,,,false\n" +
            "3,,30,,true";
        using var csv = new CsvReader(new StringReader(content), CultureInfo.InvariantCulture);
        using var source = new CsvDataReader(csv);

        var option = new MappingDataReaderOption();
        option.AddColumn("Col1");
        option.AddColumn<string, string?>("Col3", static x => String.IsNullOrEmpty(x) ? null : x);
        option.AddColumn<string, bool>("Col5", Boolean.Parse);

        using var reader = new MappingDataReader(option, source);

        // Assert
        Assert.Equal(3, reader.FieldCount);

        var col1Index = reader.GetOrdinal("Col1");
        var col3Index = reader.GetOrdinal("Col3");
        var col5Index = reader.GetOrdinal("Col5");
        Assert.Equal(0, col1Index);
        Assert.Equal(1, col3Index);
        Assert.Equal(2, col5Index);

        // 1st
        Assert.True(reader.Read());

        Assert.False(reader.IsDBNull(col3Index));
        Assert.False(reader.IsDBNull(col5Index));

        var values = new object[reader.FieldCount];
        reader.GetValues(values);
        Assert.Equal("1", values[col1Index]);
        Assert.Equal("10", values[col3Index]);
        Assert.True((bool)values[col5Index]);

        // 2nd
        Assert.True(reader.Read());

        Assert.True(reader.IsDBNull(col3Index));
        Assert.False(reader.IsDBNull(col5Index));

        Assert.Equal("2", reader.GetValue(col1Index));
        Assert.Equal(DBNull.Value, reader.GetValue(col3Index));
        Assert.False((bool)reader.GetValue(col5Index));

        // 3rd
        Assert.True(reader.Read());

        Assert.False(reader.IsDBNull(col3Index));

        Assert.Equal("3", reader.GetValue(col1Index));
        Assert.Equal("30", reader.GetValue(col3Index));
        Assert.True((bool)reader.GetValue(col5Index));

        // Completed
        Assert.False(reader.Read());
    }

    [Fact]
    public void TestConvert()
    {
        var content =
            "Col1,Col2,Col3,Col4,Col5,Col6,Col7\n" +
            "true,1,A,2000-12-31 23:59:59,00000000-0000-0000-0000-000000000000,00112233445566778899AABBCCDDEEFF,0123456789";
        using var csv = new CsvReader(new StringReader(content), CultureInfo.InvariantCulture);
        using var source = new CsvDataReader(csv);

        var option = new MappingDataReaderOption();
        option.AddColumn<string, bool>("Col1", Boolean.Parse);
        option.AddColumn<string, int>("Col2", Int32.Parse);
        option.AddColumn<string, char>("Col3", static x => x[0]);
        option.AddColumn<string, DateTime>("Col4", DateTime.Parse);
        option.AddColumn<string, Guid>("Col5", Guid.Parse);
        option.AddColumn<string, byte[]>("Col6", Convert.FromHexString);
        option.AddColumn<string, char[]>("Col7", static x => x.ToCharArray());

        using var reader = new MappingDataReader(option, source);

        // Assert
        Assert.True(reader.Read());

        Assert.True(reader.GetBoolean(0));

        Assert.Equal(1, reader.GetInt32(1));

        Assert.Equal('A', reader.GetChar(2));

        Assert.Equal(new DateTime(2000, 12, 31, 23, 59, 59), reader.GetDateTime(3));

        Assert.Equal(Guid.Empty, reader.GetGuid(4));

        var bytes = new byte[4];
        reader.GetBytes(5, 4, bytes, 0, bytes.Length);
        Assert.Equal(new byte[] { 0x44, 0x55, 0x66, 0x77 }, bytes);

        var chars = new char[4];
        reader.GetChars(6, 2, chars, 0, chars.Length);
        Assert.Equal(['2', '3', '4', '5'], chars);
    }
}
