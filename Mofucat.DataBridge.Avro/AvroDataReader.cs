namespace Mofucat.DataBridge;

using System;
using System.Buffers;
using System.Data;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Avro;
using Avro.File;
using Avro.Generic;

#pragma warning disable IDE0032
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

    private readonly Dictionary<string, int> currentOrdinals = new(StringComparer.OrdinalIgnoreCase);

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
    // Helper
    //--------------------------------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref Entry GetEntryRef(int i)
    {
        if ((uint)i >= (uint)fieldCount)
        {
            ThrowIndexOutOfRange();
        }

        return ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(entries), i);
    }

    // ReSharper disable once NotResolvedInText
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowIndexOutOfRange() => throw new ArgumentOutOfRangeException("i");

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

            currentOrdinals[field.Name] = i;
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

        static Type ConvertType(string name, Schema.Type type) => type switch
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

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public bool Read()
    {
        if (!reader.HasNext())
        {
            return false;
        }

        currentRecord = reader.Next();

        ref var entriesBase = ref MemoryMarshal.GetArrayDataReference(entries);
        ref var valuesBase = ref MemoryMarshal.GetArrayDataReference(currentValues);
        var count = fieldCount;
        for (var i = 0; i < count; i++)
        {
            ref var entry = ref Unsafe.Add(ref entriesBase, i);
            var value = currentRecord[entry.Name];
            var converter = entry.Converter;
            Unsafe.Add(ref valuesBase, i) = converter is not null ? converter(value) : value;
        }

        return true;
    }

    public bool NextResult() => false;

    //--------------------------------------------------------------------------------
    // Metadata
    //--------------------------------------------------------------------------------

    public IDataReader GetData(int i) => throw new NotSupportedException();

    public DataTable GetSchemaTable() => throw new NotSupportedException();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string GetDataTypeName(int i)
    {
        ref var entry = ref GetEntryRef(i);
        return entry.Type.Name;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Type GetFieldType(int i)
    {
        ref var entry = ref GetEntryRef(i);
        return entry.Type;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string GetName(int i)
    {
        ref var entry = ref GetEntryRef(i);
        return entry.Name;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetOrdinal(string name)
    {
        return currentOrdinals.GetValueOrDefault(name, -1);
    }

    //--------------------------------------------------------------------------------
    // Value
    //--------------------------------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsDBNull(int i) => currentValues[i] is null or DBNull;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public object GetValue(int i) => currentValues[i] ?? DBNull.Value;

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public int GetValues(object[] values)
    {
        ref var valuesBase = ref MemoryMarshal.GetArrayDataReference(currentValues);
        for (var i = 0; i < fieldCount; i++)
        {
            values[i] = Unsafe.Add(ref valuesBase, i) ?? DBNull.Value;
        }

        return fieldCount;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool GetBoolean(int i)
    {
        var value = currentValues[i];
        return value is bool t ? t : Convert.ToBoolean(value, CultureInfo.InvariantCulture);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte GetByte(int i)
    {
        var value = currentValues[i];
        return value is byte t ? t : Convert.ToByte(value, CultureInfo.InvariantCulture);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public char GetChar(int i)
    {
        var value = currentValues[i];
        return value is char t ? t : Convert.ToChar(value, CultureInfo.InvariantCulture);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public short GetInt16(int i)
    {
        var value = currentValues[i];
        return value is short t ? t : Convert.ToInt16(value, CultureInfo.InvariantCulture);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetInt32(int i)
    {
        var value = currentValues[i];
        return value is int t ? t : Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long GetInt64(int i)
    {
        var value = currentValues[i];
        return value is long t ? t : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float GetFloat(int i)
    {
        var value = currentValues[i];
        return value is float t ? t : Convert.ToSingle(value, CultureInfo.InvariantCulture);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double GetDouble(int i)
    {
        var value = currentValues[i];
        return value is double t ? t : Convert.ToDouble(value, CultureInfo.InvariantCulture);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public decimal GetDecimal(int i)
    {
        var value = currentValues[i];
        return value is decimal t ? t : Convert.ToDecimal(value, CultureInfo.InvariantCulture);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DateTime GetDateTime(int i)
    {
        var value = currentValues[i];
        return value is DateTime t ? t : Convert.ToDateTime(value, CultureInfo.InvariantCulture);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
            if (buffer is null)
            {
                return 0;
            }

            var count = Math.Min(length, array.Length - (int)fieldOffset);
            if (count > 0)
            {
                array.AsSpan((int)fieldOffset, count).CopyTo(buffer.AsSpan(bufferOffset, count));
            }

            return count;
        }

        var name = value?.GetType().Name ?? "null";
        throw new NotSupportedException($"Convert to bytes is not supported. type=[{name}]");
    }

    public long GetChars(int i, long fieldOffset, char[]? buffer, int bufferOffset, int length)
    {
        var value = currentValues[i];
        if (value is char[] array)
        {
            if (buffer is null)
            {
                return 0;
            }

            var count = Math.Min(length, array.Length - (int)fieldOffset);
            if (count > 0)
            {
                array.AsSpan((int)fieldOffset, count).CopyTo(buffer.AsSpan(bufferOffset, count));
            }

            return count;
        }

        var name = value?.GetType().Name ?? "null";
        throw new NotSupportedException($"Convert to chars is not supported. type=[{name}]");
    }
}
#pragma warning restore IDE0032
