using NUnit.Framework;
using UnityEngine;

/// <summary>
/// The words on the setup badges. Each positional engraving quotes its real number at the tier it
/// is held, in the same form the player will read it over a unit — so a badge that stopped agreeing
/// with the effect would show up here before it showed up on the board.
/// </summary>
public class EngravingPreviewTests
{
    [Test]
    public void MarkedBadgeQuotesTheStartingHealth()
    {
        var marked = ScriptableObject.CreateInstance<MarkedEngraving>();
        marked.startingCut = 0.2f;
        marked.extraCutPerTier = 0.1f;

        Assert.That(marked.PreviewLabel(1), Is.EqualTo("MARKED · 80%"));
        Assert.That(marked.PreviewLabel(2), Is.EqualTo("MARKED · 70%"));
        Assert.That(marked.PreviewLabel(3), Is.EqualTo("MARKED · 60%"));
    }

    [Test]
    public void VanguardBadgeQuotesTheDamageBonus()
    {
        var vanguard = ScriptableObject.CreateInstance<VanguardEngraving>();
        vanguard.damageBonusPerTier = 0.2f;

        Assert.That(vanguard.PreviewLabel(1), Is.EqualTo("VANGUARD +20%"));
        Assert.That(vanguard.PreviewLabel(3), Is.EqualTo("VANGUARD +60%"));
    }

    [Test]
    public void BulwarkBadgeQuotesTheBlocking()
    {
        var bulwark = ScriptableObject.CreateInstance<BulwarkEngraving>();
        bulwark.blockingPerTier = 6f;

        Assert.That(bulwark.PreviewLabel(1), Is.EqualTo("BULWARK -6"));
        Assert.That(bulwark.PreviewLabel(2), Is.EqualTo("BULWARK -12"));
    }

    [Test]
    public void AnEngravingWithNoPositionPreviewsNothing()
    {
        // Swift is a stat bonus, true wherever the hero stands: there is nothing to telegraph.
        var swift = ScriptableObject.CreateInstance<SwiftEngraving>();
        var into = new System.Collections.Generic.List<Engraving.Badge>();
        swift.Preview(null, 1, into);
        Assert.That(into, Is.Empty);
    }
}
