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

    private static TacticsEngraving Tactic(TargetMode target)
    {
        var e = ScriptableObject.CreateInstance<TacticsEngraving>();
        e.targetMode = target;
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
    public void AUnitWithNoTacticsChargesTheNearest()
    {
        var board = Field(out var hero, out var near, out _);
        Assert.That(BoardSnapshot.PredictOpening(board, hero), Is.SameAs(near));
    }

    [Test]
    public void WithTheFrontRankEmptyTheArrowTakesTheDiagonalAsTheFightWill()
    {
        // No lane bonus any more: plain distance, the same number the fight ranks by. The enemy
        // straight ahead stands a column deep (4.0 away) and one stands in the front rank a row over
        // (2.5): the fight picks the diagonal, so the arrow must too.
        // The scene's grid, so distances are the world's: columns 2 apart, rows 1.5, front ranks ±1.
        var grid = new GameObject("GridForOpenerTest").AddComponent<BattleGrid>(); _made.Add(grid.gameObject);
        grid.columns = 4; grid.rows = 3; grid.cellSize = new Vector2(2f, 1.5f);
        grid.allyFrontBottom = new Vector2(-1f, -3.6f); grid.enemyFrontBottom = new Vector2(1f, -3.6f);
        var setInstance = typeof(BattleGrid).GetProperty("Instance").GetSetMethod(true);
        var was = BattleGrid.Instance;
        setInstance.Invoke(null, new object[] { grid });
        try
        {
            var board = new Board<Entity>(3, 3);
            var hero = Unit("hero"); var deep = Unit("deep"); var diagonal = Unit("diagonal");
            hero.isTeam = true; deep.isTeam = false; diagonal.isTeam = false;
            board.Place(hero, allySide: true, column: 0, row: 1);
            board.Place(deep, allySide: false, column: 1, row: 1);
            board.Place(diagonal, allySide: false, column: 0, row: 0);
            Assert.That(BoardSnapshot.PredictOpening(board, hero), Is.SameAs(diagonal));
        }
        finally { setInstance.Invoke(null, new object[] { was }); }
    }

    [Test]
    public void AnItemThatSaysTheFarthestMovesTheArrow()
    {
        var board = Field(out var hero, out var near, out var far);
        var crest = Tactic(TargetMode.Furthest);
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
