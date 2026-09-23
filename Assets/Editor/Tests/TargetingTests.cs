using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

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

    // ---- the leash


    [Test]
    public void ATargetIsKeptUntilItDiesAndATauntOverridesIt()
    {
        var made = new List<GameObject>();
        Entity Unit(bool team, float x)
        {
            var go = new GameObject("u"); made.Add(go);
            var e = go.AddComponent<Entity>(); e.isTeam = team; go.transform.position = new Vector3(x, 0f, 0f);
            if (!EntityRegistry.All.Contains(e)) EntityRegistry.Register(e);   // edit mode runs no OnEnable
            return e;
        }
        try
        {
            var chooser = Unit(true, 0f);
            var far = Unit(false, 6f);
            // Nothing closer turns it: "far" was chosen, and a nearer enemy is not a reason to leave.
            var near = Unit(false, 1f);
            Assert.That(Targeting.Choose(chooser, TargetMode.Nearest, far), Is.SameAs(far), "kept until it dies");
            Assert.That(Targeting.Choose(chooser, TargetMode.Nearest, null), Is.SameAs(near), "a fresh pick is the nearest");
            far.gameObject.SetActive(false);   // gone from the fight, as the dead are
            Assert.That(Targeting.Choose(chooser, TargetMode.Nearest, far), Is.SameAs(near), "and when it dies, the next pick");
        }
        finally { foreach (var go in made) { var e = go.GetComponent<Entity>(); if (e != null) EntityRegistry.Unregister(e); Object.DestroyImmediate(go); } }
    }
}
