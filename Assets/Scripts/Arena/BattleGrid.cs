using UnityEngine;

/// <summary>
/// The deployment grid: two mirrored blocks of cells, the company's on the left and the enemy's on
/// the right. Units are *placed* on it before a fight and reset back to their cells afterwards —
/// once combat starts they move freely, so this shapes the opening formation rather than constraining
/// movement.
///
/// Cells are addressed per side as (column, row). Column 0 is the front rank — the one nearest the
/// enemy — so "front" means the same thing on both sides regardless of which way the grid runs in
/// world space. Row 0 is the bottom. That symmetry is what lets later abilities talk about the
/// opposing lane or an adjacent ally without caring which side is asking.
/// </summary>
public class BattleGrid : MonoBehaviour
{
    /// <summary>The active grid, or null if the scene has none.</summary>
    public static BattleGrid Instance { get; private set; }

    [Header("Shape")]
    public int columns = 3;
    public int rows = 3;
    [Tooltip("Spacing between cell centres. Y is smaller than X because rows read as depth in this " +
             "side-on view, and the playable ground band is short.")]
    public Vector2 cellSize = new Vector2(1.9f, 1.25f);

    [Header("Placement")]
    [Tooltip("Centre of the company's front-rank, bottom row cell. The grid extends left (back " +
             "ranks) and up (higher rows) from here.")]
    public Vector2 allyFrontBottom = new Vector2(-2.2f, -3.2f);
    [Tooltip("Centre of the enemy's front-rank, bottom row cell. Extends right and up.")]
    public Vector2 enemyFrontBottom = new Vector2(2.2f, -3.2f);

    private void Awake() => Instance = this;
    private void OnDestroy() { if (Instance == this) Instance = null; }

    public bool IsValidCell(int column, int row) =>
        column >= 0 && column < columns && row >= 0 && row < rows;

    /// <summary>
    /// World position of a cell centre. Columns run away from the centre line on each side, so
    /// column 0 is always the rank closest to the enemy.
    /// </summary>
    public Vector3 CellToWorld(bool allySide, int column, int row)
    {
        Vector2 origin = allySide ? allyFrontBottom : enemyFrontBottom;
        float dir = allySide ? -1f : 1f;   // allies stack backwards to the left, enemies to the right

        float top = origin.y + (rows - 1) * cellSize.y;
        var cell = new Vector3(origin.x + dir * column * cellSize.x,
                               FitRow(origin.y + row * cellSize.y, origin.y, top),
                               0f);

        return OffTheWall(cell);
    }

    /// <summary>
    /// A row's height, squeezed evenly when the grid is taller than the arena will let it be.
    ///
    /// The grid is one authored thing and the arenas are several: the coliseum is 4.2 tall with a
    /// 1.2 band top and bottom, which leaves 1.8 for a grid whose three rows span 3.0. Clamping each
    /// cell on its own put the bottom and top rows on the band's edges and left the middle where it
    /// was — gaps of 1.1 and 0.7, bodies overlapping, and a jostle at the bell as the scrum sorted
    /// it out. Scaling the whole span into the band keeps the rows evenly spaced and in order, which
    /// is what a formation is. Only the rectangle knows its band as two numbers; a round arena is
    /// left to <see cref="OffTheWall"/>, which pulls along the radius and keeps ranks anyway.
    /// </summary>
    private static float FitRow(float y, float lowestRow, float highestRow)
    {
        var arena = ArenaBounds.Instance;
        if (arena == null || arena.shape != ArenaShape.Rectangle) return y;

        var physics = CombatPhysics.Active;
        if (physics == null || !physics.enableBodies || physics.softWallPush <= 0f) return y;

        float bandLow = arena.MinY + physics.softWall, bandHigh = arena.MaxY - physics.softWall;
        float span = highestRow - lowestRow, band = bandHigh - bandLow;
        if (span <= 0.0001f || band <= 0.0001f || span <= band) return y;

        return bandLow + (y - lowestRow) / span * band;
    }

    /// <summary>
    /// The cell, pulled in far enough that the soft wall will leave a unit standing there alone.
    ///
    /// The grid is authored in its own coordinates and the arena is authored separately, so nothing
    /// made them agree: a back-rank cell could sit inside the band that <see cref="CombatPhysics"/>
    /// pushes inward from. The unit was then slid toward the centre at the bell with no walk
    /// animation, because the animation follows the AI's intent and the shove is not the AI's doing.
    /// It read as broken pathing and was reported as such.
    ///
    /// Doing it here rather than at each caller is deliberate — placement, the encounter spawner,
    /// the board snapshot's distances and the formation preview all come through this one function,
    /// so they cannot disagree about where a cell is.
    ///
    /// It is not sufficient on its own, which is why it is public: the arena is resized per map by
    /// <see cref="BackgroundCycler"/>, so a cell that was clear when the formation was set can be
    /// inside the band by the time the fight starts. The bell applies this again, and that is the
    /// one that actually holds.
    ///
    /// A unit will not sit exactly where this puts it: bodies push each other apart every frame, so
    /// the outermost of a packed formation is shoved back toward the band by its neighbours and
    /// settles a little inside it. Measured at the bell — seated on the line, units come to rest at
    /// about 1.14 against a 1.20 band, which is a residual push of under a tenth of a unit per
    /// second. Granting extra margin does not move that: the formation is wider than the arena's
    /// safe area, so a bigger margin only stacks everyone on the boundary for the scrum to expand
    /// again. The equilibrium is the system working; what mattered was the 0.41 that preceded it.
    /// </summary>
    public static Vector3 OffTheWall(Vector3 cell)
    {
        var arena = ArenaBounds.Instance;
        if (arena == null) return cell;

        var physics = CombatPhysics.Active;
        if (physics == null || !physics.enableBodies || physics.softWallPush <= 0f) return cell;

        return arena.ClampInside(cell, physics.softWall);
    }

    /// <summary>
    /// The cell nearest <paramref name="world"/> on the given side, clamped into the grid. Used when
    /// dropping a unit — a drop slightly outside still lands somewhere sensible rather than failing.
    /// </summary>
    public void ClosestCell(bool allySide, Vector3 world, out int column, out int row)
    {
        Vector2 origin = allySide ? allyFrontBottom : enemyFrontBottom;
        float dir = allySide ? -1f : 1f;

        column = Mathf.RoundToInt((world.x - origin.x) / (cellSize.x * dir));
        row = Mathf.RoundToInt((world.y - origin.y) / cellSize.y);

        column = Mathf.Clamp(column, 0, columns - 1);
        row = Mathf.Clamp(row, 0, rows - 1);
    }

    /// <summary>True if a world point falls within the company's half of the field.</summary>
    public bool IsOnAllySide(Vector3 world) => world.x < CentreLine;

    /// <summary>The dividing line between the two halves.</summary>
    public float CentreLine => (allyFrontBottom.x + enemyFrontBottom.x) * 0.5f;

    private void OnDrawGizmos()
    {
        DrawSide(true, new Color(0.3f, 0.8f, 1f, 0.85f));
        DrawSide(false, new Color(1f, 0.4f, 0.35f, 0.85f));
    }

    private void DrawSide(bool allySide, Color color)
    {
        Gizmos.color = color;
        var size = new Vector3(cellSize.x * 0.86f, cellSize.y * 0.7f, 0f);

        for (int c = 0; c < columns; c++)
        {
            for (int r = 0; r < rows; r++)
                Gizmos.DrawWireCube(CellToWorld(allySide, c, r), size);
        }
    }
}
