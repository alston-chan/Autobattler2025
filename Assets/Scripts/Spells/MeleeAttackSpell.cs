using System.Collections;
using UnityEngine;

[CreateAssetMenu(menuName = "Spells/MeleeAttackSpell")]
public class MeleeAttackSpell : Spell
{
    [Header("Melee Attack Properties")]
    public float damage = 10f;
    public float critChance = 0.2f;
    public float critKnockbackForce = 3.5f;
    public float normalKnockbackForce = 0f;

    [Header("Hit Timing")]
    [Tooltip("Fallback delay before damage lands when the attack animation has no hit event.")]
    public float hitDelayFallback = 0.2f;
    [Tooltip("Safety timeout: max seconds to wait for the animation hit event before landing damage anyway.")]
    public float maxHitWait = 1f;

    // Contact-frame event names: characters fire "Hit", FantasyMonsters fire "Attack".
    private const string CharacterHitEvent = "Hit";
    private const string MonsterHitEvent = "Attack";

    // Basic weapon attack — its rate scales with the caster's AttackSpeed.
    public override bool ScalesWithAttackSpeed => true;
    public override float BaseDamage => damage;

    public override bool CanCast(Entity caster, Entity target) => target != null;

    public override IEnumerator Cast(Entity caster, Entity target)
    {
        // Play the swing at attack-speed so the visual (and its 'Hit' event) stays in sync.
        float attackSpeed = GetAttackSpeed(caster);
        Animator animator = GetAnimator(caster);
        if (animator != null) animator.speed = attackSpeed;

        // Trigger the swing animation.
        if (caster.isCharacter && caster.character != null)
        {
            if (Random.value < 0.5f)
                caster.character.Slash();
            else
                caster.character.Jab();
        }
        else if (caster.monster != null)
        {
            caster.monster.Attack();
        }

        // Land damage on the animation's real contact frame instead of a fixed guess.
        // Timing tolerances scale with attack speed since the whole swing is sped up/slowed down.
        yield return WaitForAnimationEvent(caster, CharacterHitEvent, MonsterHitEvent,
            hitDelayFallback / attackSpeed, maxHitWait / attackSpeed);

        if (animator != null) animator.speed = 1f;

        // The target may have died or despawned during the wind-up.
        if (target == null || target.isDead) yield break;

        bool isCrit = Random.value < critChance;
        float finalDamage = caster.Stats != null ? caster.Stats.Damage.Value : damage;
        target.TakeDamage(finalDamage, caster, isCrit);

        // May have died from this hit — nothing left to knock back.
        if (target == null || target.isDead) yield break;

        float knockbackForce = isCrit ? critKnockbackForce : normalKnockbackForce;
        if (knockbackForce > 0f)
        {
            Vector3 knockbackDir = (target.transform.position - caster.transform.position).normalized;
            target.ApplyKnockback(knockbackDir, knockbackForce);
        }
    }
}
