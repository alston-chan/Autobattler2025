using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// The words on the badges, and how several grants of one engraving on one unit read as one line.
/// The merged line is where the stacking rule becomes visible to the player — "BULWARK -12 ×2"
/// says the two add, "MARKED · 80% ×2" says they don't — so it is pinned per engraving.
/// </summary>
public class EngravingPreviewTests
{
    private static MarkedEngraving Marked()
    {
        var marked = ScriptableObject.CreateInstance<MarkedEngraving>();
        marked.startingCut = 0.2f;
        marked.extraCutPerTier = 0.1f;
        return marked;
    }

    private static BulwarkEngraving Bulwark()
    {
        var bulwark = ScriptableObject.CreateInstance<BulwarkEngraving>();
        bulwark.blockingPerTier = 6f;
        return bulwark;
    }

    private static VanguardEngraving Vanguard()
    {
        var vanguard = ScriptableObject.CreateInstance<VanguardEngraving>();
        vanguard.damageBonusPerTier = 0.2f;
        return vanguard;
    }

    [Test]
    public void BadgesQuoteTheRealNumberAtEachTier()
    {
        Assert.That(Marked().PreviewLabel(1), Is.EqualTo("MARKED · 80%"));
        Assert.That(Marked().PreviewLabel(3), Is.EqualTo("MARKED · 60%"));
        Assert.That(Vanguard().PreviewLabel(1), Is.EqualTo("VANGUARD +20%"));
        Assert.That(Bulwark().PreviewLabel(2), Is.EqualTo("BULWARK -12"));
    }

    [Test]
    public void OneGrantHasNoCount()
    {
        Assert.That(Bulwark().MergedLabel(new List<int> { 1 }), Is.EqualTo("BULWARK -6"));
        Assert.That(Marked().MergedLabel(new List<int> { 2 }), Is.EqualTo("MARKED · 70%"));
    }

    [Test]
    public void FlatBlockingAdds()
    {
        // An ally between a Tier I and a Tier II bearer gets both: 6 + 12.
        Assert.That(Bulwark().MergedLabel(new List<int> { 1, 2 }), Is.EqualTo("BULWARK -18 ×2"));
    }

    [Test]
    public void PercentDamageAdds()
    {
        Assert.That(Vanguard().MergedLabel(new List<int> { 1, 1 }), Is.EqualTo("VANGUARD +40% ×2"));
    }

    [Test]
    public void AMarkDoesNotStackTheStrongestWins()
    {
        // Two bearers across from one enemy set the same floor; the deeper cut is what applies.
        Assert.That(Marked().MergedLabel(new List<int> { 1, 3 }), Is.EqualTo("MARKED · 60% ×2"));
        Assert.That(Marked().MergedLabel(new List<int> { 1, 1 }), Is.EqualTo("MARKED · 80% ×2"));
    }

    [Test]
    public void AnEngravingWithNoPositionPreviewsNothing()
    {
        // Swift is a stat bonus, true wherever the hero stands: there is nothing to telegraph.
        var swift = ScriptableObject.CreateInstance<SwiftEngraving>();
        var into = new List<Engraving.Badge>();
        swift.Preview(null, 1, into);
        Assert.That(into, Is.Empty);
    }
}
