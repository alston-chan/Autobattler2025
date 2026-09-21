using System.Collections.Generic;
using Assets.HeroEditor.InventorySystem.Scripts;
using Assets.HeroEditor.InventorySystem.Scripts.Data;
using Assets.HeroEditor.InventorySystem.Scripts.Enums;
using NUnit.Framework;
using UnityEditor;

/// <summary>
/// The default ladder — the five fights a fresh run plays (Encounter1..5) — as content.
///
/// Every spawn there wears an authored kit, because a randomly dressed enemy is a crowd with
/// weapons: it rolls a verb nobody chose, stands wherever, and fights the same way as the one
/// beside it. A kit is a fighter with a plan — the weapon teaches the verb, the head says how to
/// use it — so these tests hold the two things that make the ladder readable: every enemy is
/// authored, and the first fight puts all four space verbs on the field where they can be watched.
///
/// The failure they exist for is silent: a kit whose weapon stops teaching a verb still spawns, and
/// still fights, just with nothing to cast.
/// </summary>
public class RivalCompanyTests
{
    private const string KitDir = "Assets/Data/EnemyKits/";

    /// <summary>Every kit the ladder draws on. A new one belongs here and in a fight.</summary>
    private static readonly string[] AllKits =
    {
        "Archer", "Berserker", "Knight", "Lancer", "Ninja", "WallKeeper", "WandMage",
        "RainArcher", "TarWarlock", "Cutthroat", "Whirler",
    };

    [OneTimeSetUp]
    public void LoadItems()
    {
        // The game assigns this from an inspector field at runtime, so a test has to do it itself.
        ItemCollection.Active = AssetDatabase.LoadAssetAtPath<ItemCollection>("Assets/Data/ItemCollection.asset");
        Assert.That(ItemCollection.Active, Is.Not.Null, "no item collection to dress anyone from");
    }

    private static EnemyKit Kit(string name)
    {
        var kit = AssetDatabase.LoadAssetAtPath<EnemyKit>(KitDir + name + ".asset");
        Assert.That(kit, Is.Not.Null, KitDir + name + ".asset is missing");
        return kit;
    }

    private static EncounterData Fight(int n)
    {
        var encounter = AssetDatabase.LoadAssetAtPath<EncounterData>($"Assets/Data/Run/Encounter{n}.asset");
        Assert.That(encounter, Is.Not.Null, $"Encounter{n} is missing");
        return encounter;
    }

    /// <summary>What an item would grant its wearer: its own entry, else its weapon class's default.</summary>
    private static Engraving EngravingOn(string itemId)
    {
        var entry = ResonanceDatabase.Active.FindFor(new Item(itemId));
        return entry != null ? entry.engraving : null;
    }

    private static List<string> VerbsOf(EnemyKit kit)
    {
        var verbs = new List<string>();
        foreach (var id in kit.itemIds)
            if (EngravingOn(id) is GrantSpellEngraving verb && verb.spell != null)
                verbs.Add(verb.spell.DisplayName);
        return verbs;
    }

    // ---------- every kit is a fighter, not a costume ----------

    [Test]
    public void EveryKitWearsKnownItemsAndCarriesExactlyOneWeapon()
    {
        foreach (var name in AllKits)
        {
            var kit = Kit(name);
            int weapons = 0;

            foreach (var id in kit.itemIds)
            {
                var itemParams = ItemCollection.Active.Items.Find(i => i.Id == id);
                Assert.That(itemParams, Is.Not.Null, $"{name} wears '{id}', which is not an item");
                if (itemParams.Type == ItemType.Weapon) weapons++;
            }

            // The weapon decides the basic attack, the reach, the muster and the verb. None means a
            // unit that spawns and then stands there; two means whichever the rig happens to keep.
            Assert.That(weapons, Is.EqualTo(1), $"{name} carries {weapons} weapons");
        }
    }

    [Test]
    public void EveryKitsWeaponTeachesAVerb()
    {
        foreach (var name in AllKits)
            Assert.That(VerbsOf(Kit(name)), Is.Not.Empty, $"{name} has nothing to cast");
    }

