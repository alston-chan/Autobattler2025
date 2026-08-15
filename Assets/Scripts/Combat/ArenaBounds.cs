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
    public ArenaShape shape = ArenaShape.Rectangle;
    public float minX = -8.5f;
    public float maxX = 8.5f;
    [Tooltip("Floor — the lowest an entity can be pushed. Set to your ground line.")]
    public float minY = -4f;
    [Tooltip("Ceiling — the highest an entity can be. Knockback (e.g. Shockwave) can't launch anyone above this.")]
    public float maxY = 1f;

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
        p.x = Mathf.Clamp(p.x, minX, maxX);
        p.y = Mathf.Clamp(p.y, minY, maxY);
        return p;
    }

    /// <summary>
    /// Clamp into the ellipse inscribed in the box. A point outside is projected radially back onto the
    /// rim — not the exact nearest point, but cheap and visually indistinguishable for gameplay.
    /// </summary>
    private Vector3 ClampEllipse(Vector3 p)
    {
        float cx = (minX + maxX) * 0.5f;
        float cy = (minY + maxY) * 0.5f;
        float rx = (maxX - minX) * 0.5f;
        float ry = (maxY - minY) * 0.5f;
        if (rx <= 0.0001f || ry <= 0.0001f) return ClampRect(p);   // degenerate box

        float dx = (p.x - cx) / rx;
        float dy = (p.y - cy) / ry;
        float d = Mathf.Sqrt(dx * dx + dy * dy);
        if (d <= 1f) return p;   // already inside

        p.x = cx + (dx / d) * rx;
        p.y = cy + (dy / d) * ry;
        return p;
    }

    /// <summary>Clamp against the active bounds, or return the position unchanged if none exist.</summary>
    public static Vector3 ClampToArena(Vector3 p) => Instance != null ? Instance.Clamp(p) : p;

    /// <summary>
    /// Set the global bounds, creating the instance if none exists yet. Lets a per-map driver (e.g.
    /// <see cref="BackgroundCycler"/>) push a map's play area without caring about script order.
    /// </summary>
    public static void SetBounds(float minX, float maxX, float minY, float maxY, ArenaShape shape = ArenaShape.Rectangle)
    {
        var inst = Instance;
        if (inst == null)
            inst = new GameObject("ArenaBounds (auto)").AddComponent<ArenaBounds>();
        inst.shape = shape;
        inst.minX = minX;
        inst.maxX = maxX;
        inst.minY = minY;
        inst.maxY = maxY;
    }

    private void OnDrawGizmos()
    {
        DrawGizmo(minX, maxX, minY, maxY, shape, new Color(0.25f, 0.9f, 1f, 0.8f));
    }

    /// <summary>Draw a bounds outline (rectangle or ellipse) — shared by the per-map gizmo too.</summary>
    public static void DrawGizmo(float minX, float maxX, float minY, float maxY, ArenaShape shape, Color color)
    {
        Gizmos.color = color;

        if (shape == ArenaShape.Ellipse)
        {
            float cx = (minX + maxX) * 0.5f;
            float cy = (minY + maxY) * 0.5f;
            float rx = (maxX - minX) * 0.5f;
            float ry = (maxY - minY) * 0.5f;

            const int segments = 48;
            Vector3 prev = new Vector3(cx + rx, cy, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float a = (i / (float)segments) * Mathf.PI * 2f;
                var next = new Vector3(cx + Mathf.Cos(a) * rx, cy + Mathf.Sin(a) * ry, 0f);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
            return;
        }

        var bl = new Vector3(minX, minY, 0f);
        var br = new Vector3(maxX, minY, 0f);
        var tr = new Vector3(maxX, maxY, 0f);
        var tl = new Vector3(minX, maxY, 0f);
        Gizmos.DrawLine(bl, br);
        Gizmos.DrawLine(br, tr);
        Gizmos.DrawLine(tr, tl);
        Gizmos.DrawLine(tl, bl);
    }
}
