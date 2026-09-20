using SmartLayoutSwitcher.Core;

namespace SmartLayoutSwitcher.Core.Tests;

public class LayoutHistoryTests
{
    private static readonly LayoutId EN = new("00000409");
    private static readonly LayoutId RU = new("00000419");
    private static readonly LayoutId CZ = new("00000405");

    [Fact]
    public void Initialize_SetsPairSeed()
    {
        var h = new LayoutHistory();
        h.Initialize(EN);

        Assert.Equal(EN, h.Current);
        Assert.Equal(EN, h.Primary);
        Assert.False(h.HasPair);
        Assert.False(h.TryGetToggleTarget(out _));
    }

    [Fact]
    public void TwoMostRecent_FirstUserChoice_FormsPair()
    {
        var h = new LayoutHistory();
        h.Initialize(EN); // "Start EN"

        Assert.True(h.ApplyUserSelection(RU)); // "User chooses RU"
        Assert.True(h.HasPair);                // "Pair = EN/RU"

        h.ApplyInternalToggle();               // "Toggle"
        Assert.Equal(EN, h.Current);           // "Current = EN"

        h.ApplyInternalToggle();               // "Toggle"
        Assert.Equal(RU, h.Current);           // "Current = RU"
    }

    [Fact]
    public void ThirdLanguage_ReformsPair_FromPreviousActual()
    {
        var h = new LayoutHistory();
        h.Initialize(EN);
        h.ApplyUserSelection(RU);              // pair = { EN, RU }
        Assert.Equal(RU, h.Current);

        Assert.True(h.ApplyUserSelection(CZ)); // "User chooses CZ"
        Assert.Equal(CZ, h.Current);           // pair = { RU, CZ }

        h.ApplyInternalToggle();
        Assert.Equal(RU, h.Current);           // Toggle -> RU

        h.ApplyInternalToggle();
        Assert.Equal(CZ, h.Current);           // Toggle -> CZ
    }

    [Fact]
    public void PopupSelection_ReplacesActiveMemberAndPreservesOtherMember()
    {
        var h = new LayoutHistory();
        h.Initialize(RU);
        h.ApplyUserSelection(EN);              // pair = { RU, EN }, EN active

        Assert.True(h.ApplyPopupSelection(CZ));
        Assert.Equal(RU, h.Primary);           // preserve the non-active member
        Assert.Equal(CZ, h.Secondary);
        Assert.Equal(CZ, h.Current);

        h.ApplyInternalToggle();
        Assert.Equal(RU, h.Current);
    }

    [Fact]
    public void PopupSelection_WhenPrimaryIsActive_PreservesSecondaryMember()
    {
        var h = new LayoutHistory();
        h.Initialize(RU);
        h.ApplyUserSelection(EN);
        h.ApplyInternalToggle();               // RU active

        Assert.True(h.ApplyPopupSelection(CZ));
        Assert.Equal(EN, h.Primary);
        Assert.Equal(CZ, h.Secondary);
        Assert.Equal(CZ, h.Current);
    }

    [Fact]
    public void ApplyingSameLayoutAgain_DoesNotChangeHistory()
    {
        var h = new LayoutHistory();
        h.Initialize(EN);
        h.ApplyUserSelection(RU);
        var primaryBefore = h.Primary;
        var secondaryBefore = h.Secondary;

        // Windows reports EN again while current is RU — must not reorder the pair.
        Assert.False(h.ApplyUserSelection(EN));
        Assert.Equal(EN, h.Current);
        Assert.Equal(primaryBefore, h.Primary);
        Assert.Equal(secondaryBefore, h.Secondary);
    }

    [Fact]
    public void InternalToggles_NeverReorderThePair()
    {
        var h = new LayoutHistory();
        h.Initialize(EN);
        h.ApplyUserSelection(RU);

        for (var i = 0; i < 20; i++)
        {
            h.ApplyInternalToggle();
            Assert.Equal(new[] { EN, RU }, new[] { h.Primary, h.Secondary });
        }
    }

    [Fact]
    public void ManualSwitchWithinPair_KeepsPairComposition()
    {
        var h = new LayoutHistory();
        h.Initialize(EN);
        h.ApplyUserSelection(RU);

        Assert.False(h.ApplyUserSelection(EN));
        Assert.Equal(EN, h.Current);
        Assert.Equal(EN, h.Primary);
        Assert.Equal(RU, h.Secondary);
    }

    [Fact]
    public void EmptySelection_IsIgnored()
    {
        var h = new LayoutHistory();
        h.Initialize(EN);
        Assert.False(h.ApplyUserSelection(LayoutId.Empty));
        Assert.Equal(EN, h.Current);
    }

    [Fact]
    public void RestorePair_RejectsInvalidInput()
    {
        var h = new LayoutHistory();
        Assert.Throws<ArgumentException>(() => h.RestorePair(EN, LayoutId.Empty));
        Assert.Throws<ArgumentException>(() => h.RestorePair(EN, EN));
    }

    [Fact]
    public void RestorePair_SetsState()
    {
        var h = new LayoutHistory();
        h.RestorePair(RU, EN);

        Assert.True(h.HasPair);
        Assert.Equal(RU, h.Current);

        h.ApplyInternalToggle();
        Assert.Equal(EN, h.Current);
    }

    [Fact]
    public void RestorePair_RetainsAnActiveLayoutOutsideThePair()
    {
        var h = new LayoutHistory();
        h.RestorePair(EN, RU, CZ);

        Assert.True(h.HasPair);
        Assert.Equal(CZ, h.Current);
        Assert.True(h.TryGetToggleTarget(out var target));
        Assert.Equal(EN, target);

        h.ApplyInternalToggle();
        Assert.Equal(EN, h.Current);
        Assert.Equal(EN, h.Primary);
        Assert.Equal(RU, h.Secondary);
    }
}