    [Test]
    public void NoKitWearsTwoThingsTellingItHowToFight()
    {
        // Tactics are an override, and two of them are a coin toss over the unit's whole behaviour.
        foreach (var name in AllKits)
        {
            int tactics = 0;
            foreach (var id in Kit(name).itemIds)
                if (EngravingOn(id) is TacticsEngraving) tactics++;
            Assert.That(tactics, Is.LessThanOrEqualTo(1), $"{name} wears {tactics} tactics items");
        }
    }

    // ---------- the ladder ----------

    [Test]
    public void EveryFightIsFiveAuthoredFightersOnCellsOfTheirOwn()
    {
        for (int n = 1; n <= 5; n++)
        {
            var fight = Fight(n);
            Assert.That(fight.spawns.Count, Is.EqualTo(5), $"{fight.encounterName} is not a five-a-side");

            var taken = new HashSet<(int, int)>();
            foreach (var spawn in fight.spawns)
            {
                Assert.That(spawn.prefab, Is.Not.Null, $"{fight.encounterName} has a spawn with no body");
                Assert.That(spawn.kit, Is.Not.Null,
                            $"{fight.encounterName} has a spawn with no kit — it would roll random gear and a random verb");
                Assert.That(taken.Add((spawn.column, spawn.row)), Is.True,
                            $"{fight.encounterName} stacks two units on cell ({spawn.column},{spawn.row})");
            }
        }
    }

    [Test]
    public void TheFirstFightShowsEverySpaceVerb()
    {
        // Fight one is the shop window for the physics verbs: a ring on the ground, a pool that
        // stays, blades that orbit and a star that bounces, all inside the first thirty seconds.
        var seen = new List<string>();
        foreach (var spawn in Fight(1).spawns) seen.AddRange(VerbsOf(spawn.kit));

        foreach (var verb in new[] { "Arrow Rain", "Tar Pool", "Whirl", "Ricochet" })
            Assert.That(seen, Has.Member(verb), $"Rival Company no longer casts {verb}");
    }

    [Test]
    public void TheLadderNeverGetsEasier()
    {
        // The company is healed between fights, so a run's difficulty cannot come from attrition:
        // it has to be authored into the fights themselves, or fight five is fight one again.
        float health = 0f, damage = 0f;
        for (int n = 1; n <= 5; n++)
        {
            var loadout = Fight(n).defaultLoadout;
            Assert.That(loadout, Is.Not.Null, $"Encounter{n} has no loadout — its spawns would be unarmed");
            Assert.That(loadout.healthMultiplier, Is.GreaterThanOrEqualTo(health), $"Encounter{n} is softer than the fight before it");
            Assert.That(loadout.damageMultiplier, Is.GreaterThanOrEqualTo(damage), $"Encounter{n} hits softer than the fight before it");
            health = loadout.healthMultiplier;
            damage = loadout.damageMultiplier;
        }

        var first = Fight(1).defaultLoadout;
        Assert.That(damage, Is.GreaterThan(first.damageMultiplier), "the last fight of the ladder hits no harder than the first");
    }

    [Test]
    public void EveryLadderLoadoutCanDressASpawnThatNamesNoKit()
    {
        // The pool is the fallback under the authored spawn: without it a spawn added later rolls
        // random gear, and one random fighter in an authored five is the thing the kits replaced.
        for (int n = 1; n <= 5; n++)
        {
            var loadout = Fight(n).defaultLoadout;
            Assert.That(loadout.kits, Is.Not.Empty, $"{loadout.name} has no kit pool");
            foreach (var kit in loadout.kits) Assert.That(kit, Is.Not.Null, $"{loadout.name} has an empty slot in its pool");
        }
    }

    [Test]
    public void EveryKitIsMetSomewhereInTheLadder()
    {
        var met = new HashSet<string>();
        for (int n = 1; n <= 5; n++)
            foreach (var spawn in Fight(n).spawns)
                if (spawn.kit != null) met.Add(spawn.kit.name);

        foreach (var name in AllKits)
            Assert.That(met, Has.Member(name), $"{name} is authored but never fielded");
    }
}
