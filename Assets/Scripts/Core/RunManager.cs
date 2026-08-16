using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives the run: start a fight, and when it's decided either set up the next one or end the run.
///
/// The loop (Docs/RunLoop.md, Slice 1 — a straight sequence of fights):
/// <code>
///   spawn encounter → fight → victory → revive + heal the company → next encounter
///                          ↘ defeat  → run over
/// </code>
/// Two of the design's decisions are load-bearing here: the company is <b>fully restored between
/// fights</b> (the run's resources are gear and progress, not HP), and <b>only a wipe ends the run</b>
/// — so units that fell in a won fight come back for the next one.
/// </summary>
public class RunManager : MonoBehaviour
{
    [Tooltip("The sequence of fights. Without one, the run loop stays off and the scene behaves as before.")]
    public RunData runData;

    public RunState State { get; private set; }

    private EncounterSpawner _spawner;
    private readonly List<Entity> _company = new List<Entity>();

    /// <summary>Where the company stands before a fight. Survives combat so it can be restored.</summary>
    public GridFormation Formation { get; } = new GridFormation(true);

    /// <summary>True when a run is configured and still going.</summary>
    public bool IsRunning => State != null && State.Outcome == RunOutcome.InProgress;

    /// <summary>
    /// Begin a run: remember the company, then put the first encounter on the board.
    /// <paramref name="company"/> is the player's roster, which persists for the whole run.
    /// </summary>
    public void BeginRun(IEnumerable<Entity> company)
    {
        if (runData == null || runData.encounters == null || runData.encounters.Count == 0)
        {
            Debug.Log("[RunManager] No RunData assigned — leaving the scene's own units in place.");
            return;
        }

        _spawner = GetComponent<EncounterSpawner>();
        if (_spawner == null) _spawner = gameObject.AddComponent<EncounterSpawner>();

        _company.Clear();
        foreach (var unit in company)
        {
            if (unit == null) continue;
            _company.Add(unit);

            // The company lines up on the left facing right, toward the enemy.
            unit.SetFacing(true);

            // Keep the company's GameObjects when they fall, so they can be revived next fight.
            if (unit.DeathFeedback != null) unit.DeathFeedback.persistOnDeath = true;
        }

        // Deploy the company onto its half of the grid. Anyone the player hasn't positioned gets a
        // cell automatically, so a run always starts from a real formation.
        Formation.AutoPlace(_company);

        State = new RunState(runData);
        StartCurrentEncounter();
    }

    /// <summary>Clear the field and spawn whatever fight the run is on.</summary>
    private void StartCurrentEncounter()
    {
        var encounter = State.Current;
        if (encounter == null) return;

        _spawner.ClearEnemies();
        int spawned = _spawner.Spawn(encounter);

        Debug.Log($"[RunManager] {State.Progress} — {encounter.encounterName} ({spawned} enemies).");
    }

    /// <summary>
    /// Called when a fight is decided. <paramref name="won"/> is false when the company was wiped.
    /// Returns true if there's another fight to play.
    /// </summary>
    public bool ResolveEncounter(bool won)
    {
        if (!IsRunning) return false;

        if (!won)
        {
            State.MarkDefeat();
            Debug.Log($"[RunManager] Run over — the company fell on {State.Progress}.");
            return false;
        }

        if (!State.AdvanceAfterVictory())
        {
            Debug.Log("[RunManager] Run won — every encounter cleared.");
            return false;
        }

        RestoreCompany();
        StartCurrentEncounter();
        return true;
    }

    /// <summary>
    /// Patch the company up between fights: the fallen are revived, the survivors healed, and
    /// everyone is put back in fighting shape.
    /// </summary>
    private void RestoreCompany()
    {
        foreach (var unit in _company)
        {
            if (unit == null) continue;

            if (!unit.gameObject.activeSelf) unit.gameObject.SetActive(true);

            // A unit that died mid-fight was left facing whatever killed it.
            unit.SetFacing(true);

            if (unit.Health != null)
            {
                bool wasDead = unit.Health.IsDead;
                if (wasDead)
                {
                    unit.Health.Revive();
                    if (unit.DeathFeedback != null) unit.DeathFeedback.RestoreAfterRevive();
                }
                else
                {
                    unit.Health.HealToFull();
                }
            }

            // Mana starts each fight empty, so ultimates are earned within the fight rather than
            // carried over from the last one.
            if (unit.Mana != null) unit.Mana.currentMana = 0f;
        }

        // Back to the formation the player arranged — units end a fight wherever the chase left them.
        Formation.Prune();
        Formation.SnapAll();
    }
}
