using System.Collections;
using UnityEngine;

[CreateAssetMenu(menuName = "Spells/ShockwaveSpell")]
public class ShockwaveSpell : Spell
{
    [Header("Shockwave Properties")]
    public float damage = 15f;
    public float radius = 4f;
    public float knockbackForce = 5f;

    public GameObject shockwaveEffectPrefab;

    public override bool CanCast(Entity caster, Entity target)
    {
        return true;
    }

    public override IEnumerator Cast(Entity caster, Entity target)
    {
        if (shockwaveEffectPrefab != null)
        {
            GameObject effect = GameObject.Instantiate(shockwaveEffectPrefab, caster.transform.position, Quaternion.identity);
            effect.transform.SetParent(caster.transform);
        }

        // Find all entities in the scene
        Entity[] allEntities = GameObject.FindObjectsOfType<Entity>();
        foreach (Entity entity in allEntities)
        {
            if (entity == caster || entity.isDead) continue;
            // Only affect enemies
            if (entity.isTeam != caster.isTeam)
            {
                float dist = Vector3.Distance(caster.transform.position, entity.transform.position);
                if (dist <= radius)
                {
                    // Apply damage
                    entity.TakeDamage(damage);
                    // Apply knockback
                    Vector3 dir = (entity.transform.position - caster.transform.position).normalized;
                    entity.ApplyKnockback(dir, knockbackForce);
                }
            }
        }
        yield return null;
    }
}
