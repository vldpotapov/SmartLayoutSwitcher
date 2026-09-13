namespace SmartLayoutSwitcher.Core;

public enum KeyReaction
{
    PassThrough,
    Suppress,
}

/// <summary>
/// Pure state machine for the Left Alt + Left Shift combo. Windows-independent so it
/// can be unit-tested (spec §27). Also decides which key events must be suppressed so
/// Windows' own Alt+Shift handling does not fire a second switch.
///
/// Right Alt / Right Shift / AltGr are never touched here.
/// </summary>
public sealed class HotkeyReactor
{
    private bool _leftAltDown;
    private bool _leftShiftDown;
    // Whether the matching key-down was delivered to the foreground app. A
    // delivered down must always have a delivered up, otherwise Alt/Shift sticks.
    private bool _leftAltPassed;
    private bool _leftShiftPassed;
    private bool _engaged;
    private bool _waitingForRelease;
    private bool _cycleMode;
    private long? _engagedAtMs;

    /// <summary>Allows the host to decide short/long press; null when no combo is armed.</summary>
    public long? EngagedSinceMs => _engagedAtMs;

    public bool IsEngaged => _engaged;

    /// <summary>Fired exactly once on the key-down that completes the combo.</summary>
    public event Action<long>? ComboEngaged;

    /// <summary>
    /// Fired when the combo is released: the first modifier up (short press) or, in
    /// cycle mode, the left Alt up that commits the popup selection. Arg = held duration (ms).
    /// </summary>
    public event Action<long>? ComboReleased;

    /// <summary>
    /// Fired on each fresh Left Shift key-down while the long-press popup is open
    /// (cycle mode): the host moves the popup selection to the next layout.
    /// </summary>
    public event Action? CycleStep;

    /// <summary>
    /// Locks the combo into "popup open" mode: Left Shift taps now move the popup
    /// selection instead of ending the press, and only Left Alt-up commits it.
    /// </summary>
    public void EnterCycleMode() => _cycleMode = true;

    public void ExitCycleMode() => _cycleMode = false;

    /// <summary>Forcibly clears all state (e.g. a modifier up event got lost).</summary>
    public void Reset() => (_leftAltDown, _leftShiftDown, _leftAltPassed, _leftShiftPassed,
        _engaged, _waitingForRelease, _cycleMode, _engagedAtMs) =
        (false, false, false, false, false, false, false, null);

    public KeyReaction LeftAltDown(long nowMs) => ModifierDown(isLeftAlt: true, nowMs, isRepeat: false);
    public KeyReaction LeftAltUp(long nowMs) => ModifierUp(isLeftAlt: true, nowMs);
    public KeyReaction LeftShiftDown(long nowMs, bool isRepeat = false) => ModifierDown(isLeftAlt: false, nowMs, isRepeat);
    public KeyReaction LeftShiftUp(long nowMs) => ModifierUp(isLeftAlt: false, nowMs);

    // Generic aliases let the host reuse the same balanced-key state machine for
    // another two-key shortcut, such as Win+Space.
    public KeyReaction PrimaryDown(long nowMs, bool isRepeat = false) => ModifierDown(isLeftAlt: true, nowMs, isRepeat);
    public KeyReaction PrimaryUp(long nowMs) => ModifierUp(isLeftAlt: true, nowMs);
    public KeyReaction SecondaryDown(long nowMs, bool isRepeat = false) => ModifierDown(isLeftAlt: false, nowMs, isRepeat);
    public KeyReaction SecondaryUp(long nowMs) => ModifierUp(isLeftAlt: false, nowMs);

    /// <summary>Any non-modifier key: we never block user input.</summary>
    public KeyReaction OtherKeyDown() => KeyReaction.PassThrough;

    /// <summary>Any non-modifier key: we never block user input.</summary>
    public KeyReaction OtherKeyUp() => KeyReaction.PassThrough;

    // Right modifiers are deliberately absent: AltGr (Right Alt) must pass through untouched.

    private KeyReaction ModifierDown(bool isLeftAlt, long nowMs, bool isRepeat)
    {
        // Was the key already down before this event? A fresh press always has a
        // preceding key-up; OS auto-repeat keydowns arrive while it is still down.
        // Either signal means "hold", never a cycle.
        var wasDown = isLeftAlt ? _leftAltDown : _leftShiftDown;
        var held = isRepeat || wasDown;

        if (isLeftAlt) _leftAltDown = true; else _leftShiftDown = true;

        if (_waitingForRelease)
            return KeyReaction.Suppress;

        if (_engaged && _cycleMode && !isLeftAlt)
        {
            // Popup is open and Left Shift is pressed again: move the selection.
            // Auto-repeat (holding the key) must NOT cycle, or the selection flies.
            if (!held)
                CycleStep?.Invoke();
            return KeyReaction.Suppress;
        }

        if (_engaged)
            return KeyReaction.Suppress;

        if (_leftAltDown && _leftShiftDown)
        {
            _engaged = true;
            _engagedAtMs = nowMs;
            ComboEngaged?.Invoke(nowMs);
            // The second modifier was never delivered, so its up is suppressed too.
            return KeyReaction.Suppress;
        }

        // The first modifier is passed through and must be balanced with a real up.
        if (isLeftAlt) _leftAltPassed = true; else _leftShiftPassed = true;
        return KeyReaction.PassThrough;
    }

    private KeyReaction ModifierUp(bool isLeftAlt, long nowMs)
    {
        if (_engaged)
        {
            var passedDown = isLeftAlt ? _leftAltPassed : _leftShiftPassed;
            if (isLeftAlt) _leftAltDown = false; else _leftShiftDown = false;
            if (isLeftAlt) _leftAltPassed = false; else _leftShiftPassed = false;

            if (_cycleMode && !isLeftAlt)
                // The original Shift-down can have been passed before the combo
                // engaged, while later cycling taps are entirely suppressed.
                return passedDown ? KeyReaction.PassThrough : KeyReaction.Suppress;

            var duration = nowMs - (_engagedAtMs ?? nowMs);
            _engaged = false;
            _engagedAtMs = null;
            // When this was the last physically held modifier (the normal end of
            // a long-press selection), there will be no later key-up to clear the
            // release gate. Keeping it armed would make every subsequent combo
            // inert. Only wait while the other modifier is actually still down.
            _waitingForRelease = _leftAltDown || _leftShiftDown;
            ComboReleased?.Invoke(duration);
            return passedDown ? KeyReaction.PassThrough : KeyReaction.Suppress;
        }

        var passedDownWhileWaiting = isLeftAlt ? _leftAltPassed : _leftShiftPassed;
        if (isLeftAlt) _leftAltDown = false; else _leftShiftDown = false;
        if (isLeftAlt) _leftAltPassed = false; else _leftShiftPassed = false;

        if (_waitingForRelease)
        {
            if (!_leftAltDown && !_leftShiftDown)
                _waitingForRelease = false;
            return passedDownWhileWaiting ? KeyReaction.PassThrough : KeyReaction.Suppress;
        }

        return KeyReaction.PassThrough;
    }
}
