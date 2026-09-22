using Kryz.CharacterStats;
using UnityEngine;

/// <summary>
/// Wall Keeper helmet — Besieged: the more enemies on you, the harder you are to hurt. Armour
/// per enemy currently targeting the owner, recounted whenever the owner is hit and when the fight
/// begins. Tier raises the per-enemy amount.
/// </summary>
[CreateAssetMenu(menuName = "Data/Engravings/Besieged", fileName = "Besieged")]
public class BesiegedEngraving : Engraving
{
    [Tooltip("Armour per enemy targeting you at tier I; each tier adds the same again. 24 is about a fifth less physical damage per enemy on you.")]
    public float armorPerEnemyPerTier = 24f;

    public override void OnCombatStart(Entity owner, int tier) => Recount(owner, tier);
    public override void OnDamaged(Entity owner, HitInfo hit, int tier) => Recount(owner, tier);

    public override void OnCombatEnd(Entity owner, int tier)
    {
        if (owner != null && owner.Stats != null && owner.Stats.Armor != null) owner.Stats.Armor.RemoveAllModifiersFromSource(this);
    }

    private void Recount(Entity owner, int tier)
    {
        if (owner == null || owner.isDead || owner.Stats == null || owner.Stats.Armor == null) return;
        int onMe = 0;
        foreach (var e in EntityRegistry.All)
            if (e != null && !e.isDead && e.isTeam != owner.isTeam && e.CombatAI != null && e.CombatAI.CurrentTarget == owner) onMe++;
        owner.Stats.Armor.RemoveAllModifiersFromSource(this);
        float value = armorPerEnemyPerTier * Mathf.Max(1, tier) * onMe;
        if (value > 0f)
        {
            owner.Stats.Armor.AddModifier(new StatModifier(value, StatModType.Flat, this));
        }
    }

    public override string DescribeTier(int tier) => $"+{armorPerEnemyPerTier * Mathf.Max(1, tier):0} armour per enemy targeting you.";
}
