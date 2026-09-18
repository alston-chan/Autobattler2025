/// <summary>
/// How a unit uses the space between it and the enemy (Docs/Combat.md, "Stances"). Four words that
/// each say what the unit will do with the room it has. Nobody picks one on the card: Auto is what
/// every hero has, and an item with a <see cref="TacticsEngraving"/> is what changes it.
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

/// <summary>The words the card and the item descriptions use for a unit's tactics.</summary>
public static class Tactics
{
    public static string Word(Stance stance) => stance switch
    {
        Stance.Hold => "Holds",
        Stance.Kite => "Kites",
        Stance.Dive => "Dives",
        _ => "Advances",
    };

    public static string Word(TargetMode mode) => mode switch
    {
        TargetMode.LowestHealth => "the weakest",
        TargetMode.Furthest => "the farthest",
        TargetMode.Attacker => "its attacker",
        _ => "the nearest",
    };

    public static string Word(Commitment commitment) => commitment switch
    {
        Commitment.Opportunistic => "opportunist",
        Commitment.Relentless => "never lets go",
        _ => "balanced",
    };

    /// <summary>One line for a unit: "Holds · the nearest · balanced".</summary>
    public static string Line(Entity unit) =>
        Word(unit.EffectiveStance) + " · " + Word(unit.EffectiveTarget) + " · " + Word(unit.EffectiveCommitment);
}
