/// <summary>
/// The words the card and the item descriptions use for whom a unit goes for.
///
/// This file held the Stance enum until 2026-09-22 — Advance, Hold, Kite, Dive — and a line that
/// read "Advances · the nearest · balanced". Hold went first (a unit standing idle read as a bug),
/// then Kite (the most complicated movement in the game, and the source of three pacing bugs), and
/// Dive was only "go for the farthest", which is a target mode. Commitment went with them. What is
/// left is the one choice an item makes: whom to pick.
/// </summary>
public static class Tactics
{
    public static string Word(TargetMode mode) => mode switch
    {
        TargetMode.LowestHealth => "the weakest",
        TargetMode.Furthest => "the farthest",
        TargetMode.Attacker => "its attacker",
        _ => "the nearest",
    };

    /// <summary>One line for a unit's card: "Goes for the weakest".</summary>
    public static string Line(Entity unit) => "Goes for " + Word(unit.EffectiveTarget);
}
