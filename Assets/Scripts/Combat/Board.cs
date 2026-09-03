using System;
using System.Collections.Generic;
using System.Text;

/// <summary>Where one unit was deployed: which side, and which cell.</summary>
public struct Placement
{
    public bool allySide;
    public int column;
    public int row;

    public Placement(bool allySide, int column, int row)
    {
        this.allySide = allySide;
        this.column = column;
        this.row = row;
    }
}

/// <summary>
/// The deployment, as a set of questions with one-word answers (Docs/PositionalKeywords.md).
///
/// One idea drives every word: <b>the first unit in a lane, on either side</b>. Exposed means you
/// are it; Covered means someone else is; Across is the enemy's; the lane preference in targeting
/// is what it does to you. Column 0 is the front rank on both sides; a lane is a row, running from
/// one side's back rank to the other's.
///
/// Generic over the unit type and free of Unity, so it can be tested with strings and read from
/// either the real formation or the one being planned under a dragged hero.
/// </summary>
public class Board<T> where T : class
{
    private readonly Dictionary<T, Placement> _at = new Dictionary<T, Placement>();
    private readonly int _columns;
    private readonly int _rows;

    public Board(int columns = 3, int rows = 3)
    {
        _columns = columns;
        _rows = rows;
    }

    public int Columns => _columns;
    public int Rows => _rows;

    public void Place(T unit, bool allySide, int column, int row)
    {
        if (unit == null) return;
        _at[unit] = new Placement(allySide, column, row);
    }

    public bool TryGet(T unit, out Placement placement) => _at.TryGetValue(unit, out placement);

    public IEnumerable<T> Units => _at.Keys;

    public T At(bool allySide, int column, int row)
    {
        foreach (var pair in _at)
            if (pair.Value.allySide == allySide && pair.Value.column == column && pair.Value.row == row)
                return pair.Key;
        return null;
    }

    // ---- the one idea

    /// <summary>The unit nearest the enemy in a lane on one side — the one that meets what comes down it.</summary>
    public T FirstInLane(bool allySide, int row)
    {
        T first = null;
        int bestColumn = int.MaxValue;
        foreach (var pair in _at)
        {
            if (pair.Value.allySide != allySide || pair.Value.row != row) continue;
            if (pair.Value.column < bestColumn)
            {
                bestColumn = pair.Value.column;
                first = pair.Key;
            }
        }
        return first;
    }

    // ---- deployment words

    public bool IsFront(T unit) => TryGet(unit, out var p) && p.column == 0;

    public bool IsRear(T unit) => TryGet(unit, out var p) && p.column == _columns - 1;

    /// <summary>No ally nearer the enemy in this lane: the first thing the enemy meets in it.</summary>
    public bool IsExposed(T unit) => TryGet(unit, out var p) && ReferenceEquals(FirstInLane(p.allySide, p.row), unit);

    /// <summary>An ally stands nearer the enemy in this lane.</summary>
    public bool IsCovered(T unit) => TryGet(unit, out _) && !IsExposed(unit);

    public bool IsAlone(T unit) => TryGet(unit, out _) && Beside(unit).Count == 0;

    // ---- scopes

    private static readonly (int dc, int dr)[] Orthogonal = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    /// <summary>Allies in the four orthogonally adjacent cells.</summary>
    public List<T> Beside(T unit)
    {
        var result = new List<T>();
        if (!TryGet(unit, out var p)) return result;
        foreach (var (dc, dr) in Orthogonal)
        {
            var neighbour = At(p.allySide, p.column + dc, p.row + dr);
            if (neighbour != null && !ReferenceEquals(neighbour, unit)) result.Add(neighbour);
        }
        return result;
    }

    /// <summary>Allies in the same column, self excluded.</summary>
    public List<T> Rank(T unit) => SameSide(unit, (a, b) => a.column == b.column);

    /// <summary>Allies in the same row, self excluded.</summary>
    public List<T> Lane(T unit) => SameSide(unit, (a, b) => a.row == b.row);

    /// <summary>The first enemy in this unit's lane, or null. Not a mirror: cover blocks it on both sides.</summary>
    public T Across(T unit) => TryGet(unit, out var p) ? FirstInLane(!p.allySide, p.row) : null;

    private List<T> SameSide(T unit, Func<Placement, Placement, bool> match)
    {
        var result = new List<T>();
        if (!TryGet(unit, out var p)) return result;
        foreach (var pair in _at)
            if (!ReferenceEquals(pair.Key, unit) && pair.Value.allySide == p.allySide && match(pair.Value, p))
                result.Add(pair.Key);
        return result;
    }

    // ---- the words, for a card

    /// <summary>"Front · Exposed · Beside 2" — how this unit was deployed, in the player's words.</summary>
    public string Keywords(T unit)
    {
        if (!TryGet(unit, out _)) return "";
        var words = new StringBuilder();
        void Add(string word) { if (words.Length > 0) words.Append(" · "); words.Append(word); }

        if (IsFront(unit)) Add("Front");
        else if (IsRear(unit)) Add("Rear");
        Add(IsExposed(unit) ? "Exposed" : "Covered");
        int beside = Beside(unit).Count;
        Add(beside == 0 ? "Alone" : "Beside " + beside);
        return words.ToString();
    }

    // ---- the opening

    /// <summary>
    /// Whom <paramref name="chooser"/> will engage at the bell: the nearest enemy, with a unit in the
    /// same lane counting <paramref name="laneBonus"/> closer than it is. Lanes are a preference,
    /// not a leash — a clearly closer enemy still wins — which is what lets the setup screen draw
    /// this as a threat line and be right.
    /// </summary>
    public T PredictOpening(T chooser, Func<Placement, Placement, float> distance, float laneBonus)
    {
        if (!TryGet(chooser, out var from)) return null;

        T best = null;
        float bestScore = float.MaxValue;
        foreach (var pair in _at)
        {
            if (pair.Value.allySide == from.allySide) continue;
            float score = distance(from, pair.Value) - (pair.Value.row == from.row ? laneBonus : 0f);
            if (score < bestScore)
            {
                bestScore = score;
                best = pair.Key;
            }
        }
        return best;
    }
}
