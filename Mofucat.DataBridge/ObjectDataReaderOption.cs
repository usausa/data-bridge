namespace Mofucat.DataBridge;

using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

public sealed class ObjectDataReaderOption<T>
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

    public Func<PropertyInfo, Func<T, object?>> AccessorFactory { get; set; } = DefaultFactory;

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
