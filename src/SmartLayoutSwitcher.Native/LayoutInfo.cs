using SmartLayoutSwitcher.Core;

namespace SmartLayoutSwitcher.Native;

/// <summary>
/// Consumer-ready description of a Windows keyboard layout.
/// </summary>
public sealed record LayoutInfo(
    IntPtr Hkl,
    uint PreloadId,
    ushort Lcid,
    string LanguageName,
    string LayoutName,
    int SortOrder)
{
    public LayoutId Id => new(PreloadId.ToString("X8"));

    public override string ToString() => $"{Id} | {LanguageName} [{LayoutName}]";
}