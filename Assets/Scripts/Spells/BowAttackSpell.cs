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

    private void OnEnable()
    {
        range = 15f; // Default bow range, can be overridden per asset
    }

    public override bool CanCast(Entity caster, Entity target) => target != null;

    public override IEnumerator Cast(Entity caster, Entity target)
    {
        caster.character.GetReady();
        float actualChargeTime = chargeTime;
        if (clipCharge != null) actualChargeTime = clipCharge.length;
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
                projectile.damage = damage;
                projectile.knockbackForce = knockbackForce;
                projectile.shooter = caster;
                projectile.target = target;
            }
        }
        yield return new WaitForSeconds(0.1f);
    }
}
