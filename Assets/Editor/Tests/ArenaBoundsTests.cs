using NUnit.Framework;
using UnityEngine;

/// <summary>
/// A cell is where a unit stands, and its tile is drawn around it. Both used to be squeezed into
/// the arena's soft-wall band on a small map, so a unit stood beside its tile and the tiles
/// overlapped.
/// </summary>
public class BattleGridTests
{
    private BattleGrid _grid;
    private ArenaBounds _arena, _was;
    private static readonly System.Reflection.MethodInfo SetInstance =
        typeof(ArenaBounds).GetProperty("Instance").GetSetMethod(true);

    [SetUp]
    public void Make()
    {
        _grid = new GameObject("BattleGridTest").AddComponent<BattleGrid>();
        _grid.columns = 4; _grid.rows = 3; _grid.cellSize = new Vector2(2f, 1.5f);
        _grid.allyFrontBottom = new Vector2(-1f, -3.6f); _grid.enemyFrontBottom = new Vector2(1f, -3.6f);

        // The coliseum: 4.2 tall, so a 1.2 wall top and bottom leaves 1.8 for rows that span 3.0.
        _arena = new GameObject("ArenaForGridTest").AddComponent<ArenaBounds>();
        _arena.shape = ArenaShape.Rectangle; _arena.center = new Vector2(0f, -2.3f); _arena.size = new Vector2(17.2f, 4.2f);
        _was = ArenaBounds.Instance;
        SetInstance.Invoke(null, new object[] { _arena });
    }

    [TearDown]
    public void CleanUp()
    {
        SetInstance.Invoke(null, new object[] { _was });
        Object.DestroyImmediate(_grid.gameObject);
        Object.DestroyImmediate(_arena.gameObject);
    }

    [Test]
    public void ACellIsItsCentreHoweverSmallTheArena()
    {
        Assert.That(Vector3.Distance(_grid.CellToWorld(true, 3, 2), new Vector3(-7f, -0.6f, 0f)), Is.LessThan(0.001f), "the company's back corner");
        Assert.That(Vector3.Distance(_grid.CellToWorld(true, 0, 1), new Vector3(-1f, -2.1f, 0f)), Is.LessThan(0.001f), "a middle row stays in the middle");
        Assert.That(Vector3.Distance(_grid.CellToWorld(false, 0, 0), new Vector3(1f, -3.6f, 0f)), Is.LessThan(0.001f), "the enemy's front");
    }

}
