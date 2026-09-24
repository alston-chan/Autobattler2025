using System.Collections.Generic;
using Assets.HeroEditor.InventorySystem.Scripts.Data;
using UnityEngine;

/// <summary>
/// A hero's resonance: each worn item's quest progress, and the engravings the hero has kept for
/// good (Docs/ShopLoop.md).
///
/// The loop is <c>buy an item at a rarity → wear it → its quest fills while it fights → once it is
/// complete, the player may bank it between fights: its effect is kept on the hero at the item's
/// rarity and the item is hollowed</c> — still worn, still of its class, but giving nothing further,
/// so the slot is the player's to fill. Progress is per (hero, item copy) and <b>pauses</b> when an
/// item comes off rather than resetting.
///
/// Banking is the player's decision, never automatic: it spends the item. Any item with an effect
/// banks; a weapon's effect is its verb, so a banked weapon puts one more verb in the hero's slots to
/// choose between. A weapon's verb otherwise goes with the weapon. A worn effect and a banked one go
/// through the same path, so a worn B and a banked B behave identically.
///
/// This component also owns each hero's private copies of the engravings affecting them — see
/// <see cref="InstanceFor"/> — which is what lets an engraving be written with ordinary fields.
/// </summary>
public class Resonance : MonoBehaviour
{
    /// <summary>An engraving this hero has absorbed permanently, at the tier it was banked.</summary>
    [System.Serializable]
    public class Banked
    {
        [Tooltip("The engraving ASSET. Applying goes through this hero's private copy of it.")]
        public Engraving engraving;
        public int tier;
        [Tooltip("The item it was banked from, so the Abilities row can draw the weapon.")]
        public string itemId;
    }

    /// <summary>
    /// Attunement per item, keyed by what the item IS — its id and modifier — rather than by the
    /// object holding it.
    ///
    /// Keying by object was tried and is wrong, because no object survives being moved:
    /// <c>ItemWorkspace.MoveItemSilent</c> adds <c>new Item(...)</c> to the destination and drops the
    /// original, so equipping and unequipping each mint a fresh instance. Instance keys therefore
    /// wiped every item's progress the moment it came off — measured at 25 attunement before an
    /// unequip and 0 after — which is the exact opposite of the promise that taking something off
    /// only pauses it.
    ///
    /// The cost is that two identical copies share one pool of progress. That is the lesser evil by
    /// a wide margin: only worn items accrue, so the shared case is a spare in the bag inheriting
    /// progress on a swap, against the alternative of every player losing everything on every swap.
    /// It is also what makes the state writable to a save file at all — see <see cref="CaptureState"/>.
    /// </summary>
    private readonly Dictionary<string, float> _attunement = new Dictionary<string, float>();

    /// <summary>
    /// Items whose progress the player has not looked at yet, and what kind of news each carries.
    ///
    /// Keyed by the same descriptor as attunement, and for the same reason: equipping or unequipping
    /// mints a fresh Item object, so an object key would drop the mark the moment a player pulled the
    /// item off to look at it.
    ///
    /// There is deliberately NO hero-level flag. <see cref="HasUnseen"/> is derived from this set, so
    /// "the hero's badge clears when the last item's does" is true by construction rather than by two
    /// pieces of bookkeeping agreeing with each other.
    /// </summary>
    private readonly Dictionary<string, ResonanceNotice> _notices =
        new Dictionary<string, ResonanceNotice>();

    /// <summary>Raised when any item's unread mark appears or clears, so badges can follow.</summary>
    public event System.Action OnNoticesChanged;

    /// <summary>
    /// Fired at the end of a <see cref="Refresh"/> that granted or revoked anything. The inventory
    /// rebuilds the hero's spell slots from here, once, after the books are settled.
    /// </summary>
    public event System.Action OnGrantsChanged;

    /// <summary>Whether this hero has anything unlooked-at — the hero-level badge.</summary>
    public bool HasUnseen => MostUrgentNotice != ResonanceNotice.None;

