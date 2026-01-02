namespace Mofucat.DataBridge;

[AttributeUsage(AttributeTargets.Property)]
public sealed class DataColumnAttribute : Attribute
{
    public int Order { get; }

    public DataColumnAttribute(int order)
    {
        Order = order;
    }
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class DataIgnoreAttribute : Attribute
{
}
