using NUnit.Framework;
using UnityEngine;

/// <summary>
/// The arena's idea of "room": how far a point is from the edge, and where a point with too little
/// is moved to.
///
/// The ellipse is the one worth pinning. Its room used to be (1 - d) * min(rx, ry): exact on the
/// short axis and, on a 17 x 4 arena, four times too small on the long one — a unit 2.6 from the
/// side wall read 0.6, so the soft wall treated most of a wide oval as its band. Every consumer
/// (the wall, the kiters' corner test, the bell's seating) reasons in world units, so the answer
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

    [Test]
    public void APointTooNearTheSideWallIsPulledAlongItsOwnRayNotTowardTheShortAxis()
    {
        _arena.shape = ArenaShape.Ellipse;
        var moved = _arena.ClampInside(new Vector3(-8f, -2.3f, 0f), 1.2f);

        // Ends up with exactly the margin of room, on the same ray. The old clamp took d down to
        // 1 - 1.2 / 2.1 and put this at x = -3.7: a back-rank archer seated in the front rank.
        Assert.That(moved.x, Is.EqualTo(-7.4f).Within(0.01f));
        Assert.That(moved.y, Is.EqualTo(-2.3f).Within(0.01f));
        Assert.That(_arena.EdgeRoom(moved), Is.EqualTo(1.2f).Within(0.02f));
    }

    [Test]
    public void APointWithRoomToSpareIsLeftAlone()
    {
        _arena.shape = ArenaShape.Ellipse;
        var p = new Vector3(-3f, -2.5f, 0f);
        Assert.That(_arena.ClampInside(p, 1.2f), Is.EqualTo(p));
    }

    // ---------- the rectangle ----------

    [Test]
    public void ARectangleMeasuresToTheNearestOfItsFourEdges()
    {
        _arena.shape = ArenaShape.Rectangle;
        // y = -3.6 is 0.8 above the floor at -4.4, and much further from everything else.
        Assert.That(_arena.EdgeRoom(new Vector3(-1f, -3.6f, 0f)), Is.EqualTo(0.8f).Within(0.01f));
    }

    [Test]
    public void ARectangleClampsEachAxisToItsBand()
    {
        _arena.shape = ArenaShape.Rectangle;
        var moved = _arena.ClampInside(new Vector3(-1f, -3.6f, 0f), 1.2f);
        Assert.That(moved.x, Is.EqualTo(-1f).Within(0.001f), "x had room and must not move");
        Assert.That(moved.y, Is.EqualTo(-3.2f).Within(0.01f), "y comes up to the band");
    }
}