    /// <summary>
    /// The most urgent thing any WORN item is waiting to say, so the hero-level badge reports a
    /// pending decision rather than burying it under a routine tier-up.
    ///
    /// Worn only, because a badge the player cannot act on is worse than none. Marks are cleared by
    /// clicking the item, and only worn items get a slot to click — so counting an unworn one left
    /// the hero permanently flagged with nothing to open. That is easy to reach: equipping a
    /// replacement displaces the old item without ever selecting it, since the item being clicked is
    /// the new one.
    ///
    /// The mark itself is kept rather than discarded, so putting the item back on brings its news
    /// back with it instead of quietly losing what it had to say.
    /// </summary>
    public ResonanceNotice MostUrgentNotice
    {
        get
        {
            var worst = ResonanceNotice.None;

            foreach (var item in WornItems())
            {
                if (item == null) continue;
                if (_notices.TryGetValue(Descriptor(item), out var notice) && notice > worst)
                    worst = notice;
            }

            return worst;
        }
    }

    /// <summary>What this item is waiting to tell the player, if anything.</summary>
    public ResonanceNotice NoticeFor(Item item) =>
        item != null && _notices.TryGetValue(Descriptor(item), out var notice)
            ? notice : ResonanceNotice.None;

    /// <summary>
    /// Acknowledge an item's news. Called when the player selects it, which is the moment they have
    /// actually seen what it had to say. An item ready to bank keeps its mark until it is banked:
    /// that is a decision still waiting, not news.
    /// </summary>
    public void MarkSeen(Item item)
    {
        if (item == null) return;
        string key = Descriptor(item);
        if (_notices.TryGetValue(key, out var notice) && notice == ResonanceNotice.Bankable) return;
        if (_notices.Remove(key)) OnNoticesChanged?.Invoke();
    }

    /// <summary>
    /// Flag an item, keeping the more urgent of what it already carried and what just happened —
    /// an item that tiered up AND became engravable should show the decision, not the news.
    /// </summary>
    private void Raise(string key, ResonanceNotice notice)
    {
        if (_notices.TryGetValue(key, out var current) && current >= notice) return;
        _notices[key] = notice;
        OnNoticesChanged?.Invoke();
    }

    /// <summary>
    /// This hero's private copies of engraving assets, one per GRANT rather than one per asset — see
    /// <see cref="InstanceFor"/>. Two items carrying the same engraving need separate copies or they
    /// would share both their per-bearer fields and their modifier source, and so could not stack.
    /// </summary>
    private readonly Dictionary<string, Engraving> _instances = new Dictionary<string, Engraving>();

    [Tooltip("Permanently absorbed engravings. These outlive the items that carried them.")]
    public List<Banked> banked = new List<Banked>();

    private Entity _entity;

    public void Initialize(Entity entity) => _entity = entity;

    private void OnDestroy()
    {
        foreach (var instance in _instances.Values)
            if (instance != null) Destroy(instance);
        _instances.Clear();
    }

    /// <summary>
    /// This hero's own copy of an engraving asset, created on first use.
    ///
    /// An engraving asset is shared by every hero carrying it, so any per-bearer state written as an
    /// ordinary field — "who did I buff", "have I triggered yet" — would be clobbered by the next
    /// bearer, and grants would never be taken back. Giving each hero a private copy makes the
    /// natural way of writing an engraving correct, instead of requiring every author to remember
    /// the trap.
    ///
    /// It also sharpens stat bookkeeping: modifiers are sourced by the copy, so removing one hero's
    /// grants can't disturb another's.
    /// </summary>
    private Engraving InstanceFor(string sourceKey, Engraving asset)
    {
        if (asset == null) return null;
        if (_instances.TryGetValue(sourceKey, out var existing) && existing != null) return existing;

        var copy = Instantiate(asset);
        copy.name = asset.name + " (" + name + " / " + sourceKey + ")";
        _instances[sourceKey] = copy;
        return copy;
    }

    public float AttunementFor(Item item) =>
        item != null && _attunement.TryGetValue(Descriptor(item), out float value) ? value : 0f;

    /// <summary>The tier an item's effect works at while worn — its rarity — or 0 if it doesn't resonate.</summary>
    public int TierFor(Item item) => EntryFor(item) == null ? 0 : Rarity.Of(item);

