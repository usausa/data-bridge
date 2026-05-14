namespace Mofucat.DataBridge;

using System;
using System.Buffers;
using System.Data;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

#pragma warning disable IDE0032
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

#pragma warning disable IDE0028
    private readonly Dictionary<string, int> currentOrdinals = new(StringComparer.OrdinalIgnoreCase);
#pragma warning restore IDE0028

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsDBNull(int i) => GetObjectValue(i) is null or DBNull;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public object GetValue(int i) => GetObjectValue(i) ?? DBNull.Value;

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public int GetValues(object[] values)
    {
        ref var entriesBase = ref MemoryMarshal.GetArrayDataReference(entries);
        for (var i = 0; i < fieldCount; i++)
        {
            values[i] = Unsafe.Add(ref entriesBase, i).Accessor(source.Current!) ?? DBNull.Value;
        }

        return fieldCount;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool GetBoolean(int i)
    {
        var value = GetObjectValue(i);
        return value is bool t ? t : Convert.ToBoolean(value, CultureInfo.InvariantCulture);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte GetByte(int i)
    {
        var value = GetObjectValue(i);
        return value is byte t ? t : Convert.ToByte(value, CultureInfo.InvariantCulture);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public char GetChar(int i)
    {
        var value = GetObjectValue(i);
        return value is char t ? t : Convert.ToChar(value, CultureInfo.InvariantCulture);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public short GetInt16(int i)
    {
        var value = GetObjectValue(i);
        return value is short t ? t : Convert.ToInt16(value, CultureInfo.InvariantCulture);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetInt32(int i)
    {
        var value = GetObjectValue(i);
        return value is int t ? t : Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long GetInt64(int i)
    {
        var value = GetObjectValue(i);
        return value is long t ? t : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float GetFloat(int i)
    {
        var value = GetObjectValue(i);
        return value is float t ? t : Convert.ToSingle(value, CultureInfo.InvariantCulture);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double GetDouble(int i)
    {
        var value = GetObjectValue(i);
        return value is double t ? t : Convert.ToDouble(value, CultureInfo.InvariantCulture);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public decimal GetDecimal(int i)
    {
        var value = GetObjectValue(i);
        return value is decimal t ? t : Convert.ToDecimal(value, CultureInfo.InvariantCulture);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DateTime GetDateTime(int i)
    {
        var value = GetObjectValue(i);
        return value is DateTime t ? t : Convert.ToDateTime(value, CultureInfo.InvariantCulture);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
        var value = GetObjectValue(i);
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
#pragma warning restore CA1725
#pragma warning restore IDE0032
