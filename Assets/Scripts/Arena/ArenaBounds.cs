using UnityEngine;

/// <summary>The shape of a play area's boundary.</summary>
public enum ArenaShape
{
    /// <summary>Axis-aligned box — flat arenas (caves, dungeons, side-view rooms).</summary>
    Rectangle,
    /// <summary>Ellipse inscribed in the box — round pits like the coliseum. Circle = equal radii.</summary>
    Ellipse
}

/// <summary>
/// A play area that keeps entities on-screen and on the arena floor. Movement (CombatAI) and knockback
/// both write <c>transform.position</c> directly — there is no Rigidbody driving them — so physics
/// colliders can't constrain them. Instead every entity clamps its position into these bounds each
/// frame (see <see cref="Entity"/> Update), which also caps how far a knockback like Shockwave can
/// launch someone.
///
/// The boundary is either a rectangle or an ellipse inscribed in the same min/max box, so a round
/// arena (coliseum sand) uses the same four numbers with a curved edge. One instance is the global
/// bounds (<see cref="Instance"/>). Drop an ArenaBounds on a GameObject to tune it with the Scene-view
/// gizmo; if none exists, GameManager creates one with these defaults, and BackgroundCycler pushes
/// per-map bounds via <see cref="SetBounds"/>.
/// </summary>
public class ArenaBounds : MonoBehaviour
{
    /// <summary>The global play-area bounds, or null if none is active.</summary>
    public static ArenaBounds Instance { get; private set; }

    [Header("Play area (world space)")]
    [Tooltip("Rectangle clamps to the box; Ellipse clamps to the oval inscribed in the box (round arenas).")]
    public ArenaShape shape = ArenaShape.Ellipse;

    [Tooltip("Middle of the play area. Move the arena by moving this; the edges follow.")]
    public Vector2 center = new Vector2(0f, -1.67f);

    [Tooltip("Width and height. The height runs from the ground line up to the ceiling a " +
             "knockback can throw someone.")]
    public Vector2 size = new Vector2(17.6f, 6.2f);

    // Edges, derived. The arena is authored as a middle and a span because those are the two things
    // anyone actually wants to change — nudge it across, make it bigger. Four independent edges made
    // the first of those a two-field edit with arithmetic in between, and nothing kept the halves in
    // step with each other.
    public float MinX => center.x - size.x * 0.5f;
    public float MaxX => center.x + size.x * 0.5f;
    public float MinY => center.y - size.y * 0.5f;
    public float MaxY => center.y + size.y * 0.5f;

