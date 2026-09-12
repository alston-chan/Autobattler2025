using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Wall Keeper lower — Hold the Line: while you have not moved, allies Beside you take less. Beside
/// is read from the frozen board at the bell (Docs/PositionalKeywords.md), the allies wear the Held
/// Line status, and the first real step the owner takes lifts it. Tier is the status's stack count.
/// </summary>
[CreateAssetMenu(menuName = "Data/Engravings/Hold the Line", fileName = "HoldTheLine")]
public class HoldTheLineEngraving : Engraving
{
    [Tooltip("The status the allies wear while the line holds (5% less taken per stack).")]
    public Status heldLine;
    [Tooltip("How far the owner may drift before the line counts as moved.")]
    public float slack = 0.6f;

    [System.NonSerialized] private readonly List<Entity> _held = new List<Entity>();
    [System.NonSerialized] private float _walked;
    [System.NonSerialized] private bool _broken;

    public override void OnCombatStart(Entity owner, int tier)
    {
        _held.Clear(); _walked = 0f; _broken = false;
        if (owner == null || heldLine == null) return;
        foreach (var ally in BoardSnapshot.Beside(owner))
        {
            if (ally == null || ally.isDead || ally.Statuses == null) continue;
            ally.Statuses.Apply(heldLine, 0f, owner, Mathf.Max(1, tier));
            _held.Add(ally);
        }
    }

    public override void OnMoved(Entity owner, float distance, int tier)
    {
        if (_broken) return;
        _walked += distance;
        if (_walked < slack) return;
        _broken = true;
        Lift();
    }

    public override void OnCombatEnd(Entity owner, int tier) => Lift();

    private void Lift()
    {
        foreach (var ally in _held)
            if (ally != null && ally.Statuses != null && heldLine != null) ally.Statuses.Remove(heldLine);
        _held.Clear();
    }

    public override string DescribeTier(int tier) => $"While you have not moved, allies Beside you take {5 * Mathf.Max(1, tier)}% less.";
}
