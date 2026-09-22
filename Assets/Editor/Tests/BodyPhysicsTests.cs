using NUnit.Framework;
using UnityEngine;

/// <summary>The arithmetic of bodies (<see cref="BodyMath"/>), checked without a scene.</summary>
public class BodyPhysicsTests
{
    [Test]
    public void TwoTouchingBodiesDoNotOverlap()
    {
        bool hit = BodyMath.Overlap(Vector3.zero, 0.5f, new Vector3(1f, 0f, 0f), 0.5f, out _, out float overlap);
        Assert.IsFalse(hit);
        Assert.LessOrEqual(overlap, 0f);
    }

    [Test]
    public void OverlapReportsDepthAndNormalFromAToB()
    {
        bool hit = BodyMath.Overlap(Vector3.zero, 0.5f, new Vector3(0f, 0.6f, 0f), 0.5f, out Vector3 n, out float overlap);
        Assert.IsTrue(hit);
        Assert.AreEqual(0.4f, overlap, 1e-4f);
        Assert.AreEqual(1f, n.y, 1e-4f);
    }

    [Test]
    public void OverlapIgnoresDepth()
    {
        bool hit = BodyMath.Overlap(new Vector3(0f, 0f, 5f), 0.5f, new Vector3(1.5f, 0f, -5f), 0.5f, out _, out _);
        Assert.IsFalse(hit, "z is not a distance on a 2D board");
    }

    [Test]
    public void SeparateSplitsThePushBetweenFreeBodies()
    {
        Vector3 pa = Vector3.zero, pb = new Vector3(0.6f, 0f, 0f);
        BodyMath.Separate(ref pa, ref pb, Vector3.right, 0.4f, false, false, 1f);
        Assert.AreEqual(-0.2f, pa.x, 1e-4f);
        Assert.AreEqual(0.8f, pb.x, 1e-4f);
    }

    [Test]
    public void SeparateLeavesAFixedBodyWhereItStands()
    {
        Vector3 pa = Vector3.zero, pb = new Vector3(0.6f, 0f, 0f);
        BodyMath.Separate(ref pa, ref pb, Vector3.right, 0.4f, true, false, 1f);
        Assert.AreEqual(0f, pa.x, 1e-4f, "the pillar did not move");
        Assert.AreEqual(1.0f, pb.x, 1e-4f, "the other body took the whole push");
    }

    [Test]
    public void SeparateAtPartialStrengthClosesOnlyThatFractionOfTheOverlap()
    {
        Vector3 pa = Vector3.zero, pb = new Vector3(0.6f, 0f, 0f);
        BodyMath.Separate(ref pa, ref pb, Vector3.right, 0.4f, false, false, 0.5f);
        Assert.AreEqual(0.8f, pb.x - pa.x, 1e-4f, "allies squeeze past over a few frames");
    }

    [Test]
    public void ASlamIsFlatNothingUnderTheThresholdAndCappedAfterTheMultiplier()
    {
        Assert.AreEqual(0f, BodyMath.SlamPercent(2f, 3f, 0.08f, 1f, 0.25f), "a shove");
        Assert.AreEqual(0.08f, BodyMath.SlamPercent(4f, 3f, 0.08f, 1f, 0.25f), 1e-5f, "just over: the full number");
        Assert.AreEqual(0.08f, BodyMath.SlamPercent(40f, 3f, 0.08f, 1f, 0.25f), 1e-5f, "far over: the same number — flat, not by speed");
        Assert.AreEqual(0.16f, BodyMath.SlamPercent(8f, 3f, 0.08f, 2f, 0.25f), 1e-5f, "a Cannonball doubles it");
        Assert.AreEqual(0.25f, BodyMath.SlamPercent(8f, 3f, 0.08f, 9f, 0.25f), 1e-5f, "and the cap holds");
    }

    [Test]
    public void ClosingSpeedIsAlongTheNormal()
    {
        Assert.AreEqual(8f, BodyMath.ClosingSpeed(new Vector3(8f, 3f, 0f), Vector3.zero, Vector3.right), 1e-5f);
        Assert.AreEqual(-2f, BodyMath.ClosingSpeed(Vector3.zero, new Vector3(2f, 0f, 0f), Vector3.right), 1e-5f, "parting is negative");
    }

    [Test]
    public void ExchangePassesMomentumOnSoTheStruckBodyFliesAndTheMoverSlows()
    {
        Vector3 mover = new Vector3(10f, 0f, 0f), struck = Vector3.zero;
        BodyMath.Exchange(ref mover, ref struck, Vector3.right, 10f, 0.6f, 0f, false);
        Assert.AreEqual(6f, struck.x, 1e-5f, "the struck body inherits the transfer");
        Assert.AreEqual(4f, mover.x, 1e-5f, "the mover keeps the rest");
        Assert.Less(mover.x, struck.x, "they part after the hit rather than hitting again");
    }

    [Test]
    public void ExchangeAgainstAPillarStopsTheMover()
    {
        Vector3 mover = new Vector3(10f, 0f, 0f), struck = Vector3.zero;
        BodyMath.Exchange(ref mover, ref struck, Vector3.right, 10f, 0.6f, 0f, true);
        Assert.AreEqual(0f, mover.x, 1e-5f);
        Assert.AreEqual(0f, struck.x, 1e-5f, "a pillar does not move");
    }

    [Test]
    public void AChargingBodyKeepsMostOfItsSpeedThroughAHit()
    {
        Vector3 mover = new Vector3(10f, 0f, 0f), struck = Vector3.zero;
        BodyMath.Exchange(ref mover, ref struck, Vector3.right, 10f, 0.6f, 0.7f, false);
        Assert.AreEqual(6f, struck.x, 1e-5f, "the struck body is thrown just as far");
        Assert.AreEqual(8.2f, mover.x, 1e-5f, "the charger loses only 30% of what it gave");
    }

    [Test]
    public void SlideKeepsOnlyTheMotionAlongTheWall()
    {
        var v = BodyMath.Slide(new Vector3(-5f, 2f, 0f), Vector3.right);
        Assert.AreEqual(0f, v.x, 1e-5f);
        Assert.AreEqual(2f, v.y, 1e-5f);
        var away = BodyMath.Slide(new Vector3(5f, 2f, 0f), Vector3.right);
        Assert.AreEqual(5f, away.x, 1e-5f, "a body leaving the wall is untouched");
    }

    [Test]
    public void BounceReflectsAFractionOfTheSpeedIntoTheWall()
    {
        var v = BodyMath.Bounce(new Vector3(-8f, 1f, 0f), Vector3.right, 0.25f);
        Assert.AreEqual(2f, v.x, 1e-5f, "8 in, a quarter back out");
        Assert.AreEqual(1f, v.y, 1e-5f, "the slide is kept");
    }
}
