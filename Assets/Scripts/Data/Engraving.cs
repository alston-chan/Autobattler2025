using System.Collections.Generic;
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
/// Two lifetimes. Something true while the engraving is held goes in <see cref="OnGranted"/> and
/// is undone in <see cref="OnRevoked"/>; something true for a fight goes in
/// <see cref="OnCombatStart"/> and is undone in <see cref="OnCombatEnd"/>, or it stacks every
/// encounter.
/// </summary>
public abstract class Engraving : ScriptableObject
{
    [Tooltip("Shown to the player on the item and once banked.")]
    public string engravingName = "Engraving";
    [TextArea, Tooltip("Plain-language description of what this does.")]
    public string description;

    /// <summary>
    /// Called when the hero comes to hold this engraving — the item goes on, or the mark is banked —
    /// whether or not a fight is on. For effects that are simply true while held, like a stat
    /// bonus, and that the equipment window should therefore show. <paramref name="tier"/> is 1..3.
    /// </summary>
    public virtual void OnGranted(Entity owner, int tier) { }

    /// <summary>Called when the hero stops holding it. Undo <see cref="OnGranted"/>.</summary>
    public virtual void OnRevoked(Entity owner, int tier) { }

    /// <summary>
    /// Called as a fight begins, after the formation is set — so an engraving can read where everyone
    /// stands, on both sides. Never at equip time: what the board looks like when an item goes on
    /// in the setup screen is not what it looks like when the fight starts, and an effect that read
    /// it early could not follow the hero when they were moved.
    /// </summary>
    public virtual void OnCombatStart(Entity owner, int tier) { }

    /// <summary>Called as a fight ends. Undo anything granted for the fight.</summary>
    public virtual void OnCombatEnd(Entity owner, int tier) { }

    /// <summary>One thing an engraving will do at the bell: whom it will touch, from which engraving, at what tier.</summary>
    public struct Badge
    {
        public Entity target;
        public Engraving engraving;
        public int tier;

        public Badge(Entity target, Engraving engraving, int tier)
        {
            this.target = target;
            this.engraving = engraving;
            this.tier = tier;
        }
    }

    /// <summary>The words shown over a unit this touches, at one tier: "BULWARK -6".</summary>
    public virtual string PreviewLabel(int tier) => DisplayName.ToUpperInvariant();

    /// <summary>
    /// How several grants of this engraving landing on one unit read as one line. The default is
    /// the strongest-wins reading — one label at the highest tier, with a count — because that is
    /// the safe assumption for anything not written to add. Engravings whose numbers add override
    /// this with the total, so the badge says what the unit will actually get.
    /// </summary>
    public virtual string MergedLabel(List<int> tiers)
    {
        int highest = 1;
        foreach (var tier in tiers) if (tier > highest) highest = tier;
        string label = PreviewLabel(highest);
        return tiers.Count > 1 ? label + " ×" + tiers.Count : label;
    }

    /// <summary>
    /// What <see cref="OnCombatStart"/> would do if the fight began now, read from the formation as
    /// it stands. Shown over the units concerned while the player arranges the company and redrawn
    /// as heroes are moved, so a positional effect is a decision made with the hero still in hand
    /// rather than a surprise at the bell. Adding nothing means the engraving would do nothing from
    /// here — which is also worth a player knowing.
    /// </summary>
    public virtual void Preview(Entity owner, int tier, List<Badge> into) { }



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
