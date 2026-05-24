namespace Mofucat.DataBridge;

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;

public sealed class ObjectDataReaderOption<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>
{
    private static readonly PropertyInfo[] DefaultSelector =
        typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static x => x.CanRead && (x.GetCustomAttribute<DataIgnoreAttribute>() is null))
            .Select(static (x, i) =>
            {
                var order = x.GetCustomAttribute<DataColumnAttribute>()?.Order ?? Int32.MaxValue;
                return new { Order = order, Index = i, Property = x };
            })
            .OrderBy(static x => x.Order)
            .ThenBy(static x => x.Index)
            .Select(static x => x.Property)
            .ToArray();

    private static readonly ConcurrentDictionary<PropertyInfo, Func<T, object?>> Accessors = new();

    public Func<IEnumerable<PropertyInfo>> PropertySelector { get; set; } = () => DefaultSelector;

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "DefaultFactory is only called at runtime and requires the caller to ensure T's properties are preserved.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "DefaultFactory uses Expression.Compile() which is only invoked when no AOT-safe AccessorFactory is provided.")]
    public Func<PropertyInfo, Func<T, object?>> AccessorFactory { get; set; } = DefaultFactory;

    [RequiresDynamicCode("Uses Expression.Compile() which is not supported in AOT environments.")]
    [RequiresUnreferencedCode("Uses reflection to access properties which may be trimmed.")]
    private static Func<T, object?> DefaultFactory(PropertyInfo pi)
    {
        if (!Accessors.TryGetValue(pi, out var func))
        {
            var parameter = Expression.Parameter(typeof(T), "x");
            var body = Expression.Convert(Expression.Property(parameter, pi), typeof(object));
            func = Expression.Lambda<Func<T, object?>>(body, parameter).Compile();
            Accessors[pi] = func;
        }
        return func;
    }
}
