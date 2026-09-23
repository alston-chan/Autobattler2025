using System.Collections.Generic;

/// <summary>
/// Where an enemy stands once it is known how it fights. An encounter authors a cell per spawn, but
/// whether a spawn is an archer is rolled at spawn time, and an archer in the front rank is a bad
/// fight: it is the first thing the company reaches, it never gets to shoot from safety, and the melee behind it
/// stand around. So archers muster at the rear of their lane and brawlers keep the front, the way
/// the player arranges their own company. Pure, so it can be tested without a scene.
/// </summary>
public static class EnemyMuster
{
    public struct Unit
    {
        public bool ranged;
        public int column;
        public int row;
        public Unit(bool ranged, int column, int row) { this.ranged = ranged; this.column = column; this.row = row; }
    }

    /// <summary>
    /// The cell each unit ends in, in the order given. Ranged units claim first: the rear column
    /// of their own lane, else the rearmost free cell nearest their lane. Melee units then keep
    /// their authored cell when it is still free, else the frontmost free cell nearest it. No two
    /// units share a cell; a board too small for everyone leaves the overflow where it was authored.
    /// </summary>
    public static List<(int column, int row)> Assign(IReadOnlyList<Unit> units, int columns, int rows)
    {
        var result = new (int column, int row)[units.Count];
        var taken = new HashSet<(int, int)>();
        int rear = columns - 1;

        for (int i = 0; i < units.Count; i++)
        {
            var u = units[i];
            if (!u.ranged) continue;
            var cell = Free(taken, rear, u.row, columns, rows) ? (rear, u.row)
                     : Best(taken, columns, rows, c => -c.column * 100 + System.Math.Abs(c.row - u.row), (u.column, u.row));
            taken.Add(cell);
            result[i] = cell;
        }

        for (int i = 0; i < units.Count; i++)
        {
            var u = units[i];
            if (u.ranged) continue;
            var cell = Free(taken, u.column, u.row, columns, rows) ? (u.column, u.row)
                     : Best(taken, columns, rows, c => c.column * 100 + System.Math.Abs(c.row - u.row) * 10 + System.Math.Abs(c.column - u.column), (u.column, u.row));
            taken.Add(cell);
            result[i] = cell;
        }

        return new List<(int column, int row)>(result);
    }

    private static bool Free(HashSet<(int, int)> taken, int column, int row, int columns, int rows) =>
        column >= 0 && column < columns && row >= 0 && row < rows && !taken.Contains((column, row));

    /// <summary>The free cell with the lowest score, or the fallback when the board is full.</summary>
    private static (int column, int row) Best(HashSet<(int, int)> taken, int columns, int rows,
                                              System.Func<(int column, int row), int> score, (int column, int row) fallback)
    {
        (int column, int row) best = fallback; int bestScore = int.MaxValue; bool found = false;
        for (int c = 0; c < columns; c++)
        for (int r = 0; r < rows; r++)
        {
            if (taken.Contains((c, r))) continue;
            int s = score((c, r));
            if (s < bestScore) { bestScore = s; best = (c, r); found = true; }
        }
        return found ? best : fallback;
    }
}
