using System.Collections.Generic;
using Assets.HeroEditor.InventorySystem.Scripts;
using NUnit.Framework;
using UnityEditor;

/// <summary>
/// Mass: the curve, and that the shipped kits actually spread across it.
///
/// The second half is the one that matters. Mass is authored in the Weight column of Items.csv and
/// baked into ItemCollection by an importer that <b>replaces</b> the list, so a re-import from a CSV
/// that lost the column would leave every unit at exactly the same mass — the physics quietly back
/// to what it was, with nothing failing to compile and nothing on screen to say so.
/// </summary>
public class BodyMassTests
{
    [OneTimeSetUp]
    public void LoadItems()
    {
        ItemCollection.Active = AssetDatabase.LoadAssetAtPath<ItemCollection>("Assets/Data/ItemCollection.asset");
        Assert.That(ItemCollection.Active, Is.Not.Null);
    }

    private static int WeightOf(string itemId)
    {
        var p = ItemCollection.Active.Items.Find(i => i.Id == itemId);
        Assert.That(p, Is.Not.Null, itemId + " is not an item");
        return p.Weight;
    }

    private static float MassOfKit(params string[] itemIds)
    {
        int weight = 0;
        foreach (var id in itemIds) weight += WeightOf(id);
        return BodyMass.From(0.75f, weight, 0.8f, 1.35f);
    }

    // ---------- the curve ----------

    [Test]
    public void ABareBodyIsHeldAtTheFloorAndAFullSuitAtTheCeiling()
    {
        Assert.That(BodyMass.From(0.75f, 0f, 0.8f, 1.35f), Is.EqualTo(0.8f).Within(0.001f), "nothing worn");
        Assert.That(BodyMass.From(0.75f, 200f, 0.8f, 1.35f), Is.EqualTo(1.35f).Within(0.001f), "an absurd load");
    }

    [Test]
    public void TheMedianUnitWeighsExactlyOne()
    {
        // Twenty-five points is a middling vest, boots and helm. It has to land on 1, or every force
        // number in every verb — all of them tuned before mass existed — quietly changes meaning.
        Assert.That(BodyMass.From(0.75f, 25f, 0.8f, 1.35f), Is.EqualTo(1f).Within(0.001f));
    }

    [Test]
    public void TheWordMatchesTheNumber()
    {
        Assert.That(BodyMass.Word(0.86f), Is.EqualTo("Light"));
        Assert.That(BodyMass.Word(1f), Is.EqualTo("Medium"));
        Assert.That(BodyMass.Word(1.35f), Is.EqualTo("Heavy"));
    }

    [Test]
    public void AHeavyBodyPassesOnMoreThanALightOneButNotWithoutLimit()
    {
        Assert.That(BodyMass.TransferRatio(1.35f, 0.86f), Is.GreaterThan(1f), "an anchor bowls a mage over");
        Assert.That(BodyMass.TransferRatio(0.86f, 1.35f), Is.LessThan(1f), "a mage bounces off an anchor");
        Assert.That(BodyMass.TransferRatio(10f, 0.1f), Is.EqualTo(2f), "and never becomes a catapult");
        Assert.That(BodyMass.TransferRatio(0.1f, 10f), Is.EqualTo(0.5f));
    }

    // ---------- the content actually spreads ----------

    [Test]
    public void TheShippedKitsRunFromLightToHeavy()
    {
        var kits = new Dictionary<string, float>
        {
            ["Wall Keeper"] = MassOfKit("Extensions.AbandonedWorkshop.Armor.WallKeeperArmor.vest",
                                        "Extensions.AbandonedWorkshop.Armor.WallKeeperArmor.boots",
                                        "Extensions.AbandonedWorkshop.Helmet.WallKeeperHelm",
                                        "Extensions.AbandonedWorkshop.Shield.WallKeeperShield"),
            ["Rain Archer"] = MassOfKit("FantasyHeroes.Basic.Armor.SiegeArcherArmor.vest",
                                        "FantasyHeroes.Basic.Armor.SiegeArcherArmor.boots",
                                        "Extensions.AbandonedWorkshop.Helmet.ElegantArcherHood"),
        };

        Assert.That(BodyMass.Word(kits["Wall Keeper"]), Is.EqualTo("Heavy"));
        Assert.That(BodyMass.Word(kits["Rain Archer"]), Is.EqualTo("Light"));

        // The whole point is the ratio: the same throw has to move one visibly further than the other.
        float spread = kits["Wall Keeper"] / kits["Rain Archer"];
        Assert.That(spread, Is.GreaterThan(1.35f), "the spread is too narrow to see");
        Assert.That(spread, Is.LessThan(2f), "the spread is wide enough to delete the physics layer");
    }

    [Test]
    public void ArmourCarriesWeightAndWeaponsDoNot()
    {
        Assert.That(WeightOf("Extensions.AbandonedWorkshop.Shield.WallKeeperShield"), Is.GreaterThan(0));
        Assert.That(WeightOf("Extensions.AbandonedWorkshop.Armor.WallKeeperArmor.vest"), Is.GreaterThan(0));

        // A weapon is swung, not worn: its weight would make every damage dealer an anchor.
        Assert.That(WeightOf("FantasyHeroes.Basic.MeleeWeapon1H.WarHammer"), Is.EqualTo(0));
        Assert.That(WeightOf("FantasyHeroes.Basic.Bow.RangerBow"), Is.EqualTo(0));
    }
}
