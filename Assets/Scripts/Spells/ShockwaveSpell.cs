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

    [Header("Juice")]
    [Tooltip("Exaggerated hitstop (seconds): the battlefield freezes for a beat, then the knockback launches the hit enemies. 0 disables.")]
    public float hitstopDuration = 0.25f;
    [Tooltip("If true, EVERY entity freezes during the shockwave ('world holds its breath'). If false, only the caster and enemies hit freeze.")]
    public bool freezeEntireBattlefield = true;

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

        // Damage + knockback every enemy in the radius. Apply knockback BEFORE the freeze so the
        // velocity is set and held during hitstop, then launches when the freeze ends.
        var allEntities = EntityRegistry.All;
        for (int i = allEntities.Count - 1; i >= 0; i--)
        {
            Entity entity = allEntities[i];
            if (entity == caster || entity.isDead) continue;
            if (entity.isTeam != caster.isTeam)
            {
                float dist = Vector3.Distance(caster.transform.position, entity.transform.position);
                if (dist <= radius)
                {
                    entity.TakeDamage(damage);
                    Vector3 dir = (entity.transform.position - caster.transform.position).normalized;
                    entity.ApplyKnockback(dir, knockbackForce);
                    if (!freezeEntireBattlefield) entity.ApplyHitstop(hitstopDuration);
                }
            }
        }

        // Exaggerated hitstop. On the big AoE it's dramatic to freeze the whole battlefield: the
        // wave erupts over frozen bodies, then everyone resumes and the hit enemies launch out.
        if (freezeEntireBattlefield)
        {
            var living = EntityRegistry.All;
            for (int i = living.Count - 1; i >= 0; i--)
                living[i].ApplyHitstop(hitstopDuration);
        }
        else
        {
            caster.ApplyHitstop(hitstopDuration);
        }

        yield return null;
    }
}
