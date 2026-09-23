using System.Collections;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// The paired-blade basic attack: two strikes to a swing, one from each hand.
///
/// Both blows are real, landing on their own animation's contact frame rather than one hit being
/// doubled — the rig has a paired slash and a paired jab, so the pair leads with one and follows
/// with the other.
///
/// The interesting consequence is not the damage total but how it arrives. Armour takes a fraction
/// of every hit (see <see cref="Mitigation"/>), so two strikes and one strike of the same total are
/// mitigated alike — the split's cost now is the minimum a hit always lands for, and everything that
/// triggers per hit. Paired blades are for on-hit effects; the greatsword is for the one big number. Neither is
/// better; they want different enemies.
/// </summary>
[CreateAssetMenu(menuName = "Spells/DualWieldAttackSpell")]
public class DualWieldAttackSpell : Spell
{
    [Header("Paired Attack Properties")]
    [Tooltip("Damage PER STRIKE. Two land per swing, and each is blocked separately.")]
    public float damage = 12f;

    public float critChance = 0.15f;

    [Tooltip("Only a crit shoves; an ordinary strike stands. Small: knockback would push the target " +
             "out of reach between the first strike and the second.")]
    [FormerlySerializedAs("knockbackForce")]
    public float critKnockbackForce = 0f;

    [Header("Hit Timing")]
    [Tooltip("Fallback delay per strike if a clip has no contact event. Both paired clips do have " +
             "one, so this is a safety net.")]
    public float hitDelayFallback = 0.3f;
    public float maxHitWait = 1f;

    // Contact-frame event names: characters fire "Hit", FantasyMonsters fire "Attack".
    private const string CharacterHitEvent = "Hit";
    private const string MonsterHitEvent = "Attack";

    // Basic weapon attack — its rate scales with the caster's AttackSpeed.
    public override bool ScalesWithAttackSpeed => true;

    /// <summary>Per-strike, since that is what a single hit is worth and what mitigation is applied to.</summary>
    public override float BaseDamage => damage;

    private void Reset()
    {
        range = 1.5f;
        cooldown = 1.1f;   // two strikes fit inside this; the pair is quick, not free
        weaponRequirement = WeaponClass.Melee;
    }

    public override bool CanCast(Entity caster, Entity target) => target != null && !target.isDead;

    public override IEnumerator Cast(Entity caster, Entity target)
    {
        float attackSpeed = GetAttackSpeed(caster);
        Animator animator = GetAnimator(caster);
        if (animator != null) animator.speed = attackSpeed;

        // Lead with the slash, follow with the jab. With the rig in MeleePaired the animator selects
        // the paired variant of each, so both hands are seen to work.
        yield return Strike(caster, target, attackSpeed, slash: true);

        if (target != null && !target.isDead)
            yield return Strike(caster, target, attackSpeed, slash: false);

        if (animator != null) animator.speed = 1f;
    }

    private IEnumerator Strike(Entity caster, Entity target, float attackSpeed, bool slash)
    {
        if (caster.isCharacter && caster.character != null)
        {
            if (slash) caster.character.Slash();
            else caster.character.Jab();
        }
        else if (caster.monster != null)
        {
            caster.monster.Attack();
        }

        yield return WaitForAnimationEvent(caster, CharacterHitEvent, MonsterHitEvent,
            hitDelayFallback / attackSpeed, maxHitWait / attackSpeed);

        // The target may have died on the first strike, or walked off mid-swing.
        if (target == null || target.isDead) yield break;

        bool isCrit = AttackRoll.IsCrit(critChance);
        float finalDamage = AttackRoll.DamageOf(caster, damage);
        target.TakeDamage(finalDamage, caster, isCrit);

        if (!isCrit || critKnockbackForce <= 0f || target == null || target.isDead) yield break;

        Vector3 direction = (target.transform.position - caster.transform.position).normalized;
        target.ApplyKnockback(direction, critKnockbackForce, caster);
    }
}
