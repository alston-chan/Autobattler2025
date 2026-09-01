using UnityEngine;

/// <summary>
/// Throw a star at whoever is being fought. Cheap, quick, single-target.
///
/// Deliberately the plain one. Beside a bomb that wants a crowd and a boomerang that wants a
/// column, this wants nothing: it is the option a unit can always use well, which is what makes
/// the other two worth choosing when their conditions are met.
/// </summary>
[CreateAssetMenu(menuName = "Spells/StarThrowSpell")]
public class StarThrowSpell : ThrownSupplySpell
{
    [Header("Star")]
    public float damage = 18f;

    [Tooltip("Chance for a hit to land as a critical, rolled per victim.")]
    public float critChance = 0.15f;

    [Tooltip("Flat and fast. A thrown blade that floats reads as a leaf.")]
    public float speed = 16f;

    [Tooltip("How close it must pass to bite. Generous, because it is a small sprite moving quickly " +
             "and a star that visibly passes through someone must not miss.")]
    public float hitRadius = 0.6f;

    [Tooltip("How far past the target it keeps going before falling short, so a miss carries on " +
             "rather than stopping in mid-air.")]
    public float overshoot = 3f;

    public override float BaseDamage => damage;

    private void Reset()
    {
        range = 7f;
        cooldown = 3f;
        manaCost = 50f;   // half a bar: the affordable one
    }

    protected override void Release(Entity caster, Entity target)
    {
        var go = BuildSupply(caster, "ThrownStar");
        if (go == null) return;

        // Aimed through the target rather than at it, so a miss flies past and leaves.
        Vector3 origin = go.transform.position;
        Vector3 direction = (target.transform.position - origin);
        direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.right;
        Vector3 end = target.transform.position + direction * overshoot;
        go.AddComponent<ThrownStar>()
          .Launch(caster, end, speed, damage, hitRadius, spinDegreesPerSecond, critChance);
    }
}
