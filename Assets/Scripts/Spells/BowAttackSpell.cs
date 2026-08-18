using System.Collections;
using UnityEngine;

[CreateAssetMenu(menuName = "Spells/BowAttackSpell")]
public class BowAttackSpell : Spell
{
    [Header("Bow Attack Properties")]
    public GameObject arrowPrefab;
    public float damage = 10f;
    public float knockbackForce = 3.5f;
    public float chargeTime = 0.5f;
    public AnimationClip clipCharge;

    // Basic weapon attack — its rate scales with the caster's AttackSpeed.
    public override bool ScalesWithAttackSpeed => true;
    public override float BaseDamage => damage;

    private void Reset()
    {
        range = 15f; // Sensible default for a NEW bow asset; override per-asset in the Inspector.
    }

    public override bool CanCast(Entity caster, Entity target) => target != null;

    public override IEnumerator Cast(Entity caster, Entity target)
    {
        if (caster.character == null) yield break; // bow attacks are character-only

        // Draw at attack-speed: faster attack speed = quicker draw and shorter charge.
        float attackSpeed = GetAttackSpeed(caster);
        Animator animator = caster.character.Animator;

        caster.character.GetReady();
        float actualChargeTime = (clipCharge != null ? clipCharge.length : chargeTime) / attackSpeed;
        if (animator != null) animator.speed = attackSpeed;
        caster.character.Animator.SetInteger("Charge", 1);
        yield return new WaitForSeconds(actualChargeTime);
        caster.character.Animator.SetInteger("Charge", 2);

        if (arrowPrefab != null && caster.fireTransform != null)
        {
            var arrow = GameObject.Instantiate(arrowPrefab, caster.fireTransform);
            var rb = arrow.GetComponent<Rigidbody2D>();
            const float speed = 18.75f;
            arrow.transform.localPosition = Vector3.zero;
            arrow.transform.localRotation = Quaternion.identity;
            arrow.transform.SetParent(null);
            rb.velocity = speed * caster.fireTransform.right * Mathf.Sign(caster.character.transform.lossyScale.x) * Random.Range(0.85f, 1.15f);

            var projectile = arrow.GetComponent<Assets.HeroEditor.Common.Scripts.ExampleScripts.Projectile>();
            if (projectile != null)
            {
                projectile.damage = caster.Stats != null ? caster.Stats.Damage.Value : damage;
                projectile.knockbackForce = knockbackForce;
                projectile.shooter = caster;
                projectile.target = target;
            }
        }

        if (animator != null) animator.speed = 1f;
        yield return new WaitForSeconds(0.1f / attackSpeed);

        // Back to ready (HeroEditor: 0 = ready, 1 = charging, 2 = release). Leaving it on release
        // strands the archer in that pose between fights, and — worse — a draw interrupted at 1 makes
        // the next shot's SetInteger("Charge", 1) a no-op, so the animator never re-enters the draw
        // and the bow silently stops animating.
        if (caster.character != null && caster.character.Animator != null)
            caster.character.Animator.SetInteger("Charge", 0);
    }
}
