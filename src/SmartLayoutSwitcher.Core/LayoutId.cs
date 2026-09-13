namespace SmartLayoutSwitcher.Core;

/// <summary>
/// Stable identity of a Windows keyboard layout, independent of the Win32 layer.
/// The Id is the lowercase hex preload id, e.g. "00000409" (EN-US) or "00000409:00000419".
/// </summary>
public sealed record LayoutId(string Id)
{
    public static readonly LayoutId Empty = new(string.Empty);

    public bool IsEmpty => string.IsNullOrEmpty(Id);

    public override string ToString() => IsEmpty ? "(none)" : Id;
}