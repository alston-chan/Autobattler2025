using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// The promises the act map makes (see <see cref="MapGenerator"/>): a seed reproduces a run, no path
/// dead-ends, elites land where the recipe says, and every node holds the fight its depth calls for.
/// Plus the walk itself — a map run opens on the map, only the row ahead can be chosen, and the boss
/// ends it.
///
/// A generated map with a hole in it looks exactly like one without, so none of this can be checked
/// by looking. Each rule is rolled across many seeds; a generator that is only usually right is the
/// bug being guarded against.
/// </summary>
public class ActMapTests
{
    private const int Seeds = 40;

    private ActData _act;
    private EncounterData _early, _late, _elite, _boss;
    private EnemyLoadout _eliteLoadout;

    [SetUp]
    public void BuildRecipe()
    {
        _early = Encounter("Early", 3);
        _late = Encounter("Late", 5);
        _elite = Encounter("Elite", 5);
        _boss = Encounter("Boss", 6);
        _eliteLoadout = ScriptableObject.CreateInstance<EnemyLoadout>();

        _act = ScriptableObject.CreateInstance<ActData>();
        _act.name = "TestAct";
        _act.rows = 7;
        _act.minNodesPerRow = 2;
        _act.maxNodesPerRow = 4;
        _act.combatBands = new List<ActData.Band> { Band(0, 2, Pool(_early)), Band(3, 5, Pool(_late)) };
        _act.elitePool = Pool(_elite, _eliteLoadout);
        _act.guaranteedElites = 2;
        _act.eliteEarliestRow = 2;
        _act.bossPool = Pool(_boss);
    }

    // ---------- the generator ----------

    [Test]
    public void TheSameSeedRollsTheSameMap()
    {
        for (int seed = 1; seed <= Seeds; seed++)
            Assert.That(Signature(MapGenerator.Generate(_act, seed)),
                        Is.EqualTo(Signature(MapGenerator.Generate(_act, seed))),
                        "seed " + seed + " produced two different maps");
    }

    [Test]
    public void EveryNodeIsReachableFromTheBottomRow()
    {
        for (int seed = 1; seed <= Seeds; seed++)
        {
            var map = MapGenerator.Generate(_act, seed);
            var seen = new HashSet<MapNode>();
            var frontier = new Queue<MapNode>(map.Row(0));
            while (frontier.Count > 0)
            {
                var node = frontier.Dequeue();
                if (!seen.Add(node)) continue;
                foreach (var next in node.Next) frontier.Enqueue(next);
            }

            int total = 0;
            foreach (var _ in map.AllNodes()) total++;
            Assert.That(seen.Count, Is.EqualTo(total), "seed " + seed + " left a node nobody can reach");
        }
    }

    [Test]
    public void EveryNodeLeadsUpToTheBoss()
    {
        // Edges only go one row up and every non-boss node has one, so following any of them
        // reaches the top. Both halves are asserted, since the proof needs both.
        for (int seed = 1; seed <= Seeds; seed++)
        {
            var map = MapGenerator.Generate(_act, seed);
            foreach (var node in map.AllNodes())
            {
                if (node.Type == NodeType.Boss)
                {
                    Assert.That(node.Next, Is.Empty, "the boss leads somewhere");
                    continue;
                }
                Assert.That(node.Next, Is.Not.Empty, $"seed {seed}: node {node.Row}.{node.Lane} is a dead end");
                foreach (var next in node.Next)
                    Assert.That(next.Row, Is.EqualTo(node.Row + 1), $"seed {seed}: an edge skips a row");
            }
        }
    }

    [Test]
    public void RowsStayWithinTheRecipe()
    {
        for (int seed = 1; seed <= Seeds; seed++)
        {
            var map = MapGenerator.Generate(_act, seed);
            Assert.That(map.RowCount, Is.EqualTo(7));
            for (int r = 0; r < map.RowCount - 1; r++)
                Assert.That(map.Row(r).Count, Is.InRange(2, 4), $"seed {seed}: row {r} is the wrong width");

            Assert.That(map.Row(map.RowCount - 1).Count, Is.EqualTo(1), "the boss row should hold the boss alone");
            Assert.That(map.Boss.Type, Is.EqualTo(NodeType.Boss));
        }
    }

    [Test]
    public void TheGuaranteedElitesLandOnSeparateRowsAboveTheFloor()
    {
        for (int seed = 1; seed <= Seeds; seed++)
        {
            var map = MapGenerator.Generate(_act, seed);
            var eliteRows = new List<int>();
            foreach (var node in map.AllNodes())
                if (node.Type == NodeType.Elite) eliteRows.Add(node.Row);

            Assert.That(eliteRows.Count, Is.EqualTo(2), $"seed {seed}: wrong number of elites");
            Assert.That(eliteRows[0], Is.Not.EqualTo(eliteRows[1]), $"seed {seed}: both elites share a row");
            foreach (int row in eliteRows)
            {
                Assert.That(row, Is.GreaterThanOrEqualTo(2), $"seed {seed}: an elite sits below the floor");
                Assert.That(row, Is.LessThan(map.RowCount - 1), $"seed {seed}: an elite sits on the boss row");
            }
        }
    }

