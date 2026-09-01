using UnityEngine;

/// <summary>
/// A throwing star: fast, flat, and spent on the first thing it touches.
///
/// The counterpart to the bomb. A bomb is thrown at a place and rewards enemies being bunched; a
/// star is thrown along a line and rewards nothing but being aimed — it is the cheap, certain
/// option next to the bomb's slow, situational one. It travels flat rather than lobbed because a
/// thing that arrives quickly should look like it arrives quickly.
/// </summary>
public class ThrownStar : MonoBehaviour
{
    private Entity _thrower;
    private Vector3 _end;
    private float _speed, _damage, _hitRadius, _spin, _critChance;
    private bool _spent;

    public void Launch(Entity thrower, Vector3 end, float speed, float damage, float hitRadius, float spin, float critChance)
    {
        _thrower = thrower;
        _end = end;
        _speed = Mathf.Max(0.1f, speed);
        _damage = damage;
        _hitRadius = hitRadius;
        _spin = spin;
        _critChance = critChance;

        Vector3 heading = _end - transform.position;
        if (heading.sqrMagnitude > 0.0001f) transform.right = heading.normalized;

        // Nothing keeps a star alive once it has flown past everything.
        Destroy(gameObject, 4f);
    }

    private void Update()
    {
        if (_spent) return;

        transform.position = Vector3.MoveTowards(transform.position, _end, _speed * Time.deltaTime);
        transform.Rotate(0f, 0f, _spin * Time.deltaTime);

        var all = EntityRegistry.All;
        for (int i = all.Count - 1; i >= 0; i--)
        {
            var entity = all[i];
            if (entity == null || entity.isDead) continue;
            if (_thrower != null && entity.isTeam == _thrower.isTeam) continue;
            if (Vector3.Distance(transform.position, entity.transform.position) > _hitRadius) continue;

            entity.TakeDamage(_damage, _thrower, AttackRoll.IsCrit(_critChance));
            _spent = true;
            Destroy(gameObject);
            return;
        }

        // Reached where it was aimed without touching anyone: a clean miss.
        if (Vector3.Distance(transform.position, _end) < 0.01f) Destroy(gameObject);
    }
}
