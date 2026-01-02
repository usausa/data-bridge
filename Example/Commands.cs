namespace Example;

using System.Data;
using System.Globalization;

using CsvHelper;

using Microsoft.Data.SqlClient;

using Mofucat.DataBridge;

using MySqlConnector;

using Smart.CommandLine.Hosting;
using Smart.Data.Mapper;

public static class CommandBuilderExtensions
{
    public static void AddCommands(this ICommandBuilder commands)
    {
        commands.AddCommand<ObjectCommand>(obj =>
        {
            obj.AddSubCommand<ObjectImportCommand>(imp =>
            {
                imp.AddSubCommand<ObjectImportMyCommand>();
                imp.AddSubCommand<ObjectImportSqlCommand>();
            });
        });
        commands.AddCommand<MapCommand>(obj =>
        {
            obj.AddSubCommand<MapImportCommand>(imp =>
            {
                imp.AddSubCommand<MapImportMyCommand>();
                imp.AddSubCommand<MapImportSqlCommand>();
            });
        });
        commands.AddCommand<AvroCommand>(obj =>
        {
            obj.AddSubCommand<AvroImportCommand>(imp =>
            {
                imp.AddSubCommand<AvroImportMyCommand>();
                imp.AddSubCommand<AvroImportSqlCommand>();
            });
        });
    }
}

//--------------------------------------------------------------------------------
// Object
//--------------------------------------------------------------------------------
[Command("object", "Object example")]
public sealed class ObjectCommand
{
}

[Command("imp", "Import example")]
public sealed class ObjectImportCommand
{
}

[Command("my", "Load to MySQL")]
public sealed class ObjectImportMyCommand : ICommandHandler
{
    public async ValueTask ExecuteAsync(CommandContext context)
    {
        using var reader = new ObjectDataReader<Data>(DataHelper.CreateObjectList());
        await DataHelper.ImportToMySql(reader);
    }
}

[Command("sql", "Load to SQL Server")]
public sealed class ObjectImportSqlCommand : ICommandHandler
{
    public async ValueTask ExecuteAsync(CommandContext context)
    {
        using var reader = new ObjectDataReader<Data>(DataHelper.CreateObjectList());
        await DataHelper.ImportToSql(reader);
    }
}

//--------------------------------------------------------------------------------
// Mapping
//--------------------------------------------------------------------------------
[Command("map", "Mapping example")]
public sealed class MapCommand
{
}

[Command("imp", "Import example")]
public sealed class MapImportCommand
{
}

[Command("my", "Load to MySQL")]
public sealed class MapImportMyCommand : ICommandHandler
{
    public async ValueTask ExecuteAsync(CommandContext context)
    {
#pragma warning disable CA2000
        using var reader = new MappingDataReader(DataHelper.CreateCsvOption(), DataHelper.CreateCsvReader());
#pragma warning restore CA2000
        await DataHelper.ImportToMySql(reader);
    }
}

[Command("sql", "Load to SQL Server")]
public sealed class MapImportSqlCommand : ICommandHandler
{
    public async ValueTask ExecuteAsync(CommandContext context)
    {
#pragma warning disable CA2000
        using var reader = new MappingDataReader(DataHelper.CreateCsvOption(), DataHelper.CreateCsvReader());
#pragma warning restore CA2000
        await DataHelper.ImportToSql(reader);
    }
}

//--------------------------------------------------------------------------------
// Avro
//--------------------------------------------------------------------------------
[Command("avro", "Avro example")]
public sealed class AvroCommand
{
}

[Command("imp", "Import example")]
public sealed class AvroImportCommand
{
}

[Command("my", "Load to MySQL")]
public sealed class AvroImportMyCommand : ICommandHandler
{
    public async ValueTask ExecuteAsync(CommandContext context)
    {
        using var reader = new AvroDataReader(DataHelper.CreateAvroOption(), File.OpenRead("data.avro"));
        await DataHelper.ImportToMySql(reader);
    }
}

[Command("sql", "Load to SQL Server")]
public sealed class AvroImportSqlCommand : ICommandHandler
{
    public async ValueTask ExecuteAsync(CommandContext context)
    {
        using var reader = new AvroDataReader(DataHelper.CreateAvroOption(), File.OpenRead("data.avro"));
        await DataHelper.ImportToSql(reader);
    }
}

//--------------------------------------------------------------------------------
// Data
//--------------------------------------------------------------------------------

internal sealed class Data
{
    public int Id { get; set; }

    public string Name { get; set; } = default!;

    public string? Option { get; set; }

    public bool Flag { get; set; }

    public DateTime CreateAt { get; set; }
}

internal static class DataHelper
{
    //--------------------------------------------------------------------------------
    // Import
    //--------------------------------------------------------------------------------

    public static async ValueTask ImportToMySql(IDataReader reader)
    {
        await using var con = new MySqlConnection("Server=mysql-server;Database=test;User Id=test;Password=test;AllowLoadLocalInfile=true");
        await con.ExecuteAsync("TRUNCATE TABLE data");

        await con.OpenAsync();
        var loader = new MySqlBulkCopy(con)
        {
            DestinationTableName = "data"
        };
        await loader.WriteToServerAsync(reader);
    }

    public static async ValueTask ImportToSql(IDataReader reader)
    {
        await using var con = new SqlConnection("Server=mssql-server;Database=test;User Id=test;Password=test;TrustServerCertificate=true");
        await con.ExecuteAsync("TRUNCATE TABLE Data");

        await con.OpenAsync();
        using var loader = new SqlBulkCopy(con);
        loader.DestinationTableName = "Data";
        await loader.WriteToServerAsync(reader);
    }

    //--------------------------------------------------------------------------------
    // Mapping
    //--------------------------------------------------------------------------------

    private const string Content =
        "Col1,Col2,Col3,Col4,Col5,Col6\n" +
        "1,30,Data-1,option,true,2000-12-31 23:59:59\n" +
        "2,,Data-2,,false,2000-12-31 23:59:59";

#pragma warning disable CA2000
    public static CsvDataReader CreateCsvReader() =>
        new(new CsvReader(new StringReader(Content), CultureInfo.InvariantCulture));
#pragma warning restore CA2000

    public static MappingDataReaderOption CreateCsvOption()
    {
        var option = new MappingDataReaderOption();
        option.AddColumn("Col1");
        option.AddColumn("Col3");
        option.AddColumn<string, string?>("Col4", static x => String.IsNullOrEmpty(x) ? null : x);
        option.AddColumn<string, bool>("Col5", Boolean.Parse);
        option.AddColumn<string, DateTime>("Col6", DateTime.Parse);
        return option;
    }

    //--------------------------------------------------------------------------------
    // Object
    //--------------------------------------------------------------------------------

    public static List<Data> CreateObjectList() =>
    [
        new() { Id = 1, Name = "Data-1", Option = "option", Flag = true, CreateAt = DateTime.Now },
        new() { Id = 2, Name = "Data-2", Flag = false, CreateAt = DateTime.Now }
    ];

    //--------------------------------------------------------------------------------
    // Avro
    //--------------------------------------------------------------------------------

    public static AvroDataReaderOption CreateAvroOption()
    {
        var option = new AvroDataReaderOption();
        option.AddConverter<long, DateTime>(s => s == "create_at" ? x => new DateTime(x) : null);
        return option;
    }
}
