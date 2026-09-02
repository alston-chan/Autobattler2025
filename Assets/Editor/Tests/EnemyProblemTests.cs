using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;

/// <summary>
/// The shipped enemy problems, checked as content: a Swarm is many weak bodies, a Bulwark is one
/// wall, Snipers stand at the back, and Act 1 actually poses them.
///
/// These are the fights the map's branching exists for — a route is chosen for what it asks — so a
/// problem that quietly stops being one (a swarm trimmed to four rats, a sniper moved to the front
/// rank, a pool that no longer contains it) breaks the map without breaking anything that compiles.
/// </summary>
public class EnemyProblemTests
{
    private const string Dir = "Assets/Data/Run/Archetypes/";
    private const int GridSize = 3;   // BattleGrid: three columns and three rows per side

    private static EncounterData Encounter(string name)
    {
        var encounter = AssetDatabase.LoadAssetAtPath<EncounterData>(Dir + name + ".asset");
        Assert.That(encounter, Is.Not.Null, Dir + name + ".asset is missing");
        return encounter;
    }

    private static EncounterPool Pool(string name)
    {
        var pool = AssetDatabase.LoadAssetAtPath<EncounterPool>("Assets/Data/Run/Act1/" + name + ".asset");
        Assert.That(pool, Is.Not.Null, name + " pool is missing");
        return pool;
    }

    private static bool Contains(EncounterPool pool, EncounterData encounter)
    {
        foreach (var entry in pool.entries)
            if (entry != null && entry.encounter == encounter && entry.weight > 0f) return true;
        return false;
    }

    // ---------- each problem is what it says ----------

    [Test]
    public void ASwarmIsManyWeakBodies()
    {
        var swarm = Encounter("Swarm_RatPack");
        Assert.That(swarm.problems, Is.EqualTo(new List<EnemyProblem> { EnemyProblem.Swarm }));
        Assert.That(swarm.spawns.Count, Is.GreaterThanOrEqualTo(8), "a swarm of fewer than eight is a band");

        foreach (var spawn in swarm.spawns)
        {
            Assert.That(spawn.unitData, Is.Not.Null, "every rat needs the swarm unit data");
            Assert.That(spawn.unitData.isCharacter, Is.False, "rats are monsters, not humans in costume");
            Assert.That(spawn.unitData.maxHealth, Is.LessThan(200f), "a swarm body must die to a few hits");
        }
    }

    [Test]
    public void ABulwarkIsOneWall()
    {
        var bulwark = Encounter("Bulwark_Warden");
        Assert.That(bulwark.problems, Is.EqualTo(new List<EnemyProblem> { EnemyProblem.Bulwark }));
        Assert.That(bulwark.spawns.Count, Is.EqualTo(1), "a wall is one unit; two is a band");

        var unit = bulwark.spawns[0].unitData;
        Assert.That(unit, Is.Not.Null);
        Assert.That(unit.maxHealth, Is.GreaterThanOrEqualTo(2000f), "a wall has to outlast an ordinary fight's whole roster");
        Assert.That(bulwark.spawns[0].column, Is.EqualTo(0), "the wall stands in the front rank");
    }

    [Test]
    public void SnipersStandAtTheBackAndHitHard()
    {
        var nest = Encounter("Sniper_Nest");
        Assert.That(nest.problems, Is.EqualTo(new List<EnemyProblem> { EnemyProblem.Sniper }));
        Assert.That(nest.spawns.Count, Is.EqualTo(3));

        foreach (var spawn in nest.spawns)
        {
            Assert.That(spawn.column, Is.EqualTo(GridSize - 1), "a sniper in the front rank is just an archer");
            Assert.That(spawn.unitData, Is.Not.Null);
            Assert.That(spawn.unitData.isRanged, Is.True);
            Assert.That(spawn.unitData.maxHealth, Is.LessThanOrEqualTo(200f), "glass");
            Assert.That(spawn.unitData.damageMultiplier, Is.GreaterThanOrEqualTo(2f), "cannon");
        }
    }

