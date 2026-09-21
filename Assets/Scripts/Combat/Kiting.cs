/// <summary>
/// When backing away has stopped being a plan and become a twitch.
///
/// A kiter retreats at <c>kiteSpeed</c>, which is a fraction of its move speed, so a chaser at full
/// speed never falls behind. Measured at 10 Hz: a wand user with six units of reach spent ten
/// seconds walking backwards across the whole arena with its chaser a steady 1.5 away, and then,
/// with the wall behind it, shuffled back and forth over a third of a unit about once a second for
/// the rest of the fight — the pacing a player sees and reads as broken pathing.
///
/// Nothing in the kite rule could end that, because every frame asked the same question and got the
/// same answer. This is the question it was missing, kept out here where it can be tested: has the
/// running actually opened the gap? The leash asks the mirror of it on the chaser's side
/// (<see cref="Targeting.LeashBroke"/>) — a unit that cannot reach its target lets go; a unit that
/// cannot escape its chaser turns and fights.
/// </summary>
public static class Kiting
{
    /// <summary>How long a unit backs away before the distance has to show for it.</summary>
    public const float GiveUpSeconds = 2.5f;

    /// <summary>How much further away the chaser must be by then for the retreat to count as working.</summary>
    public const float Progress = 0.6f;

    /// <summary>
    /// Whether retreating is still opening the gap. True while it is too early to tell, and true
    /// once the chaser really has been left behind.
    /// </summary>
    public static bool Escaping(float secondsRetreating, float distanceAtStart, float distanceNow) =>
        secondsRetreating <= GiveUpSeconds || distanceNow >= distanceAtStart + Progress;
}
