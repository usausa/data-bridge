namespace Mofucat.DataBridge;

using System;
using System.Globalization;

using Avro;

public sealed class AvroDataExporterOption
{
    internal Schema.Type DecimalSchemeType { get; set; } = Schema.Type.String;

    internal Func<decimal, object> DecimalConverter { get; set; } = x => x.ToString("G29", CultureInfo.InvariantCulture);

    internal Schema.Type DateTimeSchemeType { get; set; } = Schema.Type.Long;

    internal Func<DateTime, object> DateTimeConverter { get; set; } = dateTime => dateTime.ToUniversalTime().Ticks;

    internal Schema.Type GuidSchemeType { get; set; } = Schema.Type.String;

    internal Func<Guid, object> GuidConverter { get; set; } = x => x.ToString("D");

    public AvroDataExporterOption ConfigureDecimal(Schema.Type type, Func<decimal, object> converter)
    {
        DecimalSchemeType = type;
        DecimalConverter = converter;
        return this;
    }

    public AvroDataExporterOption ConfigureDateTime(Schema.Type type, Func<DateTime, object> converter)
    {
        DateTimeSchemeType = type;
        DateTimeConverter = converter;
        return this;
    }

    public AvroDataExporterOption ConfigureGuid(Schema.Type type, Func<Guid, object> converter)
    {
        GuidSchemeType = type;
        GuidConverter = converter;
        return this;
    }
}
