using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tactics come from items: a hero fights as authored until a worn tactics engraving says otherwise,
/// the last item worn has the last word, and taking it off hands the choice back. Plus the three
/// places each tactics item has to be listed for a player to ever meet it.
/// </summary>
public class TacticsEngravingTests
{
    private readonly List<GameObject> _made = new List<GameObject>();

    [TearDown]
    public void TearDown()
    {
        foreach (var go in _made) if (go != null) Object.DestroyImmediate(go);
        _made.Clear();
    }

    private Entity Fresh()
    {
        var go = new GameObject("unit");
        _made.Add(go);
        return go.AddComponent<Entity>();
    }

    private static TacticsEngraving Item(TargetMode target)
    {
        var e = ScriptableObject.CreateInstance<TacticsEngraving>();
        e.targetMode = target;
        return e;
    }

    [Test]
    public void AUnitGoesForTheNearestUntilAnItemSaysOtherwise()
    {
        var unit = Fresh();
        Assert.That(unit.EffectiveTarget, Is.EqualTo(TargetMode.Nearest));
        Assert.That(unit.TacticsSource, Is.Null);
    }

    [Test]
    public void AWornTacticsItemHasTheLastWordAndComingOffGivesItBack()
    {
        var unit = Fresh();
        unit.targetMode = TargetMode.Attacker;   // what a kit authored
        var helm = Item(TargetMode.LowestHealth);

        helm.OnGranted(unit, 1);
        Assert.That(unit.EffectiveTarget, Is.EqualTo(TargetMode.LowestHealth));
        Assert.That(unit.TacticsSource, Is.SameAs(helm));

        helm.OnRevoked(unit, 1);
        Assert.That(unit.EffectiveTarget, Is.EqualTo(TargetMode.Attacker), "back to what was authored, not to the default");
        Assert.That(unit.TacticsSource, Is.Null);
    }

    [Test]
    public void TheLastItemWornWinsAndLosingItFallsBackToTheOther()
    {
        var unit = Fresh();
        var first = Item(TargetMode.Furthest);
        var second = Item(TargetMode.LowestHealth);
        first.OnGranted(unit, 1);
        second.OnGranted(unit, 1);
        Assert.That(unit.EffectiveTarget, Is.EqualTo(TargetMode.LowestHealth));
        second.OnRevoked(unit, 1);
        Assert.That(unit.EffectiveTarget, Is.EqualTo(TargetMode.Furthest));
    }

    [Test]
    public void TheDescriptionAndTheCardSayWhomItGoesFor()
    {
        Assert.That(Item(TargetMode.LowestHealth).DescribeTier(1), Is.EqualTo("Goes for the weakest"));
        var unit = Fresh();
        Item(TargetMode.Furthest).OnGranted(unit, 1);
        Assert.That(Tactics.Line(unit), Is.EqualTo("Goes for the farthest"));
    }

    [Test]
    public void TheDiversCrestGoesForTheFarthest()
    {
        // Dive was a stance until stances went; what it did was pick the farthest enemy, so the crest
        // says that now, and the asset must carry it or the diver is an ordinary unit.
        var crest = AssetDatabase.LoadAssetAtPath<TacticsEngraving>("Assets/Data/Engravings/Tactics_Diver.asset");
        Assert.That(crest.targetMode, Is.EqualTo(TargetMode.Furthest));
    }

    private static readonly string[] TacticsItems =
    {
        "FantasyHeroes.Basic.Helmet.BerserkHelm",
        "FantasyHeroes.Basic.Helmet.SpearmanHelm1",
        "Extensions.AbandonedWorkshop.Helmet.ElegantArcherHood",
        "FantasyHeroes.Basic.Helmet.AssassinHood [Paint]",
        "FantasyHeroes.Basic.Armor.Cleric [Paint].vest",
    };

    [Test]
    public void HoldTheLineIsCoverNotATactic()
    {
        // It carried the Hold stance so its wearer would not walk off and break it; Hold is gone and
        // the line covers whoever stands near, so the wearer fights like anyone else.
        var unit = Fresh();
        var line = ScriptableObject.CreateInstance<HoldTheLineEngraving>();
        line.OnGranted(unit, 1);
        Assert.That(unit.TacticsSource, Is.Null, "no tactics from a bodyguard");
        Assert.That(line.DescribeTier(2), Is.EqualTo("Allies within 2 of you take 10% less."));
        line.OnRevoked(unit, 1);
    }

