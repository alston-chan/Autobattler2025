/// <summary>
/// How a unit uses the space between it and the enemy. The one movement decision the player makes
/// per hero, on the card before the bell (Docs/Combat.md, "Stances"). It is a dropdown, not a
/// script: four words that each say what the unit will do with the room it has.
/// </summary>
public enum Stance
{
    /// <summary>Ranged units Kite, everyone else Advances.</summary>
    Auto = 0,
    /// <summary>Walk at the target and fight it where it stands. What every unit did before there was a choice.</summary>
    Advance = 1,
    /// <summary>Stand your ground: fight what comes within reach, and only advance once you are hurt or the wait runs out.</summary>
    Hold = 2,
    /// <summary>Keep your distance: back away from anything that closes, and shoot between steps.</summary>
    Kite = 3,
    /// <summary>Go for the back line: pick the farthest enemy and run at it.</summary>
    Dive = 4,
}
