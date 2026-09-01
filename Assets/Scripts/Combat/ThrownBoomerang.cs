using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A boomerang: out to a point, then back to the hand that threw it, cutting whatever it passes
/// both ways.
///
/// This is the one thrown weapon whose shape is not a point or a line but a PATH, and the reason
/// it is worth having: a bomb asks the enemy to be bunched together, a star asks for one good
/// target, and a boomerang asks them to be strung out between the thrower and somewhere else —
/// which is exactly what a line of enemies advancing on a formation looks like. Everything it
/// passes is hit once going out and once coming back, so it is at its best against depth and
/// nearly worthless against a single foe.
///
/// It returns to wherever the thrower is NOW rather than where they stood, so a thrower who has
/// moved still catches it, and one who has died lets it fall where they fell.
/// </summary>
public class ThrownBoomerang : MonoBehaviour
{
    private Entity _thrower;
    private Vector3 _apex;
    private float _speed, _damage, _hitRadius, _spin, _critChance;
    private bool _returning;

    // Cleared at the turn, so each enemy is cut once on the way out and once on the way home.
    private readonly HashSet<Entity> _hitThisLeg = new HashSet<Entity>();

    public void Launch(Entity thrower, Vector3 apex, float speed, float damage, float hitRadius, float spin, float critChance)
    {
        _thrower = thrower;
        _apex = apex;
        _speed = Mathf.Max(0.1f, speed);
        _damage = damage;
        _hitRadius = hitRadius;
        _spin = spin;
        _critChance = critChance;

        // A boomerang that somehow never comes home must not orbit for the rest of the fight.
        Destroy(gameObject, 8f);
    }

    private void Update()
    {
        Vector3 destination = _returning ? ReturnPoint() : _apex;

        transform.position = Vector3.MoveTowards(transform.position, destination, _speed * Time.deltaTime);
        transform.Rotate(0f, 0f, _spin * Time.deltaTime);

        CutEnemiesInReach();

        if (Vector3.Distance(transform.position, destination) > 0.05f) return;

        if (_returning)
        {
            Destroy(gameObject);   // caught
            return;
        }

        _returning = true;
        _hitThisLeg.Clear();       // the way home is a fresh chance to hit the same people
    }

    /// <summary>Home is the thrower if they still stand, otherwise the spot they were last seen.</summary>
    private Vector3 ReturnPoint() =>
        _thrower != null && !_thrower.isDead ? _thrower.transform.position : transform.position;

    private void CutEnemiesInReach()
    {
        var all = EntityRegistry.All;
        for (int i = all.Count - 1; i >= 0; i--)
        {
            var entity = all[i];
            if (entity == null || entity.isDead) continue;
            if (_thrower != null && entity.isTeam == _thrower.isTeam) continue;
            if (_hitThisLeg.Contains(entity)) continue;
            if (Vector3.Distance(transform.position, entity.transform.position) > _hitRadius) continue;

            entity.TakeDamage(_damage, _thrower, AttackRoll.IsCrit(_critChance));
            _hitThisLeg.Add(entity);
        }
    }
}
