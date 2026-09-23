using UnityEngine;

/// <summary>Whom a unit picks when it picks. The one thing about targeting an item can change.</summary>
public enum TargetMode
{
    /// <summary>Whoever is closest. What every unit does unless its gear says otherwise.</summary>
    Nearest = 0,

    /// <summary>Whoever is nearest to death, wherever they are. A finisher.</summary>
    LowestHealth = 1,

    /// <summary>Whoever is furthest away — past the front rank, at the line behind it. A diver.</summary>
    Furthest = 2,

    /// <summary>Whoever is coming for this unit; else the nearest. The archer's answer to an assassin.</summary>
    Attacker = 3,
}

/// <summary>
/// Choosing whom to fight. The whole rule, in the order it is asked:
///
///   1. Taunted? Fight whoever taunted you.
///   2. Your target is alive and in sight? Keep it — until it dies.
///   3. Otherwise pick by your mode (nearest, weakest, farthest, your attacker), preferring
///      whatever a hunt or an opener item asks for.
///
/// And one exception, asked by <see cref="CombatAI"/> because it needs reach: a unit that picks
/// the NEAREST, whose target has stepped out of reach while another enemy stands inside it, turns
/// on that one. Walking past a fight to reach one that keeps moving read as a unit that would not
/// commit. A unit that picked on purpose — the weakest, the farthest, its attacker — keeps going.
///
/// This replaced, on 2026-09-22, a stance for each unit (advance, kite, dive), a commitment
/// (opportunistic, balanced, relentless), a leash timer per commitment, a stickiness margin that
/// the lock made dead code, a same-lane bonus and a lock-on switch: thirty-six combinations of
/// knobs a player could not see, for behaviour a player reads as "who is it fighting".
/// </summary>
public static class Targeting
{
    /// <summary>How much nearer an enemy that is targeting the chooser counts, for the Attacker mode.</summary>
    public static float AttackerBonus = 20f;

    /// <summary>The target to fight now, given the one already being fought.</summary>
    public static Entity Choose(Entity chooser, TargetMode mode, Entity current)
    {
        if (chooser == null) return null;

        var taunter = chooser.Statuses != null ? chooser.Statuses.TauntedBy : null;
        if (taunter != null && IsEnemyOf(chooser, taunter)) return taunter;

        if (current != null && IsEnemyOf(chooser, current)) return current;

        return Pick(chooser, mode);
    }

    /// <summary>
    /// A fresh pick by <paramref name="mode"/>, ignoring what the unit is fighting now. What an
    /// ability that chooses its own target uses too.
    /// </summary>
    public static Entity Pick(Entity chooser, TargetMode mode)
    {
        if (chooser == null) return null;

        // A hunt or an opener narrows the field to what it wants, when anything qualifies.
        var filter = chooser.OpeningPending && chooser.Opener != null ? chooser.Opener : chooser.Hunt;
        var all = EntityRegistry.All;
        bool anyPass = false;
        if (filter != null)
            for (int i = 0; i < all.Count; i++)
                if (IsEnemyOf(chooser, all[i]) && filter(all[i])) { anyPass = true; break; }

        Entity best = null;
        float bestScore = float.MaxValue;
        for (int i = 0; i < all.Count; i++)
        {
            var candidate = all[i];
            if (!IsEnemyOf(chooser, candidate)) continue;
            if (anyPass && !filter(candidate)) continue;
            float score = Score(chooser, candidate, mode);
            if (score < bestScore) { bestScore = score; best = candidate; }
        }

        return best != null ? best : FallbackWhenAllHidden(chooser, mode);
    }

    /// <summary>Whether one unit may fight another at all right now.</summary>
    public static bool IsEnemyOf(Entity chooser, Entity candidate)
    {
        if (chooser == null || candidate == null) return false;
        if (candidate == chooser || candidate.isDead) return false;
        if (!candidate.gameObject.activeInHierarchy) return false;
        if (candidate.isTeam == chooser.isTeam) return false;

        // Out of sight by a dive, or by a status (Smoke): either way not a pick.
        return !candidate.IsAggroDropped && !(candidate.Statuses != null && candidate.Statuses.Untargetable);
    }

    /// <summary>
    /// When every enemy has dropped aggro at once, someone still has to be fought — otherwise the
    /// whole battle politely stops. Hiding buys a unit time, never immunity.
    /// </summary>
    private static Entity FallbackWhenAllHidden(Entity chooser, TargetMode mode)
    {
        Entity best = null;
        float bestScore = float.MaxValue;
        var all = EntityRegistry.All;
        for (int i = 0; i < all.Count; i++)
        {
            var candidate = all[i];
            if (candidate == null || candidate == chooser || candidate.isDead) continue;
            if (!candidate.gameObject.activeInHierarchy) continue;
            if (candidate.isTeam == chooser.isTeam) continue;
            float score = Score(chooser, candidate, mode);
            if (score < bestScore) { bestScore = score; best = candidate; }
        }
        return best;
    }

    private static float Score(Entity chooser, Entity candidate, TargetMode mode) =>
        ScoreFor(mode,
                 Vector3.Distance(chooser.transform.position, candidate.transform.position),
                 HealthFraction(candidate),
                 mode == TargetMode.Attacker && candidate.CombatAI != null && candidate.CombatAI.CurrentTarget == chooser);

    /// <summary>
    /// Rank a candidate: lower is better, and never negative. Kept free of Entity so the ranking can
    /// be tested without a battlefield, and so the setup screen's arrow (BoardSnapshot.PredictOpening)
    /// ranks exactly as the fight does.
    /// </summary>
    public static float ScoreFor(TargetMode mode, float distance, float healthFraction, bool comingForChooser = false)
    {
        switch (mode)
        {
            case TargetMode.LowestHealth:
                return Mathf.Clamp01(healthFraction);

            case TargetMode.Furthest:
                // Inverted so that further away scores lower, and never divides by zero.
                return 1f / (1f + Mathf.Max(0f, distance));

            case TargetMode.Attacker:
                // Distance, with whoever is coming for the chooser counted as if it stood far closer.
                return Mathf.Max(0f, distance - (comingForChooser ? AttackerBonus : 0f));

            default:
                return Mathf.Max(0f, distance);
        }
    }

    public static float HealthFraction(Entity entity)
    {
        var health = entity.Health;
        if (health == null || health.maxHealth <= 0f) return 1f;
        return Mathf.Clamp01(health.currentHealth / health.maxHealth);
    }
}
