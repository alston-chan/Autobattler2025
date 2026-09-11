using UnityEngine;

/// <summary>
/// A weapon's engraving is its verb (Docs/Spells.md): wearing the weapon puts the spell in the
/// hero's kit, resonating it keeps the spell for good. This is that rule as an engraving — hold it
/// and the spell is in a slot, lose it and the slot empties. Tier does nothing yet; the spell's
/// own numbers are where tiers will land when ScaledValues arrive.
/// </summary>
[CreateAssetMenu(menuName = "Data/Engravings/Grant Spell", fileName = "Verb")]
public class GrantSpellEngraving : Engraving
{
    [Tooltip("The verb this item teaches.")]
    public Spell spell;

    public override void OnGranted(Entity owner, int tier)
    {
        if (owner == null || spell == null) return;
        // A hero with an inventory rebuilds its slots from what it wears, verbs included; anything
        // else (an enemy given a verb) is slotted directly.
        if (owner.characterInventory != null) { owner.characterInventory.SyncSpellSlots(); return; }
        if (owner.spellSlots == null) owner.spellSlots = new System.Collections.Generic.List<Spell>();
        if (!owner.spellSlots.Contains(spell))
        {
            owner.spellSlots.Add(spell);
            // The first verb a hero comes to hold is the one they cast; a later one waits in the bench
            // until the player swaps to it in Setup.
            if (owner.ActiveSpell == null) owner.activeSpellSlot = owner.spellSlots.Count - 1;
        }
        if (owner.CombatAI != null) owner.CombatAI.RefreshSpells();
    }

    public override void OnRevoked(Entity owner, int tier)
    {
        if (owner == null || spell == null || owner.spellSlots == null) return;
        if (owner.characterInventory != null) { owner.characterInventory.SyncSpellSlots(); return; }
        int index = owner.spellSlots.IndexOf(spell);
        if (index < 0) return;
        owner.spellSlots.RemoveAt(index);
        if (owner.activeSpellSlot >= owner.spellSlots.Count) owner.activeSpellSlot = owner.spellSlots.Count - 1;
        if (owner.CombatAI != null) owner.CombatAI.RefreshSpells();
    }

    public override string DescribeTier(int tier) => spell != null ? $"Verb: {spell.DisplayName}" : "Verb: (none)";
}