    [Test]
    public void EveryNodeHoldsTheFightItsDepthCallsFor()
    {
        for (int seed = 1; seed <= Seeds; seed++)
        {
            var map = MapGenerator.Generate(_act, seed);
            foreach (var node in map.AllNodes())
            {
                switch (node.Type)
                {
                    case NodeType.Boss:
                        Assert.That(node.Encounter, Is.SameAs(_boss));
                        break;
                    case NodeType.Elite:
                        Assert.That(node.Encounter, Is.SameAs(_elite));
                        Assert.That(node.Loadout, Is.SameAs(_eliteLoadout), "an elite should fight at the elite pool's toughness");
                        break;
                    default:
                        Assert.That(node.Encounter, Is.SameAs(node.Row <= 2 ? _early : _late),
                                    $"seed {seed}: row {node.Row} drew from the wrong band");
                        break;
                }
            }
        }
    }

    [Test]
    public void ARowNobodyCoversIsSaidOutLoud()
    {
        _act.combatBands = new List<ActData.Band> { Band(0, 2, Pool(_early)) };

        LogAssert.Expect(LogType.Warning, new Regex("no Combat pool covers row"));
        var map = MapGenerator.Generate(_act, 3);

        foreach (var node in map.Row(4))
            if (node.Type == NodeType.Combat)
                Assert.That(node.Encounter, Is.Null, "a node past the last band should hold no fight");
    }

    [Test]
    public void PoolForCombatRowFollowsTheBands()
    {
        Assert.That(_act.PoolForCombatRow(0).entries[0].encounter, Is.SameAs(_early));
        Assert.That(_act.PoolForCombatRow(2).entries[0].encounter, Is.SameAs(_early));
        Assert.That(_act.PoolForCombatRow(3).entries[0].encounter, Is.SameAs(_late));
        Assert.That(_act.PoolForCombatRow(9), Is.Null);
    }

    // ---------- the walk ----------

    private RunState MapRun(int seed = 7)
    {
        var run = ScriptableObject.CreateInstance<RunData>();
        run.act = _act;
        run.mapSeed = seed;
        return new RunState(run);
    }

    [Test]
    public void AMapRunOpensOnTheMap()
    {
        var state = MapRun();
        Assert.That(state.IsMapRun, Is.True);
        Assert.That(state.AwaitingPath, Is.True);
        Assert.That(state.Current, Is.Null, "nothing should be staged before a path is chosen");
        Assert.That(state.AvailableNext, Is.EqualTo(state.Map.Row(0)));
    }

    [Test]
    public void OnlyTheRowAheadCanBeChosen()
    {
        var state = MapRun();
        var tooFar = state.Map.Row(3)[0];
        Assert.That(state.Choose(tooFar), Is.False, "a node two rows up is not reachable");
        Assert.That(state.AwaitingPath, Is.True);

        var start = state.Map.Row(0)[0];
        Assert.That(state.Choose(start), Is.True);
        Assert.That(state.AwaitingPath, Is.False);
        Assert.That(state.Current, Is.SameAs(start.Encounter));
        Assert.That(state.Choose(start.Next[0]), Is.False, "no choosing mid-fight");
    }

    [Test]
    public void ClearingTheBossWinsTheRun()
    {
        var state = MapRun();
        state.Choose(state.Map.Row(0)[0]);

        int fights = 1;
        while (state.AdvanceAfterVictory())
        {
            Assert.That(state.AwaitingPath, Is.True, "a won fight should hand the choice back");
            Assert.That(state.Choose(state.AvailableNext[0]), Is.True);
            fights++;
        }

        Assert.That(state.Outcome, Is.EqualTo(RunOutcome.Won));
        Assert.That(state.CurrentNodeType, Is.EqualTo(NodeType.Boss));
        Assert.That(fights, Is.EqualTo(7), "one fight per row, boss included");
    }

    [Test]
    public void AFlatRunIsUntouched()
    {
        var run = ScriptableObject.CreateInstance<RunData>();
        run.encounters = new List<EncounterData> { _early, _late };
        var state = new RunState(run);

        Assert.That(state.IsMapRun, Is.False);
        Assert.That(state.AwaitingPath, Is.False);
        Assert.That(state.Current, Is.SameAs(_early));
        Assert.That(state.Progress, Is.EqualTo("Fight 1 / 2"));
        Assert.That(state.AdvanceAfterVictory(), Is.True);
        Assert.That(state.Current, Is.SameAs(_late));
        Assert.That(state.AdvanceAfterVictory(), Is.False);
        Assert.That(state.Outcome, Is.EqualTo(RunOutcome.Won));
    }

    // ---------- helpers ----------

    private static EncounterData Encounter(string name, int spawns)
    {
        var encounter = ScriptableObject.CreateInstance<EncounterData>();
        encounter.encounterName = name;
        for (int i = 0; i < spawns; i++) encounter.spawns.Add(new EncounterData.Spawn());
        return encounter;
    }

    private static EncounterPool Pool(EncounterData encounter, EnemyLoadout loadout = null)
    {
        var pool = ScriptableObject.CreateInstance<EncounterPool>();
        pool.entries.Add(new EncounterPool.Entry { encounter = encounter, weight = 1f });
        pool.loadout = loadout;
        return pool;
    }

    private static ActData.Band Band(int from, int to, EncounterPool pool) =>
        new ActData.Band { fromRow = from, toRow = to, pool = pool };

    /// <summary>Everything that makes one map different from another, as text.</summary>
    private static string Signature(ActMap map)
    {
        var text = new StringBuilder();
        foreach (var node in map.AllNodes())
        {
            text.Append(node.Row).Append('.').Append(node.Lane).Append(':').Append(node.Type).Append(':')
                .Append(node.Encounter != null ? node.Encounter.encounterName : "-").Append("->");
            foreach (var next in node.Next) text.Append(next.Row).Append('.').Append(next.Lane).Append(',');
            text.Append('|');
        }
        return text.ToString();
    }
}
