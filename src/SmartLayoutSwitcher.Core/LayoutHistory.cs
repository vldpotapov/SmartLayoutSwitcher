namespace SmartLayoutSwitcher.Core;

/// <summary>
/// State machine for the pair of the two most recently used layouts.
///
/// Model (spec §14–§16):
///   Initial:      Current = Primary = actual layout, Secondary = null
///   User switch to a third layout X: new pair = { Current (previous actual), X }, Current = X
///   Internal toggle: Current moves between Primary and Secondary, pair stays unchanged
/// </summary>
public sealed class LayoutHistory
{
    public LayoutId Primary { get; private set; } = LayoutId.Empty;
    public LayoutId Secondary { get; private set; } = LayoutId.Empty;
    public LayoutId Current { get; private set; } = LayoutId.Empty;

    public bool HasPair => !Primary.IsEmpty && !Secondary.IsEmpty;

    public void Initialize(LayoutId first)
    {
        ArgumentNullException.ThrowIfNull(first);

        Primary = first;
        Secondary = LayoutId.Empty;
        Current = first;
    }

    /// <summary>
    /// Restores a previously persisted pair. Both layouts must still be installed
    /// (guarded by the caller). Active layout becomes <paramref name="first"/>.
    /// </summary>
    public void RestorePair(LayoutId first, LayoutId second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        if (first.IsEmpty || second.IsEmpty || first == second)
            throw new ArgumentException("Pair must contain two distinct, non-empty layouts.");

        Primary = first;
        Secondary = second;
        Current = first;
    }

    /// <summary>
    /// Restores a pair while retaining the layout Windows reports as active.
    /// The active layout may temporarily be outside the pair (for example, when
    /// a window remembers a third layout). The next toggle then moves to the
    /// primary member of the restored pair.
    /// </summary>
    public void RestorePair(LayoutId first, LayoutId second, LayoutId current)
    {
        ArgumentNullException.ThrowIfNull(current);

        RestorePair(first, second);
        if (!current.IsEmpty)
            Current = current;
    }

    /// <summary>
    /// Reports a layout that is really active (chosen by the user, not by our hotkey).
    /// Returns true when the stored pair changed.
    /// </summary>
    public bool ApplyUserSelection(LayoutId selected)
    {
        ArgumentNullException.ThrowIfNull(selected);
        if (selected.IsEmpty)
            return false;

        if (selected == Current)
            return false;

        if (Secondary.IsEmpty)
        {
            // First real choice after startup: pair = { actual, chosen }.
            Secondary = selected;
            Current = selected;
            return true;
        }

        if (selected == Primary || selected == Secondary)
        {
            // Manual move within the current pair: history composition is unchanged.
            Current = selected;
            return false;
        }

        // New layout outside the pair: pair becomes { previous actual, selected }.
        Primary = Current;
        Secondary = selected;
        Current = selected;
        return true;
    }

    /// <summary>
    /// Applies a selection made in the layout popup. Unlike a manual Windows
    /// language switch, popup selection edits the current pair: a new layout
    /// replaces the active member and preserves the other member. Thus, from
    /// RU ↔ EN with EN active, choosing Czech produces RU ↔ Czech.
    /// </summary>
    public bool ApplyPopupSelection(LayoutId selected)
    {
        ArgumentNullException.ThrowIfNull(selected);
        if (selected.IsEmpty || selected == Current)
            return false;

        if (!HasPair || (Current != Primary && Current != Secondary))
            return ApplyUserSelection(selected);

        if (selected == Primary || selected == Secondary)
        {
            Current = selected;
            return false;
        }

        var preserved = Current == Primary ? Secondary : Primary;
        Primary = preserved;
        Secondary = selected;
        Current = selected;
        return true;
    }

    /// <summary>
    /// Returns the layout the hotkey should switch to without mutating state.
    /// </summary>
    public bool TryGetToggleTarget(out LayoutId target)
    {
        if (!HasPair)
        {
            target = LayoutId.Empty;
            return false;
        }

        target = Current == Primary ? Secondary : Primary;
        return !target.IsEmpty;
    }

    /// <summary>
    /// Applies an internal (hotkey-driven) toggle: Current moves to the other member
    /// of the pair. The pair composition itself never changes.
    /// </summary>
    public void ApplyInternalToggle()
    {
        if (!TryGetToggleTarget(out var target))
            return;

        Current = target;
    }
}
