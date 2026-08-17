using UnityEngine;

/// <summary>
/// "Below half health, this hero hits harder."
///
/// The risk lane: it rewards a unit that is being hit, which pulls against protecting it and toward
/// gear that lets it survive at low health rather than avoid damage. Deliberately a *conditional*
/// seed rather than a flat one, so it demonstrates a trigger — the bonus arrives mid-fight, off an
/// event, instead of being handed out at the start.
/// </summary>
[CreateAssetMenu(menuName = "Data/Seed/Desperate", fileName = "Seed_Desperate")]
public class DesperateSeed : HeroSeed
{
    [Range(0f, 1f), Tooltip("Health fraction the hero must drop below for the bonus to switch on.")]
    public float healthThreshold = 0.5f;
    [Tooltip("Extra damage as a fraction of base — 0.5 is +50%.")]
    public float damageBonus = 0.5f;

    private Entity _owner;
    private bool _active;

    private void Reset()
    {
        seedName = "Desperate";
        description = "Deals significantly more damage while below half health.";
    }

    public override void OnCombatStart(Entity owner)
    {
        _owner = owner;
        _active = false;
        if (owner != null && owner.Health != null) owner.Health.OnDamaged += HandleDamaged;
    }

    public override void OnCombatEnd(Entity owner)
    {
        if (owner != null && owner.Health != null) owner.Health.OnDamaged -= HandleDamaged;

        // Always strip the bonus: the company is healed between fights, so a unit that ended one
        // fight wounded would otherwise start the next one still enraged at full health.
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
            damageBonus, Kryz.CharacterStats.StatModType.PercentAdd, this));
        _active = true;
    }
}