    private void Awake()
    {
        // There should only be one; a scene instance and the GameManager fallback never coexist
        // because GameManager only spawns a fallback when Instance is still null.
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Clamp a world position into the play area. X and Y only — Z is untouched.</summary>
    public Vector3 Clamp(Vector3 p)
    {
        return shape == ArenaShape.Ellipse ? ClampEllipse(p) : ClampRect(p);
    }

    private Vector3 ClampRect(Vector3 p)
    {
        p.x = Mathf.Clamp(p.x, MinX, MaxX);
        p.y = Mathf.Clamp(p.y, MinY, MaxY);
        return p;
    }

    /// <summary>
    /// Clamp into the ellipse inscribed in the box. A point outside is projected radially back onto the
    /// rim — not the exact nearest point, but cheap and visually indistinguishable for gameplay.
    /// </summary>
    private Vector3 ClampEllipse(Vector3 p)
    {
        float rx = size.x * 0.5f;
        float ry = size.y * 0.5f;
        if (rx <= 0.0001f || ry <= 0.0001f) return ClampRect(p);   // degenerate box

        float dx = (p.x - center.x) / rx;
        float dy = (p.y - center.y) / ry;
        float d = Mathf.Sqrt(dx * dx + dy * dy);
        if (d <= 1f) return p;   // already inside

        p.x = center.x + (dx / d) * rx;
        p.y = center.y + (dy / d) * ry;
        return p;
    }

    /// <summary>Clamp against the active bounds, or return the position unchanged if none exist.</summary>
    public static Vector3 ClampToArena(Vector3 p) => Instance != null ? Instance.Clamp(p) : p;

    /// <summary>
    /// How far a point is from the edge, in world units: zero on or outside it. For the ellipse an
    /// approximation along the radius, which is what a unit deciding whether it has room wants.
    /// </summary>
    public float EdgeRoom(Vector3 p)
    {
        if (shape == ArenaShape.Rectangle)
            return Mathf.Max(0f, Mathf.Min(Mathf.Min(p.x - MinX, MaxX - p.x), Mathf.Min(p.y - MinY, MaxY - p.y)));

        float rx = size.x * 0.5f, ry = size.y * 0.5f;
        if (rx <= 0.0001f || ry <= 0.0001f) return 0f;
        float dx = (p.x - center.x) / rx, dy = (p.y - center.y) / ry;
        float d = Mathf.Sqrt(dx * dx + dy * dy);
        if (d >= 1f) return 0f;
        if (d < 0.0001f) return Mathf.Min(rx, ry);   // at the centre: the short radius is the honest answer

        // Distance from p to the edge along the ray from the centre through p. The ellipse's radius
        // in that direction, in world units, is |p - centre| / d, so the room is that radius times
        // (1 - d). It used to be (1 - d) * min(rx, ry), which on a 17 x 4 arena told a unit sitting
        // 2.6 units from the side wall that it had 0.6 — the soft wall then herded both formations
        // into the middle at the bell, and the enemy front rank opened the fight standing inside
        // the company's. The short radius was right on the short axis and four times too small
        // everywhere else.
        float worldRadius = Mathf.Sqrt((p.x - center.x) * (p.x - center.x) + (p.y - center.y) * (p.y - center.y)) / d;
        return worldRadius * (1f - d);
    }

    /// <summary>Room to the edge from a point, against the active bounds; unbounded when there are none.</summary>
    public static float RoomToEdge(Vector3 p) => Instance != null ? Instance.EdgeRoom(p) : float.MaxValue;

    /// <summary>
    /// Set the global bounds, creating the instance if none exists yet. Lets a per-map driver (e.g.
    /// <see cref="BackgroundCycler"/>) push a map's play area without caring about script order.
    /// </summary>
    public static void SetBounds(Vector2 center, Vector2 size, ArenaShape shape = ArenaShape.Rectangle)
    {
        var inst = Instance;
        if (inst == null)
            inst = new GameObject("ArenaBounds (auto)").AddComponent<ArenaBounds>();
        inst.shape = shape;
        inst.center = center;
        inst.size = size;
    }

    private void OnDrawGizmos()
    {
        DrawGizmo(center, size, shape, new Color(0.25f, 0.9f, 1f, 0.8f));
    }

    /// <summary>Draw a bounds outline (rectangle or ellipse) — shared by the per-map gizmo too.</summary>
    public static void DrawGizmo(Vector2 center, Vector2 size, ArenaShape shape, Color color)
    {
        Gizmos.color = color;

        float rx = size.x * 0.5f;
        float ry = size.y * 0.5f;

        if (shape == ArenaShape.Ellipse)
        {
            const int segments = 48;
            Vector3 prev = new Vector3(center.x + rx, center.y, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float a = (i / (float)segments) * Mathf.PI * 2f;
                var next = new Vector3(center.x + Mathf.Cos(a) * rx, center.y + Mathf.Sin(a) * ry, 0f);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
            return;
        }

        var bl = new Vector3(center.x - rx, center.y - ry, 0f);
        var br = new Vector3(center.x + rx, center.y - ry, 0f);
        var tr = new Vector3(center.x + rx, center.y + ry, 0f);
        var tl = new Vector3(center.x - rx, center.y + ry, 0f);
        Gizmos.DrawLine(bl, br);
        Gizmos.DrawLine(br, tr);
        Gizmos.DrawLine(tr, tl);
        Gizmos.DrawLine(tl, bl);
    }
}
