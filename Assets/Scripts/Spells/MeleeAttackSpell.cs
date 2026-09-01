using System.Collections;
using UnityEngine;

[CreateAssetMenu(menuName = "Spells/MeleeAttackSpell")]
public class MeleeAttackSpell : Spell
{
    /// <summary>
    /// Which swing the rig plays. The animator picks the clip from the GRIP — one-handed,
    /// two-handed, paired — so this is the only say a weapon gets in how it looks.
    ///
    /// Only two of these read as different weapons: a slash travels through an arc and a jab goes
    /// straight down the weapon's axis, which is why a spear looks like a spear. A one-handed axe
    /// and mace cannot be told apart from a sword by silhouette at all, and have to earn their
    /// identity from rhythm and numbers instead.
    /// </summary>
    public enum Swing { Slash, Jab }

    [Header("Melee Attack Properties")]
    [Tooltip("Which motion this weapon swings with. Chosen per weapon rather than per swing: the " +
             "attack used to pick slash or jab at random, so a spear thrust half the time and a " +
             "sword did too, and the motion told the player nothing about what was hitting them.")]
    public Swing swing = Swing.Slash;

    [Tooltip("Multiplies the swing's playback speed on top of attack speed. Below 1 reads as weight " +
             "— a laboured axe — above 1 as a light, quick weapon.")]
    public float swingSpeedScale = 1f;

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

        // A heavy weapon plays its swing slower than a light one even at the same attack speed.
        float playback = attackSpeed * Mathf.Max(0.05f, swingSpeedScale);

        Animator animator = GetAnimator(caster);
        if (animator != null) animator.speed = playback;

        // Trigger the swing animation.
        if (caster.isCharacter && caster.character != null)
        {
            if (swing == Swing.Jab) caster.character.Jab();
            else caster.character.Slash();
        }
        else if (caster.monster != null)
        {
            caster.monster.Attack();
        }

        // Land damage on the animation's real contact frame instead of a fixed guess.
        // Timing tolerances scale with attack speed since the whole swing is sped up/slowed down.
        yield return WaitForAnimationEvent(caster, CharacterHitEvent, MonsterHitEvent,
            hitDelayFallback / playback, maxHitWait / playback);

        if (animator != null) animator.speed = 1f;

        // The target may have died or despawned during the wind-up.
        if (target == null || target.isDead) yield break;

        bool isCrit = AttackRoll.IsCrit(critChance);
        float finalDamage = AttackRoll.DamageOf(caster, damage);
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