    /// <summary>Raised when any worn item's attunement changes, so UI can follow it live.</summary>
    public event System.Action OnAttunementChanged;

    /// <summary>
    /// Whether this item has a quest: it carries an effect and is not yet spent.
    /// </summary>
    public bool HasQuest(Item item) => QuestOf(item) != null;

    /// <summary>The item's entry when it has a quest (<see cref="HasQuest"/>), else null.</summary>
    public ResonanceDatabase.Entry QuestOf(Item item)
    {
        var entry = EntryFor(item);
        return entry != null && entry.engraving != null ? entry : null;
    }

    /// <summary>
    /// Credit <paramref name="amount"/> toward every worn item whose quest counts
    /// <paramref name="requirement"/>. Items counting something else are untouched, so a hero wearing
    /// a kill-counting blade and a block-counting shield advances each on its own terms. A quest that
    /// completes here raises <see cref="ResonanceNotice.Bankable"/>, so the player sees it is ready.
    /// </summary>
    public void Accrue(ResonanceRequirement requirement, float amount)
    {
        if (amount <= 0f) return;

        bool changed = false;

        foreach (var item in WornItems())
        {
            var entry = QuestOf(item);
            if (entry == null || entry.requirement != requirement) continue;

            // Two worn copies of one item share a key, so credit it once rather than twice.
            string key = Descriptor(item);
            if (!_credited.Add(key)) continue;

            _attunement.TryGetValue(key, out float current);
            _attunement[key] = current + amount;
            changed = true;

            if (!entry.IsComplete(current) && entry.IsComplete(current + amount))
                Raise(key, ResonanceNotice.Bankable);
        }
        _credited.Clear();

        // Progress changes nothing about what the item does — its rarity decides that — so there
        // is nothing to reconcile here.
        if (changed) OnAttunementChanged?.Invoke();
    }

    /// <summary>
    /// The banked marks that are verbs — a weapon's, banked — in the order they were banked. These fill
    /// the Abilities row and the skill slots after the hand weapon's verb.
    /// </summary>
    public List<Banked> BankedAbilities()
    {
        var abilities = new List<Banked>();
        foreach (var mark in banked)
            if (mark != null && mark.engraving is GrantSpellEngraving grant && grant.spell != null) abilities.Add(mark);
        return abilities;
    }

    /// <summary>Whether the Abilities row is full; a weapon cannot be banked while it is.</summary>
    public bool AbilitySlotsFull => BankedAbilities().Count >= Entity.MaxBankedAbilities;

    /// <summary>The end of a fight: credit it to quests counting combats.</summary>
    public void AccrueAfterCombat() => Accrue(ResonanceRequirement.CombatsWorn, 1f);

    /// <summary>
    /// Whether the player can bank this item now: its quest is complete, it is worn, and no fight is
    /// on. Heroes only — banking hollows the item through the inventory window, and an enemy has
    /// none. Between fights only, because hollowing a weapon mid-swing or armour mid-hit would take
    /// it away in the fight that earned it.
    /// </summary>
    public bool CanBank(Item item) => Ready(item) && !(QuestOf(item).engraving is GrantSpellEngraving && AbilitySlotsFull);

    /// <summary>
    /// Whether this weapon is ready to bank but the Abilities row is full, so banking it means
    /// replacing one of the three (<see cref="Bank(Item, Banked)"/>).
    /// </summary>
    public bool MustReplaceToBank(Item item) => Ready(item) && QuestOf(item).engraving is GrantSpellEngraving && AbilitySlotsFull;

    /// <summary>Complete, worn, and no fight on — everything banking asks except room in the row.</summary>
    private bool Ready(Item item)
    {
        var entry = QuestOf(item);
        if (entry == null || _inCombat || !entry.IsComplete(AttunementFor(item))) return false;
        var inventory = _entity != null ? _entity.characterInventory : null;
        return inventory != null && inventory.Equipment != null && inventory.Equipment.Items.Contains(item);
    }

    /// <summary>
    /// Bank an item whose quest is complete: its effect is kept on the hero for good at the item's
    /// rarity — a weapon's, its verb — and the item is hollowed. The player's decision, never
    /// automatic: keeping a complete item worn keeps its stats, and banking spends them. Returns
    /// whether it banked.
    /// </summary>
    public bool Bank(Item item) => Bank(item, null);

