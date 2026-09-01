using UnityEngine;

/// <summary>
/// Throw a boomerang past the enemy and let it cut its way back.
///
/// Thrown BEYOND the target rather than at it, because the whole value of the weapon is the length
/// of the path: everything strung out between the thrower and the far point is hit going out and
/// hit again coming home. Against one enemy it is two hits and a long wait; against a column
/// advancing on the formation it is the best thing in the kit.
/// </summary>
[CreateAssetMenu(menuName = "Spells/BoomerangThrowSpell")]
public class BoomerangThrowSpell : ThrownSupplySpell
{
    [Header("Boomerang")]
    [Tooltip("Per pass. Everything on the path takes this on the way out AND on the way back, so " +
             "the damage a throw is worth depends entirely on how many bodies the line crosses.")]
    public float damage = 12f;

    public float speed = 11f;
    public float hitRadius = 0.7f;

    [Tooltip("How far past the target the throw reaches before turning. This, not the damage, is " +
             "the weapon's real stat: it decides how much of the field the path crosses.")]
    public float overshoot = 3.5f;

    public override float BaseDamage => damage;

    private void Reset()
    {
        range = 7f;
        cooldown = 5f;
        manaCost = 75f;
    }

    protected override void Release(Entity caster, Entity target)
    {
        var go = BuildSupply(caster, "ThrownBoomerang");
        if (go == null) return;

        Vector3 origin = go.transform.position;
        Vector3 direction = (target.transform.position - origin);
        direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.right;
        Vector3 apex = target.transform.position + direction * overshoot;
        go.AddComponent<ThrownBoomerang>()
          .Launch(caster, apex, speed, damage, hitRadius, spinDegreesPerSecond);
    }
}
