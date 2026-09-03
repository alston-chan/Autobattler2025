using UnityEngine;

/// <summary>
/// The live game as a <see cref="Board{T}"/>: the company from its formation — real, or as planned
/// under a dragged hero — and the enemy from where it spawned.
///
/// Also where deployment is frozen. At the bell every unit's lane and column are stamped onto it
/// (<see cref="Entity.DeployedLane"/>), and that stamp is what targeting reads for the rest of the
/// fight. Units scatter the instant combat starts; a lane read live would hand the preference out
/// and take it back as the AI shuffled people, which the player can neither see nor plan
/// (Docs/PositionalKeywords.md, rule 1).
/// </summary>
public static class BoardSnapshot
{
    /// <summary>
    /// Read the board as it stands. With <paramref name="planned"/>, the company is read under the
    /// formation's current plan — a hero in the player's hand counts as standing where it would land.
    /// </summary>
    public static Board<Entity> Capture(GridFormation formation, bool planned)
    {
        var grid = BattleGrid.Instance;
        var board = new Board<Entity>(grid != null ? grid.columns : 3, grid != null ? grid.rows : 3);

        if (formation != null)
        {
            for (int column = 0; column < board.Columns; column++)
                for (int row = 0; row < board.Rows; row++)
                {
                    var hero = planned ? formation.PlannedAt(column, row) : formation.At(column, row);
                    if (hero != null && !hero.isDead) board.Place(hero, true, column, row);
                }
        }

        if (grid != null)
        {
            var all = EntityRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                var enemy = all[i];
                if (enemy == null || enemy.isTeam || enemy.isDead) continue;
                grid.ClosestCell(false, enemy.transform.position, out int column, out int row);
                board.Place(enemy, false, column, row);
            }
        }

        return board;
    }

    /// <summary>
    /// Stamp every unit with the lane and column it was deployed in. Called at the bell; nothing
    /// re-reads position after this until the next fight.
    /// </summary>
    public static void Freeze(GridFormation formation)
    {
        var all = EntityRegistry.All;
        for (int i = 0; i < all.Count; i++)
            if (all[i] != null) all[i].DeployedLane = all[i].DeployedColumn = -1;

        var board = Capture(formation, planned: false);
        foreach (var unit in board.Units)
        {
            if (unit == null || !board.TryGet(unit, out var placement)) continue;
            unit.DeployedLane = placement.row;
            unit.DeployedColumn = placement.column;
        }
    }

    /// <summary>Distance between two cells as the world sees it, so predictions match the fight.</summary>
    public static float WorldDistance(Placement a, Placement b)
    {
        var grid = BattleGrid.Instance;
        if (grid == null) return Mathf.Abs(a.column - b.column) + Mathf.Abs(a.row - b.row);
        return Vector3.Distance(grid.CellToWorld(a.allySide, a.column, a.row),
                                grid.CellToWorld(b.allySide, b.column, b.row));
    }

    /// <summary>Whom a unit on this board will engage at the bell, by the same rule targeting uses.</summary>
    public static Entity PredictOpening(Board<Entity> board, Entity chooser) =>
        board.PredictOpening(chooser, WorldDistance, Targeting.LaneBonus);
}
