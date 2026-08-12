using UnityEngine;

/// <summary>
/// Brief per-entity freeze-frame ("hitstop") on impact — a core combat-juice trick that makes
/// hits feel meaty. Freezes the entity's animator, and (via <see cref="Entity"/> gating its
/// movement/knockback while active) holds it in place, then restores. Per-entity rather than
/// global Time.timeScale so simultaneous fights don't stutter one another.
/// </summary>
public class Hitstop : MonoBehaviour
{
    private float _timer;
    private Animator _animator;
    private float _restoreSpeed = 1f;

    /// <summary>True while the entity is frozen by hitstop.</summary>
    public bool IsActive => _timer > 0f;

    public void Initialize(Entity entity)
    {
        if (entity.isCharacter && entity.character != null) _animator = entity.character.Animator;
        else if (entity.monster != null) _animator = entity.monster.Animator;
    }

    /// <summary>
    /// Freeze for <paramref name="duration"/> real seconds. Overlapping calls extend the freeze
    /// (take the longer), they don't stack.
    /// </summary>
    public void Freeze(float duration)
    {
        if (duration <= 0f) return;

        // Remember the speed to return to, but only when starting a fresh freeze so we don't
        // capture our own 0.
        if (!IsActive && _animator != null) _restoreSpeed = _animator.speed;

        _timer = Mathf.Max(_timer, duration);
        if (_animator != null) _animator.speed = 0f;
    }

    private void Update()
    {
        if (_timer <= 0f) return;

        _timer -= Time.deltaTime;
        if (_timer <= 0f && _animator != null && Mathf.Approximately(_animator.speed, 0f))
            _animator.speed = _restoreSpeed; // only restore if we still own the speed
    }
}
