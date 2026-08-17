using UnityEngine;

/// <summary>
/// A hero's innate, permanent effect — what makes one unit different from another *without* giving it
/// a role (Docs/Characters.md).
///
/// Class in this game is emergent from gear, so a unit can't start as "the healer" without undoing
/// that. A seed instead nudges: it suggests a direction and pulls at build decisions, while leaving
/// the unit free to become anything. In Resonance terms it is a starting Engraving — personal,
/// permanent, and stacking with everything banked later.
///
/// Subclasses hook the moments they need. Effects granted for a fight should be applied in
/// <see cref="OnCombatStart"/> and undone in <see cref="OnCombatEnd"/>, since a seed that only ever
/// adds would stack its own bonus every encounter.
/// </summary>
public abstract class HeroSeed : ScriptableObject
{
    [Tooltip("Shown to the player when picking a hero.")]
    public string seedName = "Seed";
    [TextArea, Tooltip("Plain-language description of what this does.")]
    public string description;

    /// <summary>Called once when the hero joins the company.</summary>
    public virtual void OnAcquired(Entity owner) { }

    /// <summary>
    /// Called as a fight begins, after the formation is set — so a seed can read where everyone
    /// stands and grant effects based on it.
    /// </summary>
    public virtual void OnCombatStart(Entity owner) { }

    /// <summary>Called as a fight ends. Undo anything granted for the fight.</summary>
    public virtual void OnCombatEnd(Entity owner) { }

    /// <summary>Display name, falling back to the asset name.</summary>
    public string DisplayName => string.IsNullOrEmpty(seedName) ? name : seedName;
}