    /// <summary>
    /// Bank a weapon into the full Abilities row in place of <paramref name="replace"/>, one of
    /// <see cref="BankedAbilities"/>: the new verb takes its slot and the old one is gone for good.
    /// With no replacement this is <see cref="Bank(Item)"/>. Returns whether it banked.
    /// </summary>
    public bool Bank(Item item, Banked replace)
    {
        int slot = replace != null ? banked.IndexOf(replace) : -1;
        if (replace != null)
        {
            if (!MustReplaceToBank(item) || slot < 0 || !(replace.engraving is GrantSpellEngraving)) return false;
        }
        else if (!CanBank(item)) return false;

        var entry = QuestOf(item);
        int tier = Rarity.Of(item);
        var mark = new Banked { engraving = entry.engraving, tier = tier, itemId = item.Id };

        // In place, so the row keeps its order and the refresh below revokes the old verb's grant
        // (its source key now names a different engraving) as it grants the new one.
        if (slot >= 0) banked[slot] = mark;
        else banked.Add(mark);

        // Read the key BEFORE hollowing: hollowing changes the item's modifier, and so its
        // descriptor, and the progress being cleared is filed under the old one.
        string spentKey = Descriptor(item);
        _entity.characterInventory.HollowItem(item);
        _attunement.Remove(spentKey);
        if (_notices.Remove(spentKey)) OnNoticesChanged?.Invoke();
        OnAttunementChanged?.Invoke();

        Debug.Log($"[Resonance] {_entity.name} banked {entry.engraving.DisplayName} at {Rarity.Letter(tier)}" +
                  (replace != null ? $" in place of {replace.engraving.DisplayName}." : "."));
        return true;
    }

    /// <summary>One engraving grant: which asset, at what tier.</summary>
    private struct Grant
    {
        public Engraving asset;
        public int tier;
    }

    /// <summary>
    /// Grants currently applied, keyed by SOURCE — a particular worn item, or a particular banked
    /// mark — rather than by engraving.
    ///
    /// Two items carrying the same engraving are two grants and both apply. Keying by engraving
    /// collapsed them into one, so putting on a second Swift item moved nothing: measured at 1.25
    /// attacks/sec wearing a Swift bow, and still 1.25 after adding a Swift hat that was genuinely
    /// equipped. Engraving cannot double-count either way, because it hollows the item — an engraved
    /// mark and a worn item of one engraving are always two things the hero went and got.
    /// </summary>
    private readonly Dictionary<string, Grant> _active = new Dictionary<string, Grant>();

    private readonly List<string> _stale = new List<string>();

    /// <summary>Scratch set so one Accrue call credits each distinct item once.</summary>
    private readonly HashSet<string> _credited = new HashSet<string>();

    /// <summary>
    /// Bring the engravings acting on this hero in line with what they are wearing and have banked.
    ///
    /// Engravings used to be opened and closed on the combat transitions alone, which meant an item
    /// equipped between fights did nothing until the next one started — the player put a bow on,
    /// watched the stat not move, and had no way to tell whether it had worked. Reconciling against
    /// what is actually worn means the grant lands when the item does.
    ///
    /// Applying is idempotent because this tracks what it has already applied, so equipping cannot
    /// stack a second copy of a grant that is already live.
    ///
    /// <paramref name="force"/> reapplies everything from scratch. Combat boundaries use it because
    /// some engravings read the world around them when they open — Bulwark buffs whoever is standing
    /// beside the bearer — and that reading goes stale when the formation is rearranged.
    /// </summary>
    public void Refresh()
    {
        var desired = DesiredGrants();

        // A source that is gone, or now owed a different tier or a different engraving, is taken
        // back before its replacement goes on — otherwise a change would leave both applied.
        _stale.Clear();
        foreach (var pair in _active)
        {
            if (!desired.TryGetValue(pair.Key, out var want) ||
                want.tier != pair.Value.tier || want.asset != pair.Value.asset)
            {
                _stale.Add(pair.Key);
            }
        }

        // The books are updated BEFORE the engraving hears about it, so anything an OnGranted or
        // OnRevoked asks of this hero — GrantedVerbs, TierOfVerb — answers for the state it is being
        // told about. It used to be the other way round, and a verb's OnGranted rebuilt the spell
        // slots from a list its own grant was not yet in: the sixth cast of a fight crossed tier II,
        // the grant was revoked and re-granted at the new tier, and the hero fought on with no
        // ability at all.
        bool changed = _stale.Count > 0;
        for (int i = 0; i < _stale.Count; i++)
        {
            var grant = _active[_stale[i]];
            _active.Remove(_stale[i]);
            Invoke(_stale[i], grant, false);
        }

        foreach (var pair in desired)
        {
            if (_active.ContainsKey(pair.Key)) continue;
            _active[pair.Key] = pair.Value;
            Invoke(pair.Key, pair.Value, true);
            changed = true;
        }

        if (changed) OnGrantsChanged?.Invoke();

        // Reconciling happens on every equipment change, and what the badges show depends on what is
        // worn — so this is also the moment they may need to appear or disappear.
        OnNoticesChanged?.Invoke();
    }

