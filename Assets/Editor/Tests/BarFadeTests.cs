using NUnit.Framework;

/// <summary>
/// A bar's opacity. The fade down is the easy half; the case that matters is the way back, which
/// did not exist — a bar still attached to a unit that lived again kept the alpha it had faded to,
/// and a finished fade is invisible.
/// </summary>
public class BarFadeTests
{
    [Test]
    public void ABarFadesWhileItsOwnerIsDead()
    {
        float fade = BarFade.Step(ownerIsDead: true, fadeOnDeath: true, current: 1f, deltaTime: 0.25f, duration: 1f);
        Assert.That(fade, Is.EqualTo(0.75f).Within(0.001f));
    }

    [Test]
    public void ItStopsAtInvisibleRatherThanGoingPastIt()
    {
        Assert.That(BarFade.Step(true, true, 0.1f, 1f, 1f), Is.EqualTo(0f));
        Assert.That(BarFade.Step(true, true, 0f, 1f, 1f), Is.EqualTo(0f));
    }

    [Test]
    public void ALivingOwnerMeansASolidBar()
    {
        // The bug, stated: whatever the bar faded to, being alive puts it back.
        Assert.That(BarFade.Step(ownerIsDead: false, fadeOnDeath: true, current: 0f, deltaTime: 0.016f, duration: 1f),
                    Is.EqualTo(BarFade.Solid), "a revived unit's bar stayed invisible");
        Assert.That(BarFade.Step(false, true, 0.4f, 0.016f, 1f), Is.EqualTo(BarFade.Solid));
    }

    [Test]
    public void ADeadOwnerKeepsItsBarWhenTheFadeIsTurnedOff()
    {
        Assert.That(BarFade.Step(true, fadeOnDeath: false, current: 1f, deltaTime: 1f, duration: 1f), Is.EqualTo(1f));
    }

    [Test]
    public void AZeroDurationDoesNotDivideByZero()
    {
        Assert.That(BarFade.Step(true, true, 1f, 0.016f, 0f), Is.EqualTo(0f));
    }
}
