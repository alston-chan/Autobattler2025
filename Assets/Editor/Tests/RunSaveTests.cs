using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// A run can be put back exactly from what a save keeps: a seed and a path for the map, an index
/// for a flat run, and a snapshot that survives the trip through JSON with nothing dropped.
/// </summary>
public class RunSaveTests
{
    private static RunData Act1() =>
        AssetDatabase.LoadAssetAtPath<RunData>("Assets/Data/Run/Act1/Act1Run.asset");

    private static RunData Demo() =>
        AssetDatabase.LoadAssetAtPath<RunData>("Assets/Data/Run/DemoRun.asset");

    [Test]
    public void AForcedSeedRollsTheSameMapTwice()
    {
        var first = new RunState(Act1(), 7);
        var second = new RunState(Act1(), 7);
        Assert.That(first.Map.Seed, Is.EqualTo(second.Map.Seed));
        for (int r = 0; r < first.Map.RowCount; r++)
            Assert.That(first.Map.Row(r).Count, Is.EqualTo(second.Map.Row(r).Count), "row " + r);
    }

    [Test]
    public void ReplayingThePathLandsOnTheSameFight()
    {
        var live = new RunState(Act1(), 7);
        live.Choose(live.Map.Row(0)[0]);
        live.AdvanceAfterVictory();
        live.Choose(live.AvailableNext[0]);
        Assert.That(live.Path.Count, Is.EqualTo(2));
        Assert.That(live.AwaitingPath, Is.False, "mid-run: a fight is staged");

        var resumed = new RunState(Act1(), 7);
        Assert.That(resumed.Replay(new List<Vector2Int>(live.Path), awaitingPath: false), Is.True);

        Assert.That(resumed.CurrentNode.Row, Is.EqualTo(live.CurrentNode.Row));
        Assert.That(resumed.CurrentNode.Lane, Is.EqualTo(live.CurrentNode.Lane));
        Assert.That(resumed.Current.name, Is.EqualTo(live.Current.name), "a different fight was staged");
        Assert.That(resumed.AwaitingPath, Is.False);
        Assert.That(resumed.Map.Row(0)[0].Cleared, Is.True, "the first node should read as cleared");
        Assert.That(resumed.CurrentNode.Cleared, Is.False, "the staged fight is not cleared yet");
        Assert.That(resumed.Progress, Is.EqualTo(live.Progress));
    }

    [Test]
    public void ReplayingUpToAChoiceLeavesTheChoiceOpen()
    {
        var live = new RunState(Act1(), 7);
        live.Choose(live.Map.Row(0)[1]);
        live.AdvanceAfterVictory();
        Assert.That(live.AwaitingPath, Is.True);

        var resumed = new RunState(Act1(), 7);
        Assert.That(resumed.Replay(new List<Vector2Int>(live.Path), awaitingPath: true), Is.True);
        Assert.That(resumed.AwaitingPath, Is.True);
        Assert.That(resumed.CurrentNode.Cleared, Is.True);
        Assert.That(resumed.AvailableNext.Count, Is.EqualTo(live.AvailableNext.Count));
    }

    [Test]
    public void APathThatNoLongerFitsIsRefused()
    {
        var resumed = new RunState(Act1(), 7);
        var nonsense = new List<Vector2Int> { new Vector2Int(0, 99) };
        Assert.That(resumed.Replay(nonsense, awaitingPath: false), Is.False);
    }

    [Test]
    public void AFlatRunResumesAtItsIndex()
    {
        var demo = Demo();
        var state = new RunState(demo);
        state.ResumeAt(2);
        Assert.That(state.Current, Is.SameAs(demo.encounters[2]));
        Assert.That(state.Progress, Is.EqualTo("Fight 3 / 5"));

        state.ResumeAt(99);
        Assert.That(state.Current, Is.SameAs(demo.encounters[demo.encounters.Count - 1]), "clamped to the last fight");
    }

    [Test]
    public void ASnapshotSurvivesJson()
    {
        var snapshot = new RunSnapshot
        {
            runAsset = "Act1Run",
            mapSeed = 7,
            pathRows = new List<int> { 0, 1 },
            pathLanes = new List<int> { 1, 0 },
            awaitingPath = true,
            encounterIndex = 3
        };
        var hero = new SavedHero { name = "Hero_Bow", column = 2, row = 1 };
        hero.equipped.Add(new SavedItem { id = "FantasyHeroes.Basic.Bow.HunterBow" });
        hero.equipped.Add(new SavedItem { id = "Spellbook.Star", modifierId = 0, modifierLevel = 0 });
        hero.resonance.attunement.Add(new Resonance.AttunementRecord { itemKey = "FantasyHeroes.Basic.Bow.HunterBow|0|0", attunement = 2.5f });
        hero.resonance.banked.Add(new Resonance.BankedRecord { engravingName = "Engraving_Swift", tier = 2 });
        snapshot.heroes.Add(hero);
        snapshot.bag.Add(new SavedItem { id = "FantasyHeroes.Basic.Armor.BanditArmor.gloves" });

        var back = JsonUtility.FromJson<RunSnapshot>(JsonUtility.ToJson(snapshot));

        Assert.That(back.runAsset, Is.EqualTo("Act1Run"));
        Assert.That(back.mapSeed, Is.EqualTo(7));
        Assert.That(back.pathRows, Is.EqualTo(new List<int> { 0, 1 }));
        Assert.That(back.pathLanes, Is.EqualTo(new List<int> { 1, 0 }));
        Assert.That(back.awaitingPath, Is.True);
        Assert.That(back.heroes.Count, Is.EqualTo(1));
        Assert.That(back.heroes[0].name, Is.EqualTo("Hero_Bow"));
        Assert.That(back.heroes[0].column, Is.EqualTo(2));
        Assert.That(back.heroes[0].equipped.Count, Is.EqualTo(2));
        Assert.That(back.heroes[0].resonance.attunement[0].attunement, Is.EqualTo(2.5f).Within(0.0001f));
        Assert.That(back.heroes[0].resonance.banked[0].engravingName, Is.EqualTo("Engraving_Swift"));
        Assert.That(back.heroes[0].resonance.banked[0].tier, Is.EqualTo(2));
        Assert.That(back.bag.Count, Is.EqualTo(1));
    }

    [Test]
    public void AnItemRoundTripsThroughItsRecord()
    {
        var item = new Assets.HeroEditor.InventorySystem.Scripts.Data.Item("FantasyHeroes.Basic.Bow.HunterBow", 1);
        var record = SavedItem.From(item);
        var back = record.ToItem();
        Assert.That(back.Id, Is.EqualTo(item.Id));
        Assert.That(back.Count, Is.EqualTo(1));
        Assert.That(back.Modifier, Is.Null.Or.Property("Level").EqualTo(0));
    }
}