    /// <summary>
    /// Every grant this hero should be under, keyed by where it comes from: one entry per worn
    /// resonant item at whatever tier it has attuned to, and one per banked mark. Both arrive by the
    /// same route, so a worn engraving and a banked one behave identically in play.
    ///
    /// Two worn copies of the SAME item share a key and so grant once, which matches attunement —
    /// they share one pool of progress too, being indistinguishable by anything the game records.
    /// </summary>
    private Dictionary<string, Grant> DesiredGrants()
    {
        var desired = new Dictionary<string, Grant>();

        foreach (var item in EquippedResonantItems())
        {
            var entry = EntryFor(item);
            if (entry == null || entry.engraving == null) continue;

            // A worn effect applies from the moment it goes on, at the item's rarity.
            desired["worn:" + Descriptor(item)] = new Grant
            {
                asset = entry.engraving,
                tier = Rarity.Of(item)
            };
        }

        for (int i = 0; i < banked.Count; i++)
        {
            var mark = banked[i];
            if (mark == null || mark.engraving == null) continue;
            desired["banked:" + i] = new Grant { asset = mark.engraving, tier = mark.tier };
        }

        if (_innate != null)
            for (int i = 0; i < _innate.Count; i++)
                if (_innate[i] != null) desired["innate:" + i] = new Grant { asset = _innate[i], tier = 1 };

        return desired;
    }

    /// <summary>
    /// Open or close everything for a fight. Kept as the combat-boundary entry point; both directions
    /// reconcile from scratch, which clears out per-fight state while leaving the grants a hero has
    /// earned by wearing something in place.
    /// </summary>
    /// <summary>
    /// The fight boundary. Every held engraving gets its combat hook here, all at once, after the
    /// formation is set — and its end hook when the fight ends.
    ///
    /// This used to be Refresh with a flag, and Refresh invoked OnCombatStart the moment a grant
    /// appeared: when the item was equipped, in the setup screen, with the flag ignored. So a
    /// positional engraving read the board as it stood at equip time and never again — dragging the
    /// hero to another cell moved nothing, and Marked wounded an enemy before the fight had begun.
    /// Holding an engraving and fighting with it are two different moments now.
    /// </summary>
    public void ApplyForCombat(bool starting)
    {
        if (starting)
        {
            Refresh();                       // settle what is worn and banked, without fighting yet
            _inCombat = true;
            Listen(true);
            foreach (var pair in _active) Fight(pair.Key, pair.Value, true);
        }
        else
        {
            foreach (var pair in _active) Fight(pair.Key, pair.Value, false);
            Listen(false);
            _inCombat = false;
        }
    }

    private bool _inCombat;

    /// <summary>Whether a fight is on for this hero — banking waits for it to end.</summary>
    public bool InCombat => _inCombat;
    private bool _listening;

    // ---- the bus, routed to what this hero holds. One subscription per hero per fight; the
    // engravings just override a hook.

