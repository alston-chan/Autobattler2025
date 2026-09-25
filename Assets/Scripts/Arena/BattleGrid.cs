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

    /// <summary>
    /// How far the current map has raised the board above its authored rows (<see cref="ArenaBoundsPreset.gridLift"/>).
    /// A rectangular floor sits higher in its painting than the coliseum's sand, so its map lifts the
    /// tiles to the middle of its floor; round maps leave them where they are.
    /// </summary>
    public float Lift { get; private set; }

    private Vector2 _authoredAlly, _authoredEnemy;
    private bool _authoredKnown;

    private void Awake()
    {
        Instance = this;
        RememberAuthored();
    }
    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void RememberAuthored()
    {
        if (_authoredKnown) return;
        _authoredAlly = allyFrontBottom;
        _authoredEnemy = enemyFrontBottom;
        _authoredKnown = true;
    }

    /// <summary>
    /// Raise the board <paramref name="lift"/> above its authored rows. Everyone standing on the field
    /// moves with it, so a unit keeps its cell, and the tiles are rebuilt where the cells now are.
    /// </summary>
    public void SetLift(float lift)
    {
        RememberAuthored();
        float delta = lift - Lift;
        if (Mathf.Approximately(delta, 0f)) return;
        Lift = lift;
        allyFrontBottom = _authoredAlly + Vector2.up * lift;
        enemyFrontBottom = _authoredEnemy + Vector2.up * lift;

        foreach (var e in EntityRegistry.All)
            if (e != null) e.transform.position += Vector3.up * delta;

        var view = GetComponent<BattleGridView>();
        if (view != null)
        {
            bool shown = view.IsVisible;
            view.Build();
            view.SetVisible(shown);
        }
    }

    public bool IsValidCell(int column, int row) =>
        column >= 0 && column < columns && row >= 0 && row < rows;

    /// <summary>
    /// World position of a cell centre. Columns run away from the centre line on each side, so
    /// column 0 is always the rank closest to the enemy.
    ///
    /// The centre, exactly, and nothing else. It used to be squeezed off the arena's walls, away
    /// from a soft wall that has since been removed, so a unit stood beside its tile rather than on
    /// it and the tiles overlapped.
    /// </summary>
    public Vector3 CellToWorld(bool allySide, int column, int row)
    {
        Vector2 origin = allySide ? allyFrontBottom : enemyFrontBottom;
        float dir = allySide ? -1f : 1f;   // allies stack backwards to the left, enemies to the right
        return new Vector3(origin.x + dir * column * cellSize.x, origin.y + row * cellSize.y, 0f);
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
