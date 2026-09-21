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

    /// <summary>The verb as a sentence, for the designer: whom, then what, in order.</summary>
    [Sirenix.OdinInspector.ShowInInspector, Sirenix.OdinInspector.ReadOnly, Sirenix.OdinInspector.MultiLineProperty(2), Sirenix.OdinInspector.LabelText("Does")]
    private string Does => spell is CompositeSpell c ? c.Reads : (spell != null ? spell.description : "(no verb)");

    public override void OnGranted(Entity owner, int tier)
    {
        if (owner == null || spell == null) return;
        // A hero with an inventory rebuilds its slots from what it wears, verbs included, once the
        // resonance books are settled (CharacterInventory listens to Resonance.OnGrantsChanged) —
        // not here, where a tier-up's revoke-and-regrant would rebuild them twice and interrupt
        // whatever the hero was casting. Anything else (an enemy given a verb) is slotted directly.
        if (owner.characterInventory != null) return;
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
        if (owner.characterInventory != null) return;
        int index = owner.spellSlots.IndexOf(spell);
        if (index < 0) return;
        owner.spellSlots.RemoveAt(index);
        if (owner.activeSpellSlot >= owner.spellSlots.Count) owner.activeSpellSlot = owner.spellSlots.Count - 1;
        if (owner.CombatAI != null) owner.CombatAI.RefreshSpells();
    }

    /// <summary>
    /// What the verb does at this tier. Not "Verb: Whirl" — every panel that shows this already
    /// prints the engraving's name directly above it, so naming it again was the whole line wasted
    /// on a reward card, which is exactly where a player needs the numbers to compare.
    /// </summary>
    public override string DescribeTier(int tier)
    {
        if (spell == null) return "Teaches nothing.";
        if (spell is CompositeSpell composite) return composite.ReadsAt(Mathf.Max(1, tier));
        return string.IsNullOrEmpty(spell.description) ? "Teaches " + spell.DisplayName + "." : spell.description;
    }
}
