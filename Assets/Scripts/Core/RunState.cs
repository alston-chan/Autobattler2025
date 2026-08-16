using UnityEngine;

/// <summary>
/// Where the player is in the current run: which fight is next, and whether the run is still going.
/// Pure state with no Unity dependencies beyond the data asset, so it stays easy to reason about as
/// gold, curses and roster changes get added to it later (Docs/RunLoop.md).
/// </summary>
public class RunState
{
    private readonly RunData _data;

    /// <summary>Index of the encounter being fought (0-based).</summary>
    public int EncounterIndex { get; private set; }

    /// <summary>How the run finished, or <see cref="RunOutcome.InProgress"/> while it's still live.</summary>
    public RunOutcome Outcome { get; private set; } = RunOutcome.InProgress;

    public RunState(RunData data)
    {
        _data = data;
    }

    public int TotalEncounters => _data != null && _data.encounters != null ? _data.encounters.Count : 0;

    /// <summary>Human-readable position in the run, e.g. "Fight 2 / 5".</summary>
    public string Progress => $"Fight {Mathf.Min(EncounterIndex + 1, TotalEncounters)} / {TotalEncounters}";

    /// <summary>The encounter to fight now, or null if the run has no more.</summary>
    public EncounterData Current =>
        _data != null && _data.encounters != null &&
        EncounterIndex >= 0 && EncounterIndex < _data.encounters.Count
            ? _data.encounters[EncounterIndex]
            : null;

    /// <summary>
    /// Advance past the fight just won. Returns true if another encounter is waiting; false when
    /// that was the last one, which wins the run.
    /// </summary>
    public bool AdvanceAfterVictory()
    {
        EncounterIndex++;
        if (Current != null) return true;

        Outcome = RunOutcome.Won;
        return false;
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
