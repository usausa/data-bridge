namespace Mofucat.DataBridge;

using System;
using System.Buffers;
using System.Data;
using System.Globalization;

using Avro;
using Avro.File;
using Avro.Generic;

#pragma warning disable CA1725
public sealed class AvroDataReader : IDataReader
{
    private static readonly AvroDataReaderOption DefaultOption = new();

    private struct Entry
    {
        public string Name;

        public Type Type;

        public Func<object, object?>? Converter;
    }

    private readonly IFileReader<GenericRecord> reader;

    private readonly int fieldCount;

    private Entry[] entries;

    private object?[] currentValues;

    private GenericRecord currentRecord = default!;

    //--------------------------------------------------------------------------------
    // Property
    //--------------------------------------------------------------------------------

    public int FieldCount => fieldCount;

    public int Depth => 0;

    public bool IsClosed { get; private set; }

    public int RecordsAffected => -1;

    public object this[int i] => GetValue(i);

    public object this[string name] => GetValue(GetOrdinal(name));

    //--------------------------------------------------------------------------------
    // Constructor
    //--------------------------------------------------------------------------------

    public AvroDataReader(Stream stream)
        : this(DefaultOption, stream)
    {
    }

    public AvroDataReader(AvroDataReaderOption option, Stream stream)
    {
        reader = DataFileReader<GenericRecord>.OpenReader(stream);
        var scheme = (RecordSchema)reader.GetSchema();

        fieldCount = scheme.Fields.Count;
        entries = ArrayPool<Entry>.Shared.Rent(fieldCount);
        currentValues = ArrayPool<object?>.Shared.Rent(fieldCount);

        for (var i = 0; i < scheme.Fields.Count; i++)
        {
            var field = scheme.Fields[i];
            var type = ResolveType(field);
            var converter = option.ResolveConverter(field.Name, type);

            ref var entry = ref entries[i];
            entry.Name = field.Name;
            entry.Type = converter?.Type ?? type;
            entry.Converter = converter?.Factory;
        }
    }

    private static Type ResolveType(Field field)
    {
        if (field.Schema is PrimitiveSchema primitiveSchema)
        {
            return ConvertType(field.Name, primitiveSchema.Tag);
        }
        if (field.Schema is UnionSchema unionSchema)
        {
            foreach (var schema in unionSchema.Schemas)
            {
                if ((schema is PrimitiveSchema ps) && (ps.Tag != Schema.Type.Null))
                {
                    return ConvertType(field.Name, ps.Tag);
                }
            }
        }

        throw new NotSupportedException($"Unsupported Avro type. field=[{field.Name}]");

        Type ConvertType(string name, Schema.Type type) => type switch
        {
            Schema.Type.Boolean => typeof(bool),
            Schema.Type.Int => typeof(int),
            Schema.Type.Long => typeof(long),
            Schema.Type.Float => typeof(float),
            Schema.Type.Double => typeof(double),
            Schema.Type.Bytes => typeof(byte[]),
            Schema.Type.String => typeof(string),
            Schema.Type.Fixed => typeof(byte[]),
            _ => throw new NotSupportedException($"Unsupported Avro type. field=[{name}], type=[{type}]")
        };
    }

    public void Dispose()
    {
        if (IsClosed)
        {
            return;
        }

        reader.Dispose();

        if (entries.Length > 0)
        {
            ArrayPool<Entry>.Shared.Return(entries, true);
            entries = [];
        }
        if (currentValues.Length > 0)
        {
            ArrayPool<object?>.Shared.Return(currentValues, true);
            currentValues = [];
        }

        IsClosed = true;
    }

    public void Close()
    {
        Dispose();
    }

    //--------------------------------------------------------------------------------
    // Iterator
    //--------------------------------------------------------------------------------

    public bool Read()
    {
        if (!reader.HasNext())
        {
            return false;
        }

        currentRecord = reader.Next();

        for (var i = 0; i < fieldCount; i++)
        {
            ref var entry = ref entries[i];
            var value = currentRecord[entry.Name];
            var converter = entry.Converter;
            currentValues[i] = converter is not null ? converter(value) : value;
        }

        return true;
    }

    public bool NextResult() => false;

    //--------------------------------------------------------------------------------
    // Metadata
    //--------------------------------------------------------------------------------

