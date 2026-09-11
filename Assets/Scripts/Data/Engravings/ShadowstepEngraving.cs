using UnityEngine;

/// <summary>
/// Ninja lower — Shadowstep: every few units moved, throw a shuriken at the nearest Marked enemy.
/// Movement becomes damage, but only against a Mark, so the piece wants the helmet or the dagger
/// beside it. Tier shortens the distance between stars.
/// </summary>
[CreateAssetMenu(menuName = "Data/Engravings/Shadowstep", fileName = "Shadowstep")]
public class ShadowstepEngraving : Engraving
{
    [Tooltip("The Mark it looks for.")]
    public Status mark;
    [Tooltip("Units of movement per star at tier I; each tier takes one unit off, never below 1.")]
    public float unitsPerStar = 4f;
    [Tooltip("Star damage as a share of the hero's Damage stat.")]
    public float damageScale = 0.5f;
    public float speed = 16f;
    public float hitRadius = 0.6f;
    [Range(0f, 1f)] public float critChance = 0.1f;

    [System.NonSerialized] private float _walked;

    public override void OnCombatStart(Entity owner, int tier) => _walked = 0f;

    public override void OnMoved(Entity owner, float distance, int tier)
    {
        if (owner == null || owner.isDead || mark == null) return;
        _walked += distance;
        float per = Mathf.Max(1f, unitsPerStar - (Mathf.Max(1, tier) - 1));
        if (_walked < per) return;
        _walked -= per;

        Entity marked = null; float bestD = float.MaxValue;
        foreach (var e in EntityRegistry.All)
        {
            if (e == null || e.isDead || e.isTeam == owner.isTeam || e.Statuses == null || !e.Statuses.Has(mark)) continue;
            float d = (e.transform.position - owner.transform.position).sqrMagnitude;
            if (d < bestD) { bestD = d; marked = e; }
        }
        if (marked == null) return;
        Supplies.ThrowStar(owner, marked,
            AttackRoll.DamageOf(owner, 10f) * damageScale, speed, hitRadius, 540f, critChance);
    }

    public override string DescribeTier(int tier)
        => $"Every {Mathf.Max(1f, unitsPerStar - (Mathf.Max(1, tier) - 1)):0.#} units moved, a shuriken at the nearest Marked enemy.";
}
