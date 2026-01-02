namespace Mofucat.DataBridge;

using System.Data;

public static class DataReaderExtensions
{
    public static ObjectDataReader<T> AsDataReader<T>(this IEnumerable<T> source) => new(source);

    public static ObjectDataReader<T> AsDataReader<T>(this IEnumerable<T> source, ObjectDataReaderOption<T> option) => new(option, source);

    public static MappingDataReader Mapping(this IDataReader source, MappingDataReaderOption option) => new(option, source);
}
