using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// "Below half health, the bearer hits harder."
///
/// The risk lane: it rewards a hero who is being hit, pulling against protecting them and toward gear
/// that survives at low health rather than avoids damage. Conditional rather than flat on purpose —
/// the bonus arrives mid-fight off an event, so the engraving hooks a trigger instead of only an
/// opening buff.
/// </summary>
[CreateAssetMenu(menuName = "Data/Engraving/Desperate", fileName = "Engraving_Desperate")]
public class DesperateEngraving : Engraving
{
    [Range(0f, 1f), Tooltip("Health fraction the bearer must drop below for the bonus to switch on.")]
    public float healthThreshold = 0.5f;
    [Tooltip("Extra damage as a fraction of base, per tier — 0.25 is +25% at Tier I.")]
    public float damageBonusPerTier = 0.25f;

    // An engraving is one shared asset however many heroes carry it, so per-bearer state cannot live
    // in plain fields — a second bearer would overwrite the first's. Each bearer's watcher is held
    // against them here, and everything else it needs is captured in the closure below.
    private readonly Dictionary<Entity, System.Action<DamageInfo>> _watchers =
        new Dictionary<Entity, System.Action<DamageInfo>>();

    private void Reset()
    {
        engravingName = "Desperate";
        description = "Deals significantly more damage while below half health.";
    }

    public override void OnCombatStart(Entity owner, int tier)
    {
        if (owner == null || owner.Health == null) return;

        OnCombatEnd(owner, tier);   // never stack a second watcher on the same bearer

        int strength = Mathf.Max(1, tier);
        bool triggered = false;

        System.Action<DamageInfo> watcher = info =>
        {
            if (triggered || owner == null || owner.Stats == null) return;

            float max = owner.maxHealth;
            if (max <= 0f || info.remainingHealth > max * healthThreshold) return;

            owner.Stats.Damage.AddModifier(new Kryz.CharacterStats.StatModifier(
                damageBonusPerTier * strength, Kryz.CharacterStats.StatModType.PercentAdd, this));
            triggered = true;
        };

        owner.Health.OnDamaged += watcher;
        _watchers[owner] = watcher;
    }

    public override void OnCombatEnd(Entity owner, int tier)
    {
        if (owner == null) return;

        if (_watchers.TryGetValue(owner, out var watcher))
        {
            if (owner.Health != null) owner.Health.OnDamaged -= watcher;
            _watchers.Remove(owner);
        }

        // Always strip the bonus: the company is healed between fights, so a hero who ended one fight
        // wounded would otherwise start the next still enraged at full health.
        if (owner.Stats != null && owner.Stats.Damage != null)
            owner.Stats.Damage.RemoveAllModifiersFromSource(this);
    }
}
