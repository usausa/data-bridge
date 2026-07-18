namespace Mofucat.DataBridge;

using System;
using System.Buffers;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

#pragma warning disable IDE0032
#pragma warning disable CA1725
public sealed class MappingDataReader : IDataReader
{
    private struct Entry
    {
        public int SourceIndex;

        public Type? ConvertType;

        public Func<object, object>? Converter;
    }

    private readonly IDataReader source;

    private readonly int fieldCount;

#pragma warning disable IDE0028
    private readonly Dictionary<string, int> currentOrdinals = new(StringComparer.OrdinalIgnoreCase);
#pragma warning restore IDE0028

    private Entry[] entries;

    private object?[] currentValues;

    //--------------------------------------------------------------------------------
    // Property
    //--------------------------------------------------------------------------------

    public int FieldCount => fieldCount;

    public int Depth => source.Depth;

    public bool IsClosed { get; private set; }

    public int RecordsAffected => -1;

    public object this[int i] => GetValue(i);

    public object this[string name] => GetValue(GetOrdinal(name));

    //--------------------------------------------------------------------------------
    // Helper
    //--------------------------------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref object? GetCurrentValueRef(int i)
    {
        if ((uint)i >= (uint)fieldCount)
        {
            ThrowIndexOutOfRange();
        }

        return ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(currentValues), i);
    }

    // ReSharper disable once NotResolvedInText
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowIndexOutOfRange() => throw new ArgumentOutOfRangeException("i");

    //--------------------------------------------------------------------------------
    // Constructor
    //--------------------------------------------------------------------------------

    public MappingDataReader(MappingDataReaderOption option, IDataReader source)
    {
        this.source = source;

        if (option.Columns is null)
        {
            fieldCount = source.FieldCount;
            entries = ArrayPool<Entry>.Shared.Rent(fieldCount);
            for (var i = 0; i < fieldCount; i++)
            {
                ref var entry = ref entries[i];
                entry.SourceIndex = i;
                entry.ConvertType = null;
                entry.Converter = null;
            }
        }
        else
        {
            fieldCount = option.Columns.Count;
            entries = ArrayPool<Entry>.Shared.Rent(fieldCount);
            for (var i = 0; i < fieldCount; i++)
            {
                var column = option.Columns[i];

                ref var entry = ref entries[i];
                if (column.Index is not null)
                {
                    entry.SourceIndex = column.Index.Value;
                }
                else
                {
                    if (column.Name is null)
                    {
                        throw new ArgumentException("Column name is required.");
                    }

                    var index = source.GetOrdinal(column.Name);
                    if (index < 0)
                    {
                        throw new ArgumentException($"Column '{column.Name}' not found.");
                    }

                    entry.SourceIndex = index;
                }

                entry.ConvertType = column.ConvertType;
                entry.Converter = column.Converter;
            }
        }

        for (var i = 0; i < fieldCount; i++)
        {
            ref var entry = ref entries[i];

            if (entry.ConvertType is null)
            {
                var sourceType = source.GetFieldType(entry.SourceIndex);
                if (option.TypeConverters?.TryGetValue(sourceType, out var converter) ?? false)
                {
                    entry.ConvertType = converter.ConvertType;
                    entry.Converter = converter.Converter;
                }
            }

            currentOrdinals.TryAdd(source.GetName(entry.SourceIndex), i);
        }

        currentValues = ArrayPool<object?>.Shared.Rent(fieldCount);
    }

    public void Dispose()
    {
        if (IsClosed)
        {
            return;
        }

        source.Close();
        source.Dispose();

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

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public bool Read()
    {
        if (!source.Read())
        {
            return false;
        }

        var count = fieldCount;

        // Optimize single column
        if (count == 1)
        {
            ref var entry = ref entries[0];
            var value = source.GetValue(entry.SourceIndex);
            var converter = entry.Converter;
            currentValues[0] = converter is not null ? converter(value) : value;
            return true;
        }

        ref var entriesBase = ref MemoryMarshal.GetArrayDataReference(entries);
        ref var valuesBase = ref MemoryMarshal.GetArrayDataReference(currentValues);
        for (var i = 0; i < count; i++)
        {
            ref var e = ref Unsafe.Add(ref entriesBase, i);
            var value = source.GetValue(e.SourceIndex);
            var converter = e.Converter;
            Unsafe.Add(ref valuesBase, i) = converter is not null ? converter(value) : value;
        }

        return true;
    }

    public bool NextResult() => source.NextResult();

    //--------------------------------------------------------------------------------
    // Metadata
    //--------------------------------------------------------------------------------

    public IDataReader GetData(int i) => throw new NotSupportedException();

    public DataTable GetSchemaTable() => throw new NotSupportedException();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string GetDataTypeName(int i)
    {
        ref var entry = ref entries[i];
        return entry.ConvertType?.Name ?? source.GetDataTypeName(entry.SourceIndex);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [UnconditionalSuppressMessage("Trimming", "IL2093", Justification = "The returned Type is stored at construction time and is not used for reflection.")]
    public Type GetFieldType(int i)
    {
        ref var entry = ref entries[i];
        return entry.ConvertType ?? source.GetFieldType(entry.SourceIndex);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string GetName(int i)
    {
        ref var entry = ref entries[i];
        return source.GetName(entry.SourceIndex);
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
    public bool IsDBNull(int i) => currentValues[i] is null or DBNull;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public object GetValue(int i) => currentValues[i] ?? DBNull.Value;

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public int GetValues(object[] values)
    {
        var count = Math.Min(values.Length, fieldCount);
        ref var valuesBase = ref MemoryMarshal.GetArrayDataReference(currentValues);
        for (var i = 0; i < count; i++)
        {
            values[i] = Unsafe.Add(ref valuesBase, i) ?? DBNull.Value;
        }

        return count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool GetBoolean(int i) => (bool)currentValues[i]!;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte GetByte(int i) => (byte)currentValues[i]!;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public char GetChar(int i) => (char)currentValues[i]!;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public short GetInt16(int i) => (short)currentValues[i]!;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetInt32(int i) => (int)currentValues[i]!;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long GetInt64(int i) => (long)currentValues[i]!;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float GetFloat(int i) => (float)currentValues[i]!;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double GetDouble(int i) => (double)currentValues[i]!;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public decimal GetDecimal(int i) => (decimal)currentValues[i]!;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DateTime GetDateTime(int i) => (DateTime)currentValues[i]!;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Guid GetGuid(int i) => (Guid)currentValues[i]!;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string GetString(int i) => (string)currentValues[i]!;

    public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferOffset, int length)
    {
        var value = GetCurrentValueRef(i);
        if (value is byte[] array)
        {
            if (buffer is null)
            {
                return array.Length;
            }

            var count = Math.Max(0, Math.Min(length, array.Length - (int)fieldOffset));
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
        var value = GetCurrentValueRef(i);
        if (value is char[] array)
        {
            if (buffer is null)
            {
                return array.Length;
            }

            var count = Math.Max(0, Math.Min(length, array.Length - (int)fieldOffset));
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
