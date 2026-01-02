namespace Mofucat.DataBridge;

using System.Data.Common;
using System.Data;
using System.Globalization;
#if NET9_0_OR_GREATER
using System.Runtime.CompilerServices;
#endif

using Avro.File;
using Avro.Generic;
using Avro;

public sealed class AvroDataExporter
{
    private static readonly AvroDataExporterOption DefaultOption = new();

    private readonly AvroDataExporterOption option;

    private readonly DbConnection con;

    public string Name { get; set; } = default!;

    public Codec Codec { get; set; } = Codec.CreateCodec(Codec.Type.Null);

    public AvroDataExporter(DbConnection con)
        : this(DefaultOption, con)
    {
    }

    public AvroDataExporter(AvroDataExporterOption option, DbConnection con)
    {
        this.option = option;
        this.con = con;
    }

#if NET9_0_OR_GREATER
    [OverloadResolutionPriority(1)]
#endif
    public Task ExportAsync(Stream stream, FormattableString sql) =>
        ExportInternalAsync(stream, sql.Format, sql.GetArguments());

#if NET9_0_OR_GREATER
    [OverloadResolutionPriority(0)]
#endif
    public Task ExportAsync(Stream stream, string sql, params object[] arguments) =>
        ExportInternalAsync(stream, sql, arguments);

#if !NET9_0_OR_GREATER
    public Task ExportAsync(Stream stream, RawString sql, params object[] arguments) =>
        ExportInternalAsync(stream, sql.Value, arguments);
#endif

    private async Task ExportInternalAsync(
        Stream stream,
        string sql,
        params object?[] arguments)
    {
        var closeConnection = false;
        try
        {
            if (con.State != ConnectionState.Open)
            {
                await con.OpenAsync().ConfigureAwait(false);
                closeConnection = true;
            }

#pragma warning disable CA2007
            await using var cmd = con.CreateCommand();
#pragma warning restore CA2007
            BuildCommand(cmd, sql, arguments);

            var schema = await GetSchemaAsync(cmd).ConfigureAwait(false);
            var datumWriter = new GenericDatumWriter<GenericRecord>(schema);
            using var dataFileWriter = DataFileWriter<GenericRecord>.OpenWriter(datumWriter, stream, Codec);

#pragma warning disable CA2007
            await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess).ConfigureAwait(false);
#pragma warning restore CA2007
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                var record = new GenericRecord(schema);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    var value = reader.GetValue(i);
                    if (value is decimal d)
                    {
                        value = option.DecimalConverter(d);
                    }
                    else if (value is DateTime dateTime)
                    {
                        value = option.DateTimeConverter(dateTime);
                    }
                    else if (value is Guid guid)
                    {
                        value = option.GuidConverter(guid);
                    }

                    record.Add(schema.Fields[i].Name, value is DBNull ? null : value);
                }

                dataFileWriter.Append(record);
            }
        }
        finally
        {
            if (closeConnection)
            {
                await con.CloseAsync().ConfigureAwait(false);
            }
        }
    }

    private static void BuildCommand(DbCommand cmd, string sql, object?[] arguments)
    {
        var parameterNames = arguments.Length > 0 ? new object[arguments.Length] : [];

        for (var i = 0; i < arguments.Length; i++)
        {
            var name = $"@p{i}";
            parameterNames[i] = name;

            var parameter = cmd.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = arguments[i] ?? DBNull.Value;
            cmd.Parameters.Add(parameter);
        }

#pragma warning disable CA2100
        cmd.CommandText = String.Format(CultureInfo.InvariantCulture, sql, parameterNames);
#pragma warning restore CA2100
    }

    private async ValueTask<RecordSchema> GetSchemaAsync(DbCommand cmd)
    {
        var fields = new List<Field>();

#pragma warning disable CA2007
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly).ConfigureAwait(false);
#pragma warning restore CA2007
        var st = await reader.GetSchemaTableAsync().ConfigureAwait(false);
        var pos = 0;
        foreach (var column in st!.Rows.Cast<DataRow>())
        {
            var columnName = (string)column["ColumnName"];
            var type = (Type)column["DataType"];
            var allowNull = (bool)column["AllowDBNull"];

            Schema.Type schemeType;
            if (type == typeof(bool))
            {
                schemeType = Schema.Type.Boolean;
            }
            else if ((type == typeof(int)) || (type == typeof(short)) || (type == typeof(char)) || (type == typeof(byte)))
            {
                schemeType = Schema.Type.Int;
            }
            else if (type == typeof(long))
            {
                schemeType = Schema.Type.Long;
            }
            else if (type == typeof(float))
            {
                schemeType = Schema.Type.Float;
            }
            else if (type == typeof(double))
            {
                schemeType = Schema.Type.Double;
            }
            else if (type == typeof(decimal))
            {
                schemeType = option.DecimalSchemeType;
            }
            else if (type == typeof(DateTime))
            {
                schemeType = option.DateTimeSchemeType;
            }
            else if (type == typeof(Guid))
            {
                schemeType = option.GuidSchemeType;
            }
            else if (type == typeof(string))
            {
                schemeType = Schema.Type.String;
            }
            else if (type == typeof(byte[]))
            {
                schemeType = Schema.Type.Bytes;
            }
            else
            {
                throw new NotSupportedException($"Type {type} is not supported.");
            }

            var fieldScheme = (Schema)PrimitiveSchema.Create(schemeType);
            if (allowNull)
            {
                fieldScheme = UnionSchema.Create([fieldScheme, PrimitiveSchema.Create(Schema.Type.Null)]);
            }

            fields.Add(new Field(fieldScheme, columnName, pos++));
        }

        return RecordSchema.Create(Name, fields);
    }
}
