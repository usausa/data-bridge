#if !NET9_0_OR_GREATER
namespace Mofucat.DataBridge;

using System.ComponentModel;

[EditorBrowsable(EditorBrowsableState.Never)]
#pragma warning disable CA1815
public readonly struct RawString
{
    internal readonly string Value;

    private RawString(string value) => Value = value;

#pragma warning disable CA2225
    public static implicit operator RawString(string s) => new(s);

    // ReSharper disable once UnusedParameter.Global
#pragma warning disable CA1065
    public static implicit operator RawString(FormattableString s) => throw new InvalidCastException();
#pragma warning restore CA1065
#pragma warning restore CA2225
}
#pragma warning restore CA1815
#endif
