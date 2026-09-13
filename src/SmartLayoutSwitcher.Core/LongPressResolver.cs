namespace SmartLayoutSwitcher.Core;

/// <summary>
/// Decides whether a held combo is a long press (shows the layout popup) or a short
/// press (toggles the pair). Threshold lives in settings, default 600 ms (spec §6).
/// </summary>
public sealed class LongPressResolver
{
    public long ThresholdMs { get; }

    public LongPressResolver(long thresholdMs = 600)
    {
        if (thresholdMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(thresholdMs), "Threshold must be positive.");
        ThresholdMs = thresholdMs;
    }

    public bool IsLongPress(long durationMs) => durationMs >= ThresholdMs;
}