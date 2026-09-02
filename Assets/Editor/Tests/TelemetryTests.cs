using NUnit.Framework;

/// <summary>
/// The arithmetic behind the fight scoreboard: a fight's own numbers are the run's rows less where
/// they stood when the fight began, and a bar is its value against the largest on the board.
/// </summary>
public class TelemetryTests
{
    [Test]
    public void AFightIsTheDifferenceSinceItBegan()
    {
        var before = new CombatTelemetry.Row { DamageDealt = 100f, DamageTaken = 40f, Blocked = 6f, Kills = 2, Ults = 1, Hits = 10 };
        var now = new CombatTelemetry.Row { DamageDealt = 250f, DamageTaken = 90f, Blocked = 18f, Kills = 3, Ults = 3, Hits = 25 };

        var fight = now.Since(before);

        Assert.That(fight.DamageDealt, Is.EqualTo(150f));
        Assert.That(fight.DamageTaken, Is.EqualTo(50f));
        Assert.That(fight.Blocked, Is.EqualTo(12f));
        Assert.That(fight.Kills, Is.EqualTo(1));
        Assert.That(fight.Ults, Is.EqualTo(2));
        Assert.That(fight.Hits, Is.EqualTo(15));
        Assert.That(fight.Fights, Is.EqualTo(1), "a fight's own row is one fight");
    }

    [Test]
    public void AUnitFirstSeenThisFightCountsAllOfIt()
    {
        var now = new CombatTelemetry.Row { DamageDealt = 75f, Kills = 1 };
        var fight = now.Since(null);
        Assert.That(fight.DamageDealt, Is.EqualTo(75f));
        Assert.That(fight.Kills, Is.EqualTo(1));
    }

    [Test]
    public void CopyingARowDoesNotShareIt()
    {
        var row = new CombatTelemetry.Row { DamageDealt = 10f };
        var copy = row.Copy();
        row.DamageDealt = 99f;
        Assert.That(copy.DamageDealt, Is.EqualTo(10f));
    }

    [Test]
    public void ABarIsItsShareOfTheLargest()
    {
        Assert.That(FightScoreboard.Fraction(50f, 200f), Is.EqualTo(0.25f).Within(0.0001f));
        Assert.That(FightScoreboard.Fraction(200f, 200f), Is.EqualTo(1f).Within(0.0001f));
        Assert.That(FightScoreboard.Fraction(0f, 0f), Is.EqualTo(0f), "a board of zeros is all empty, not all full");
        Assert.That(FightScoreboard.Fraction(300f, 200f), Is.EqualTo(1f), "never past the end of the trough");
    }

    [Test]
    public void EachStatReadsItsOwnColumn()
    {
        var row = new CombatTelemetry.Row { DamageDealt = 1f, DamageTaken = 2f, Blocked = 3f, Kills = 4, Ults = 5 };
        Assert.That(FightScoreboard.ValueOf(row, FightScoreboard.Stat.Dealt), Is.EqualTo(1f));
        Assert.That(FightScoreboard.ValueOf(row, FightScoreboard.Stat.Taken), Is.EqualTo(2f));
        Assert.That(FightScoreboard.ValueOf(row, FightScoreboard.Stat.Blocked), Is.EqualTo(3f));
        Assert.That(FightScoreboard.ValueOf(row, FightScoreboard.Stat.Kills), Is.EqualTo(4f));
        Assert.That(FightScoreboard.ValueOf(row, FightScoreboard.Stat.Ults), Is.EqualTo(5f));
        Assert.That(FightScoreboard.ValueOf(null, FightScoreboard.Stat.Dealt), Is.EqualTo(0f), "a hero with no row did nothing");
    }
}
