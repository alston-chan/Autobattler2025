using UnityEngine;

/// <summary>
/// An item's signature effect — the identity a hero absorbs by wearing and resonating it
/// (Docs/Resonance.md). This is the payload the whole resonance loop moves around: it applies
/// <b>while the item is worn</b>, and once the item is resonated the same effect is banked
/// permanently at the tier reached.
///
/// Strength scales with <c>tier</c> rather than each engraving inventing its own levelling, so a
/// worn Tier III item and a banked Tier III engraving are the same effect at the same strength — and
/// tiering up pays off immediately instead of only at cash-out.
///
/// Effects granted for a fight must be undone in <see cref="OnCombatEnd"/>, or they stack every
/// encounter.
/// </summary>
public abstract class Engraving : ScriptableObject
{
    [Tooltip("Shown to the player on the item and once banked.")]
    public string engravingName = "Engraving";
    [TextArea, Tooltip("Plain-language description of what this does.")]
    public string description;

    /// <summary>
    /// Called as a fight begins, after the formation is set — so an engraving can read where everyone
    /// stands. <paramref name="tier"/> is 1..3.
    /// </summary>
    public virtual void OnCombatStart(Entity owner, int tier) { }

    /// <summary>Called as a fight ends. Undo anything granted for the fight.</summary>
    public virtual void OnCombatEnd(Entity owner, int tier) { }

    public string DisplayName => string.IsNullOrEmpty(engravingName) ? name : engravingName;

    /// <summary>
    /// What this engraving actually does at a given tier, with real numbers — "+15% Attack Speed",
    /// not "attacks faster".
    ///
    /// Prose alone can't support the decision the mechanic asks for: choosing between two items, or
    /// deciding whether another tier is worth the combats, means comparing magnitudes. Each engraving
    /// overrides this; the fallback is the prose description so a new one is never blank.
    /// </summary>
    public virtual string DescribeTier(int tier) => description;
}
