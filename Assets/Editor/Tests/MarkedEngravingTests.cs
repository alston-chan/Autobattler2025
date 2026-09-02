using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The Marked engraving's numbers, and the three places the item has to be listed for a player to
/// ever meet it: the item collection (so it exists), the resonance database (so wearing it does
/// something), and a reward pool (so it drops). Any one missing fails silently — the gloves would
/// simply be gloves.
/// </summary>
public class MarkedEngravingTests
{
    private const string ItemId = "FantasyHeroes.Basic.Armor.BanditArmor.gloves";

    private static MarkedEngraving Fresh()
    {
        var engraving = ScriptableObject.CreateInstance<MarkedEngraving>();
        engraving.startingCut = 0.2f;
        engraving.extraCutPerTier = 0.1f;
        return engraving;
    }

    [Test]
    public void TierOneStartsTheTargetAtEightyPercent()
    {
        var engraving = Fresh();
        Assert.That(engraving.CutFor(1), Is.EqualTo(0.2f).Within(0.0001f));
        Assert.That(engraving.CutFor(2), Is.EqualTo(0.3f).Within(0.0001f));
        Assert.That(engraving.CutFor(3), Is.EqualTo(0.4f).Within(0.0001f));
    }

    [Test]
    public void ATierBelowOneIsTreatedAsOne()
    {
        // Tier 0 is what an unattuned worn item reports in some paths; it must still be the base cut.
        Assert.That(Fresh().CutFor(0), Is.EqualTo(0.2f).Within(0.0001f));
    }

    [Test]
    public void TheDescriptionQuotesTheNumberThePlayerWillSee()
    {
        var engraving = Fresh();
        Assert.That(engraving.DescribeTier(1), Does.Contain("80%"));
        Assert.That(engraving.DescribeTier(2), Does.Contain("70%"));
        Assert.That(engraving.DescribeTier(3), Does.Contain("60%"));
    }

    [Test]
    public void TheGlovesExistAsAnItem()
    {
        var collection = AssetDatabase.LoadAssetAtPath<Assets.HeroEditor.InventorySystem.Scripts.ItemCollection>(
            "Assets/Data/ItemCollection.asset");
        Assert.That(collection, Is.Not.Null);
        Assert.That(collection.Items.Exists(i => i.Id == ItemId), Is.True, ItemId + " is not in the item collection");
    }

    [Test]
    public void WearingTheGlovesMeansMarked()
    {
        var database = AssetDatabase.LoadAssetAtPath<ResonanceDatabase>("Assets/Resources/ResonanceDatabase.asset");
        Assert.That(database, Is.Not.Null);

        var entry = database.entries.Find(e => e.itemId == ItemId);
        Assert.That(entry, Is.Not.Null, "the gloves have no resonance entry — wearing them would do nothing");
        Assert.That(entry.engraving, Is.InstanceOf<MarkedEngraving>());
        Assert.That(entry.engraving.DisplayName, Is.EqualTo("Marked"));
    }

    [Test]
    public void TheGlovesCanDrop()
    {
        var standard = AssetDatabase.LoadAssetAtPath<RewardPool>("Assets/Data/Run/StandardRewards.asset");
        var elite = AssetDatabase.LoadAssetAtPath<RewardPool>("Assets/Data/Run/Act1/EliteRewards.asset");
        Assert.That(standard.itemIds, Has.Member(ItemId));
        Assert.That(elite.itemIds, Has.Member(ItemId));
    }
}
