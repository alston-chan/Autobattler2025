using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// "Hits harder while deployed in the front rank."
///
/// The other half of the positional pair with Bulwark: where Bulwark rewards standing *among* the
/// company, this rewards standing *ahead* of it. Together they pull a formation in opposite
/// directions, which is what makes the grid a decision rather than a layout.
///
/// Read from the arranged formation at the start of a fight, not from live position — units advance
/// on the enemy the moment combat starts, so "in front" would otherwise be true of everyone within
/// seconds and mean nothing.
/// </summary>
[CreateAssetMenu(menuName = "Data/Engraving/Vanguard", fileName = "Engraving_Vanguard")]
public class VanguardEngraving : Engraving
{
    [Tooltip("Extra damage as a fraction of base, per tier — 0.2 is +20% at Tier I.")]
    public float damageBonusPerTier = 0.2f;
    [Tooltip("Which column counts as the front rank. Column 0 is nearest the enemy.")]
    public int frontColumn = 0;

    private void Reset()
    {
        engravingName = "Vanguard";
        description = "Deals more damage while deployed in the front rank.";
    }

    public override string DescribeTier(int tier) =>
        $"+{damageBonusPerTier * Mathf.Max(1, tier) * 100f:0.#}% damage while deployed in the front rank.";

    /// <summary>The badge shown over the bearer while it stands in the front rank: "VANGUARD +20%".</summary>
    public string PreviewLabel(int tier) => $"VANGUARD +{damageBonusPerTier * Mathf.Max(1, tier) * 100f:0.#}%";

    public override void Preview(Entity owner, int tier, List<Badge> into)
    {
        if (InFrontRank(owner)) into.Add(new Badge(owner, PreviewLabel(tier)));
    }

    public override void OnCombatStart(Entity owner, int tier)
    {
        if (owner == null || owner.Stats == null || !InFrontRank(owner)) return;

        owner.Stats.Damage.AddModifier(new Kryz.CharacterStats.StatModifier(
            damageBonusPerTier * Mathf.Max(1, tier), Kryz.CharacterStats.StatModType.PercentAdd, this));
        Callout(owner, DisplayName);
    }

    private bool InFrontRank(Entity owner)
    {
        var runManager = GameManager.Instance != null ? GameManager.Instance.runManager : null;
        if (runManager == null || owner == null) return false;
        return runManager.Formation.TryGetCell(owner, out var cell) && cell.x == frontColumn;
    }

    public override void OnCombatEnd(Entity owner, int tier)
    {
        if (owner != null && owner.Stats != null && owner.Stats.Damage != null)
            owner.Stats.Damage.RemoveAllModifiersFromSource(this);
    }
}
