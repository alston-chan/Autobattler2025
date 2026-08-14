using System.Collections;
using UnityEngine;

/// <summary>
/// Melee ultimate: two rapid strikes on the current target. Reuses the existing Slash animation and
/// its "Hit" contact event, so it needs no new art (the melee clip budget is tight — Docs/Effects.md).
/// A cost ability: the AI fires it when the mana bar is full, and <see cref="Spell.manaCost"/> is spent.
/// </summary>
[CreateAssetMenu(menuName = "Spells/Ultimate/Double Strike")]
public class DoubleStrikeSpell : Spell
{
    [Header("Double Strike")]
    [Tooltip("How many hits the ability lands.")]
    public int strikes = 2;
    [Tooltip("Damage per strike, as a multiple of the caster's Damage stat.")]
    public float damagePerStrike = 1f;
    public float knockbackForce = 4f;
    [Tooltip("How much faster than a normal swing each strike animates. This is what makes Double " +
             "Strike a burst — at 1x it's just two normal attacks in a row; ~2.5x reads as a flurry.")]
    public float strikeSpeed = 2.5f;
    [Tooltip("Pause between strikes (seconds, divided by the sped-up strike rate). Small = a tight flurry.")]
    public float betweenStrikes = 0.05f;

    // Contact-frame event names: characters fire "Hit", FantasyMonsters fire "Attack".
    private const string CharacterHitEvent = "Hit";
    private const string MonsterHitEvent = "Attack";

    private void Reset()
    {
        spellName = "Double Strike";
        range = 1.6f;
        cooldown = 0f;      // gated by mana, not a clock
        manaCost = 100f;    // = default Mana.maxMana → fires when the bar is full
        weaponRequirement = WeaponClass.Melee;
    }

    public override bool CanCast(Entity caster, Entity target) => target != null && !target.isDead;

    public override IEnumerator Cast(Entity caster, Entity target)
    {
        // The strikes play FASTER than a normal swing (attack speed × strikeSpeed) so the ability is a
        // burst, not just two ordinary attacks back to back.
        float flurrySpeed = GetAttackSpeed(caster) * Mathf.Max(0.1f, strikeSpeed);
        Animator animator = GetAnimator(caster);
        float damage = (caster.Stats != null ? caster.Stats.Damage.Value : 10f) * damagePerStrike;

        int count = Mathf.Max(1, strikes);
        for (int s = 0; s < count; s++)
        {
            if (target == null || target.isDead) yield break;

            if (animator != null) animator.speed = flurrySpeed;
            if (caster.isCharacter && caster.character != null) caster.character.Slash();
            else if (caster.monster != null) caster.monster.Attack();

            // Land on the animation's real contact frame; tolerances scale with the sped-up rate.
            yield return WaitForAnimationEvent(caster, CharacterHitEvent, MonsterHitEvent,
                0.2f / flurrySpeed, 1f / flurrySpeed);
            if (animator != null) animator.speed = 1f;

            if (target != null && !target.isDead)
            {
                target.TakeDamage(damage, caster);
                AbilityFeedback.Impact(target);   // heavy hitstop on each strike

                if (knockbackForce > 0f && !target.isDead)
                {
                    Vector3 dir = (target.transform.position - caster.transform.position).normalized;
                    target.ApplyKnockback(dir, knockbackForce);
                }
            }

            if (s < count - 1 && betweenStrikes > 0f)
                yield return new WaitForSeconds(betweenStrikes / flurrySpeed);
        }
    }
}
