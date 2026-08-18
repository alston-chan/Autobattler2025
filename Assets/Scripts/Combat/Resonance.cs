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
    /// frees. Returns false if the item isn't worn, doesn't resonate, or hasn't reached Tier I —
    /// there is nothing to bank before the first threshold.
    /// </summary>
    public bool Resonate(Item item)
    {
        var entry = EntryFor(item);
        if (entry == null || entry.engraving == null) return false;

        int tier = entry.TierAt(AttunementFor(item));

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

    /// <summary>
    /// Open or close every engraving affecting this hero for a fight — the ones on worn items, at
    /// whatever tier they have reached, plus everything banked. Both go through the same call so a
    /// worn engraving and a banked one are indistinguishable in play.
    /// </summary>
    public void ApplyForCombat(bool starting)
    {
        foreach (var item in EquippedResonantItems())
        {
            var entry = EntryFor(item);
            // Tier I is free — a worn engraving always applies. The item's identity is the reason to
            // wear it, so it works from the moment it goes on; attunement only deepens it.
            Invoke(entry.engraving, entry.TierAt(AttunementFor(item)), starting);
        }

        foreach (var mark in banked)
        {
            if (mark == null) continue;
            Invoke(mark.engraving, mark.tier, starting);
        }
    }

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
