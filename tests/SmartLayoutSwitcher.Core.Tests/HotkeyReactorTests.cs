using SmartLayoutSwitcher.Core;

namespace SmartLayoutSwitcher.Core.Tests;

public class HotkeyReactorTests
{
    private static (HotkeyReactor r, List<(string name, long duration)> events) Create()
    {
        var r = new HotkeyReactor();
        var events = new List<(string, long)>();
        r.ComboEngaged += ts => events.Add(("engaged", ts));
        r.ComboReleased += duration => events.Add(("released", duration));
        return (r, events);
    }

    [Fact]
    public void LeftAltThenLeftShift_EngagesOnce_AndBalancesThePassedAlt()
    {
        var (r, events) = Create();

        Assert.Equal(KeyReaction.PassThrough, r.LeftAltDown(100));
        Assert.Equal(KeyReaction.Suppress, r.LeftShiftDown(120));
        Assert.Equal(KeyReaction.PassThrough, r.LeftAltUp(200));
        Assert.Equal(KeyReaction.Suppress, r.LeftShiftUp(220));

        Assert.Single(events, e => e.name == "engaged");
        Assert.Single(events, e => e.name == "released");
        Assert.Equal(200 - 120, events.Single(e => e.name == "released").duration);
    }

    [Fact]
    public void LeftShiftThenLeftAlt_AlsoEngages()
    {
        var (r, events) = Create();

        Assert.Equal(KeyReaction.PassThrough, r.LeftShiftDown(100));
        Assert.Equal(KeyReaction.Suppress, r.LeftAltDown(130));
        Assert.Equal(KeyReaction.PassThrough, r.LeftShiftUp(170));
        Assert.Equal(KeyReaction.Suppress, r.LeftAltUp(190));

        Assert.Single(events, e => e.name == "engaged");
        Assert.Equal(170 - 130, events.Single(e => e.name == "released").duration);
    }

    [Fact]
    public void SingleModifiers_DoNotEngage_AndPassThrough()
    {
        var (r, events) = Create();

        Assert.Equal(KeyReaction.PassThrough, r.LeftAltDown(0));
        Assert.Equal(KeyReaction.PassThrough, r.LeftAltUp(100));
        Assert.Equal(KeyReaction.PassThrough, r.LeftShiftDown(120));
        Assert.Equal(KeyReaction.PassThrough, r.LeftShiftUp(200));

        Assert.Empty(events);
    }

    [Fact]
    public void RightAlt_IsNeverTreatedAsCombo()
    {
        var (r, events) = Create();

        // AltGr path: Right Alt held with Left Shift — must stay inert (and pass through).
        Assert.Equal(KeyReaction.PassThrough, r.OtherKeyDown()); // RAlt down
        Assert.Equal(KeyReaction.PassThrough, r.LeftShiftDown(0));
        Assert.Equal(KeyReaction.PassThrough, r.OtherKeyUp());   // RAlt up
        Assert.Equal(KeyReaction.PassThrough, r.LeftShiftUp(50));

        Assert.Empty(events);
        Assert.False(r.IsEngaged);
    }

    [Fact]
    public void RepeatKeyDown_WhileEngaged_DoesNotReengage()
    {
        var (r, events) = Create();

        r.LeftAltDown(0);
        r.LeftShiftDown(10);   // engage
        var suppressed = new List<KeyReaction>
        {
            r.LeftShiftDown(25), // key repeat while held
            r.LeftAltDown(30),
        };

        // No extra engage events; repeats are suppressed, not re-triggered.
        Assert.Single(events, e => e.name == "engaged");
        Assert.All(suppressed, s => Assert.Equal(KeyReaction.Suppress, s));

        Assert.Equal(KeyReaction.Suppress, r.LeftShiftUp(100));
        Assert.Equal(KeyReaction.PassThrough, r.LeftAltUp(120));
    }