    private void Listen(bool on)
    {
        if (on == _listening) return;
        _listening = on;
        if (on)
        {
            CombatEvents.Hit += RouteHit;
            CombatEvents.Kill += RouteKill;
            CombatEvents.Cast += RouteCast;
            CombatEvents.Moved += RouteMoved;
            CombatEvents.Blink += RouteBlink;
            CombatEvents.ShieldEnded += RouteShieldEnded;
        }
        else
        {
            CombatEvents.Hit -= RouteHit;
            CombatEvents.Kill -= RouteKill;
            CombatEvents.Cast -= RouteCast;
            CombatEvents.Moved -= RouteMoved;
            CombatEvents.Blink -= RouteBlink;
            CombatEvents.ShieldEnded -= RouteShieldEnded;
        }
    }

    private void OnDisable() => Listen(false);

    private void Route(System.Action<Engraving, int> call)
    {
        // A hook may change what is held (a bank mid-fight), and a hook may raise another event
        // (a Substitute's blink is a Moved inside a Damaged), so route over a copy of its own — a
        // shared scratch list was cleared under the outer loop by the nested one.
        var routing = new List<KeyValuePair<string, Grant>>(_active);
        foreach (var pair in routing)
        {
            var engraving = InstanceFor(pair.Key, pair.Value.asset);
            if (engraving != null) call(engraving, pair.Value.tier);
        }
    }

    private void RouteHit(HitInfo hit)
    {
        if (hit.source == _entity) Route((e, t) => e.OnHit(_entity, hit, t));
        if (hit.target == _entity) Route((e, t) => e.OnDamaged(_entity, hit, t));
    }

    private void RouteKill(Entity killer, Entity victim)
    {
        if (killer == _entity) Route((e, t) => e.OnKill(_entity, victim, t));
    }

    private void RouteCast(Entity caster, Spell spell)
    {
        if (caster == _entity) Route((e, t) => e.OnCast(_entity, spell, t));
    }

    private void RouteMoved(Entity entity, float distance)
    {
        if (entity == _entity) Route((e, t) => e.OnMoved(_entity, distance, t));
    }

    private void RouteBlink(Entity entity, Vector3 from, Vector3 to)
    {
        if (entity == _entity) Route((e, t) => e.OnBlink(_entity, from, to, t));
    }

    private void RouteShieldEnded(Entity entity, bool broken)
    {
        if (entity == _entity) Route((e, t) => e.OnShieldEnded(_entity, broken, t));
    }

    /// <summary>A grant coming or going. Mid-fight, that includes its combat hook.</summary>
    private void Invoke(string sourceKey, Grant grant, bool granting)
    {
        var engraving = InstanceFor(sourceKey, grant.asset);
        if (engraving == null) return;

        if (granting)
        {
            engraving.OnGranted(_entity, grant.tier);
            if (_inCombat) engraving.OnCombatStart(_entity, grant.tier);
        }
        else
        {
            if (_inCombat) engraving.OnCombatEnd(_entity, grant.tier);
            engraving.OnRevoked(_entity, grant.tier);
        }
    }

    /// <summary>The verbs the held weapons and banked weapon-marks teach (<see cref="GrantSpellEngraving"/>), in grant order.</summary>
    public List<Spell> GrantedVerbs()
    {
        var verbs = new List<Spell>();
        foreach (var pair in _active)
        {
            var grant = InstanceFor(pair.Key, pair.Value.asset) as GrantSpellEngraving;
            if (grant != null && grant.spell != null && !verbs.Contains(grant.spell)) verbs.Add(grant.spell);
        }
        return verbs;
    }

    /// <summary>The tier this verb is held at — the highest, if two grants teach it — else 1.</summary>
    public int TierOfVerb(Spell spell)
    {
        int best = 0;
        foreach (var pair in _active)
        {
            var grant = InstanceFor(pair.Key, pair.Value.asset) as GrantSpellEngraving;
            if (grant != null && grant.spell == spell) best = Mathf.Max(best, pair.Value.tier);
        }
        return best > 0 ? best : 1;
    }

