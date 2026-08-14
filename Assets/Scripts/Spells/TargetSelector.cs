using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Reusable target selection for abilities — the "Selector" half of the ability grammar's
/// Trigger → Selector → Effect (Docs/AbilityGrammar.md). Kept separate from any one spell so cleave,
/// chain lightning, AoE nukes and multi-shot can all share the same enemy-picking logic.
/// </summary>
public static class TargetSelector
{
    /// <summary>
    /// The <paramref name="count"/> living enemies nearest the caster, closest first. Returns fewer
    /// (or none) when there aren't that many enemies alive. Allocates a fresh list — abilities fire
    /// rarely, so this is not a hot path.
    /// </summary>
    public static List<Entity> ClosestEnemies(Entity caster, int count)
    {
        var enemies = LivingEnemies(caster);
        enemies.Sort((a, b) => SqrDistance(caster, a).CompareTo(SqrDistance(caster, b)));
        if (enemies.Count > count) enemies.RemoveRange(count, enemies.Count - count);
        return enemies;
    }

    /// <summary>Every living enemy of <paramref name="caster"/>, unordered.</summary>
    public static List<Entity> LivingEnemies(Entity caster)
    {
        var result = new List<Entity>();
        if (caster == null) return result;

        var all = EntityRegistry.All;
        for (int i = 0; i < all.Count; i++)
        {
            var e = all[i];
            if (e == null || e == caster || e.isDead) continue;
            if (e.isTeam != caster.isTeam) result.Add(e);
        }
        return result;
    }

    /// <summary>How many enemies are alive — cheap gate for CanCast without building a list.</summary>
    public static int CountLivingEnemies(Entity caster)
    {
        if (caster == null) return 0;
        int n = 0;
        var all = EntityRegistry.All;
        for (int i = 0; i < all.Count; i++)
        {
            var e = all[i];
            if (e == null || e == caster || e.isDead) continue;
            if (e.isTeam != caster.isTeam) n++;
        }
        return n;
    }

    private static float SqrDistance(Entity a, Entity b)
        => ((Vector2)a.transform.position - (Vector2)b.transform.position).sqrMagnitude;
}
