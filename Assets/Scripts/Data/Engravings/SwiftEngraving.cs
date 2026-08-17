using UnityEngine;

/// <summary>
/// "Strikes faster."
///
/// Deliberately the plain one. Attack speed shortens the cooldown on weapon attacks and so also feeds
/// mana, which charges ultimates — it compounds with a hero's whole kit rather than asking anything
/// of position or health. Every choice being conditional would make the offer a puzzle each time;
/// a straightforward option gives the player somewhere safe to put a pick.
/// </summary>
[CreateAssetMenu(menuName = "Data/Engraving/Swift", fileName = "Engraving_Swift")]
public class SwiftEngraving : Engraving
{
    [Tooltip("Attack speed added as a fraction of base, per tier — 0.15 is +15% at Tier I.")]
    public float attackSpeedPerTier = 0.15f;

    private void Reset()
    {
        engravingName = "Swift";
        description = "Attacks faster, which also builds toward ultimates sooner.";
    }

    public override void OnCombatStart(Entity owner, int tier)
    {
        if (owner == null || owner.Stats == null || owner.Stats.AttackSpeed == null) return;

        owner.Stats.AttackSpeed.AddModifier(new Kryz.CharacterStats.StatModifier(
            attackSpeedPerTier * Mathf.Max(1, tier), Kryz.CharacterStats.StatModType.PercentAdd, this));

        // CombatAI caches cooldowns from attack speed when its spell set is built, so it has to be
        // told the number changed or the faster swing wouldn't take effect until the next fight.
        if (owner.CombatAI != null) owner.CombatAI.RefreshSpells();
    }

    public override void OnCombatEnd(Entity owner, int tier)
    {
        if (owner == null || owner.Stats == null || owner.Stats.AttackSpeed == null) return;
        owner.Stats.AttackSpeed.RemoveAllModifiersFromSource(this);
        if (owner.CombatAI != null) owner.CombatAI.RefreshSpells();
    }
}