    [Test]
    public void TheEliteStacksTwoProblems()
    {
        var elite = Encounter("Elite_WallAndNest");
        Assert.That(elite.problems, Is.EqualTo(new List<EnemyProblem> { EnemyProblem.Bulwark, EnemyProblem.Sniper }));
        Assert.That(elite.ProblemLabel, Is.EqualTo("BULWARK + SNIPER"));

        int walls = 0, snipers = 0;
        foreach (var spawn in elite.spawns)
        {
            if (spawn.unitData == null) continue;
            if (spawn.unitData.maxHealth >= 2000f) walls++;
            else if (spawn.unitData.isRanged) snipers++;
        }
        // One sniper, not a nest of three: measured with two, the pair behind the wall's knockback
        // fired for seventy seconds and wiped a starting-kit company every time. An elite is meant
        // to be the dangerous route, not a certain one.
        Assert.That(walls, Is.EqualTo(1));
        Assert.That(snipers, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void AnOrdinaryFightHasNoLabel()
    {
        var plain = AssetDatabase.LoadAssetAtPath<EncounterData>("Assets/Data/Run/Encounter1.asset");
        Assert.That(plain.ProblemLabel, Is.Empty);
    }

    // ---------- they cannot be retuned out from under themselves ----------

    [Test]
    public void EveryArchetypeSpawnNamesItsOwnLoadout()
    {
        // A pool's loadout override applies to spawns without one. An archetype's toughness is
        // the archetype — a swarm rat at an elite pool's 5x health is not a swarm any more.
        foreach (var name in new[] { "Swarm_RatPack", "Bulwark_Warden", "Sniper_Nest", "Elite_WallAndNest" })
            foreach (var spawn in Encounter(name).spawns)
                Assert.That(spawn.loadout, Is.Not.Null, name + " has a spawn a pool could retune");
    }

    [Test]
    public void EverySpawnFitsTheGridWithoutSharingACell()
    {
        var names = new List<string> { "Swarm_RatPack", "Bulwark_Warden", "Sniper_Nest", "Elite_WallAndNest" };
        foreach (var name in names)
        {
            var taken = new HashSet<(int, int)>();
            foreach (var spawn in Encounter(name).spawns)
            {
                Assert.That(spawn.column, Is.InRange(0, GridSize - 1), name + " spawns off the grid");
                Assert.That(spawn.row, Is.InRange(0, GridSize - 1), name + " spawns off the grid");
                Assert.That(taken.Add((spawn.column, spawn.row)), Is.True,
                            $"{name} stacks two units on cell ({spawn.column},{spawn.row})");
            }
        }
    }

    // ---------- Act 1 poses them ----------

    [Test]
    public void ActOnePosesEveryProblem()
    {
        Assert.That(Contains(Pool("Act1_Early"), Encounter("Swarm_RatPack")), "the swarm should be met early");
        Assert.That(Contains(Pool("Act1_Late"), Encounter("Bulwark_Warden")), "the wall waits for the late rows");
        Assert.That(Contains(Pool("Act1_Late"), Encounter("Sniper_Nest")), "the nest waits for the late rows");
        Assert.That(Contains(Pool("Act1_Elites"), Encounter("Elite_WallAndNest")), "the stacked problem is an elite");
    }

    [Test]
    public void ActOneCoversEveryRow()
    {
        var act = AssetDatabase.LoadAssetAtPath<ActData>("Assets/Data/Run/Act1/Act1.asset");
        Assert.That(act, Is.Not.Null);

        for (int row = 0; row < act.rows - 1; row++)
        {
            var pool = act.PoolForCombatRow(row);
            Assert.That(pool, Is.Not.Null, $"row {row} has no pool — a node there would hold no fight");
            Assert.That(pool.IsEmpty, Is.False, $"row {row}'s pool is empty");
        }
        Assert.That(act.elitePool != null && !act.elitePool.IsEmpty, "no elite pool");
        Assert.That(act.bossPool != null && !act.bossPool.IsEmpty, "no boss pool");
    }
}
