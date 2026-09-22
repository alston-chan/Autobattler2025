using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Wall Keeper lower — Hold the Line: allies near you take less. They wear the Held Line status
/// while they stand within <see cref="radius"/> of you, wherever the fight has taken you both; tier
/// is the status's stack count.
///
/// It used to be "while you have not moved, allies Beside you take less", and it carried the Hold
/// stance so its wearer would not walk off and break it with its first step. Hold was removed
/// (a unit standing idle at the bell read as a bug), and the line no longer needs one: a bodyguard
/// is still a reason to seat the Wall Keeper next to the archers, without anyone standing still.
/// </summary>
[CreateAssetMenu(menuName = "Data/Engravings/Hold the Line", fileName = "HoldTheLine")]
public class HoldTheLineEngraving : Engraving
{
    [Tooltip("The status the allies wear while they are close (5% less taken per stack).")]
    public Status heldLine;
    [Tooltip("How close an ally must stand to be covered. 2 covers the cells beside yours at the bell.")]
    public float radius = 2f;

    public override void OnCombatStart(Entity owner, int tier)
    {
        if (owner == null || heldLine == null) return;
        var guard = owner.GetComponent<Bodyguard>();
        if (guard == null) guard = owner.gameObject.AddComponent<Bodyguard>();
        guard.Begin(owner, heldLine, radius, Mathf.Max(1, tier));
    }

    public override void OnCombatEnd(Entity owner, int tier) => Stop(owner);
    public override void OnRevoked(Entity owner, int tier) => Stop(owner);

    private static void Stop(Entity owner)
    {
        var guard = owner != null ? owner.GetComponent<Bodyguard>() : null;
        if (guard != null) guard.End();
    }

    public override string DescribeTier(int tier) => $"Allies within {radius:0.#} of you take {5 * Mathf.Max(1, tier)}% less.";
}

/// <summary>
/// Hold the Line's cover, kept current a few times a second: allies who come within reach put the
/// status on, those who leave take it off, and everyone is uncovered when the guard falls or the
/// fight ends. On the guard itself because an engraving has no clock.
/// </summary>
public class Bodyguard : MonoBehaviour
{
    private const float Interval = 0.2f;
    private static readonly List<Bodyguard> Active = new List<Bodyguard>();

    private Entity _owner; private Status _status; private float _radius; private int _stacks; private float _next;
    private readonly HashSet<Entity> _covered = new HashSet<Entity>();
    private readonly List<Entity> _leaving = new List<Entity>();

    /// <summary>Who is covered right now.</summary>
    public IReadOnlyCollection<Entity> Covered => _covered;

    public void Begin(Entity owner, Status status, float radius, int stacks)
    {
        End();
        _owner = owner; _status = status; _radius = radius; _stacks = stacks; _next = 0f;
        enabled = true;
        if (!Active.Contains(this)) Active.Add(this);
    }

    public void End()
    {
        Active.Remove(this);
        foreach (var ally in _covered) Uncover(ally);
        _covered.Clear();
        enabled = false;
    }

    // Two guards can cover one ally; the status comes off when the last of them lets go.
    private void Uncover(Entity ally)
    {
        if (ally == null || ally.Statuses == null || _status == null) return;
        foreach (var other in Active)
            if (other != this && other._status == _status && other._covered.Contains(ally)) return;
        ally.Statuses.Remove(_status);
    }

    // Dead is deactivated, and a deactivated guard covers nobody.
    private void OnDisable() { if (_covered.Count > 0) End(); }

    private void Update()
    {
        if (_owner == null || _status == null || Time.time < _next) return;
        _next = Time.time + Interval;
        if (_owner.isDead || !_owner.IsFighting) { End(); return; }

        Vector3 at = _owner.transform.position;
        foreach (var ally in EntityRegistry.All)
        {
            if (ally == null || ally == _owner || ally.isTeam != _owner.isTeam || ally.Statuses == null) continue;
            bool near = !ally.isDead && ally.gameObject.activeInHierarchy && Vector2.Distance(ally.transform.position, at) <= _radius;
            if (near && _covered.Add(ally)) ally.Statuses.Apply(_status, 0f, _owner, _stacks);
        }

        _leaving.Clear();
        foreach (var ally in _covered)
            if (ally == null || ally.isDead || !ally.gameObject.activeInHierarchy || Vector2.Distance(ally.transform.position, at) > _radius) _leaving.Add(ally);
        foreach (var ally in _leaving)
        {
            _covered.Remove(ally);
            Uncover(ally);
        }
    }
}
