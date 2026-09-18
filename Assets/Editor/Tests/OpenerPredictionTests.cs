using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// The arrow on the setup screen says whom a unit will charge at the bell, and it has to say what
/// the fight will do: the unit's own target rule, from its gear, not "the nearest" for everyone.
/// </summary>
public class OpenerPredictionTests
{
    private readonly List<GameObject> _made = new List<GameObject>();

    [TearDown]
    public void TearDown()
    {
        foreach (var go in _made) if (go != null) Object.DestroyImmediate(go);
        _made.Clear();
    }

    private Entity Unit(string name)
    {
        var go = new GameObject(name); _made.Add(go);
        return go.AddComponent<Entity>();
    }

    private static TacticsEngraving Tactic(Stance stance = Stance.Auto, TargetMode? target = null)
    {
        var e = ScriptableObject.CreateInstance<TacticsEngraving>();
        e.stance = stance; e.setsTarget = target.HasValue; if (target.HasValue) e.targetMode = target.Value;
        return e;
    }

    /// <summary>A hero in the middle lane; one enemy straight ahead and close, one far in the corner.</summary>
    private Board<Entity> Field(out Entity hero, out Entity near, out Entity far)
    {
        var board = new Board<Entity>(3, 3);
        hero = Unit("hero"); near = Unit("near"); far = Unit("far");
        hero.isTeam = true; near.isTeam = false; far.isTeam = false;
        board.Place(hero, allySide: true, column: 0, row: 1);
        board.Place(near, allySide: false, column: 0, row: 1);
        board.Place(far, allySide: false, column: 2, row: 0);
        return board;
    }

    [Test]
    public void AUnitWithNoTacticsChargesTheNearestInItsLane()
    {
        var board = Field(out var hero, out var near, out _);
        Assert.That(BoardSnapshot.PredictOpening(board, hero), Is.SameAs(near));
    }

    [Test]
    public void ADiverChargesTheFarthestWhateverItsRuleSays()
    {
        var board = Field(out var hero, out _, out var far);
        Tactic(Stance.Dive, TargetMode.Nearest).OnGranted(hero, 1);
        Assert.That(BoardSnapshot.PredictOpening(board, hero), Is.SameAs(far));
    }

    [Test]
    public void AnItemThatSaysTheFarthestMovesTheArrow()
    {
        var board = Field(out var hero, out var near, out var far);
        var crest = Tactic(target: TargetMode.Furthest);
        crest.OnGranted(hero, 1);
        Assert.That(BoardSnapshot.PredictOpening(board, hero), Is.SameAs(far));
        crest.OnRevoked(hero, 1);
        Assert.That(BoardSnapshot.PredictOpening(board, hero), Is.SameAs(near), "taking the item off takes the arrow back");
    }

    [Test]
    public void AnEnemyGetsTheSameAnswerFromItsOwnSide()
    {
        var board = Field(out var hero, out var near, out _);
        Assert.That(BoardSnapshot.PredictOpening(board, near), Is.SameAs(hero));
    }
}
