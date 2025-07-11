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

    private void OnEnable()
    {
        range = 1.5f; // Default melee range, can be overridden per asset
    }

    public override bool CanCast(Entity caster, Entity target) => target != null;

    public override IEnumerator Cast(Entity caster, Entity target)
    {
        if (caster.isCharacter)
        {
            if (Random.value < 0.5f)
                caster.character.Slash();
            else
                caster.character.Jab();
        }
        else
        {
            caster.monster.Attack();
        }

        yield return new WaitForSeconds(0.2f);

        bool isCrit = Random.value < critChance;
        target.TakeDamage(damage);

        if (isCrit && target != null)
        {
            Vector3 knockbackDir = (target.transform.position - caster.transform.position).normalized;
            target.ApplyKnockback(knockbackDir, critKnockbackForce);
        }
        else if (normalKnockbackForce > 0f)
        {
            Vector3 knockbackDir = (target.transform.position - caster.transform.position).normalized;
            target.ApplyKnockback(knockbackDir, normalKnockbackForce);
        }
    }
}
