using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Each map's walls follow its painting and its board sits on its floor (<see cref="ArenaBoundsPreset"/>):
/// a round map's ellipse keeps its painted width, however far past the screen it runs; a map's lift
/// raises the cells; and every cell of every map's lifted board stands inside that map's walls —
/// a cell outside would have its unit dragged off it by the clamp the first frame of a fight.
/// </summary>
public class MapLayoutTests
{
    private static readonly System.Reflection.MethodInfo SetInstance =
        typeof(ArenaBounds).GetProperty("Instance").GetSetMethod(true);

    [Test]
    public void ARoundWallFollowsThePaintedRingPastTheScreen()
    {
        var was = ArenaBounds.Instance;
        var arena = new GameObject("RoundArena").AddComponent<ArenaBounds>();
        try
        {
            SetInstance.Invoke(null, new object[] { arena });
            // The coliseum's ring: 18.1 wide (past the ±8.9 the camera shows), 5.8 tall, around y -1.92.
            arena.shape = ArenaShape.Ellipse; arena.center = new Vector2(0f, -1.92f); arena.size = new Vector2(18.1f, 5.8f);

            // Just inside the painted ring at x = 6: it stays where it is. Squeezing the ellipse to the
            // screen margin put this unit's wall 0.8 lower than the paint.
            float ringY = -1.92f + 2.9f * Mathf.Sqrt(1f - (6f / 9.05f) * (6f / 9.05f));
            var inside = new Vector3(6f, ringY - 0.05f, 0f);
            Assert.That(Vector3.Distance(arena.Clamp(inside), inside), Is.LessThan(0.001f), "a unit inside the painted ring was pushed off it");

            // Past the far wall: back onto the ring.
            var beyond = arena.Clamp(new Vector3(0f, 3f, 0f));
            Assert.That(beyond.y, Is.EqualTo(-1.92f + 2.9f).Within(0.01f), "the far wall should be the ring's top");

            // The straight side walls still keep a body on screen.
            Assert.That(arena.Clamp(new Vector3(8.8f, -1.92f, 0f)).x, Is.LessThanOrEqualTo(arena.MaxX + 0.001f));
        }
        finally
        {
            SetInstance.Invoke(null, new object[] { was });
            Object.DestroyImmediate(arena.gameObject);
        }
    }

    [Test]
    public void ARoundMapsNearWallKeepsFeetOffTheFrontWall()
    {
        var was = ArenaBounds.Instance;
        var arena = new GameObject("NearWallArena").AddComponent<ArenaBounds>();
        try
        {
            SetInstance.Invoke(null, new object[] { arena });
            arena.shape = ArenaShape.Ellipse; arena.center = new Vector2(0f, -1.92f); arena.size = new Vector2(18.1f, 5.8f);
            arena.nearWallRaise = 0.3f;

            // Knocked to the bottom of the ring: stopped 0.3 above its lowest point, not on the front wall.
            var low = arena.Clamp(new Vector3(0f, -6f, 0f));
            Assert.That(low.y, Is.EqualTo(-1.92f - 2.9f + 0.3f).Within(0.01f), "the near wall should stand 0.3 above the ring's bottom");

            // Inside the ring but under the near wall: raised to it.
            var under = arena.Clamp(new Vector3(0f, -4.7f, 0f));
            Assert.That(under.y, Is.EqualTo(-1.92f - 2.9f + 0.3f).Within(0.01f));

            // Toward the corners the ring's curve is already above the near wall, so the near wall leaves
            // a unit standing just inside the curve alone — the back cells at x = ±7 keep their ground.
            float cornerY = -1.92f - 2.9f * Mathf.Sqrt(1f - (7f / 9.05f) * (7f / 9.05f));
            var corner = new Vector3(7f, cornerY + 0.05f, 0f);
            Assert.That(Vector3.Distance(arena.Clamp(corner), corner), Is.LessThan(0.001f), "the near wall moved a unit near a corner");
        }
        finally
        {
            SetInstance.Invoke(null, new object[] { was });
            Object.DestroyImmediate(arena.gameObject);
        }
    }

    [Test]
    public void ALiftRaisesTheCellsAndIsNotCumulative()
    {
        var grid = new GameObject("LiftGrid").AddComponent<BattleGrid>();
        try
        {
            grid.columns = 4; grid.rows = 3; grid.cellSize = new Vector2(2f, 1.5f);
            grid.allyFrontBottom = new Vector2(-1f, -3.6f); grid.enemyFrontBottom = new Vector2(1f, -3.6f);
            grid.SetLift(0.75f);
            Assert.That(grid.CellToWorld(true, 0, 0).y, Is.EqualTo(-2.85f).Within(0.001f));
            grid.SetLift(0.75f);
            grid.SetLift(1f);
            Assert.That(grid.CellToWorld(false, 3, 2).y, Is.EqualTo(-3.6f + 3f + 1f).Within(0.001f), "a lift is where the board stands, not a nudge on top of the last one");
            grid.SetLift(0f);
            Assert.That(grid.CellToWorld(true, 0, 0).y, Is.EqualTo(-3.6f).Within(0.001f));
        }
        finally { Object.DestroyImmediate(grid.gameObject); }
    }

    [Test]
    public void EveryMapsBoardStandsInsideItsWalls()
    {
        var guids = AssetDatabase.FindAssets("t:ArenaBoundsPreset", new[] { "Assets/Data/ArenaBounds/Maps" });
        Assert.That(guids.Length, Is.GreaterThanOrEqualTo(24), "one preset per background");
        foreach (var guid in guids)
        {
            var p = AssetDatabase.LoadAssetAtPath<ArenaBoundsPreset>(AssetDatabase.GUIDToAssetPath(guid));
            float hx = p.size.x * 0.5f, hy = p.size.y * 0.5f;
            for (int side = -1; side <= 1; side += 2)
                for (int c = 0; c < 4; c++)
                    for (int r = 0; r < 3; r++)
                    {
                        // The scene's board: columns 2 apart from ±1, rows 1.5 apart from -3.6.
                        float x = side * (1f + c * 2f), y = -3.6f + r * 1.5f + p.gridLift;
                        float dx = (x - p.center.x) / hx, dy = (y - p.center.y) / hy;
                        bool inside = p.shape == ArenaShape.Ellipse
                            ? dx * dx + dy * dy <= 1f && y >= p.center.y - hy + p.nearWallRaise
                            : Mathf.Abs(dy) <= 1f;
                        Assert.That(inside, Is.True, $"{p.name}: the cell at ({x}, {y}) is outside the walls");
                    }
        }
    }
}
