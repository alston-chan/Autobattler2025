using NUnit.Framework;

/// <summary>
/// How a unit ranks the enemies in front of it, and when it changes its mind.
///
/// Tested through the two pure decisions rather than by staging a fight: ranking a candidate, and
/// deciding whether a rival is worth turning to. Everything else in <see cref="Targeting"/> is
/// walking the registry, which needs live units and is verified in play.
/// </summary>
public class TargetingTests
{
    // Lower is better in every mode, so one relative margin can mean the same thing in all of them.

    [Test]
    public void NearestPrefersTheCloserEnemy()
    {
        float near = Targeting.ScoreFor(TargetMode.Nearest, distance: 2f, healthFraction: 1f);
        float far = Targeting.ScoreFor(TargetMode.Nearest, distance: 9f, healthFraction: 0.1f);

        Assert.That(near, Is.LessThan(far), "closer must rank better, whatever their health");
    }

    [Test]
    public void LowestHealthIgnoresDistanceEntirely()
    {
        float woundedButFar = Targeting.ScoreFor(TargetMode.LowestHealth, distance: 40f, healthFraction: 0.05f);
        float healthyAndNear = Targeting.ScoreFor(TargetMode.LowestHealth, distance: 1f, healthFraction: 0.95f);

        // The assassin's whole premise: cross the field for the one worth finishing.
        Assert.That(woundedButFar, Is.LessThan(healthyAndNear));
    }

    [Test]
    public void FurthestPrefersTheDistantEnemy()
    {
        float distant = Targeting.ScoreFor(TargetMode.Furthest, distance: 12f, healthFraction: 1f);
        float adjacent = Targeting.ScoreFor(TargetMode.Furthest, distance: 1f, healthFraction: 1f);

        Assert.That(distant, Is.LessThan(adjacent), "reaching past the front rank means further ranks better");
    }

    [Test]
    public void ScoresAreNeverNegative()
    {
        // The relative stickiness margin only behaves if every score is positive.
        foreach (TargetMode mode in System.Enum.GetValues(typeof(TargetMode)))
        {
            Assert.That(Targeting.ScoreFor(mode, 0f, 0f), Is.GreaterThanOrEqualTo(0f), mode.ToString());
            Assert.That(Targeting.ScoreFor(mode, 100f, 1f), Is.GreaterThanOrEqualTo(0f), mode.ToString());
        }
    }

    [Test]
    public void AMarginallyBetterRivalDoesNotStealTheTarget()
    {
        // Two enemies a hair apart used to trade the unit back and forth every frame, so it closed
        // on neither and the player saw dithering.
        Assert.That(Targeting.BeatsIncumbent(bestScore: 4.9f, incumbentScore: 5f, stickiness: 0.25f),
                    Is.False, "a 2% improvement must not be worth turning around for");
    }

    [Test]
    public void AClearlyBetterRivalDoesStealTheTarget()
    {
        Assert.That(Targeting.BeatsIncumbent(bestScore: 2f, incumbentScore: 5f, stickiness: 0.25f),
                    Is.True, "a target 60% better is worth turning to");
    }

    [Test]
    public void ALaneMateCountsCloserByTheBonus()
    {
        float plain = Targeting.ScoreFor(TargetMode.Nearest, 5f, 1f, sameLane: false);
        float lane = Targeting.ScoreFor(TargetMode.Nearest, 5f, 1f, sameLane: true);
        Assert.That(plain - lane, Is.EqualTo(Targeting.LaneBonus).Within(0.0001f));
    }

    [Test]
    public void TheLaneBonusNeverMakesAScoreNegative()
    {
        Assert.That(Targeting.ScoreFor(TargetMode.Nearest, 0.5f, 1f, sameLane: true), Is.GreaterThanOrEqualTo(0f));
    }

    [Test]
    public void OnlyNearestHonoursTheLane()
    {
        // The other modes are a deliberate choice of whom to fight; a lane preference would second-guess it.
        Assert.That(Targeting.ScoreFor(TargetMode.LowestHealth, 5f, 0.4f, true),
                    Is.EqualTo(Targeting.ScoreFor(TargetMode.LowestHealth, 5f, 0.4f, false)));
        Assert.That(Targeting.ScoreFor(TargetMode.Furthest, 5f, 1f, true),
                    Is.EqualTo(Targeting.ScoreFor(TargetMode.Furthest, 5f, 1f, false)));
    }

    [Test]
    public void ZeroStickinessTakesAnyImprovement()
    {
        Assert.That(Targeting.BeatsIncumbent(4.99f, 5f, 0f), Is.True,
                    "an ability that picks its own target asks with no stickiness at all");
    }

    // ---- the leash

    [Test]
    public void TheLeashBreaksOnlyOutOfReachAndOnlyAfterItsTime()
    {
        float leash = Targeting.LeashSeconds;
        // Within reach, no amount of waiting breaks it: the unit is fighting, not chasing.
        Assert.That(Targeting.LeashBroke(leash * 10f, inReach: true), Is.False);
        // Out of reach but still making progress recently: not yet.
        Assert.That(Targeting.LeashBroke(leash * 0.5f, inReach: false), Is.False);
        // Out of reach and no progress for longer than the leash: it breaks.
        Assert.That(Targeting.LeashBroke(leash + 0.01f, inReach: false), Is.True);
    }
}
