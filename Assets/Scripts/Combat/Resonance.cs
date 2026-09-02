using System.Collections.Generic;
using Assets.HeroEditor.InventorySystem.Scripts.Data;
using UnityEngine;

/// <summary>
/// A hero's resonance: how far each worn item has attuned, and which engravings they have banked
/// permanently (Docs/Resonance.md).
///
/// The loop is <c>equip → attune while worn → cross tier thresholds → resonate (cash out) → the
/// engraving is banked permanently and the item is hollowed — still worn, still a weapon of its
/// class, but stripped of everything it gave</c>. Attunement is per
/// (hero, item) and <b>pauses</b> when an item is unequipped rather than resetting, so swapping gear
/// is never punishing — the item just idles while something else holds the slot.
///
/// Worn and banked engravings apply through the same path, so a worn Tier II engraving and a banked
/// Tier II engraving behave identically. That equivalence is what makes cashing out feel like keeping
/// the soul of the item rather than losing it.
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

            var inventory = _entity != null ? _entity.characterInventory : null;
            if (inventory == null || inventory.Equipment == null) return worst;

            foreach (var item in inventory.Equipment.Items)
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
    /// actually seen what it had to say.
    /// </summary>
    public void MarkSeen(Item item)
    {
        if (item == null) return;
        if (_notices.Remove(Descriptor(item))) OnNoticesChanged?.Invoke();
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

    /// <summary>Tier an item has currently reached (0–3), or 0 if it doesn't resonate.</summary>
    public int TierFor(Item item)
    {
        var entry = EntryFor(item);
        return entry == null ? 0 : entry.TierAt(AttunementFor(item));
    }

    /// <summary>Raised when any worn item's attunement changes, so UI can follow it live.</summary>
    public event System.Action OnAttunementChanged;

    /// <summary>
    /// Credit <paramref name="amount"/> toward every worn item whose requirement is
    /// <paramref name="requirement"/>. Items counting something else are untouched, so a hero wearing
    /// a kill-counting blade and a block-counting shield advances each on its own terms during the
    /// same fight.
    /// </summary>
    public void Accrue(ResonanceRequirement requirement, float amount)
    {
        if (amount <= 0f) return;

        bool changed = false;
        bool crossedTier = false;

        foreach (var item in EquippedResonantItems())
        {
            var entry = EntryFor(item);
            if (entry == null || entry.requirement != requirement) continue;

            // Two worn copies of one item share a key, so credit it once rather than twice.
            string key = Descriptor(item);
            if (!_credited.Add(key)) continue;

            _attunement.TryGetValue(key, out float current);
            float updated = current + amount;
            _attunement[key] = updated;
            changed = true;

            if (entry.TierAt(updated) != entry.TierAt(current))
            {
                crossedTier = true;
                Raise(key, ResonanceNotice.TierUp);
            }

            // Becoming bankable is the one that asks something of the player, so it outranks a tier.
            if (!entry.CanEngrave(current) && entry.CanEngrave(updated))
                Raise(key, ResonanceNotice.EngraveReady);
        }
        _credited.Clear();

        // The kill that completes the quota is the moment the reward is earned, so it lands then
        // rather than at the end of the fight. Reconciling is only worth doing when a threshold was
        // actually crossed — this runs on every hit landed and every blow blocked, and nothing
        // changes on the vast majority of them.
        if (crossedTier) Refresh();

        if (changed) OnAttunementChanged?.Invoke();
    }

    /// <summary>Credit the fight to items counting combats. Called once a fight is over.</summary>
    public void AccrueAfterCombat() => Accrue(ResonanceRequirement.CombatsWorn, 1f);

    /// <summary>
    /// Cash out: bank the item's engraving at the tier reached, then hollow the item — it stays
    /// equipped and still counts as a weapon of its class, so an archer who spends their bow is
    /// still an archer, but it gives nothing further. Refused unless the item is worn and has met
    /// its engrave requirement — wearing grants the engraving immediately, but keeping it forever
    /// has to be earned, or cashing out would be free and the bank-or-press decision would vanish.
    /// </summary>
    public bool Resonate(Item item)
    {
        var entry = EntryFor(item);
        if (entry == null || entry.engraving == null) return false;

        float attunement = AttunementFor(item);
        if (!entry.CanEngrave(attunement)) return false;

        int tier = entry.TierAt(attunement);

        var inventory = _entity != null ? _entity.characterInventory : null;
        if (inventory == null || !inventory.Equipment.Items.Contains(item)) return false;

        banked.Add(new Banked { engraving = entry.engraving, tier = tier });

        // Read the key BEFORE hollowing: hollowing changes the item's modifier, and so its
        // descriptor, and the progress being cleared is filed under the old one.
        string spentKey = Descriptor(item);

        // The item is spent — its essence is engraved, and what stays equipped is the husk.
        inventory.HollowItem(item);
        _attunement.Remove(spentKey);
        _notices.Remove(spentKey);
        OnAttunementChanged?.Invoke();

        Debug.Log($"[Resonance] {_entity.name} banked {entry.engraving.DisplayName} at tier {tier}.");
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
    /// equipped. Banking cannot double-count either way, because Resonate consumes the item — a
    /// banked mark and a worn item of one engraving are always two things the hero went and got.
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

        for (int i = 0; i < _stale.Count; i++)
        {
            Invoke(_stale[i], _active[_stale[i]], false);
            _active.Remove(_stale[i]);
        }

        foreach (var pair in desired)
        {
            if (_active.ContainsKey(pair.Key)) continue;
            Invoke(pair.Key, pair.Value, true);
            _active[pair.Key] = pair.Value;
        }

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

            // Tier I is free — a worn engraving always applies. The item's identity is the reason to
            // wear it, so it works from the moment it goes on; attunement only deepens it.
            desired["worn:" + Descriptor(item)] = new Grant
            {
                asset = entry.engraving,
                tier = entry.TierAt(AttunementFor(item))
            };
        }

        for (int i = 0; i < banked.Count; i++)
        {
            var mark = banked[i];
            if (mark == null || mark.engraving == null) continue;
            desired["banked:" + i] = new Grant { asset = mark.engraving, tier = mark.tier };
        }

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
            foreach (var pair in _active) Fight(pair.Key, pair.Value, true);
        }
        else
        {
            foreach (var pair in _active) Fight(pair.Key, pair.Value, false);
            _inCombat = false;
        }
    }

    private bool _inCombat;

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

        return ResonanceDatabase.Active.Find(item.Id);
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
            state.banked.Add(new BankedRecord { engravingName = mark.engraving.name, tier = mark.tier });
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

                banked.Add(new Banked { engraving = engraving, tier = record.tier });
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
        var inventory = _entity != null ? _entity.characterInventory : null;
        if (inventory == null) yield break;

        foreach (var item in inventory.Equipment.Items)
        {
            if (item == null) continue;
            if (EntryFor(item) != null) yield return item;
        }
    }
}
