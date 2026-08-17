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

    public override void OnCombatStart(Entity owner, int tier)
    {
        var runManager = GameManager.Instance != null ? GameManager.Instance.runManager : null;
        if (runManager == null || owner == null || owner.Stats == null) return;

        if (!runManager.Formation.TryGetCell(owner, out var cell)) return;
        if (cell.x != frontColumn) return;

        owner.Stats.Damage.AddModifier(new Kryz.CharacterStats.StatModifier(
            damageBonusPerTier * Mathf.Max(1, tier), Kryz.CharacterStats.StatModType.PercentAdd, this));
    }

    public override void OnCombatEnd(Entity owner, int tier)
    {
        if (owner != null && owner.Stats != null && owner.Stats.Damage != null)
            owner.Stats.Damage.RemoveAllModifiersFromSource(this);
    }
}
