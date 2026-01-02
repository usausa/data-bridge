# Mofucat.DataBridge

[![NuGet](https://img.shields.io/nuget/v/Mofucat.DataBridge.svg)](https://www.nuget.org/packages/Mofucat.DataBridge)

## ObjectDataReader

```csharp
// Source
var list = new List<Data>
[
    new() { Id = 1, Value = "Name-1" },
    new() { Id = 2 }
];

// ObjectDataReader
using var reader = new ObjectDataReader<Data>(list);

// BulkCopy
await using var con = new SqlConnection(ConnectionString);
await con.OpenAsync();
using var loader = new SqlBulkCopy(con);
loader.DestinationTableName = "Data";
await loader.WriteToServerAsync(reader);
```

## MappingDataReader

```csharp
// Source
var csvReader = ...

// MappingReader 
var option = new MappingDataReaderOption();
option.AddColumn("Col1");
option.AddColumn("Col3");
option.AddColumn<string, bool>("Col5", Boolean.Parse);
using var reader = new MappingDataReader(option, csvReader);

// BulkCopy
await using var con = new SqlConnection(ConnectionString);
await con.OpenAsync();
using var loader = new SqlBulkCopy(con);
loader.DestinationTableName = "Data";
await loader.WriteToServerAsync(reader);
```

# Mofucat.DataBridge.Avro

[![NuGet](https://img.shields.io/nuget/v/Mofucat.DataBridge.Avro.svg)](https://www.nuget.org/packages/Mofucat.DataBridge.Avro)

## AvroDataReader

```csharp
// AvroDataReader
using var reader = new AvroDataReader(File.OpenRead("data.avro"));

// BulkCopy
await using var con = new SqlConnection(ConnectionString);
await con.OpenAsync();
using var loader = new SqlBulkCopy(con);
loader.DestinationTableName = "Data";
await loader.WriteToServerAsync(reader);
```

## AvroDataExporter

```csharp
await using var con = new SqlConnection(ConnectionString);

// AvroDataExporter
var exporter = new AvroDataExporter(con)
{
    Name = "Data",
    Codec = new SnappyCodec()
};

await exporter.ExportAsync(File.OpenWrite($"data-{id}.avro"), $"SELECT * FROM Data WHERE Id = {id}");
```
