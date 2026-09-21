using NUnit.Framework;

/// <summary>
/// When a retreat has failed. Measured from a real fight at 10 Hz: a kiter retreats at a fraction
/// of its move speed, so a chaser at full speed holds the gap at about 1.5 units the whole way
/// across the arena, and then the wall turns the retreat into a shuffle on the spot. The rule below
/// is what ends it, so these are the cases it has to get right.
/// </summary>
public class KitingTests
{
    [Test]
    public void ARetreatIsGivenTimeBeforeItIsJudged()
    {
        // A unit that has only just started backing away has not failed at anything yet, even
        // though the chaser is still exactly as close.
        Assert.That(Kiting.Escaping(0f, 1.5f, 1.5f), Is.True);
        Assert.That(Kiting.Escaping(Kiting.GiveUpSeconds, 1.5f, 1.4f), Is.True);
    }

    [Test]
    public void AChaserThatKeepsPaceEndsTheRetreat()
    {
        // The measured case: seconds of running, and the gap is where it started.
        Assert.That(Kiting.Escaping(Kiting.GiveUpSeconds + 0.1f, 1.5f, 1.5f), Is.False);

        // Losing ground is worse still.
        Assert.That(Kiting.Escaping(6f, 2f, 1.1f), Is.False);
    }

    [Test]
    public void ARetreatThatIsWorkingIsLeftAlone()
    {
        // Just past the margin, not exactly on it: the boundary is a float comparison and testing
        // it for equality tests the arithmetic, not the rule.
        Assert.That(Kiting.Escaping(10f, 1.5f, 1.5f + Kiting.Progress + 0.01f), Is.True);
        Assert.That(Kiting.Escaping(30f, 2f, 9f), Is.True, "an archer that got clear stays clear");
    }

    [Test]
    public void TheMarginIsWorthMoreThanTheJostlingOfBodies()
    {
        // Bodies in a scrum shove each other a tenth of a unit at a time; that must not read as
        // escaping, or the rule never fires where it is needed most.
        Assert.That(Kiting.Progress, Is.GreaterThan(0.3f));
        Assert.That(Kiting.Escaping(4f, 1.5f, 1.7f), Is.False);
    }
}
