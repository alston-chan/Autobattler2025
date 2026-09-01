using System.Linq;
using Assets.HeroEditor.InventorySystem.Scripts;
using Assets.HeroEditor.InventorySystem.Scripts.Data;
using Assets.HeroEditor.InventorySystem.Scripts.Enums;
using NUnit.Framework;
using UnityEditor;

/// <summary>
/// The equipment compatibility rules, pinned against the real catalogue.
///
/// These exist because this exact rule has been broken three separate times: fixed in the random
/// loadout roll, broken again by signature items, broken again when paired blades arrived. Each
/// break was invisible until someone noticed a hero holding a greatsword behind a shield. Verifying
/// it by hand each time is what these replace.
/// </summary>
public class LoadoutRuleTests
{
    private const string CataloguePath = "Assets/Data/ItemCollection.asset";

    [OneTimeSetUp]
    public void LoadCatalogue()
    {
        // The game assigns this from an inspector field on the inventory prefab; a test has to say
        // it out loud, or every Item built here comes back with null Params.
        if (ItemCollection.Active == null)
            ItemCollection.Active = AssetDatabase.LoadAssetAtPath<ItemCollection>(CataloguePath);

        Assert.That(ItemCollection.Active, Is.Not.Null, $"No item catalogue at {CataloguePath}");
    }

    /// <summary>First item of a class, optionally restricted to one grip.</summary>
    private static Item ItemOf(ItemClass itemClass, bool? twoHanded = null)
    {
        var match = ItemCollection.Active.Items.FirstOrDefault(p =>
            p != null && p.Class == itemClass &&
            (twoHanded == null || p.Tags.Contains(ItemTag.TwoHanded) == twoHanded.Value));

        Assert.That(match, Is.Not.Null,
            $"The catalogue has no {itemClass} with twoHanded={twoHanded} to test against");
        return new Item(match.Id);
    }

    [Test]
    public void TwoHandedWeaponAndShieldCannotBeWornTogether()
    {
        var greatsword = ItemOf(ItemClass.Sword, twoHanded: true);
        var shield = ItemOf(ItemClass.Light) ?? null;   // shields are their own type, fetched below

        shield = ItemCollection.Active.Items
            .Where(p => p != null && p.Type == ItemType.Shield)
            .Select(p => new Item(p.Id)).FirstOrDefault();
        Assert.That(shield, Is.Not.Null, "The catalogue has no shield to test against");

        Assert.That(Loadout.Conflicts(greatsword, shield), Is.True, "a two-hander must displace a shield");
        Assert.That(Loadout.Conflicts(shield, greatsword), Is.True, "a shield must displace a two-hander");
    }

    [Test]
    public void PairedBladesAndShieldCannotBeWornTogether()
    {
        var daggers = ItemOf(ItemClass.Dagger, twoHanded: false);
        var shield = ItemCollection.Active.Items
            .Where(p => p != null && p.Type == ItemType.Shield)
            .Select(p => new Item(p.Id)).First();

        // Daggers carry no TwoHanded tag, so this only holds because DualWield.IsPaired says a pair
        // fills both hands. It is the case that came back after the rule was "fixed".
        Assert.That(Loadout.Conflicts(daggers, shield), Is.True, "paired blades must displace a shield");
        Assert.That(Loadout.Conflicts(shield, daggers), Is.True, "a shield must displace paired blades");
    }

    [Test]
    public void OneHandedWeaponAndShieldCoexist()
    {
        var sword = ItemOf(ItemClass.Sword, twoHanded: false);
        var wand = ItemOf(ItemClass.Wand, twoHanded: false);
        var shield = ItemCollection.Active.Items
            .Where(p => p != null && p.Type == ItemType.Shield)
            .Select(p => new Item(p.Id)).First();

        Assert.That(Loadout.Conflicts(sword, shield), Is.False, "a one-hander leaves a hand for a shield");
        Assert.That(Loadout.Conflicts(shield, sword), Is.False);
        Assert.That(Loadout.Conflicts(wand, shield), Is.False, "a wand is held in one hand");
        Assert.That(Loadout.Conflicts(shield, wand), Is.False);
    }

    [Test]
    public void BowDisplacesAShield()
    {
        var bow = ItemOf(ItemClass.Bow);
        var shield = ItemCollection.Active.Items
            .Where(p => p != null && p.Type == ItemType.Shield)
            .Select(p => new Item(p.Id)).First();

        Assert.That(Loadout.Conflicts(bow, shield), Is.True, "a bow is drawn with both hands");
    }

    [Test]
    public void NothingConflictsWithItselfOrWithNothing()
    {
        var sword = ItemOf(ItemClass.Sword, twoHanded: false);

        Assert.That(Loadout.Conflicts(sword, sword), Is.False, "an item cannot displace itself");
        Assert.That(Loadout.Conflicts(null, sword), Is.False);
        Assert.That(Loadout.Conflicts(sword, null), Is.False);
    }

    [Test]
    public void NormaliseRemovesTheConflictAndReportsIt()
    {
        var greatsword = ItemOf(ItemClass.Sword, twoHanded: true);
        var shield = ItemCollection.Active.Items
            .Where(p => p != null && p.Type == ItemType.Shield)
            .Select(p => new Item(p.Id)).First();

        var worn = new System.Collections.Generic.List<Item> { shield, greatsword };
        var removed = Loadout.Normalise(worn, greatsword);

        Assert.That(worn, Has.Member(greatsword), "the item being equipped must survive");
        Assert.That(worn, Has.No.Member(shield), "the conflicting shield must come off");
        Assert.That(removed, Has.Member(shield), "and the caller must be told what came off");
    }
}
