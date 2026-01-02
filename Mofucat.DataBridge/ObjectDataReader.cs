namespace Mofucat.DataBridge;

using System;
using System.Buffers;
using System.Data;
using System.Globalization;
using System.Runtime.CompilerServices;

#pragma warning disable CA1725
public sealed class ObjectDataReader<T> : IDataReader
{
    private static readonly ObjectDataReaderOption<T> DefaultOption = new();

    private struct Entry
    {
        public string Name;

        public Type Type;

        public Func<T, object?> Accessor;
    }

    private readonly IEnumerator<T> source;

    private readonly int fieldCount;

    private readonly Dictionary<string, int> currentOrdinals = new(StringComparer.OrdinalIgnoreCase);

    private Entry[] entries;

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

    public ObjectDataReader(IEnumerable<T> source)
        : this(DefaultOption, source)
    {
    }

    public ObjectDataReader(ObjectDataReaderOption<T> option, IEnumerable<T> source)
    {
        var properties = option.PropertySelector().ToArray();
        fieldCount = properties.Length;
        entries = ArrayPool<Entry>.Shared.Rent(fieldCount);

        for (var i = 0; i < properties.Length; i++)
        {
            var property = properties[i];

            currentOrdinals[property.Name] = i;

            ref var entry = ref entries[i];
            entry.Name = property.Name;
            entry.Type = property.PropertyType;
            entry.Accessor = option.AccessorFactory(property);
        }

        this.source = source.GetEnumerator();
    }

    public void Dispose()
    {
        if (IsClosed)
        {
            return;
        }

        source.Dispose();

        if (entries.Length > 0)
        {
            ArrayPool<Entry>.Shared.Return(entries, true);
            entries = [];
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

    public bool Read() => source.MoveNext();

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
        if (currentOrdinals.TryGetValue(name, out var ordinal))
        {
            return ordinal;
        }

        throw new ArgumentException($"Column {name} is not found.", nameof(name));
    }

    //--------------------------------------------------------------------------------
    // Value
    //--------------------------------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private object? GetObjectValue(int i)
    {
        ref var entry = ref entries[i];
        return entry.Accessor(source.Current!);
    }

    public bool IsDBNull(int i) => GetObjectValue(i) is null or DBNull;

    public object GetValue(int i) => GetObjectValue(i) ?? DBNull.Value;

    public int GetValues(object[] values)
    {
        for (var i = 0; i < fieldCount; i++)
        {
            values[i] = GetObjectValue(i) ?? DBNull.Value;
        }
        return fieldCount;
    }

    public bool GetBoolean(int i)
    {
        var value = GetObjectValue(i);
        return value is bool t ? t : Convert.ToBoolean(value, CultureInfo.InvariantCulture);
    }

    public byte GetByte(int i)
    {
        var value = GetObjectValue(i);
        return value is byte t ? t : Convert.ToByte(value, CultureInfo.InvariantCulture);
    }

    public char GetChar(int i)
    {
        var value = GetObjectValue(i);
        return value is char t ? t : Convert.ToChar(value, CultureInfo.InvariantCulture);
    }

    public short GetInt16(int i)
    {
        var value = GetObjectValue(i);
        return value is short t ? t : Convert.ToInt16(value, CultureInfo.InvariantCulture);
    }

    public int GetInt32(int i)
    {
        var value = GetObjectValue(i);
        return value is int t ? t : Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    public long GetInt64(int i)
    {
        var value = GetObjectValue(i);
        return value is long t ? t : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    public float GetFloat(int i)
    {
        var value = GetObjectValue(i);
        return value is float t ? t : Convert.ToSingle(value, CultureInfo.InvariantCulture);
    }

    public double GetDouble(int i)
    {
        var value = GetObjectValue(i);
        return value is double t ? t : Convert.ToDouble(value, CultureInfo.InvariantCulture);
    }

    public decimal GetDecimal(int i)
    {
        var value = GetObjectValue(i);
        return value is decimal t ? t : Convert.ToDecimal(value, CultureInfo.InvariantCulture);
    }

    public DateTime GetDateTime(int i)
    {
        var value = GetObjectValue(i);
        return value is DateTime t ? t : Convert.ToDateTime(value, CultureInfo.InvariantCulture);
    }

    public Guid GetGuid(int i)
    {
        var value = GetObjectValue(i);
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
        var value = GetObjectValue(i);
        return value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture)!;
    }

    public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferOffset, int length)
    {
        var value = GetObjectValue(i);
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
        throw new NotSupportedException($"Convert to bytes is not supported. type=[{name}]");
    }

    public long GetChars(int i, long fieldOffset, char[]? buffer, int bufferOffset, int length)
    {
        var value = GetObjectValue(i);
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
        throw new NotSupportedException($"Convert to chars is not supported. type=[{name}]");
    }
}
