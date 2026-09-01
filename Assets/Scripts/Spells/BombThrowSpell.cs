using UnityEngine;

/// <summary>
/// Lob a bomb at where the enemy is standing.
///
/// The ability that hits a PLACE. Shockwave is an area attack too, but it erupts from the caster,
/// so it rewards being surrounded; a bomb rewards the enemy being bunched somewhere else, which is
/// a different question to ask of a formation.
///
/// Aiming at a point also disposes of a whole class of bug rather than solving it: a homing
/// projectile has to decide what to do when its target dies mid-flight, and a bomb does not care —
/// it lands where it was thrown and hits whoever is standing there.
/// </summary>
[CreateAssetMenu(menuName = "Spells/BombThrowSpell")]
public class BombThrowSpell : ThrownSupplySpell
{
    [Header("Blast")]
    public float damage = 30f;
    public float radius = 2.5f;

    [Tooltip("Chance for a hit to land as a critical, rolled per victim.")]
    public float critChance = 0.15f;

    [Tooltip("Applied outward from the point of impact, so a bomb in a crowd scatters it.")]
    public float knockbackForce = 4f;
    public float hitstopDuration = 0.12f;

    [Header("Flight")]
    [Tooltip("Seconds in the air. Long enough to read as a lob, and long enough that a unit which " +
             "happens to move can walk out of it — the price of throwing at a place.")]
    public float flightTime = 0.55f;
    [Tooltip("Height of the arc at its peak, in world units.")]
    public float arcHeight = 2.2f;

    public override float BaseDamage => damage;

    private void Reset()
    {
        range = 7f;
        cooldown = 6f;
        manaCost = 100f;
    }

    protected override void Release(Entity caster, Entity target)
    {
        var go = BuildSupply(caster, "ThrownBomb");
        if (go == null) return;

        // Aimed where the target stands AT RELEASE, and committed to from there.
        go.AddComponent<ThrownBomb>()
          .Launch(caster, target.transform.position, flightTime, arcHeight, damage, radius,
                  knockbackForce, hitstopDuration, spinDegreesPerSecond, critChance);
    }
}
