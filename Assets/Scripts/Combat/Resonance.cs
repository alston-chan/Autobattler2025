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
/// </summary>
public class Resonance : MonoBehaviour
{
    /// <summary>An engraving this hero has absorbed permanently, at the tier it was banked.</summary>
    [System.Serializable]
    public class Banked
    {
        public Engraving engraving;
        public int tier;
    }

    [Tooltip("Attunement earned per item, keyed by item id. Survives unequipping.")]
    private readonly Dictionary<string, float> _attunement = new Dictionary<string, float>();

    [Tooltip("Permanently absorbed engravings. These outlive the items that carried them.")]
    public List<Banked> banked = new List<Banked>();

    private Entity _entity;

    public void Initialize(Entity entity) => _entity = entity;

    public float AttunementFor(string itemId) =>
        _attunement.TryGetValue(itemId, out float value) ? value : 0f;

    /// <summary>Tier an equipped item has currently reached (0–3), or 0 if it doesn't resonate.</summary>
    public int TierFor(string itemId)
    {
        var entry = ResonanceDatabase.Active != null ? ResonanceDatabase.Active.Find(itemId) : null;
        return entry == null ? 0 : entry.TierAt(AttunementFor(itemId));
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

        var database = ResonanceDatabase.Active;
        if (database == null) return;

        bool changed = false;
        foreach (var item in EquippedResonantItems())
        {
            var entry = database.Find(item.Id);
            if (entry == null || entry.requirement != requirement) continue;

            _attunement.TryGetValue(item.Id, out float current);
            _attunement[item.Id] = current + amount;
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
    public bool Resonate(string itemId)
    {
        var database = ResonanceDatabase.Active;
        var entry = database != null ? database.Find(itemId) : null;
        if (entry == null || entry.engraving == null) return false;

        int tier = entry.TierAt(AttunementFor(itemId));
        if (tier < 1) return false;

        var inventory = _entity != null ? _entity.characterInventory : null;
        var worn = inventory != null ? inventory.Equipment.Items.Find(i => i.Id == itemId) : null;
        if (worn == null) return false;

        banked.Add(new Banked { engraving = entry.engraving, tier = tier });

        // The item is spent — its essence is engraved, the steel is gone.
        inventory.ConsumeItem(worn);
        _attunement.Remove(itemId);

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
        var database = ResonanceDatabase.Active;
        if (database != null)
        {
            foreach (var item in EquippedResonantItems())
            {
                var entry = database.Find(item.Id);
                int tier = entry.TierAt(AttunementFor(item.Id));
                if (tier < 1) continue;   // no effect until the first threshold

                if (starting) entry.engraving.OnCombatStart(_entity, tier);
                else entry.engraving.OnCombatEnd(_entity, tier);
            }
        }

        foreach (var mark in banked)
        {
            if (mark == null || mark.engraving == null) continue;
            if (starting) mark.engraving.OnCombatStart(_entity, mark.tier);
            else mark.engraving.OnCombatEnd(_entity, mark.tier);
        }
    }

    /// <summary>Worn items that appear in the resonance database.</summary>
    private IEnumerable<Item> EquippedResonantItems()
    {
        var database = ResonanceDatabase.Active;
        var inventory = _entity != null ? _entity.characterInventory : null;
        if (database == null || inventory == null) yield break;

        foreach (var item in inventory.Equipment.Items)
        {
            if (item == null) continue;
            if (database.Find(item.Id) != null) yield return item;
        }
    }
}