    /// <summary>What every held engraving would do if the fight began now (<see cref="Engraving.Preview"/>).</summary>
    public void CollectPreviews(List<Engraving.Badge> into)
    {
        foreach (var pair in _active)
        {
            var engraving = InstanceFor(pair.Key, pair.Value.asset);
            if (engraving != null) engraving.Preview(_entity, pair.Value.tier, into);
        }
    }

    private void Fight(string sourceKey, Grant grant, bool starting)
    {
        var engraving = InstanceFor(sourceKey, grant.asset);
        if (engraving == null) return;

        if (starting) engraving.OnCombatStart(_entity, grant.tier);
        else engraving.OnCombatEnd(_entity, grant.tier);
    }

    /// <summary>The resonance entry for an item, or null if it doesn't resonate.</summary>
    public ResonanceDatabase.Entry EntryFor(Item item)
    {
        if (item == null || ResonanceDatabase.Active == null) return null;

        // A hollow item has already given up its engraving. Answering null here is what stops it
        // attuning, granting, or offering to be engraved a second time — one check, every path.
        if (HollowItems.IsHollow(item)) return null;

        return ResonanceDatabase.Active.FindFor(item);
    }

    #region Save / load

    /// <summary>
    /// One item's progress, in a form a save file can hold.
    ///
    /// The key is the same descriptor attunement is tracked by in memory — item id plus modifier —
    /// so writing and reading back need no ordering, no index and no object identity. It is opaque
    /// on purpose: whatever identifies an item to the runtime is exactly what identifies it here,
    /// and the two cannot drift apart.
    /// </summary>
    [System.Serializable]
    public class AttunementRecord
    {
        public string itemKey;
        public float attunement;
    }

    /// <summary>
    /// A banked mark, by engraving asset name. Names rather than references, because a save file
    /// cannot point at a ScriptableObject; <see cref="ResonanceDatabase.FindEngraving"/> resolves it.
    /// </summary>
    [System.Serializable]
    public class BankedRecord
    {
        public string engravingName;
        public string itemId;
        public int tier;
    }

    /// <summary>Everything a save needs to restore this hero's resonance.</summary>
    [System.Serializable]
    public class State
    {
        public List<AttunementRecord> attunement = new List<AttunementRecord>();
        public List<BankedRecord> banked = new List<BankedRecord>();
    }

    /// <summary>Write out this hero's resonance. Safe to call at any time.</summary>
    public State CaptureState()
    {
        var state = new State();

        foreach (var pair in _attunement)
        {
            if (pair.Value <= 0f) continue;
            state.attunement.Add(new AttunementRecord { itemKey = pair.Key, attunement = pair.Value });
        }

        foreach (var mark in banked)
        {
            if (mark == null || mark.engraving == null) continue;
            state.banked.Add(new BankedRecord { engravingName = mark.engraving.name, tier = mark.tier, itemId = mark.itemId });
        }

        return state;
    }

    /// <summary>
    /// Restore this hero's resonance. Reapplies engravings at the end, so the restored state is live
    /// rather than merely stored.
    ///
    /// Keys are self-contained, so this does not care whether the inventory has been rebuilt yet —
    /// progress is remembered for an item whether or not the hero is currently holding one.
    /// </summary>
    public void RestoreState(State state)
    {
        _attunement.Clear();
        banked.Clear();

        if (state != null)
        {
            foreach (var record in state.attunement)
            {
                if (record == null || string.IsNullOrEmpty(record.itemKey)) continue;
                _attunement[record.itemKey] = record.attunement;
            }

            var database = ResonanceDatabase.Active;
            foreach (var record in state.banked)
            {
                if (record == null) continue;

                var engraving = database != null ? database.FindEngraving(record.engravingName) : null;
                if (engraving == null)
                {
                    // A mark whose engraving no longer exists is dropped rather than silently
                    // becoming a null entry that every reader then has to guard against.
                    Debug.LogWarning($"[Resonance] {name}: no engraving named '{record.engravingName}' " +
                                     "— banked mark dropped.");
                    continue;
                }

                banked.Add(new Banked { engraving = engraving, tier = record.tier, itemId = record.itemId });
            }
        }

        Refresh();
        OnAttunementChanged?.Invoke();
    }