    [Fact]
    public void Holding_Produces_ExactlyOneFullCycle()
    {
        var (r, events) = Create();

        // Hold LAlt+LShift for two "seconds" (simulated), one key down per modifier.
        r.LeftAltDown(0);
        r.LeftShiftDown(10);   // engage (one toggle source)
        r.LeftShiftUp(2010);
        r.LeftAltUp(2020);

        Assert.Single(events, e => e.name == "engaged");
        Assert.Single(events, e => e.name == "released");
        Assert.Equal(2000, events.Single(e => e.name == "released").duration);
    }

    [Fact]
    public void Release_ThenNewPress_EngagesAgain()
    {
        var (r, events) = Create();

        r.LeftAltDown(0);
        r.LeftShiftDown(10);
        r.LeftShiftUp(20);
        r.LeftAltUp(30);        // fully released → waiting reset

        r.LeftAltDown(100);
        Assert.Equal(KeyReaction.Suppress, r.LeftShiftDown(110)); // re-engages

        Assert.Equal(2, events.Count(e => e.name == "engaged"));
    }

    [Fact]
    public void Reset_ClearsPendingState()
    {
        var (r, events) = Create();

        r.LeftAltDown(0);
        r.LeftShiftDown(10);
        r.Reset();

        Assert.False(r.IsEngaged);
        Assert.Equal(KeyReaction.PassThrough, r.LeftAltUp(100)); // stale up after reset
        Assert.Single(events);                                  // only the "engaged" from before
        Assert.Equal("engaged", events.Single().name);
    }

    [Fact]
    public void OtherKeys_AlwaysPassThrough()
    {
        var (r, events) = Create();
        Assert.Equal(KeyReaction.PassThrough, r.OtherKeyDown());
        Assert.Equal(KeyReaction.PassThrough, r.OtherKeyUp());
        Assert.Empty(events);
    }

    // ------------------------------------------------ cycle mode (long-press popup)

    [Fact]
    public void CycleMode_ShiftTap_RaisesCycleStep_NotComboReleased()
    {
        var (r, events) = Create();
        var steps = 0;
        r.CycleStep += () => steps++;

        r.LeftAltDown(0);
        r.LeftShiftDown(10);     // engage
        r.EnterCycleMode();      // popup shown with both keys still held

        // Shift tap: down+up must move the selection, not release the combo.
        Assert.Equal(KeyReaction.Suppress, r.LeftShiftUp(800));
        Assert.Equal(1, events.Count(e => e.name == "engaged"));
        Assert.DoesNotContain(events, e => e.name == "released");
        Assert.True(r.IsEngaged);

        Assert.Equal(KeyReaction.Suppress, r.LeftShiftDown(900));
        Assert.Equal(KeyReaction.Suppress, r.LeftShiftUp(920));
        Assert.Equal(1, steps);

        Assert.Equal(KeyReaction.Suppress, r.LeftShiftDown(930));
        Assert.Equal(KeyReaction.Suppress, r.LeftShiftUp(940));
        Assert.Equal(2, steps);
        Assert.True(r.IsEngaged);
    }

    [Fact]
    public void CycleMode_AltUp_CommitsAndReleases()
    {
        var (r, events) = Create();

        r.LeftAltDown(0);
        r.LeftShiftDown(10);
        r.EnterCycleMode();
        r.LeftShiftUp(800);
        r.LeftShiftDown(900);    // cycled selection
        r.LeftShiftUp(920);

        // Only Alt-up ends the press.
        Assert.Equal(KeyReaction.PassThrough, r.LeftAltUp(1000));
        Assert.False(r.IsEngaged);
        Assert.Single(events, e => e.name == "released");
        Assert.Equal(1000 - 10, events.Single(e => e.name == "released").duration);

        // The combo was already fully released, so a duplicate stale event is inert.
        Assert.Equal(KeyReaction.PassThrough, r.LeftShiftUp(1010));
    }

