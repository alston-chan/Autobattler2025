using UnityEngine;

/// <summary>How a unit decides whom to fight.</summary>
public enum TargetMode
{
    /// <summary>Whoever is closest. What every unit did before there was a choice.</summary>
    Nearest = 0,

    /// <summary>Whoever is nearest to death, wherever they are. A finisher.</summary>
    LowestHealth = 1,

    /// <summary>Whoever is furthest away — reaching past the front rank at the line behind it.</summary>
    Furthest = 2,
}

/// <summary>
/// Choosing whom to fight, which used to be one hardcoded line inside the movement loop: the
/// closest living enemy, recomputed from scratch every tick.
///
/// Two things were wrong with that. A unit standing between two enemies at almost equal distance
/// flickered between them as they shuffled, so it never closed on either — the players sees
/// dithering and cannot tell why. And "closest" is the only question anyone could ask, which rules
/// out assassins, taunts, focus fire, and anything that reaches for the back line.
///
/// Scores are all "lower is better" and all positive, so the stickiness margin below can be a
/// single relative number that means the same thing in every mode.
/// </summary>
public static class Targeting
{
    /// <summary>
    /// How much closer a unit in the same lane counts, in world units. Lanes are a preference, not
    /// a leash: at the bell, when everyone stands in their cells, one cell's worth is enough to make
    /// the lane's first unit the target every time — so the setup screen can draw the opening as a
    /// threat line and be right — while mid-fight a clearly closer enemy still wins, so nobody ever
    /// marches past the unit that is hitting them. Zero is plain Nearest.
    /// </summary>
    public static float LaneBonus = 1.9f;

    /// <summary>
    /// Pick a target, preferring to keep the one already being fought.
    ///
    /// <paramref name="stickiness"/> is how much better a rival must be before the unit turns away:
    /// 0.25 means a quarter better. Without it, two enemies a hair apart trade the unit back and
    /// forth every frame and it closes on neither.
    /// </summary>
    public static Entity Choose(Entity chooser, TargetMode mode, Entity current, float stickiness)
    {
        if (chooser == null) return null;

        Entity best = null;
        float bestScore = float.MaxValue;
        bool anyTargetable = false;

        var all = EntityRegistry.All;
        for (int i = 0; i < all.Count; i++)
        {
            var candidate = all[i];
            if (!IsEnemyOf(chooser, candidate)) continue;

            anyTargetable = true;

            float score = Score(chooser, candidate, mode);
            if (score >= bestScore) continue;

            bestScore = score;
            best = candidate;
        }

        // Nobody left to fight, or everyone worth fighting has slipped out of sight.
        if (!anyTargetable) return FallbackWhenAllHidden(chooser, mode);

        // A target that has vanished from view is dropped at once — that is the whole point of
        // dropping aggro, and honouring stickiness here would leave the assassin still being chased.
        if (current == null || !IsEnemyOf(chooser, current)) return best;

        float currentScore = Score(chooser, current, mode);
        return BeatsIncumbent(bestScore, currentScore, stickiness) ? best : current;
    }

    /// <summary>
    /// Pick a target for a single ability, ignoring both what the unit is currently fighting and
    /// how sticky it is. An assassin's dive answers its own question, not the AI's.
    /// </summary>
    public static Entity Pick(Entity chooser, TargetMode mode) => Choose(chooser, mode, null, 0f);

    /// <summary>Whether one unit may fight another at all right now.</summary>
    public static bool IsEnemyOf(Entity chooser, Entity candidate)
    {
        if (chooser == null || candidate == null) return false;
        if (candidate == chooser || candidate.isDead) return false;
        if (!candidate.gameObject.activeInHierarchy) return false;
        if (candidate.isTeam == chooser.isTeam) return false;

        return !candidate.IsAggroDropped;
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
            if (score >= bestScore) continue;

            bestScore = score;
            best = candidate;
        }

        return best;
    }

    private static float Score(Entity chooser, Entity candidate, TargetMode mode) =>
        ScoreFor(mode,
                 Vector3.Distance(chooser.transform.position, candidate.transform.position),
                 HealthFraction(candidate),
                 chooser.OpeningPending && chooser.DeployedLane >= 0 && chooser.DeployedLane == candidate.DeployedLane);

    public static float ScoreFor(TargetMode mode, float distance, float healthFraction) =>
        ScoreFor(mode, distance, healthFraction, sameLane: false);

    /// <summary>
    /// Rank a candidate: lower is better, and always positive, so one relative margin fits every
    /// mode. Kept free of Entity so the ranking can be tested without a battlefield. Only Nearest
    /// honours the lane: the other modes are a deliberate choice of whom to fight, and a lane
    /// preference would second-guess it.
    /// </summary>
    public static float ScoreFor(TargetMode mode, float distance, float healthFraction, bool sameLane)
    {
        switch (mode)
        {
            case TargetMode.LowestHealth:
                return Mathf.Clamp01(healthFraction);

            case TargetMode.Furthest:
                // Inverted so that further away scores lower, and never divides by zero.
                return 1f / (1f + Mathf.Max(0f, distance));

            default:
                return Mathf.Max(0f, distance - (sameLane ? LaneBonus : 0f));
        }
    }

    /// <summary>
    /// Whether a rival is enough better to be worth turning away from the current target.
    ///
    /// The margin is relative so it means the same in every mode: 0.25 is "a quarter better",
    /// whether better is measured in metres or in fractions of a health bar.
    /// </summary>
    public static bool BeatsIncumbent(float bestScore, float incumbentScore, float stickiness) =>
        bestScore < incumbentScore * (1f - Mathf.Clamp01(stickiness));

    private static float HealthFraction(Entity entity)
    {
        var health = entity.Health;
        if (health == null || health.maxHealth <= 0f) return 1f;

        return Mathf.Clamp01(health.currentHealth / health.maxHealth);
    }
}
