using System.Collections;
using UnityEngine;

/// <summary>
/// The two-handed basic attack: a wound-up blow, played on the rig's <c>ChargeAttack2H</c>.
///
/// Greatswords and mauls shared the one-handed swing, so carrying one changed the numbers and
/// nothing else. This gives them their own shape — fewer, heavier blows, and a crit that throws.
///
/// Every blow used to shove, as the cost of the big weapon: a shoved target is out of reach and has
/// to be walked to again. What a player saw was two units stepping back and forth after every swing,
/// which read as indecision, so an ordinary hit now stands and only a crit moves anyone — the same
/// rule as every other basic attack.
/// </summary>
[CreateAssetMenu(menuName = "Spells/HeavyAttackSpell")]
public class HeavyAttackSpell : Spell
{
    [Header("Heavy Attack Properties")]
    public float damage = 20f;
    public float critChance = 0.2f;

    [Tooltip("Only a crit shoves; an ordinary hit stands.")]
    public float critKnockbackForce = 6f;

    [Header("Hit Timing")]
    [Tooltip("Fallback delay before damage lands when the animation has no hit event. Longer than " +
             "the one-handed swing because this one winds up first.")]
    public float hitDelayFallback = 0.45f;
    [Tooltip("Safety timeout: max seconds to wait for the animation hit event before landing damage.")]
    public float maxHitWait = 1.5f;

    /// <summary>Animator trigger for the rig's two-handed wind-up (state <c>ChargeAttack2H</c>).</summary>
    private const string ChargeTrigger = "ChargeAttack2H";

    // Contact-frame event names: characters fire "Hit", FantasyMonsters fire "Attack".
    private const string CharacterHitEvent = "Hit";
    private const string MonsterHitEvent = "Attack";

    // Basic weapon attack — its rate scales with the caster's AttackSpeed.
    public override bool ScalesWithAttackSpeed => true;
    public override float BaseDamage => damage;

    private void Reset()
    {
        // Sensible defaults for a NEW asset; override per-asset in the Inspector.
        range = 1.8f;      // slightly longer reach than a one-hander
        cooldown = 1.5f;   // the whole identity: half as often, twice as hard
        weaponRequirement = WeaponClass.Melee;
    }

    public override bool CanCast(Entity caster, Entity target) => target != null && !target.isDead;

    public override IEnumerator Cast(Entity caster, Entity target)
    {
        float attackSpeed = GetAttackSpeed(caster);
        Animator animator = GetAnimator(caster);
        if (animator != null) animator.speed = attackSpeed;

        // HeroEditor's CharacterAnimation helper exposes Slash and Jab but not this, so the trigger
        // is set directly — the same way the bow spells drive "Charge".
        if (caster.isCharacter && animator != null) animator.SetTrigger(ChargeTrigger);
        else if (caster.monster != null) caster.monster.Attack();

        // Land damage on the animation's real contact frame rather than a guess. Tolerances scale
        // with attack speed, since the whole swing is sped up or slowed down with it.
        yield return WaitForAnimationEvent(caster, CharacterHitEvent, MonsterHitEvent,
            hitDelayFallback / attackSpeed, maxHitWait / attackSpeed);

        if (animator != null) animator.speed = 1f;

        // The target may have died or walked off during a wind-up this long.
        if (target == null || target.isDead) yield break;

        bool isCrit = AttackRoll.IsCrit(critChance);
        float finalDamage = AttackRoll.DamageOf(caster, damage);
        target.TakeDamage(finalDamage, caster, isCrit);

        if (target == null || target.isDead) yield break;

        if (isCrit && critKnockbackForce > 0f)
        {
            Vector3 direction = (target.transform.position - caster.transform.position).normalized;
            target.ApplyKnockback(direction, critKnockbackForce, caster);
        }
    }
}
