namespace Mofucat.DataBridge;

using System.Data;
using System.Diagnostics.CodeAnalysis;

public static class DataReaderExtensions
{
    [RequiresDynamicCode("Uses Expression.Compile() for property access. Provide an ObjectDataReaderOption with an explicit AccessorFactory for AOT compatibility.")]
    [RequiresUnreferencedCode("Uses reflection to discover properties. Provide an ObjectDataReaderOption with an explicit AccessorFactory for AOT compatibility.")]
    public static ObjectDataReader<T> AsDataReader<T>(this IEnumerable<T> source) => new(source);

    public static ObjectDataReader<T> AsDataReader<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(this IEnumerable<T> source, ObjectDataReaderOption<T> option) => new(option, source);

    public static MappingDataReader Mapping(this IDataReader source, MappingDataReaderOption option) => new(option, source);
}
