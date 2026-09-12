using Kryz.CharacterStats;
using UnityEngine;

/// <summary>
/// Wall Keeper helmet — Besieged: the more enemies on you, the harder you are to hurt. Blocking
/// per enemy currently targeting the owner, recounted whenever the owner is hit and when the fight
/// begins. Tier raises the per-enemy amount.
/// </summary>
[CreateAssetMenu(menuName = "Data/Engravings/Besieged", fileName = "Besieged")]
public class BesiegedEngraving : Engraving
{
    [Tooltip("Blocking per enemy targeting you at tier I; each tier adds the same again.")]
    public float blockingPerEnemyPerTier = 4f;

    public override void OnCombatStart(Entity owner, int tier) => Recount(owner, tier);
    public override void OnDamaged(Entity owner, HitInfo hit, int tier) => Recount(owner, tier);

    public override void OnCombatEnd(Entity owner, int tier)
    {
        if (owner != null && owner.Stats != null && owner.Stats.Blocking != null) owner.Stats.Blocking.RemoveAllModifiersFromSource(this);
    }

    private void Recount(Entity owner, int tier)
    {
        if (owner == null || owner.isDead || owner.Stats == null || owner.Stats.Blocking == null) return;
        int onMe = 0;
        foreach (var e in EntityRegistry.All)
            if (e != null && !e.isDead && e.isTeam != owner.isTeam && e.CombatAI != null && e.CombatAI.CurrentTarget == owner) onMe++;
        owner.Stats.Blocking.RemoveAllModifiersFromSource(this);
        float value = blockingPerEnemyPerTier * Mathf.Max(1, tier) * onMe;
        if (value > 0f)
        {
            owner.Stats.Blocking.AddModifier(new StatModifier(value, StatModType.Flat, this));
        }
    }

    public override string DescribeTier(int tier) => $"+{blockingPerEnemyPerTier * Mathf.Max(1, tier):0} blocking per enemy targeting you.";
}
