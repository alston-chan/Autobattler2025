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

    // Safe as ordinary fields: Resonance hands every hero their own copy of this engraving, so these
    // describe one bearer rather than being shared across all of them.
    private System.Action<DamageInfo> _watcher;
    private int _tier = 1;
    private bool _triggered;

    private void Reset()
    {
        engravingName = "Desperate";
        description = "Deals significantly more damage while below half health.";
    }

    public override string DescribeTier(int tier) =>
        $"+{damageBonusPerTier * Mathf.Max(1, tier) * 100f:0.#}% damage while below " +
        $"{healthThreshold * 100f:0.#}% health.";

    public override void OnCombatStart(Entity owner, int tier)
    {
        if (owner == null || owner.Health == null) return;

        OnCombatEnd(owner, tier);   // never stack a second watcher

        _tier = Mathf.Max(1, tier);
        _triggered = false;
        _watcher = info => HandleDamaged(owner, info);
        owner.Health.OnDamaged += _watcher;
    }

    public override void OnCombatEnd(Entity owner, int tier)
    {
        if (owner == null) return;

        if (_watcher != null && owner.Health != null) owner.Health.OnDamaged -= _watcher;
        _watcher = null;
        _triggered = false;

        // Always strip the bonus: the company is healed between fights, so a hero who ended one fight
        // wounded would otherwise start the next still enraged at full health.
        if (owner.Stats != null && owner.Stats.Damage != null)
            owner.Stats.Damage.RemoveAllModifiersFromSource(this);
    }

    private void HandleDamaged(Entity owner, DamageInfo info)
    {
        if (_triggered || owner == null || owner.Stats == null) return;

        float max = owner.maxHealth;
        if (max <= 0f || info.remainingHealth > max * healthThreshold) return;

        owner.Stats.Damage.AddModifier(new Kryz.CharacterStats.StatModifier(
            damageBonusPerTier * _tier, Kryz.CharacterStats.StatModType.PercentAdd, this));
        _triggered = true;
    }
}
