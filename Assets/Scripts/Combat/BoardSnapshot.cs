using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The live game as a <see cref="Board{T}"/>: the company from its formation — real, or as planned
/// under a dragged hero — and the enemy from where it spawned.
///
/// Also where deployment is frozen. At the bell every unit's lane and column are stamped onto it
/// (<see cref="Entity.DeployedLane"/>), and that stamp is what targeting reads for the opening pick
/// (<see cref="Entity.OpeningPending"/>). Units scatter the instant combat starts; a lane read live
/// would hand the preference out and take it back as the AI shuffled people, which the player can
/// neither see nor plan (Docs/PositionalKeywords.md, rule 0).
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
    /// <summary>The board as frozen at the last bell; null before any fight. What the positional words read from.</summary>
    public static Board<Entity> Last { get; private set; }

    private static readonly List<Entity> None = new List<Entity>();

    /// <summary>Allies orthogonally next to this unit at the bell. Empty when there is no board.</summary>
    public static List<Entity> Beside(Entity unit) => Last != null && unit != null && Last.TryGet(unit, out _) ? Last.Beside(unit) : None;
    /// <summary>Allies in this unit's column at the bell.</summary>
    public static List<Entity> Rank(Entity unit) => Last != null && unit != null && Last.TryGet(unit, out _) ? Last.Rank(unit) : None;
    /// <summary>Allies in this unit's row at the bell.</summary>
    public static List<Entity> Lane(Entity unit) => Last != null && unit != null && Last.TryGet(unit, out _) ? Last.Lane(unit) : None;
    /// <summary>The first enemy in this unit's lane at the bell, or null.</summary>
    public static Entity Across(Entity unit) => Last != null && unit != null && Last.TryGet(unit, out _) ? Last.Across(unit) : null;
    /// <summary>Every enemy in this unit's lane at the bell, front first.</summary>
    public static List<Entity> Facing(Entity unit) => Last != null && unit != null && Last.TryGet(unit, out _) ? Last.Facing(unit) : None;
    /// <summary>Allies this unit stands in front of: same lane, further from the enemy.</summary>
    public static List<Entity> Covered(Entity unit)
    {
        if (Last == null || unit == null || !Last.TryGet(unit, out var mine)) return None;
        var result = new List<Entity>();
        foreach (var other in Last.Lane(unit))
            if (other != null && Last.TryGet(other, out var p) && p.column > mine.column) result.Add(other);
        return result;
    }
    public static bool IsAlone(Entity unit) => Last != null && unit != null && Last.IsAlone(unit);

    /// <summary>
    /// The back line for this unit: the rear cell of its own side in the lane it was deployed in
    /// (its nearest lane now, if it was not deployed). Where a Substitute or a retreat lands.
    /// </summary>
    public static Vector3 RearOf(Entity unit, Vector3 fallback)
    {
        var grid = BattleGrid.Instance;
        if (grid == null || unit == null) return fallback;
        int row = unit.DeployedLane;
        if (row < 0) grid.ClosestCell(unit.isTeam, unit.transform.position, out _, out row);
        return grid.CellToWorld(unit.isTeam, grid.columns - 1, row);
    }
    public static bool IsExposed(Entity unit) => Last != null && unit != null && Last.IsExposed(unit);

    public static void Freeze(GridFormation formation)
    {
        var all = EntityRegistry.All;
        for (int i = 0; i < all.Count; i++)
            if (all[i] != null) { all[i].DeployedLane = all[i].DeployedColumn = -1; all[i].OpeningPending = false; }

        var board = Capture(formation, planned: false);
        Last = board;
        foreach (var unit in board.Units)
        {
            if (unit == null || !board.TryGet(unit, out var placement)) continue;
            unit.DeployedLane = placement.row;
            unit.DeployedColumn = placement.column;
            unit.OpeningPending = true;
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

    /// <summary>
    /// Whom a unit on this board will engage at the bell, by the same rule targeting uses: the
    /// unit's own target mode, scored exactly as <see cref="Targeting.ScoreFor"/> scores it, from
    /// where the units stand. There used to be a same-lane bonus in both, so units charged their
    /// rows; it went with the rest of the targeting knobs (2026-09-22). Plain distance on this grid
    /// still picks the unit straight ahead whenever the enemy's front rank is filled — 2.0 across
    /// against 2.5 on the diagonal — and when it is not, the arrow and the fight agree on the
    /// diagonal instead.
    /// </summary>
    public static Entity PredictOpening(Board<Entity> board, Entity chooser)
    {
        if (board == null || chooser == null || !board.TryGet(chooser, out var from)) return null;
        var mode = chooser.EffectiveTarget;

        Entity best = null;
        float bestScore = float.MaxValue;
        foreach (var candidate in board.Units)
        {
            if (candidate == null || candidate == chooser || !board.TryGet(candidate, out var to) || to.allySide == from.allySide) continue;
            // Nobody is coming for anyone before the bell, so an Attacker reads as plain distance —
            // exactly what Targeting does at the opening pick.
            float score = Targeting.ScoreFor(mode, WorldDistance(from, to), Targeting.HealthFraction(candidate));
            if (score < bestScore) { bestScore = score; best = candidate; }
        }
        return best;
    }
}
