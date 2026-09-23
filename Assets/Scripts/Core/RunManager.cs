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
    public void BeginRun(IEnumerable<Entity> company, RunSnapshot resume = null)
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

        State = new RunState(runData, resume != null ? resume.mapSeed : 0);
        if (resume != null) Resume(resume);

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

    /// <summary>
    /// Put a saved run back: every hero on the cell it was saved on, and the run at the fight or
    /// the choice it was saved at. Gear, bag and resonance were restored while the company was
    /// dressed (GameManager reads the same snapshot); this is the part only the run can do.
    /// </summary>
    private void Resume(RunSnapshot resume)
    {
        foreach (var saved in resume.heroes)
        {
            if (saved == null) continue;
            var hero = _company.Find(h => h != null && h.name == saved.name);
            if (hero == null)
            {
                Debug.LogWarning($"[RunSave] Saved hero '{saved.name}' is not in the company — skipped.");
                continue;
            }
            Formation.Place(hero, saved.column, saved.row);
        }

        if (State.IsMapRun)
        {
            var path = new List<Vector2Int>();
            int count = Mathf.Min(resume.pathRows.Count, resume.pathLanes.Count);
            for (int i = 0; i < count; i++) path.Add(new Vector2Int(resume.pathRows[i], resume.pathLanes[i]));

            if (!State.Replay(path, resume.awaitingPath))
                Debug.LogWarning("[RunSave] The saved path no longer fits the act's map — the act's " +
                                 "recipe changed since the save. Starting the act over.");
        }
        else
        {
            State.ResumeAt(resume.encounterIndex);
        }

        Gold = resume.gold;
        _frozen.Clear();
        if (resume.frozenOffers != null) _frozen.AddRange(RunSave.ToItems(resume.frozenOffers));
        Debug.Log($"[RunSave] Resumed {State.Progress} with {Gold} gold (saved {resume.savedAt}).");
    }

    /// <summary>Everything a save needs, as the run stands right now.</summary>
    public RunSnapshot CaptureSnapshot()
    {
        var snapshot = new RunSnapshot
        {
            runAsset = runData != null ? runData.name : "",
            progress = State != null ? State.Progress : "",
            mapSeed = State != null && State.IsMapRun ? State.Map.Seed : 0,
            awaitingPath = State != null && State.AwaitingPath,
            encounterIndex = State != null ? State.EncounterIndex : 0
        };

        if (State != null && State.IsMapRun)
        {
            foreach (var step in State.Path)
            {
                snapshot.pathRows.Add(step.x);
                snapshot.pathLanes.Add(step.y);
            }
        }

        foreach (var hero in _company)
        {
            if (hero == null) continue;
            var saved = new SavedHero { name = hero.name };
            if (Formation.TryGetCell(hero, out var cell))
            {
                saved.column = cell.x;
                saved.row = cell.y;
            }
            var inventory = hero.characterInventory;
            if (inventory != null && inventory.Equipment != null)
                saved.equipped = RunSave.FromItems(inventory.Equipment.Items);
            if (hero.Resonance != null) saved.resonance = hero.Resonance.CaptureState();

            snapshot.heroes.Add(saved);
        }

        var bag = _company.Count > 0 && _company[0] != null && _company[0].characterInventory != null
            ? _company[0].characterInventory.PlayerInventory : null;
        if (bag != null) snapshot.bag = RunSave.FromItems(bag.Items);
        snapshot.gold = Gold;
        snapshot.frozenOffers = RunSave.FromItems(_frozen);

        return snapshot;
    }

    /// <summary>
    /// Write the save if this is a safe point: a run in progress, between fights, with the shop
    /// closed. Not during a fight — a fight interrupted is fought again — and not while the shop is
    /// open, since its offers are not saved and a reload would roll them again.
    /// </summary>
    public void SaveIfSafe()
    {
        if (runData == null || !runData.persist) return;
        if (!IsRunning || ShopOpen) return;
        var game = GameManager.Instance;
        if (game != null && game.StateMachine.Current != GameState.Setup) return;

        RunSave.Write(CaptureSnapshot());
    }

    /// <summary>Clear the field and spawn whatever fight the run is on.</summary>
    private void StartCurrentEncounter()
    {
        // A playtest scenario plays its own encounter every fight, in place of the run's.
        var scenario = Playtest.Scenario;
        var encounter = scenario != null && scenario.encounter != null ? scenario.encounter : State.Current;
        if (encounter == null) return;
        var loadout = scenario != null && scenario.loadout != null ? scenario.loadout : State.CurrentLoadout;

        _spawner.ClearEnemies();
        int spawned = _spawner.Spawn(encounter, loadout);

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
            RunSave.Delete();                          // a finished run is not a run to come back to
            Debug.Log($"[RunManager] Run over — the company fell on {State.Progress}.");
            return false;
        }

        // The shop is stocked from the encounter that was cleared, not the one coming up.
        var cleared = State.Current;
        var shopPool = RewardPoolFor(cleared);
        var rules = ShopRules;

        if (!State.AdvanceAfterVictory())
        {
            RunSave.Delete();
            Debug.Log("[RunManager] Run won — every encounter cleared.");
            return false;
        }

        RestoreCompany();

        // A shop after every won fight that has another after it: the last fight's gold would have
        // nothing to buy.
        OpenShop(shopPool, rules.goldPerFight + rules.winBonus);

        if (State.AwaitingPath)
        {
            // The next fight is the player's to pick. The map shows itself once the shop is left.
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
    /// Take the path to <paramref name="node"/> and stage the fight there. Refused while the shop is
    /// open — it is the fight just won's, and is left before the next is chosen — and for any node
    /// the current one does not lead to.
    /// </summary>
    public bool ChoosePath(MapNode node)
    {
        if (!IsRunning || !AwaitingPath) return false;
        if (ShopOpen) return false;
        if (!State.Choose(node)) return false;

        OnPathChanged?.Invoke();
        StartCurrentEncounter();
        SaveIfSafe();
        return true;
    }

    /// <summary>The heroes of this run, as it was begun.</summary>
    public IReadOnlyList<Entity> Company => _company;

    // ---- the shop (Docs/ShopLoop.md): gold per fight, items at rolled rarities, a reroll

    /// <summary>Gold the company holds. Earned per fight, spent in the shop, kept in the save.</summary>
    public int Gold { get; private set; }

    /// <summary>True from a won fight until the player leaves the shop. The next fight waits.</summary>
    public bool ShopOpen { get; private set; }

    /// <summary>What the shop has on offer, one entry per slot; a null entry has been bought.</summary>
    public List<Assets.HeroEditor.InventorySystem.Scripts.Data.Item> ShopOffers { get; } = new List<Assets.HeroEditor.InventorySystem.Scripts.Data.Item>();

    /// <summary>Raised whenever the shop opens, closes, sells, rerolls, freezes, or the gold changes.</summary>
    public event System.Action OnShopChanged;

    /// <summary>
    /// Whether the offers on the shelf will still be there in the next shop. Set by the player
    /// (<see cref="ToggleFreeze"/>); a freeze holds for one shop, and a reroll lets it go.
    /// </summary>
    public bool ShopFrozen { get; private set; }

    /// <summary>Unsold offers carried from a frozen shop to the next one.</summary>
    private readonly List<Assets.HeroEditor.InventorySystem.Scripts.Data.Item> _frozen = new List<Assets.HeroEditor.InventorySystem.Scripts.Data.Item>();

    private RewardPool _shopPool;
    private ShopSettings ShopRules => runData != null && runData.shop != null ? runData.shop : new ShopSettings();

    /// <summary>What an offer costs: its rarity's price.</summary>
    public int PriceOf(Assets.HeroEditor.InventorySystem.Scripts.Data.Item item) => item == null ? 0 : ShopRules.PriceOf(Rarity.Of(item));

    /// <summary>What a reroll costs.</summary>
    public int RerollCost => ShopRules.rerollCost;

    /// <summary>
    /// Pay for the fight just won and open the shop on the pool that fight names. Public so a check
    /// can open one without winning a fight first; the run itself opens it from ResolveEncounter.
    /// </summary>
    public void OpenShop(RewardPool pool, int income)
    {
        Gold += Mathf.Max(0, income);
        _shopPool = pool != null ? pool : runData != null ? runData.defaultRewardPool : null;
        RollOffers(_frozen);
        _frozen.Clear();
        ShopFrozen = false;
        ShopOpen = true;
        OnShopChanged?.Invoke();
    }

    /// <summary>
    /// Fill the shelf: <paramref name="kept"/> first (a frozen shop's leftovers, at the rarity they
    /// were), then fresh offers for the rest. Each fresh offer is a copy at a rolled rarity, with
    /// better odds the further the run has come (Rarity.OddsAt). Rarity is the grade of an item's
    /// EFFECT, so an item with none is always a C: an S that does nothing more than a C would be a lie
    /// on the card.
    /// </summary>
    private void RollOffers(List<Assets.HeroEditor.InventorySystem.Scripts.Data.Item> kept = null)
    {
        ShopOffers.Clear();
        int slots = Mathf.Max(1, ShopRules.slots);
        if (kept != null)
            foreach (var item in kept)
                if (item != null && ShopOffers.Count < slots) ShopOffers.Add(Rarity.Make(item.Id, Rarity.Of(item)));
        if (_shopPool == null || ShopOffers.Count >= slots) return;
        foreach (var id in _shopPool.Draw(slots - ShopOffers.Count))
        {
            bool hasEffect = ResonanceDatabase.Active != null && ResonanceDatabase.Active.FindFor(new Assets.HeroEditor.InventorySystem.Scripts.Data.Item(id)) != null;
            ShopOffers.Add(Rarity.Make(id, hasEffect ? Rarity.Roll(RunProgress) : Rarity.C));
        }
    }

    /// <summary>Buy the offer in <paramref name="slot"/> into the company's bag, if the gold is there.</summary>
    public bool Buy(int slot)
    {
        if (!ShopOpen || slot < 0 || slot >= ShopOffers.Count || ShopOffers[slot] == null) return false;
        var offer = ShopOffers[slot];
        int price = PriceOf(offer);
        if (Gold < price) return false;

        var inventory = _company.Count > 0 && _company[0] != null ? _company[0].characterInventory : null;
        if (inventory == null || inventory.PlayerInventory == null) return false;

        Gold -= price;
        inventory.PlayerInventory.Items.Add(Rarity.Make(offer.Id, Rarity.Of(offer)));
        inventory.PlayerInventory.Refresh(null);
        ShopOffers[slot] = null;
        OnShopChanged?.Invoke();

        Debug.Log($"[RunManager] Bought {offer.Id} at {Rarity.Letter(Rarity.Of(offer))} for {price}; {Gold} gold left.");
        return true;
    }

    /// <summary>
    /// Replace everything on offer, bought slots included, if the gold is there. A frozen shelf is
    /// let go: the reroll is the player saying they want something else.
    /// </summary>
    public bool Reroll()
    {
        if (!ShopOpen || Gold < RerollCost) return false;
        Gold -= RerollCost;
        ShopFrozen = false;
        RollOffers();
        OnShopChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Freeze or thaw the shelf. Frozen, what is still unsold when the shop closes opens the next
    /// shop at the same rarity and price, and fresh offers fill the slots that were bought. Free:
    /// the cost is the gold held back for it.
    /// </summary>
    public bool ToggleFreeze()
    {
        if (!ShopOpen) return false;
        ShopFrozen = !ShopFrozen;
        OnShopChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Close the shop. What was not bought is gone unless the shelf is frozen; the next fight, or
    /// the map, is next.
    /// </summary>
    public void LeaveShop()
    {
        if (!ShopOpen) return;
        ShopOpen = false;
        _frozen.Clear();
        if (ShopFrozen) foreach (var offer in ShopOffers) if (offer != null) _frozen.Add(offer);
        ShopOffers.Clear();
        OnShopChanged?.Invoke();
        if (AwaitingPath) OnPathChanged?.Invoke();
        SaveIfSafe();
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

    /// <summary>How far through the run the company is: 0 at the first fight, 1 at the last.</summary>
    public float RunProgress => State != null && State.TotalEncounters > 1
        ? Mathf.Clamp01((float)State.EncounterIndex / (State.TotalEncounters - 1)) : 0f;

    /// <summary>
    /// Patch the company up between fights: the fallen are revived, the survivors healed, and
    /// everyone is put back in fighting shape.
    /// </summary>
    public void RestoreCompany()
    {
        foreach (var unit in _company)
        {
            if (unit == null) continue;

            if (!unit.gameObject.activeSelf) unit.gameObject.SetActive(true);

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

            // Toward the enemy — after the revive, which restores the body's size and must not be
            // allowed the last word on which way it looks. A unit that died mid-fight was left
            // facing whatever killed it.
            unit.SetFacing(true);
        }

        // Back to the formation the player arranged — units end a fight wherever the chase left them.
        Formation.Prune();
        Formation.SnapAll();
    }
}
