using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// "Allies standing beside the bearer take less damage."
///
/// The engraving that makes the deployment grid matter: its value depends entirely on where the
/// player puts the hero. Parked in a corner it protects one ally; in the middle of the formation it
/// protects four.
///
/// Adjacency is read once as the fight begins, from the formation the player arranged — not tracked
/// live. Units scatter the moment combat starts, so a live version would hand out and revoke the
/// bonus as the AI shuffled people around, which the player can neither see nor plan around.
/// </summary>
[CreateAssetMenu(menuName = "Data/Engraving/Bulwark", fileName = "Engraving_Bulwark")]
public class BulwarkEngraving : Engraving
{
    [Tooltip("Armour granted to each adjacent ally, per tier. 36 is about a quarter less physical damage.")]
    public float armorPerTier = 36f;

    // Safe as an ordinary field: Resonance hands every hero their own copy of this engraving, so
    // this list belongs to one bearer rather than being shared across all of them.
    private readonly List<Entity> _buffed = new List<Entity>();

    private void Reset()
    {
        engravingName = "Bulwark";
        description = "Allies adjacent to the bearer take less damage from every hit.";
    }

    public override string DescribeTier(int tier) =>
        $"Allies adjacent to the bearer gain {armorPerTier * Mathf.Max(1, tier):0.#} armour — about {Mitigation.Fraction(armorPerTier * Mathf.Max(1, tier)) * 100f:0}% less physical damage — while you both stand.";

    /// <summary>The badge shown over each ally beside the bearer: "BULWARK +36".</summary>
    public override string PreviewLabel(int tier) => $"BULWARK +{armorPerTier * Mathf.Max(1, tier):0.#}";

    /// <summary>
    /// Flat armour adds, so an ally between two bearers reads as the total it will get. Bounded
    /// in play by the damage floor a hit can never go below, not by anything here.
    /// </summary>
    public override string MergedLabel(List<int> tiers)
    {
        if (tiers.Count <= 1) return base.MergedLabel(tiers);
        float total = 0f;
        foreach (var tier in tiers) total += armorPerTier * Mathf.Max(1, tier);
        return $"BULWARK +{total:0.#} ×{tiers.Count}";
    }

    public override void Preview(Entity owner, int tier, List<Badge> into)
    {
        var runManager = GameManager.Instance != null ? GameManager.Instance.runManager : null;
        if (runManager == null || owner == null) return;

        foreach (var ally in runManager.Formation.PlannedAdjacentTo(owner))
            if (ally != null) into.Add(new Badge(ally, this, tier));
    }

    public override void OnCombatStart(Entity owner, int tier)
    {
        _buffed.Clear();

        var runManager = GameManager.Instance != null ? GameManager.Instance.runManager : null;
        if (runManager == null || owner == null) return;

        float amount = armorPerTier * Mathf.Max(1, tier);

        foreach (var ally in runManager.Formation.AdjacentTo(owner))
        {
            if (ally == null || ally.Stats == null || ally.Stats.Armor == null) continue;

            // Sourced by this copy so the grant can be removed without disturbing the Armour an
            // ally gets from its own armour — or from another bearer of this same engraving.
            ally.Stats.Armor.AddModifier(new Kryz.CharacterStats.StatModifier(
                amount, Kryz.CharacterStats.StatModType.Flat, this));
            _buffed.Add(ally);
        }

    }

    public override void OnCombatEnd(Entity owner, int tier)
    {
        foreach (var ally in _buffed)
        {
            if (ally == null || ally.Stats == null || ally.Stats.Armor == null) continue;
            ally.Stats.Armor.RemoveAllModifiersFromSource(this);
        }
        _buffed.Clear();
    }
}
