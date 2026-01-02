namespace Mofucat.DataBridge;

internal sealed class MappingDataColumn
{
    public int? Index { get; set; }

    public string? Name { get; set; }

    public Type? ConvertType { get; set; }

    public Func<object, object>? Converter { get; set; }
}

public sealed class MappingDataReaderOption
{
    internal List<MappingDataColumn>? Columns { get; private set; }

    internal Dictionary<Type, (Type ConvertType, Func<object, object> Converter)>? TypeConverters { get; private set; }

    public MappingDataReaderOption AddColumn(int index)
    {
        Columns ??= new List<MappingDataColumn>();
        Columns.Add(new MappingDataColumn { Index = index });
        return this;
    }

    public MappingDataReaderOption AddColumn(string name)
    {
        Columns ??= new List<MappingDataColumn>();
        Columns.Add(new MappingDataColumn { Name = name });
        return this;
    }

    public MappingDataReaderOption AddColumn<TSource, TDestination>(int index, Func<TSource, TDestination> converter)
    {
        Columns ??= new List<MappingDataColumn>();
        Columns.Add(new MappingDataColumn { Index = index, ConvertType = typeof(TDestination), Converter = x => converter((TSource)x)! });
        return this;
    }

    public MappingDataReaderOption AddColumn<TSource, TDestination>(string name, Func<TSource, TDestination> converter)
    {
        Columns ??= new List<MappingDataColumn>();
        Columns.Add(new MappingDataColumn { Name = name, ConvertType = typeof(TDestination), Converter = x => converter((TSource)x)! });
        return this;
    }

    public MappingDataReaderOption AddConverter<TSource, TDestination>(Func<TSource, TDestination> converter)
    {
        TypeConverters ??= new Dictionary<Type, (Type, Func<object, object>)>();
        TypeConverters.Add(typeof(TSource), (typeof(TDestination), x => converter((TSource)x)!));
        return this;
    }
}
