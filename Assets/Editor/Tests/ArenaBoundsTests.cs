using NUnit.Framework;
using UnityEngine;

/// <summary>
/// The arena's idea of "room": how far a point is from the edge.
///
/// The ellipse is the one worth pinning. Its room used to be (1 - d) * min(rx, ry): exact on the
/// short axis and, on a 17 x 4 arena, four times too small on the long one — a unit 2.6 from the
/// side wall read 0.6, so the soft wall treated most of a wide oval as its band. Every consumer
/// (the wall, the kiters' corner test, the wall's give around the grid) reasons in world units, so the answer
/// has to be one.
/// </summary>
public class ArenaBoundsTests
{
    private ArenaBounds _arena;

    [SetUp]
    public void MakeArena()
    {
        _arena = new GameObject("ArenaBoundsTest").AddComponent<ArenaBounds>();
        _arena.center = new Vector2(0f, -2.3f);
        _arena.size = new Vector2(17.2f, 4.2f);   // the coliseum's numbers: rx 8.6, ry 2.1
    }

    [TearDown]
    public void CleanUp() => Object.DestroyImmediate(_arena.gameObject);

    // ---------- the ellipse ----------

    [Test]
    public void OnTheLongAxisRoomIsTheDistanceToTheSideWall()
    {
        _arena.shape = ArenaShape.Ellipse;
        // 2.6 from the wall at x = -8.6. The old answer here was 0.63.
        Assert.That(_arena.EdgeRoom(new Vector3(-6f, -2.3f, 0f)), Is.EqualTo(2.6f).Within(0.01f));
    }

    [Test]
    public void OnTheShortAxisRoomIsTheDistanceToTheCeiling()
    {
        _arena.shape = ArenaShape.Ellipse;
        // 0.5 below the top at y = -0.2. The old formula agreed here — this is the case it was tuned on.
        Assert.That(_arena.EdgeRoom(new Vector3(0f, -0.7f, 0f)), Is.EqualTo(0.5f).Within(0.01f));
    }

    [Test]
    public void OutsideTheOvalThereIsNoRoomAndAtTheCentreThereIsTheShortRadius()
    {
        _arena.shape = ArenaShape.Ellipse;
        Assert.That(_arena.EdgeRoom(new Vector3(-9f, -2.3f, 0f)), Is.EqualTo(0f));
        Assert.That(_arena.EdgeRoom(new Vector3(0f, -2.3f, 0f)), Is.EqualTo(2.1f).Within(0.01f));
    }

    // ---------- the rectangle ----------

    [Test]
    public void ARectangleMeasuresToTheNearestOfItsFourEdges()
    {
        _arena.shape = ArenaShape.Rectangle;
        // y = -3.6 is 0.8 above the floor at -4.4, and much further from everything else.
        Assert.That(_arena.EdgeRoom(new Vector3(-1f, -3.6f, 0f)), Is.EqualTo(0.8f).Within(0.01f));
    }
}

/// <summary>
/// A cell is where a unit stands, and its tile is drawn around it. Both used to be squeezed into
/// the arena's soft-wall band on a small map, so a unit stood beside its tile and the tiles
/// overlapped. The wall gives way to the grid now instead of the other way round.
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

    [Test]
    public void TheWallReachesNoFurtherThanTheGridsNearestCell()
    {
        // The top row sits 0.4 under the ceiling; that is as deep as the wall may push here.
        Assert.That(_grid.Clearance(_arena), Is.EqualTo(0.4f).Within(0.001f));
    }
}
