using UnityEngine;

/// <summary>
/// A bomb in the air: it arcs to the spot it was aimed at and detonates there.
///
/// Deliberately not a <see cref="Assets.HeroEditor.Common.Scripts.ExampleScripts.Projectile"/>.
/// That one homes at a unit and damages exactly that unit, which is the wrong shape here twice
/// over: a bomb is thrown at a PLACE, and what it hits is whoever happens to be standing there
/// when it lands. Aiming at a place also sidesteps the whole problem of a projectile whose target
/// dies mid-flight — the throw was never about that unit, so nothing needs deciding.
/// </summary>
public class ThrownBomb : MonoBehaviour
{
    private Entity _thrower;
    private Vector3 _start, _end;
    private float _flightTime, _arcHeight, _elapsed;
    private float _damage, _radius, _knockback, _hitstop;
    private float _spin;
    private bool _detonated;

    /// <summary>Send the bomb on its way. It owns its own destruction from here.</summary>
    public void Launch(Entity thrower, Vector3 landing, float flightTime, float arcHeight,
                       float damage, float radius, float knockback, float hitstop, float spin)
    {
        _thrower = thrower;
        _start = transform.position;
        _end = landing;
        _flightTime = Mathf.Max(0.05f, flightTime);
        _arcHeight = arcHeight;
        _damage = damage;
        _radius = radius;
        _knockback = knockback;
        _hitstop = hitstop;
        _spin = spin;
    }

    private void Update()
    {
        if (_detonated) return;

        _elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(_elapsed / _flightTime);

        // Straight line along the ground, lifted by a parabola that is zero at both ends and
        // highest at the midpoint — the shape of a lob, without needing physics.
        Vector3 position = Vector3.Lerp(_start, _end, t);
        position.y += _arcHeight * 4f * t * (1f - t);
        transform.position = position;

        transform.Rotate(0f, 0f, _spin * Time.deltaTime);

        if (t >= 1f) Detonate();
    }

    private void Detonate()
    {
        _detonated = true;

        // Everyone standing in the blast, friend or not? No — the thrower's own side is spared.
        // An autobattler gives the player no way to aim around their own front line, so friendly
        // fire here would punish a formation the player cannot reposition mid-fight.
        var all = EntityRegistry.All;
        for (int i = all.Count - 1; i >= 0; i--)
        {
            var entity = all[i];
            if (entity == null || entity.isDead) continue;
            if (_thrower != null && entity.isTeam == _thrower.isTeam) continue;
            if (Vector3.Distance(transform.position, entity.transform.position) > _radius) continue;

            entity.TakeDamage(_damage, _thrower);

            if (_knockback > 0f)
            {
                // Blown away FROM the blast, so a bomb landing in a crowd scatters it outward.
                Vector3 direction = (entity.transform.position - transform.position).normalized;
                entity.ApplyKnockback(direction, _knockback);
            }

            if (_hitstop > 0f) entity.ApplyHitstop(_hitstop);
        }

        Destroy(gameObject);
    }
}