    [Test]
    public void StandFastTauntsTheRowItFacesForLongerEachTier()
    {
        var stand = AssetDatabase.LoadAssetAtPath<StandFastEngraving>("Assets/Data/Engravings/StandFast.asset");
        Assert.That(stand, Is.Not.Null, "the Elite Knight Helm's engraving is missing");
        Assert.That(stand.taunted, Is.Not.Null);
        Assert.That(stand.taunted.tauntsToSource, Is.True, "Stand Fast's status must pull its victims onto the wearer");
        Assert.That(stand.DescribeTier(1), Is.EqualTo("At the bell, every enemy in your row is Taunted to you for 3 s."));
        Assert.That(stand.DescribeTier(3), Does.EndWith("for 5 s."));

        var database = AssetDatabase.LoadAssetAtPath<ResonanceDatabase>("Assets/Resources/ResonanceDatabase.asset");
        var entry = database.entries.Find(e => e.itemId == "FantasyHeroes.Knights.Helmet.EliteKnightHelm");
        Assert.That(entry != null ? entry.engraving : null, Is.SameAs(stand), "the Elite Knight Helm carries Stand Fast");
    }

    [Test]
    public void FacingIsTheWholeEnemyRowFrontFirst()
    {
        var board = new Board<string>(4, 3);
        board.Place("knight", true, 0, 1);
        board.Place("back", false, 3, 1);
        board.Place("front", false, 0, 1);
        board.Place("middle", false, 1, 1);
        board.Place("otherRow", false, 0, 2);
        board.Place("friend", true, 1, 1);
        Assert.That(board.Facing("knight"), Is.EqualTo(new[] { "front", "middle", "back" }));
        Assert.That(board.Facing("front"), Is.EqualTo(new[] { "knight", "friend" }), "and it reads the same from the other side");
    }

    [Test]
    public void EveryEnemyKitGetsItsTacticsFromAnItem()
    {
        // A kit authors no stance of its own, so one of its items has to say how it fights, or it is
        // just a unit that advances. Every kit was written with a plan; this keeps the plan on an item.
        // The Wall Keeper's plan is to guard whoever stands near it, which Hold the Line carries; the
        // Knight's is to take the row facing it onto itself, which Stand Fast does.
        var database = AssetDatabase.LoadAssetAtPath<ResonanceDatabase>("Assets/Resources/ResonanceDatabase.asset");
        foreach (var guid in AssetDatabase.FindAssets("t:EnemyKit", new[] { "Assets/Data/EnemyKits" }))
        {
            var kit = AssetDatabase.LoadAssetAtPath<EnemyKit>(AssetDatabase.GUIDToAssetPath(guid));
            bool tactics = false;
            foreach (var id in kit.itemIds)
            {
                var entry = database.entries.Find(e => e.itemId == id);
                if (entry != null && (entry.engraving is TacticsEngraving || entry.engraving is HoldTheLineEngraving || entry.engraving is StandFastEngraving)) tactics = true;
            }
            Assert.That(tactics, Is.True, kit.name + " wears nothing that says how it fights");
        }
    }

    [Test]
    public void AnAbilityThatCostsManaHasNoCooldown()
    {
        // Mana is the gate (Docs/Spells.md). A cost ability with a cooldown sat at full mana doing
        // nothing for a third of a fight, which reads as a unit that will not use its ability.
        foreach (var guid in AssetDatabase.FindAssets("t:Spell", new[] { "Assets/Data/Spells" }))
        {
            var spell = AssetDatabase.LoadAssetAtPath<Spell>(AssetDatabase.GUIDToAssetPath(guid));
            if (spell == null || spell.manaCost <= 0f) continue;
            Assert.That(spell.cooldown, Is.EqualTo(0f), spell.name + " costs mana and also has a cooldown");
        }
    }

    [Test]
    public void EveryTacticsItemIsListedWhereAPlayerCanMeetIt()
    {
        var collection = AssetDatabase.LoadAssetAtPath<Assets.HeroEditor.InventorySystem.Scripts.ItemCollection>("Assets/Data/ItemCollection.asset");
        var database = AssetDatabase.LoadAssetAtPath<ResonanceDatabase>("Assets/Resources/ResonanceDatabase.asset");
        var standard = AssetDatabase.LoadAssetAtPath<RewardPool>("Assets/Data/Run/StandardRewards.asset");
        Assert.That(collection, Is.Not.Null); Assert.That(database, Is.Not.Null); Assert.That(standard, Is.Not.Null);

        foreach (var id in TacticsItems)
        {
            Assert.That(collection.Items.Exists(i => i.Id == id), Is.True, id + " is not in the item collection");
            var entry = database.entries.Find(e => e.itemId == id);
            Assert.That(entry, Is.Not.Null, id + " has no resonance entry — wearing it would do nothing");
            Assert.That(entry.engraving, Is.InstanceOf<TacticsEngraving>(), id);
            Assert.That(entry.engraving.DescribeTier(1), Is.Not.EqualTo("Changes nothing"), id + " carries a blank tactic");
            Assert.That(standard.itemIds, Has.Member(id), id + " cannot drop");
        }
    }
}
