using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Which unit stands in which cell, for one side of the <see cref="BattleGrid"/>.
///
/// The company's formation is the thing the player actually authors, so it has to outlive a fight:
/// units wander once combat starts, and are put back on their cells before the next one. Keeping the
/// assignment here — rather than reading it back off unit positions — is what makes that reset exact,
/// and what will let abilities ask "who is adjacent to me" without measuring distances.
/// </summary>
public class GridFormation
{
    private readonly Dictionary<Entity, Vector2Int> _cells = new Dictionary<Entity, Vector2Int>();
    private readonly bool _allySide;

    public GridFormation(bool allySide) => _allySide = allySide;

    public IEnumerable<KeyValuePair<Entity, Vector2Int>> Placements => _cells;

    /// <summary>The unit standing on a cell, or null.</summary>
    public Entity At(int column, int row)
    {
        foreach (var pair in _cells)
            if (pair.Value.x == column && pair.Value.y == row) return pair.Key;
        return null;
    }

    public bool TryGetCell(Entity entity, out Vector2Int cell) => _cells.TryGetValue(entity, out cell);

    /// <summary>
    /// Put a unit on a cell and move it there. If another unit already holds the cell the two swap,
    /// which keeps every unit deployed — dropping onto an occupied tile should rearrange the
    /// formation, not evict someone out of the fight.
    /// </summary>
    public void Place(Entity entity, int column, int row)
    {
        var grid = BattleGrid.Instance;
        if (entity == null || grid == null || !grid.IsValidCell(column, row)) return;

        var target = new Vector2Int(column, row);
        var occupant = At(column, row);

        if (occupant != null && occupant != entity)
        {
            // Swap: the occupant takes whatever cell the incoming unit is vacating.
            if (_cells.TryGetValue(entity, out var from))
            {
                _cells[occupant] = from;
                occupant.transform.position = grid.CellToWorld(_allySide, from.x, from.y);
            }
            else
            {
                _cells.Remove(occupant);
            }
        }

        _cells[entity] = target;
        entity.transform.position = grid.CellToWorld(_allySide, column, row);
    }

    /// <summary>Move every remembered unit back onto its cell — the between-fight reset.</summary>
    public void SnapAll()
    {
        var grid = BattleGrid.Instance;
        if (grid == null) return;

        foreach (var pair in _cells)
        {
            if (pair.Key == null) continue;
            pair.Key.transform.position = grid.CellToWorld(_allySide, pair.Value.x, pair.Value.y);
        }
    }

    /// <summary>
    /// Give any unit without a cell one, filling the front rank first so a company that was never
    /// arranged still starts in a sensible line rather than stacked on one tile.
    /// </summary>
    public void AutoPlace(IEnumerable<Entity> units)
    {
        var grid = BattleGrid.Instance;
        if (grid == null) return;

        foreach (var unit in units)
        {
            if (unit == null || _cells.ContainsKey(unit)) continue;

            bool placed = false;
            for (int c = 0; c < grid.columns && !placed; c++)
            {
                for (int r = 0; r < grid.rows && !placed; r++)
                {
                    if (At(c, r) != null) continue;
                    Place(unit, c, r);
                    placed = true;
                }
            }

            if (!placed)
                Debug.LogWarning($"[GridFormation] No free cell for {unit.name} — the grid is full.");
        }
    }

    public void Clear() => _cells.Clear();

    /// <summary>Drop units that have been destroyed, so stale entries don't hold cells.</summary>
    public void Prune()
    {
        var dead = new List<Entity>();
        foreach (var pair in _cells)
            if (pair.Key == null) dead.Add(pair.Key);
        foreach (var e in dead) _cells.Remove(e);
    }
}
