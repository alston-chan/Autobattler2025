using System.Collections.Generic;
using Assets.HeroEditor.InventorySystem.Scripts.Data;
using UnityEngine;

/// <summary>
/// A hero's resonance: how far each worn item has attuned, and which engravings they have banked
/// permanently (Docs/Resonance.md).
///
/// The loop is <c>equip → attune while worn → cross tier thresholds → resonate (cash out) → the
/// engraving is banked permanently, the item is consumed, the slot frees</c>. Attunement is per
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
    /// Attunement per item, keyed by the item <b>instance</b> rather than its id. Two copies of the
    /// same gear are two different objects that attune separately — swapping a worn helm for an
    /// identical one from the bag should start that helm at nothing, not hand it the first one's
    /// progress.
    /// </summary>
    private readonly Dictionary<Item, float> _attunement = new Dictionary<Item, float>();

    /// <summary>This hero's private copies of engraving assets. See <see cref="InstanceFor"/>.</summary>
    private readonly Dictionary<Engraving, Engraving> _instances =
        new Dictionary<Engraving, Engraving>();

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
    private Engraving InstanceFor(Engraving asset)
    {
        if (asset == null) return null;
        if (_instances.TryGetValue(asset, out var existing) && existing != null) return existing;

        var copy = Instantiate(asset);
        copy.name = asset.name + " (" + name + ")";
        _instances[asset] = copy;
        return copy;
    }

    public float AttunementFor(Item item) =>
        item != null && _attunement.TryGetValue(item, out float value) ? value : 0f;

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
        foreach (var item in EquippedResonantItems())
        {
            var entry = EntryFor(item);
            if (entry == null || entry.requirement != requirement) continue;

            _attunement.TryGetValue(item, out float current);
            _attunement[item] = current + amount;
            changed = true;
        }

        if (changed) OnAttunementChanged?.Invoke();
    }

    /// <summary>Credit the fight to items counting combats. Called once a fight is over.</summary>
    public void AccrueAfterCombat() => Accrue(ResonanceRequirement.CombatsWorn, 1f);

    /// <summary>
    /// Cash out: bank the item's engraving at the tier reached, then consume the item so the slot
    /// frees. Refused unless the item is worn and has met its engrave requirement — wearing grants
    /// the engraving immediately, but keeping it forever has to be earned, or cashing out would be
    /// free and the bank-or-press decision would vanish.
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

        // The item is spent — its essence is engraved, the steel is gone.
        inventory.ConsumeItem(item);
        _attunement.Remove(item);
        OnAttunementChanged?.Invoke();

        Debug.Log($"[Resonance] {_entity.name} banked {entry.engraving.DisplayName} at tier {tier}.");
        return true;
    }

    /// <summary>Engravings currently applied to this hero, and the tier each was applied at.</summary>
    private readonly Dictionary<Engraving, int> _active = new Dictionary<Engraving, int>();

    private readonly List<Engraving> _stale = new List<Engraving>();

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
    public void Refresh(bool force = false)
    {
        var desired = DesiredTiers();

        if (force)
        {
            foreach (var pair in _active) Invoke(pair.Key, pair.Value, false);
            _active.Clear();
        }
        else
        {
            // Anything no longer worn, or now owed a different tier, is taken back before the
            // replacement goes on — otherwise a tier change would leave both grants applied.
            _stale.Clear();
            foreach (var pair in _active)
                if (!desired.TryGetValue(pair.Key, out int tier) || tier != pair.Value)
                    _stale.Add(pair.Key);

            for (int i = 0; i < _stale.Count; i++)
            {
                Invoke(_stale[i], _active[_stale[i]], false);
                _active.Remove(_stale[i]);
            }
        }

        foreach (var pair in desired)
        {
            if (_active.ContainsKey(pair.Key)) continue;
            Invoke(pair.Key, pair.Value, true);
            _active[pair.Key] = pair.Value;
        }
    }

    /// <summary>
    /// Which engravings should be acting on this hero, and at what tier: those on worn items at
    /// whatever tier they have attuned to, plus everything banked. Both arrive by the same route so a
    /// worn engraving and a banked one are indistinguishable in play.
    ///
    /// Keyed by engraving, so a hero wearing the item they already banked the mark of gets the better
    /// of the two rather than both stacked on top of each other.
    /// </summary>
    private Dictionary<Engraving, int> DesiredTiers()
    {
        var desired = new Dictionary<Engraving, int>();

        foreach (var item in EquippedResonantItems())
        {
            var entry = EntryFor(item);
            if (entry == null || entry.engraving == null) continue;

            // Tier I is free — a worn engraving always applies. The item's identity is the reason to
            // wear it, so it works from the moment it goes on; attunement only deepens it.
            Take(desired, entry.engraving, entry.TierAt(AttunementFor(item)));
        }

        foreach (var mark in banked)
        {
            if (mark == null || mark.engraving == null) continue;
            Take(desired, mark.engraving, mark.tier);
        }

        return desired;
    }

    private static void Take(Dictionary<Engraving, int> desired, Engraving engraving, int tier)
    {
        if (!desired.TryGetValue(engraving, out int current) || tier > current)
            desired[engraving] = tier;
    }

    /// <summary>
    /// Open or close everything for a fight. Kept as the combat-boundary entry point; both directions
    /// reconcile from scratch, which clears out per-fight state while leaving the grants a hero has
    /// earned by wearing something in place.
    /// </summary>
    public void ApplyForCombat(bool starting) => Refresh(force: true);

    private void Invoke(Engraving asset, int tier, bool starting)
    {
        var engraving = InstanceFor(asset);
        if (engraving == null) return;

        if (starting) engraving.OnCombatStart(_entity, tier);
        else engraving.OnCombatEnd(_entity, tier);
    }

    /// <summary>The resonance entry for an item, or null if it doesn't resonate.</summary>
    public ResonanceDatabase.Entry EntryFor(Item item)
    {
        if (item == null || ResonanceDatabase.Active == null) return null;
        return ResonanceDatabase.Active.Find(item.Id);
    }

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
