/// <summary>
/// How a unit uses the space between it and the enemy (Docs/Combat.md, "Stances"). Three words that
/// each say what the unit will do with the room it has.
///
/// The numbers are serialized (an item's TacticsEngraving stores its stance as one), so a member is
/// never renumbered or its number reused. 2 was Hold — stand still until hurt or four seconds had
/// passed — removed 2026-09-22: what a player saw was a unit standing idle, which read as a bug and
/// was reported as one, and what it bought (the fight a step nearer your side) could not be seen. Nobody picks one on the card: Auto is what
/// every hero has, and an item with a <see cref="TacticsEngraving"/> is what changes it.
/// </summary>
public enum Stance
{
    /// <summary>Ranged units Kite, everyone else Advances.</summary>
    Auto = 0,
    /// <summary>Walk at the target and fight it where it stands. What every unit did before there was a choice.</summary>
    Advance = 1,
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

    /// <summary>One line for a unit: "Advances · the nearest · balanced". A diver goes for the farthest, as the AI has it.</summary>
    public static string Line(Entity unit)
    {
        var stance = unit.EffectiveStance;
        var target = stance == Stance.Dive ? TargetMode.Furthest : unit.EffectiveTarget;
        return Word(stance) + " · " + Word(target) + " · " + Word(unit.EffectiveCommitment);
    }
}
