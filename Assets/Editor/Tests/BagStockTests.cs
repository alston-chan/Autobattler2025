using Assets.HeroEditor.InventorySystem.Scripts;
using NUnit.Framework;
using UnityEditor;

/// <summary>
/// The workshop bag holds every designed item, and only the sandbox gets it.
///
/// The bag used to be a hand-kept list — one bow and three spellbooks — so every item designed after
/// it was written was unreachable except by a lucky reward roll, and every run, progression runs
/// included, opened with the same free test stock. Both halves are pinned here: the list is derived
/// from the databases, and the run assets say which kind of bag they open with.
/// </summary>
public class BagStockTests
{
    private const string CataloguePath = "Assets/Data/ItemCollection.asset";
    private const string MarkedGloves = "FantasyHeroes.Basic.Armor.BanditArmor.gloves";

    [OneTimeSetUp]
    public void LoadCatalogue()
    {
        if (ItemCollection.Active == null)
            ItemCollection.Active = AssetDatabase.LoadAssetAtPath<ItemCollection>(CataloguePath);
        Assert.That(ItemCollection.Active, Is.Not.Null, $"No item catalogue at {CataloguePath}");
    }

    [Test]
    public void EveryDesignedItemIsARealItem()
    {
        var ids = BagStock.DesignedItemIds();
        Assert.That(ids, Is.Not.Empty);
        foreach (var id in ids)
            Assert.That(ItemCollection.Active.Items.Exists(i => i.Id == id), Is.True, id + " is not in the catalogue");
        Assert.That(ids, Is.Unique);
    }

    [Test]
    public void NothingDesignedIsLeftOut()
    {
        // The whole point: an item designed tomorrow appears in the workshop without anyone editing
        // a list. Every engraved item and every spellbook the databases know must be here.
        var ids = BagStock.DesignedItemIds();

        foreach (var entry in ResonanceDatabase.Active.entries)
            if (entry != null && entry.engraving != null)
                Assert.That(ids, Has.Member(entry.itemId), "engraved item missing from the workshop");

        foreach (var entry in SpellbookDatabase.Active.entries)
            if (entry != null && entry.spell != null)
                Assert.That(ids, Has.Member(entry.itemId), "spellbook missing from the workshop");
    }

    [Test]
    public void MarkedGlovesCanBeTestedAtAnyTime()
    {
        Assert.That(BagStock.DesignedItemIds(), Has.Member(MarkedGloves));

        var workshop = BagStock.For(StartingBag.Workshop);
        Assert.That(workshop.Exists(i => i.Id == MarkedGloves), Is.True, "the workshop bag has no Marked gloves");
    }

    [Test]
    public void TheWorkshopHoldsEveryDesignedItem()
    {
        var workshop = BagStock.For(StartingBag.Workshop);
        foreach (var id in BagStock.DesignedItemIds())
            Assert.That(workshop.Exists(i => i.Id == id), Is.True, id + " missing from the workshop bag");
    }

    [Test]
    public void ARunsBagOpensEmpty()
    {
        Assert.That(BagStock.For(StartingBag.Empty), Is.Empty);
    }

    [Test]
    public void OnlyTheSandboxGetsTheWorkshop()
    {
        var demo = AssetDatabase.LoadAssetAtPath<RunData>("Assets/Data/Run/DemoRun.asset");
        var act = AssetDatabase.LoadAssetAtPath<RunData>("Assets/Data/Run/Act1/Act1Run.asset");
        var gauntlet = AssetDatabase.LoadAssetAtPath<RunData>("Assets/Data/Run/Archetypes/ArchetypeGauntlet.asset");

        Assert.That(demo.bag, Is.EqualTo(StartingBag.Workshop), "the sandbox should open with everything");
        Assert.That(act.bag, Is.EqualTo(StartingBag.Empty), "a progression run must start poor");
        Assert.That(gauntlet.bag, Is.EqualTo(StartingBag.Empty), "the gauntlet measures a starting-kit company");
    }
}
