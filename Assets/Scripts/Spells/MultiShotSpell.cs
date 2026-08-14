using System.Collections;
using UnityEngine;

/// <summary>
/// Archer ultimate: looses an arrow at each of the N closest enemies. The first real use of a
/// <see cref="TargetSelector"/> — the "Selector" half of the ability grammar. Reuses the bow draw
/// animation and the existing homing <see cref="Assets.HeroEditor.Common.Scripts.ExampleScripts.Projectile"/>,
/// so each arrow curves to its own target. A cost ability fired when the mana bar is full.
/// </summary>
[CreateAssetMenu(menuName = "Spells/Ultimate/Multi Shot")]
public class MultiShotSpell : Spell
{
    [Header("Multi Shot")]
    public GameObject arrowPrefab;
    [Tooltip("How many of the nearest enemies to hit.")]
    public int targetCount = 3;
    [Tooltip("Damage per arrow, as a multiple of the caster's Damage stat.")]
    public float damagePerArrow = 1f;
    public float knockbackForce = 3.5f;
    [Tooltip("Draw time before loosing (seconds, divided by attack speed).")]
    public float chargeTime = 0.4f;
    [Tooltip("Delay between arrows (seconds, divided by attack speed) — a quick fan, not a volley.")]
    public float betweenShots = 0.06f;

    private const float ArrowSpeed = 18.75f;

    private void Reset()
    {
        spellName = "Multi Shot";
        range = 15f;
        cooldown = 0f;      // gated by mana, not a clock
        manaCost = 100f;    // = default Mana.maxMana → fires when the bar is full
    }

    public override bool CanCast(Entity caster, Entity target)
        => caster.isCharacter && caster.character != null && TargetSelector.CountLivingEnemies(caster) > 0;

    public override IEnumerator Cast(Entity caster, Entity target)
    {
        var targets = TargetSelector.ClosestEnemies(caster, targetCount);
        if (targets.Count == 0) yield break;

        float attackSpeed = GetAttackSpeed(caster);
        Animator animator = caster.character.Animator;

        // Draw, at attack speed, reusing the basic bow's charge states.
        caster.character.GetReady();
        if (animator != null) animator.speed = attackSpeed;
        caster.character.Animator.SetInteger("Charge", 1);
        yield return new WaitForSeconds(chargeTime / attackSpeed);
        caster.character.Animator.SetInteger("Charge", 2);

        float damage = (caster.Stats != null ? caster.Stats.Damage.Value : 10f) * damagePerArrow;

        for (int i = 0; i < targets.Count; i++)
        {
            var t = targets[i];
            if (t == null || t.isDead) continue;

            FireArrowAt(caster, t, damage);
            if (betweenShots > 0f) yield return new WaitForSeconds(betweenShots / attackSpeed);
        }

        if (animator != null) animator.speed = 1f;
    }

    private void FireArrowAt(Entity caster, Entity target, float damage)
    {
        if (arrowPrefab == null || caster.fireTransform == null) return;

        var arrow = Object.Instantiate(arrowPrefab, caster.fireTransform.position, Quaternion.identity);

        var projectile = arrow.GetComponent<Assets.HeroEditor.Common.Scripts.ExampleScripts.Projectile>();
        if (projectile != null)
        {
            projectile.damage = damage;
            projectile.knockbackForce = knockbackForce;
            projectile.shooter = caster;
            projectile.target = target;   // Projectile homes to its target
        }

        // Aim the initial velocity at the target; the projectile's homing handles the rest.
        var rb = arrow.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            Vector2 dir = ((Vector2)target.transform.position - (Vector2)caster.fireTransform.position).normalized;
            rb.velocity = dir * ArrowSpeed;
        }
    }
}
