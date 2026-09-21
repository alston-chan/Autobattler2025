using UnityEngine;

/// <summary>
/// A body in flight. Knockback is a velocity that dies out with damping, a brief stun while it
/// starts, and a memory of who threw it — so when this body hits another body or the wall
/// (<see cref="CombatPhysics"/>), the collision knows who to credit and how hard it was. A charging
/// body (a Bull Rush) is the same thing thrown by itself: it is the weapon, so it is not stunned
/// and not hurt by what it hits.
/// </summary>
public class Knockback : MonoBehaviour
{
    [Header("Knockback Settings")]
    [Tooltip("Used when the physics settings say nothing. A throw travels about force / damping units.")]
    [SerializeField] private float damping = 10f;
    [SerializeField] private float stunTime = 0.05f;
    [SerializeField] private float immunityTime = 0.8f;

    private Vector3 _velocity = Vector3.zero;
    private float _stunTimer;
    private float _immunityTimer;
    private float _impactCooldown;
    private Entity _entity;

    /// <summary>True while the entity is stunned from knockback and should not move.</summary>
    public bool IsStunned => _stunTimer > 0f;

    /// <summary>True while knockback velocity is still being applied.</summary>
    public bool IsActive => _velocity.magnitude > 0.01f;

    /// <summary>
    /// Whether the unit may steer itself again. A hard throw takes a body off its feet and there is
    /// nothing to be done until it lands; the tail of the slide is not that. Splitting the two is
    /// what stops a scrum's constant small shoves — measured at 38 to 70 a minute — from adding up
    /// to a quarter of the fight spent unable to walk, while barely moving.
    /// </summary>
    public bool Steerable
    {
        get
        {
            var s = CombatPhysics.Active;
            return _velocity.magnitude <= (s != null ? s.steerSpeed : 2f);
        }
    }

    /// <summary>The body's velocity in the world, units per second.</summary>
    public Vector3 Velocity => _velocity;
    public float Speed => _velocity.magnitude;

    /// <summary>Who threw this body — the caster whose spell knocked it, or the body it was hit by. Null once it has come to rest.</summary>
    public Entity Launcher { get; private set; }

    /// <summary>Thrown by itself, at something: it is not stunned and not hurt by what it hits.</summary>
    public bool Charging { get; private set; }

    /// <summary>Whether this body may hit something now. One collision is one hit, then a short pause.</summary>
    public bool CanImpact => _impactCooldown <= 0f;

    private void Awake() { _entity = GetComponent<Entity>(); }

    public void Apply(Vector3 direction, float force) => Apply(direction, force, null, false);

    /// <summary>Throw this body. <paramref name="source"/> is credited for what it hits.</summary>
    public void Apply(Vector3 direction, float force, Entity source, bool charging = false)
    {
        if (_immunityTimer > 0f) return;

        // No force, no effect — including no stun. The stun is a consequence of being knocked, so a
        // weapon that does not knock must not freeze its target either. Without this, giving an
        // attack zero knockback still stunned on every hit, which for a fast weapon reads as a
        // target that has stopped working with nothing on screen to explain it.
        if (force <= 0f) return;

        _velocity += direction.normalized * force;
        Launcher = source;
        Charging = charging;
        if (!charging) _stunTimer = Mathf.Max(_stunTimer, stunTime);
        // _immunityTimer = immunityTime;  // Uncomment to enable immunity window
    }

    /// <summary>Set the body flying at this velocity, thrown by <paramref name="source"/> — what a collision does to the struck body.</summary>
    public void Launch(Vector3 velocity, Entity source)
    {
        _velocity = velocity;
        Launcher = source;
        Charging = false;
    }

    /// <summary>Change the velocity without changing who threw it — a bounce, a slide, momentum passed on.</summary>
    public void SetVelocity(Vector3 velocity) => _velocity = velocity;

    /// <summary>Hold the body still for a moment. Extends a stun in progress, never shortens it.</summary>
    public void Stagger(float seconds) => _stunTimer = Mathf.Max(_stunTimer, seconds);

    /// <summary>This body just hit something; it may not hit again until the cooldown passes.</summary>
    public void MarkImpact()
    {
        var s = CombatPhysics.Active;
        _impactCooldown = s != null ? s.impactCooldown : 0.15f;
    }

    public void Tick()
    {
        float dt = Time.deltaTime;
        if (_stunTimer > 0f) _stunTimer -= dt;
        if (_immunityTimer > 0f) _immunityTimer -= dt;
        if (_impactCooldown > 0f) _impactCooldown -= dt;

        if (_velocity.magnitude > 0.01f)
        {
            // Move, and notice the wall: a body carried past the arena's edge is slammed into it.
            Vector3 next = transform.position + _velocity * dt;
            Vector3 clamped = ArenaBounds.ClampToArena(next);
            Vector3 back = clamped - next;
            transform.position = clamped;
            if (back.sqrMagnitude > 1e-8f && _entity != null) CombatPhysics.WallSlam(_entity, this, back.normalized);

            var s = CombatPhysics.Active;
            float d = s != null && s.damping > 0f ? s.damping : damping;
            _velocity = Vector3.Lerp(_velocity, Vector3.zero, 1 - Mathf.Exp(-d * dt));
            // At rest once the drift is too slow to see. The exponential tail never reaches zero on
            // its own, and a unit that may not walk until it does stands idle for most of a second
            // after the throw has visibly ended.
            float rest = s != null ? s.restSpeed : 0.6f;
            if (_velocity.magnitude <= Mathf.Max(0.01f, rest)) { _velocity = Vector3.zero; Launcher = null; Charging = false; }
        }

        // A thrown body leans: the top lags the push, so a slide reads as being shoved, not gliding
        // on ice. A charging body leans the other way, into its run. Back upright as it comes to rest.
        float lean = _velocity.magnitude > 0.01f ? Mathf.Clamp(_velocity.x * (Charging ? -LeanPerSpeed : LeanPerSpeed), -MaxLean, MaxLean) : 0f;
        var current = transform.rotation.eulerAngles.z; if (current > 180f) current -= 360f;
        float tilt = Mathf.MoveTowards(current, lean, LeanRate * dt);
        if (Mathf.Abs(tilt - current) > 0.001f) transform.rotation = Quaternion.Euler(0f, 0f, tilt);
    }

    private const float LeanPerSpeed = 1.6f, MaxLean = 14f, LeanRate = 160f;
}
