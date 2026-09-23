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

/// <summary>
/// The side walls stand inside what the camera shows, so a unit knocked to one is drawn whole.
/// </summary>
public class ArenaScreenFitTests
{
    [Test]
    public void AWallAuthoredPastTheViewIsPulledInsideIt()
    {
        // The coliseum: walls at ±8.6, a 16:9 camera showing ±8.9, and the 1.75 margin: ±7.14, which
        // still clears the back column's cells at ±7.
        var arena = new GameObject("ArenaForFit").AddComponent<ArenaBounds>();
        try { Assert.That(ArenaBounds.FitHalfWidth(8.6f, 8.889f, 0f, arena.screenMargin), Is.EqualTo(7.139f).Within(0.01f)); }
        finally { Object.DestroyImmediate(arena.gameObject); }
    }

    [Test]
    public void AnArenaAlreadyInsideTheViewIsLeftAlone()
    {
        Assert.That(ArenaBounds.FitHalfWidth(6f, 8.889f, 0f, 1.3f), Is.EqualTo(6f));
    }

    [Test]
    public void ANarrowerScreenPullsTheWallsInFurther()
    {
        // 16:10 shows ±8.0: the same arena's walls come in to ±6.7, and the back column (±7) is
        // then outside them — worth knowing before anyone ships a 16:10 build.
        Assert.That(ArenaBounds.FitHalfWidth(8.6f, 8.0f, 0f, 1.3f), Is.EqualTo(6.7f).Within(0.01f));
    }

    [Test]
    public void ACameraOffTheMiddleCostsTheNearerSide()
    {
        Assert.That(ArenaBounds.FitHalfWidth(8.6f, 8.889f, 0.5f, 1.3f), Is.EqualTo(7.089f).Within(0.01f));
    }
}
