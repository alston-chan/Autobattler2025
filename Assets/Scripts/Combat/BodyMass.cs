using UnityEngine;

/// <summary>
/// How hard a unit is to move, as one number.
///
/// This game is about throwing people, and until now every body flew exactly as far as every other:
/// a wand mage and a shield knight both travelled force/damping units. Mass is what makes a throw
/// read differently per target — Cannonball into the knight is a thud, into the Rain Archer it is a
/// kill; a Singularity drags the light ones out of formation and leaves the anchor standing.
///
/// It comes from what a unit wears, through the same pipeline every other stat uses: an item's
/// <c>Weight</c>, summed onto <see cref="EntityStats.Mass"/>. So an enemy in a kit and a hero in the
/// workshop get it by the same route, and taking the armour off makes the hero throwable again.
///
/// The spread is deliberately narrow. Mass divides force, so a wide one would quietly delete the
/// physics layer from fights between armoured teams — which is the game's own identity. The median
/// unit sits at 1, so every force number already tuned stays tuned, and the whole effect is
/// redistribution rather than reduction.
/// </summary>
public static class BodyMass
{
    /// <summary>What one point of an item's authored Weight is worth.</summary>
    public const float PerWeightPoint = 0.01f;

    /// <summary>The mass a unit ends up at: its bare body plus what it wears, held inside the spread.</summary>
    public static float From(float bareBody, float wornWeight, float min, float max) =>
        Mathf.Clamp(bareBody + wornWeight * PerWeightPoint, min, max);

    /// <summary>
    /// The word for a card. A number between 0.8 and 1.35 tells a player nothing; "Heavy" tells them
    /// why the knight did not move.
    /// </summary>
    public static string Word(float mass) =>
        mass < 0.95f ? "Light" : mass < 1.15f ? "Medium" : "Heavy";

    /// <summary>
    /// How much of a collision's speed the struck body takes on, given who is heavier. A thrown
    /// anchor bowls a mage over; a thrown mage bounces off the anchor. Clamped, because an unbounded
    /// ratio turns one unlucky matchup into a catapult.
    /// </summary>
    public static float TransferRatio(float moverMass, float struckMass) =>
        Mathf.Clamp(moverMass / Mathf.Max(0.01f, struckMass), 0.5f, 2f);
}
