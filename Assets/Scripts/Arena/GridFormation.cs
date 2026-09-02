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

    // ---- a planned move: "where would things stand if this unit were dropped here?"
    //
    // Previews ask the formation as it would be, not as it is, so a unit in the player's hand shows
    // its effects from the cell under it. Nothing moves; the plan is an overlay on the real cells
    // with the same swap rule Place uses — whoever is on the planned cell is treated as standing
    // where the planned unit really is. The fight itself never reads the plan.
    private Entity _planned;
    private Vector2Int _plannedCell;
    private bool _hasPlan;

    public void Plan(Entity entity, Vector2Int cell)
    {
        _planned = entity;
        _plannedCell = cell;
        _hasPlan = entity != null;
    }

    public void ClearPlan()
    {
        _planned = null;
        _hasPlan = false;
    }

    /// <summary>The unit's cell under the plan, or its real cell when it is not the one being planned.</summary>
    public bool TryGetPlannedCell(Entity entity, out Vector2Int cell)
    {
        if (_hasPlan && entity == _planned)
        {
            cell = _plannedCell;
            return true;
        }
        return TryGetCell(entity, out cell);
    }

    /// <summary>Who would stand on a cell once the plan is carried out.</summary>
    public Entity PlannedAt(int column, int row)
    {
        var occupant = At(column, row);
        if (!_hasPlan) return occupant;

        var cell = new Vector2Int(column, row);
        if (cell == _plannedCell) return _planned;

        if (occupant == _planned)
        {
            // The planned unit is leaving this cell; whoever it displaces lands here.
            var displaced = At(_plannedCell.x, _plannedCell.y);
            return displaced != null && displaced != _planned ? displaced : null;
        }

        return occupant;
    }

    /// <summary><see cref="AdjacentTo"/>, under the plan.</summary>
    public List<Entity> PlannedAdjacentTo(Entity entity)
    {
        var result = new List<Entity>();
        if (entity == null || !TryGetPlannedCell(entity, out var cell)) return result;

        var offsets = new[]
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0),
            new Vector2Int(0, 1), new Vector2Int(0, -1)
        };

        foreach (var offset in offsets)
        {
            var neighbour = PlannedAt(cell.x + offset.x, cell.y + offset.y);
            if (neighbour != null && neighbour != entity) result.Add(neighbour);
        }
        return result;
    }

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

    /// <summary>
    /// Units standing orthogonally beside <paramref name="entity"/> — the four cells sharing an edge
    /// with its own. Diagonals are excluded so "adjacent" stays a tight, readable relationship the
    /// player can plan around rather than a blob covering most of the grid.
    ///
    /// Read from the formation rather than measured by distance, so it means the same thing all
    /// fight even after units have chased each other across the arena.
    /// </summary>
    public List<Entity> AdjacentTo(Entity entity)
    {
        var result = new List<Entity>();
        if (entity == null || !_cells.TryGetValue(entity, out var cell)) return result;

        var offsets = new[]
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0),
            new Vector2Int(0, 1), new Vector2Int(0, -1)
        };

        foreach (var offset in offsets)
        {
            var neighbour = At(cell.x + offset.x, cell.y + offset.y);
            if (neighbour != null && neighbour != entity) result.Add(neighbour);
        }
        return result;
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