    public IDataReader GetData(int i) => throw new NotSupportedException();

    public DataTable GetSchemaTable() => throw new NotSupportedException();

    public string GetDataTypeName(int i)
    {
        ref var entry = ref entries[i];
        return entry.Type.Name;
    }

    public Type GetFieldType(int i)
    {
        ref var entry = ref entries[i];
        return entry.Type;
    }

    public string GetName(int i)
    {
        ref var entry = ref entries[i];
        return entry.Name;
    }

    public int GetOrdinal(string name)
    {
        for (var i = 0; i < fieldCount; i++)
        {
            if (String.Equals(GetName(i), name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }

    //--------------------------------------------------------------------------------
    // Value
    //--------------------------------------------------------------------------------

    public bool IsDBNull(int i) => currentValues[i] is null or DBNull;

    public object GetValue(int i) => currentValues[i] ?? DBNull.Value;

    public int GetValues(object[] values)
    {
        for (var i = 0; i < fieldCount; i++)
        {
            values[i] = currentValues[i] ?? DBNull.Value;
        }
        return fieldCount;
    }

    public bool GetBoolean(int i)
    {
        var value = currentValues[i];
        return value is bool t ? t : Convert.ToBoolean(value, CultureInfo.InvariantCulture);
    }

    public byte GetByte(int i)
    {
        var value = currentValues[i];
        return value is byte t ? t : Convert.ToByte(value, CultureInfo.InvariantCulture);
    }

    public char GetChar(int i)
    {
        var value = currentValues[i];
        return value is char t ? t : Convert.ToChar(value, CultureInfo.InvariantCulture);
    }

    public short GetInt16(int i)
    {
        var value = currentValues[i];
        return value is short t ? t : Convert.ToInt16(value, CultureInfo.InvariantCulture);
    }

    public int GetInt32(int i)
    {
        var value = currentValues[i];
        return value is int t ? t : Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    public long GetInt64(int i)
    {
        var value = currentValues[i];
        return value is long t ? t : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    public float GetFloat(int i)
    {
        var value = currentValues[i];
        return value is float t ? t : Convert.ToSingle(value, CultureInfo.InvariantCulture);
    }

    public double GetDouble(int i)
    {
        var value = currentValues[i];
        return value is double t ? t : Convert.ToDouble(value, CultureInfo.InvariantCulture);
    }

    public decimal GetDecimal(int i)
    {
        var value = currentValues[i];
        return value is decimal t ? t : Convert.ToDecimal(value, CultureInfo.InvariantCulture);
    }

    public DateTime GetDateTime(int i)
    {
        var value = currentValues[i];
        return value is DateTime t ? t : Convert.ToDateTime(value, CultureInfo.InvariantCulture);
    }

    public Guid GetGuid(int i)
    {
        var value = currentValues[i];
        if (value is Guid t)
        {
            return t;
        }
        if (value is string str)
        {
            return Guid.Parse(str, CultureInfo.InvariantCulture);
        }

        var name = value?.GetType().Name ?? "null";
        throw new NotSupportedException($"Convert to Guid is not supported. type=[{name}]");
    }

    public string GetString(int i)
    {
        var value = currentValues[i];
        return value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture)!;
    }

    public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferOffset, int length)
    {
        var value = currentValues[i];
        if (value is byte[] array)
        {
            var count = Math.Min(length, array.Length - (int)fieldOffset);
            if (count > 0)
            {
                array.AsSpan((int)fieldOffset, count).CopyTo(buffer);
            }
            return count;
        }

        var name = value?.GetType().Name ?? "null";
        throw new NotSupportedException($"Convert to Guid is not supported. type=[{name}]");
    }

    public long GetChars(int i, long fieldOffset, char[]? buffer, int bufferOffset, int length)
    {
        var value = currentValues[i];
        if (value is char[] array)
        {
            var count = Math.Min(length, array.Length - (int)fieldOffset);
            if (count > 0)
            {
                array.AsSpan((int)fieldOffset, count).CopyTo(buffer);
            }
            return count;
        }

        var name = value?.GetType().Name ?? "null";
        throw new NotSupportedException($"Convert to Guid is not supported. type=[{name}]");
    }
}
