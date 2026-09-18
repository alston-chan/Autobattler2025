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

    private static TacticsEngraving Item(Stance stance = Stance.Auto, TargetMode? target = null, Commitment? commitment = null)
    {
        var e = ScriptableObject.CreateInstance<TacticsEngraving>();
        e.stance = stance;
        e.setsTarget = target.HasValue; if (target.HasValue) e.targetMode = target.Value;
        e.setsCommitment = commitment.HasValue; if (commitment.HasValue) e.commitment = commitment.Value;
        return e;
    }

    [Test]
    public void AUnitFightsAsAuthoredUntilAnItemSaysOtherwise()
    {
        var unit = Fresh();
        Assert.That(unit.EffectiveStance, Is.EqualTo(Stance.Advance), "Auto on a melee unit is Advance");
        Assert.That(unit.EffectiveTarget, Is.EqualTo(TargetMode.Nearest));
        Assert.That(unit.EffectiveCommitment, Is.EqualTo(Commitment.Balanced));
        Assert.That(unit.TacticsSource, Is.Null);
    }

    [Test]
    public void AWornTacticsItemHasTheLastWordAndComingOffGivesItBack()
    {
        var unit = Fresh();
        unit.commitment = Commitment.Opportunistic;   // what a kit authored
        var helm = Item(Stance.Hold, TargetMode.LowestHealth, Commitment.Relentless);

        helm.OnGranted(unit, 1);
        Assert.That(unit.EffectiveStance, Is.EqualTo(Stance.Hold));
        Assert.That(unit.EffectiveTarget, Is.EqualTo(TargetMode.LowestHealth));
        Assert.That(unit.EffectiveCommitment, Is.EqualTo(Commitment.Relentless));
        Assert.That(unit.TacticsSource, Is.SameAs(helm));

        helm.OnRevoked(unit, 1);
        Assert.That(unit.EffectiveStance, Is.EqualTo(Stance.Advance));
        Assert.That(unit.EffectiveTarget, Is.EqualTo(TargetMode.Nearest));
        Assert.That(unit.EffectiveCommitment, Is.EqualTo(Commitment.Opportunistic), "back to what was authored, not to the default");
        Assert.That(unit.TacticsSource, Is.Null);
    }

    [Test]
    public void AnItemThatOnlySetsTheStanceLeavesTheRestAlone()
    {
        var unit = Fresh();
        unit.targetMode = TargetMode.Furthest;
        Item(Stance.Dive).OnGranted(unit, 1);
        Assert.That(unit.EffectiveStance, Is.EqualTo(Stance.Dive));
        Assert.That(unit.EffectiveTarget, Is.EqualTo(TargetMode.Furthest));
        Assert.That(unit.EffectiveCommitment, Is.EqualTo(Commitment.Balanced));
    }

    [Test]
    public void TheLastItemWornWinsAndLosingItFallsBackToTheOther()
    {
        var unit = Fresh();
        var first = Item(Stance.Hold, commitment: Commitment.Relentless);
        var second = Item(Stance.Kite);
        first.OnGranted(unit, 1);
        second.OnGranted(unit, 1);
        Assert.That(unit.EffectiveStance, Is.EqualTo(Stance.Kite));
        Assert.That(unit.EffectiveCommitment, Is.EqualTo(Commitment.Balanced), "the second item says nothing about commitment, so it is the authored one");

        second.OnRevoked(unit, 1);
        Assert.That(unit.EffectiveStance, Is.EqualTo(Stance.Hold));
        Assert.That(unit.EffectiveCommitment, Is.EqualTo(Commitment.Relentless));
    }

    [Test]
    public void TheDescriptionSaysWhatChanges()
    {
        Assert.That(Item(Stance.Hold, TargetMode.LowestHealth, Commitment.Relentless).DescribeTier(1),
                    Is.EqualTo("Holds · goes for the weakest · never lets go"));
        Assert.That(Item(Stance.Kite).DescribeTier(1), Is.EqualTo("Kites"));
        Assert.That(Item().DescribeTier(1), Is.EqualTo("Changes nothing"));
    }

    [Test]
    public void TheCardLineReadsTheTacticsInForce()
    {
        var unit = Fresh();
        Item(Stance.Dive, TargetMode.Attacker, Commitment.Opportunistic).OnGranted(unit, 1);
        Assert.That(Tactics.Line(unit), Is.EqualTo("Dives · the farthest · opportunist"), "a diver goes for the farthest whatever its target rule says, as the AI has it");
    }

    private static readonly string[] TacticsItems =
    {
        "FantasyHeroes.Basic.Helmet.BerserkHelm",
        "FantasyHeroes.Basic.Helmet.SpearmanHelm1",
        "FantasyHeroes.Knights.Helmet.EliteKnightHelm",
        "Extensions.AbandonedWorkshop.Helmet.ElegantArcherHood",
        "FantasyHeroes.Basic.Helmet.AssassinHood [Paint]",
        "FantasyHeroes.Basic.Armor.Cleric [Paint].vest",
    };

    [Test]
    public void HoldTheLineHoldsTheLine()
    {
        var unit = Fresh();
        var line = ScriptableObject.CreateInstance<HoldTheLineEngraving>();
        line.OnGranted(unit, 1);
        Assert.That(unit.EffectiveStance, Is.EqualTo(Stance.Hold));
        Assert.That(line.DescribeTier(1), Does.StartWith("Holds"));
        line.OnRevoked(unit, 1);
        Assert.That(unit.EffectiveStance, Is.EqualTo(Stance.Advance));
    }

    [Test]
    public void EveryEnemyKitGetsItsTacticsFromAnItem()
    {
        // A kit authors no stance of its own, so one of its items has to say how it fights, or it is
        // just a unit that advances. Every kit was written with a plan; this keeps the plan on an item.
        var database = AssetDatabase.LoadAssetAtPath<ResonanceDatabase>("Assets/Resources/ResonanceDatabase.asset");
        foreach (var guid in AssetDatabase.FindAssets("t:EnemyKit", new[] { "Assets/Data/EnemyKits" }))
        {
            var kit = AssetDatabase.LoadAssetAtPath<EnemyKit>(AssetDatabase.GUIDToAssetPath(guid));
            bool tactics = false;
            foreach (var id in kit.itemIds)
            {
                var entry = database.entries.Find(e => e.itemId == id);
                if (entry != null && (entry.engraving is TacticsEngraving || entry.engraving is HoldTheLineEngraving)) tactics = true;
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
