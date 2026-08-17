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

    private Entity _owner;
    private bool _active;
    private int _tier = 1;

    private void Reset()
    {
        engravingName = "Desperate";
        description = "Deals significantly more damage while below half health.";
    }

    public override void OnCombatStart(Entity owner, int tier)
    {
        _owner = owner;
        _tier = Mathf.Max(1, tier);
        _active = false;
        if (owner != null && owner.Health != null) owner.Health.OnDamaged += HandleDamaged;
    }

    public override void OnCombatEnd(Entity owner, int tier)
    {
        if (owner != null && owner.Health != null) owner.Health.OnDamaged -= HandleDamaged;

        // Always strip the bonus: the company is healed between fights, so a hero who ended one fight
        // wounded would otherwise start the next still enraged at full health.
        if (owner != null && owner.Stats != null && owner.Stats.Damage != null)
            owner.Stats.Damage.RemoveAllModifiersFromSource(this);

        _active = false;
        _owner = null;
    }

    private void HandleDamaged(DamageInfo info)
    {
        if (_active || _owner == null || _owner.Stats == null) return;

        float max = _owner.maxHealth;
        if (max <= 0f || info.remainingHealth > max * healthThreshold) return;

        _owner.Stats.Damage.AddModifier(new Kryz.CharacterStats.StatModifier(
            damageBonusPerTier * _tier, Kryz.CharacterStats.StatModType.PercentAdd, this));
        _active = true;
    }
}