    [Fact]
    public void CycleMode_WhenShiftWasPressedFirst_BalancesItBeforeCycling()
    {
        var (r, _) = Create();
        var steps = 0;
        r.CycleStep += () => steps++;

        // Shift reaches the foreground app before Alt completes the combo.
        Assert.Equal(KeyReaction.PassThrough, r.LeftShiftDown(0));
        Assert.Equal(KeyReaction.Suppress, r.LeftAltDown(10));
        r.EnterCycleMode();

        // Its matching up must also reach the app. Fresh cycle taps do not.
        Assert.Equal(KeyReaction.PassThrough, r.LeftShiftUp(800));
        Assert.Equal(KeyReaction.Suppress, r.LeftShiftDown(900));
        Assert.Equal(KeyReaction.Suppress, r.LeftShiftUp(920));
        Assert.Equal(1, steps);

        Assert.Equal(KeyReaction.Suppress, r.LeftAltUp(1000));
    }

    [Fact]
    public void CycleMode_AfterConfirmingWithAltUp_AFreshComboCanOpenAgain()
    {
        var (r, events) = Create();

        r.LeftAltDown(0);
        r.LeftShiftDown(10);
        r.EnterCycleMode();
        r.LeftShiftUp(800);
        r.LeftAltUp(1000); // Last physical modifier up: confirms the popup.

        Assert.Equal(KeyReaction.PassThrough, r.LeftAltDown(1100));
        Assert.Equal(KeyReaction.Suppress, r.LeftShiftDown(1110));
        Assert.True(r.IsEngaged);
        Assert.Equal(2, events.Count(e => e.name == "engaged"));
    }

    [Fact]
    public void ExitCycleMode_RestoresShortPressBehaviour()
    {
        var (r, events) = Create();

        r.LeftAltDown(0);
        r.LeftShiftDown(10);
        r.EnterCycleMode();
        r.ExitCycleMode();       // popup closed (ESC / click-away)

        // Shift up now ends the press like a normal short press.
        Assert.Equal(KeyReaction.Suppress, r.LeftShiftUp(300));
        Assert.Single(events, e => e.name == "released");
        Assert.Equal(300 - 10, events.Single(e => e.name == "released").duration);
    }

    [Fact]
    public void CycleMode_HoldShift_KeyRepeat_DoesNotCycle()
    {
        var (r, _) = Create();
        var steps = 0;
        r.CycleStep += () => steps++;

        r.LeftAltDown(0);
        r.LeftShiftDown(10);              // engage
        r.EnterCycleMode();
        r.LeftShiftUp(800);

        // User holds Shift: OS auto-repeat sends repeated key-downs -> must NOT cycle.
        r.LeftShiftDown(820, isRepeat: true);
        r.LeftShiftDown(850, isRepeat: true);
        r.LeftShiftDown(870, isRepeat: true);
        r.LeftShiftUp(900);
        Assert.Equal(0, steps);

        // A fresh tap still advances exactly one step.
        r.LeftShiftDown(920);
        r.LeftShiftUp(940);
        Assert.Equal(1, steps);
    }

    [Fact]
    public void CycleMode_HoldShift_WithoutKeyUp_StillDoesNotCycle()
    {
        var (r, _) = Create();
        var steps = 0;
        r.CycleStep += () => steps++;

        r.LeftAltDown(0);
        r.LeftShiftDown(10);              // engage, Shift physically held
        r.EnterCycleMode();               // popup opens while Shift stays down

        // No key-up in between: further Shift key-downs are OS auto-repeat of the
        // held key. The already-down state is sufficient to identify repeats.
        r.LeftShiftDown(820);
        r.LeftShiftDown(850);
        r.LeftShiftDown(870);
        Assert.Equal(0, steps);

        // Release + fresh press -> exactly one step.
        r.LeftShiftUp(900);
        r.LeftShiftDown(920);
        r.LeftShiftUp(940);
        Assert.Equal(1, steps);
    }

    [Fact]
    public void Reset_ClearsCycleMode()
    {
        var (r, _) = Create();

        r.LeftAltDown(0);
        r.LeftShiftDown(10);
        r.EnterCycleMode();
        r.Reset();

        Assert.False(r.IsEngaged);
        // A fresh press after reset must work as a normal short press.
        r.LeftAltDown(100);
        Assert.Equal(KeyReaction.Suppress, r.LeftShiftDown(110));
        Assert.Equal(KeyReaction.PassThrough, r.LeftAltUp(200));
    }
}
