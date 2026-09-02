using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives the run: start a fight, and when it's decided either set up the next one or end the run.
///
/// The loop (Docs/RunLoop.md, Slice 1 — a straight sequence of fights):
/// <code>
///   spawn encounter → fight → victory → spoils → revive + heal → next encounter
///                          ↘ defeat  → run over        ↘ on a map: choose a path first
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
        if (runData == null || !runData.HasContent)
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

        // A run begins from a known board. The flat run got this for free, because staging its first
        // fight clears the field; a map run stages nothing until a path is chosen, and the scene's
        // hand-placed test enemies stood on the field beside the map until then.
        _spawner.ClearEnemies();

        if (State.AwaitingPath)
        {
            // A map run opens on the map. Nothing is staged until the player picks where to start.
            Debug.Log($"[RunManager] Map rolled with seed {State.Map.Seed} — choose a path.");
            OnPathChanged?.Invoke();
            return;
        }

        StartCurrentEncounter();
    }

    /// <summary>Clear the field and spawn whatever fight the run is on.</summary>
    private void StartCurrentEncounter()
    {
        var encounter = State.Current;
        if (encounter == null) return;

        _spawner.ClearEnemies();
        int spawned = _spawner.Spawn(encounter, State.CurrentLoadout);

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

        // Offer the spoils of the fight just won, before the next one is staged — the reward is for
        // the encounter that was cleared, not the one coming up.
        OfferRewards(State.Current);

        if (!State.AdvanceAfterVictory())
        {
            Debug.Log("[RunManager] Run won — every encounter cleared.");
            return false;
        }

        RestoreCompany();

        if (State.AwaitingPath)
        {
            // The next fight is the player's to pick. The map shows itself once the spoils are taken.
            OnPathChanged?.Invoke();
            return true;
        }

        StartCurrentEncounter();
        return true;
    }

    /// <summary>True while a map run is waiting for the player to pick the next node.</summary>
    public bool AwaitingPath => State != null && State.AwaitingPath;

    /// <summary>Raised when the run starts or stops waiting on a path choice.</summary>
    public event System.Action OnPathChanged;

    /// <summary>
    /// Take the path to <paramref name="node"/> and stage the fight there. Refused while spoils are
    /// still on offer — the reward is for the fight just won and is settled before the next is
    /// chosen — and for any node the current one does not lead to.
    /// </summary>
    public bool ChoosePath(MapNode node)
    {
        if (!IsRunning || !AwaitingPath) return false;
        if (PendingRewards.Count > 0) return false;
        if (!State.Choose(node)) return false;

        OnPathChanged?.Invoke();
        StartCurrentEncounter();
        return true;
    }

    /// <summary>Items currently on offer from the fight just won. Empty once one is taken.</summary>
    public List<string> PendingRewards { get; } = new List<string>();

    /// <summary>Raised when a victory puts items on offer, and again when the offer is resolved.</summary>
    public event System.Action OnRewardsChanged;

    /// <summary>Roll the choice of drops for a cleared encounter.</summary>
    private void OfferRewards(EncounterData cleared)
    {
        PendingRewards.Clear();

        var pool = RewardPoolFor(cleared);

        if (pool != null)
            PendingRewards.AddRange(pool.Draw(Mathf.Max(1, runData.rewardChoices)));

        OnRewardsChanged?.Invoke();
    }

    /// <summary>
    /// Elites and the boss drop from their own pools when the act names them, so routing through a
    /// harder fight is paid for in kind. Otherwise the encounter's own pool, then the run's.
    /// </summary>
    private RewardPool RewardPoolFor(EncounterData cleared)
    {
        var act = runData.act;
        if (act != null)
        {
            switch (State.CurrentNodeType)
            {
                case NodeType.Boss:
                    if (act.bossRewardPool != null) return act.bossRewardPool;
                    if (act.eliteRewardPool != null) return act.eliteRewardPool;
                    break;
                case NodeType.Elite:
                    if (act.eliteRewardPool != null) return act.eliteRewardPool;
                    break;
            }
        }

        return cleared != null && cleared.rewardPool != null ? cleared.rewardPool : runData.defaultRewardPool;
    }

    /// <summary>
    /// Take one of the offered items into the shared bag, discarding the rest — the choice is the
    /// point, so the ones passed over are gone.
    /// </summary>
    public bool TakeReward(string itemId)
    {
        if (!PendingRewards.Contains(itemId)) return false;

        var inventory = _company.Count > 0 && _company[0] != null
            ? _company[0].characterInventory : null;
        if (inventory == null || inventory.PlayerInventory == null) return false;

        inventory.PlayerInventory.Items.Add(new Assets.HeroEditor.InventorySystem.Scripts.Data.Item(itemId));
        inventory.PlayerInventory.Refresh(null);

        PendingRewards.Clear();
        OnRewardsChanged?.Invoke();

        Debug.Log($"[RunManager] Took {itemId}.");
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
