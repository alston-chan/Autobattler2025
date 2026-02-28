using UnityEngine;

/// <summary>
/// Handles knockback velocity, damping, stun, and immunity timers.
/// </summary>
public class Knockback : MonoBehaviour
{
    [Header("Knockback Settings")]
    [SerializeField] private float damping = 10f;
    [SerializeField] private float stunTime = 0.05f;
    [SerializeField] private float immunityTime = 0.8f;

    private Vector3 _velocity = Vector3.zero;
    private float _stunTimer;
    private float _immunityTimer;

    /// <summary>True while the entity is stunned from knockback and should not move.</summary>
    public bool IsStunned => _stunTimer > 0f;

    /// <summary>True while knockback velocity is still being applied.</summary>
    public bool IsActive => _velocity.magnitude > 0.01f;

    public void Apply(Vector3 direction, float force)
    {
        if (_immunityTimer > 0f) return;

        _velocity += direction.normalized * force;
        _stunTimer = stunTime;
        // _immunityTimer = immunityTime;  // Uncomment to enable immunity window
    }

    public void Tick()
    {
        if (_stunTimer > 0f) _stunTimer -= Time.deltaTime;
        if (_immunityTimer > 0f) _immunityTimer -= Time.deltaTime;

        if (_velocity.magnitude > 0.01f)
        {
            transform.position += _velocity * Time.deltaTime;
            _velocity = Vector3.Lerp(_velocity, Vector3.zero, 1 - Mathf.Exp(-damping * Time.deltaTime));
        }
    }
}