    private static string Descriptor(Item item) =>
        item.Id + "|" + (item.Modifier != null ? (int)item.Modifier.Id : 0) + "|" +
        (item.Modifier != null ? item.Modifier.Level : 0);

    #endregion

    /// <summary>Worn items that appear in the resonance database.</summary>
    private IEnumerable<Item> EquippedResonantItems()
    {
        foreach (var item in WornItems())
        {
            if (item == null) continue;
            if (EntryFor(item) != null) yield return item;
        }
    }

    // Enemies have no inventory window, so what they wear is handed to them here instead.
    private List<Item> _wornOverride;

    // What the unit is born with (UnitData.traits): held at tier 1 like a worn item's effect, and
    // gone with nothing, since nothing can take it off.
    private List<Engraving> _innate;

    /// <summary>
    /// Give the unit engravings of its own, not from any item — a monster's rule. Granted on the next
    /// <see cref="Refresh"/>, which the bell always runs.
    /// </summary>
    public void SetInnate(List<Engraving> traits) => _innate = traits != null ? new List<Engraving>(traits) : null;

    /// <summary>The unit's innate engravings, for the unit card.</summary>
    public IReadOnlyList<Engraving> Innate => _innate ?? (IReadOnlyList<Engraving>)System.Array.Empty<Engraving>();

    /// <summary>
    /// Tell a unit with no inventory what it wears, so its items resonate like a hero's: an enemy in
    /// the Ninja set substitutes, and one holding a mace throws Cannonballs. Refresh afterwards.
    /// </summary>
    public void SetWorn(List<Item> items) => _wornOverride = items;

    /// <summary>What the unit wears: its inventory's items, else the list handed to it, else nothing.</summary>
    private IEnumerable<Item> WornItems()
    {
        var inventory = _entity != null ? _entity.characterInventory : null;
        return inventory != null && inventory.Equipment != null ? inventory.Equipment.Items
             : _wornOverride ?? (IEnumerable<Item>)System.Array.Empty<Item>();
    }

    /// <summary>
    /// The item behind a verb, for showing it: the worn weapon that teaches it, else a copy of the
    /// weapon it was banked from, at the grade it was banked. Null when nothing can be shown.
    /// </summary>
    public Item ItemTeaching(Spell spell)
    {
        var worn = WeaponTeaching(spell);
        if (worn != null) return worn;
        foreach (var mark in banked)
            if (mark != null && mark.engraving is GrantSpellEngraving grant && grant.spell == spell && !string.IsNullOrEmpty(mark.itemId))
                return Rarity.Make(mark.itemId, mark.tier);
        return null;
    }

    /// <summary>
    /// The item carrying an engraving this unit holds — <paramref name="engraving"/> is this unit's
    /// own copy, as the engraving's hooks see themselves, or the asset. The worn item, else the item
    /// its mark was banked from; null for a trait, which no item carries.
    /// </summary>
    public Item ItemOf(Engraving engraving)
    {
        if (engraving == null) return null;
        string key = null;
        foreach (var pair in _instances) if (pair.Value == engraving) { key = pair.Key; break; }
        if (key == null) foreach (var pair in _active) if (pair.Value.asset == engraving) { key = pair.Key; break; }
        if (key == null) return null;

        if (key.StartsWith("worn:"))
        {
            string descriptor = key.Substring(5);
            foreach (var item in WornItems()) if (item != null && Descriptor(item) == descriptor) return item;
            return null;
        }
        if (key.StartsWith("banked:") && int.TryParse(key.Substring(7), out int index) && index >= 0 && index < banked.Count)
        {
            var mark = banked[index];
            return mark != null && !string.IsNullOrEmpty(mark.itemId) ? Rarity.Make(mark.itemId, mark.tier) : null;
        }
        return null;
    }

    /// <summary>The worn weapon that teaches this verb, or null (a banked verb, or not a verb).</summary>
    public Item WeaponTeaching(Spell spell)
    {
        if (spell == null) return null;
        foreach (var item in WornItems())
        {
            if (item == null || !item.IsWeapon) continue;
            var entry = EntryFor(item);
            if (entry != null && entry.engraving is GrantSpellEngraving grant && grant.spell == spell) return item;
        }
        return null;
    }
}
