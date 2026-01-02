namespace Mofucat.DataBridge;

using System;
using System.Buffers;
using System.Data;

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

    private readonly Dictionary<string, int> currentOrdinals = new(StringComparer.OrdinalIgnoreCase);

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

            currentOrdinals[source.GetName(entry.SourceIndex)] = i;
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

    public bool Read()
    {
        if (!source.Read())
        {
            return false;
        }

        for (var i = 0; i < fieldCount; i++)
        {
            ref var entry = ref entries[i];
            var value = source.GetValue(entry.SourceIndex);
            var converter = entry.Converter;
            currentValues[i] = converter is not null ? converter(value) : value;
        }

        return true;
    }

    public bool NextResult() => source.NextResult();

    //--------------------------------------------------------------------------------
    // Metadata
    //--------------------------------------------------------------------------------

    public IDataReader GetData(int i) => throw new NotSupportedException();

    public DataTable GetSchemaTable() => throw new NotSupportedException();

    public string GetDataTypeName(int i)
    {
        ref var entry = ref entries[i];
        return entry.ConvertType?.Name ?? source.GetDataTypeName(entry.SourceIndex);
    }

    public Type GetFieldType(int i)
    {
        ref var entry = ref entries[i];
        return entry.ConvertType ?? source.GetFieldType(entry.SourceIndex);
    }

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

    public bool GetBoolean(int i) => (bool)currentValues[i]!;

    public byte GetByte(int i) => (byte)currentValues[i]!;

    public char GetChar(int i) => (char)currentValues[i]!;

    public short GetInt16(int i) => (short)currentValues[i]!;

    public int GetInt32(int i) => (int)currentValues[i]!;

    public long GetInt64(int i) => (long)currentValues[i]!;

    public float GetFloat(int i) => (float)currentValues[i]!;

    public double GetDouble(int i) => (double)currentValues[i]!;

    public decimal GetDecimal(int i) => (decimal)currentValues[i]!;

    public DateTime GetDateTime(int i) => (DateTime)currentValues[i]!;

    public Guid GetGuid(int i) => (Guid)currentValues[i]!;

    public string GetString(int i) => (string)currentValues[i]!;

    public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferOffset, int length)
    {
        var array = (byte[])currentValues[i]!;
        var count = Math.Min(length, array.Length - (int)fieldOffset);
        if (count > 0)
        {
            array.AsSpan((int)fieldOffset, count).CopyTo(buffer);
        }
        return count;
    }

    public long GetChars(int i, long fieldOffset, char[]? buffer, int bufferOffset, int length)
    {
        var array = (char[])currentValues[i]!;
        var count = Math.Min(length, array.Length - (int)fieldOffset);
        if (count > 0)
        {
            array.AsSpan((int)fieldOffset, count).CopyTo(buffer);
        }
        return count;
    }
}
