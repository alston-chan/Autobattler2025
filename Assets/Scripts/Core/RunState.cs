using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Where the player is in the current run, and whether it is still going.
///
/// Two shapes of run share this one state. A flat run is an index into an ordered list; a map run
/// is a position on an <see cref="ActMap"/> plus, between fights, the wait for the player to pick
/// the next node. Everything downstream — spawning, spoils, the round outcome — asks both the same
/// questions (<see cref="Current"/>, <see cref="AdvanceAfterVictory"/>), which is what let the map
/// arrive without touching any of it (Docs/RunLoop.md).
///
/// A run is also its own record: the seed the map was rolled from and the path taken across it are
/// everything needed to rebuild the position later, so a save stores those two things and replays
/// them (<see cref="Replay"/>) rather than storing the map.
///
/// Pure state with no Unity dependencies beyond the data assets, so it stays easy to reason about
/// as gold, curses and roster changes get added to it later.
/// </summary>
public class RunState
{
    private readonly RunData _data;
    private bool _awaitingPath;
    private readonly List<Vector2Int> _path = new List<Vector2Int>();

    /// <summary>Index of the encounter being fought (0-based). Flat runs only.</summary>
    public int EncounterIndex { get; private set; }

    /// <summary>How the run finished, or <see cref="RunOutcome.InProgress"/> while it's still live.</summary>
    public RunOutcome Outcome { get; private set; } = RunOutcome.InProgress;

    /// <summary>The act map, or null for a flat run.</summary>
    public ActMap Map { get; }

    /// <summary>The node being fought or just cleared. Null until the first path is chosen.</summary>
    public MapNode CurrentNode { get; private set; }

    /// <summary>Every node chosen so far, in order, as (row, lane). What a save keeps instead of the map.</summary>
    public IReadOnlyList<Vector2Int> Path => _path;

    public bool IsMapRun => Map != null;

    public RunState(RunData data) : this(data, 0) { }

    /// <summary>
    /// <paramref name="seed"/> forces the map's seed — how a saved run rebuilds the exact map it was
    /// on. Zero defers to the run asset: its own seed if set, otherwise a fresh roll.
    /// </summary>
    public RunState(RunData data, int seed)
    {
        _data = data;
        if (data == null || data.act == null) return;

        int chosen = seed != 0 ? seed
                   : data.mapSeed != 0 ? data.mapSeed
                   : Random.Range(1, int.MaxValue);
        Map = MapGenerator.Generate(data.act, chosen);
        _awaitingPath = true;
    }

    public int TotalEncounters => IsMapRun
        ? Map.RowCount
        : (_data != null && _data.encounters != null ? _data.encounters.Count : 0);

    /// <summary>Human-readable position in the run, e.g. "Fight 2 / 5" or "Row 3 / 7".</summary>
    public string Progress => IsMapRun
        ? $"Row {(CurrentNode == null ? 0 : CurrentNode.Row + 1)} / {Map.RowCount}"
        : $"Fight {Mathf.Min(EncounterIndex + 1, TotalEncounters)} / {TotalEncounters}";

    /// <summary>The encounter to fight now, or null if none is staged.</summary>
    public EncounterData Current
    {
        get
        {
            if (IsMapRun) return CurrentNode != null ? CurrentNode.Encounter : null;
            return _data != null && _data.encounters != null &&
                   EncounterIndex >= 0 && EncounterIndex < _data.encounters.Count
                ? _data.encounters[EncounterIndex]
                : null;
        }
    }

    /// <summary>The toughness the map wants the current fight at; null keeps the encounter's own.</summary>
    public EnemyLoadout CurrentLoadout => IsMapRun && CurrentNode != null ? CurrentNode.Loadout : null;

    /// <summary>What kind of stop this is. A flat run is all ordinary fights.</summary>
    public NodeType CurrentNodeType => IsMapRun && CurrentNode != null ? CurrentNode.Type : NodeType.Combat;

    /// <summary>True while a map run waits for the player to pick where to go next.</summary>
    public bool AwaitingPath => IsMapRun && Outcome == RunOutcome.InProgress && _awaitingPath;

    /// <summary>The nodes that can be chosen right now: the bottom row at the start, then wherever
    /// the current node leads.</summary>
    public List<MapNode> AvailableNext
    {
        get
        {
            if (!AwaitingPath) return new List<MapNode>();
            return CurrentNode == null ? new List<MapNode>(Map.Row(0)) : new List<MapNode>(CurrentNode.Next);
        }
    }

    /// <summary>Take the path to <paramref name="node"/>. Refused unless it is one of <see cref="AvailableNext"/>.</summary>
    public bool Choose(MapNode node)
    {
        if (node == null || !AwaitingPath) return false;
        if (!AvailableNext.Contains(node)) return false;

        CurrentNode = node;
        _awaitingPath = false;
        _path.Add(new Vector2Int(node.Row, node.Lane));
        return true;
    }

    /// <summary>
    /// Advance past the fight just won. Returns true if the run goes on — on a map, that means the
    /// next path is now waiting to be chosen; false when that was the last fight, which wins.
    /// </summary>
    public bool AdvanceAfterVictory()
    {
        if (IsMapRun)
        {
            if (CurrentNode == null) return false;
            CurrentNode.Cleared = true;

            if (CurrentNode.Type == NodeType.Boss)
            {
                Outcome = RunOutcome.Won;
                return false;
            }

            _awaitingPath = true;
            return true;
        }

        EncounterIndex++;
        if (Current != null) return true;

        Outcome = RunOutcome.Won;
        return false;
    }

    /// <summary>
    /// Walk a saved <paramref name="path"/> across a map rolled from the same seed, clearing every
    /// node along it. With <paramref name="awaitingPath"/> the last node is cleared too and the
    /// choice is open again; without it the last node is the fight being staged. Returns false if
    /// the path does not fit the map, which means the act's recipe changed since the save.
    /// </summary>
    public bool Replay(IList<Vector2Int> path, bool awaitingPath)
    {
        if (!IsMapRun || path == null) return true;

        for (int i = 0; i < path.Count; i++)
        {
            var row = Map.Row(path[i].x);
            if (path[i].y < 0 || path[i].y >= row.Count) return false;
            if (!Choose(row[path[i].y])) return false;

            bool last = i == path.Count - 1;
            if (!last || awaitingPath) AdvanceAfterVictory();
        }
        return true;
    }

    /// <summary>Pick up a flat run at <paramref name="encounterIndex"/>.</summary>
    public void ResumeAt(int encounterIndex)
    {
        if (IsMapRun) return;
        EncounterIndex = Mathf.Clamp(encounterIndex, 0, Mathf.Max(0, TotalEncounters - 1));
    }

    /// <summary>The company fell — the run is over (Docs/RunLoop.md: a wipe is the fail state).</summary>
    public void MarkDefeat() => Outcome = RunOutcome.Lost;
}

public enum RunOutcome
{
    InProgress,
    Won,
    Lost
}
