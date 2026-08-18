using UnityEngine;

/// <summary>
/// Deciding which unit the player just clicked. Shared so that inspecting a unit
/// (<see cref="UnitInspector"/>) and grabbing one to rearrange the formation
/// (<see cref="FormationDragger"/>) agree — two answers to "which unit is under the cursor" would
/// eventually disagree, and the player would meet a unit that can be dragged but not inspected.
///
/// Units carry a body collider already, because arrows hit through it, and it is authored to the
/// shape of the unit: 0.63 wide on a human, against the 1.1 radius this replaced. That narrowness is
/// the point — a click on a body can't spill onto the neighbour a cell over.
///
/// It is an arrow hitbox rather than a click target, though, so it needs help at both ends: it stops
/// at the shoulders (y 1.35 above the feet, where the sprite runs to about 2) and these characters
/// are drawn with big heads, so the box is extended upward; and a unit with no collider at all falls
/// back to a radius so an unusual prefab is never unclickable.
/// </summary>
public static class UnitPicking
{
    /// <summary>
    /// How far above the body collider a click still lands on the unit, in world units. Covers the
    /// head, which the collider excludes and which is a large part of what the player aims at.
    /// </summary>
    public const float DefaultHeadroom = 0.7f;

    /// <summary>Fallback pick distance for a unit with no usable body collider, in world units.</summary>
    public const float DefaultRadius = 1.1f;

    /// <summary>
    /// Whether a world point lands on this unit — inside its body collider, or in the headroom above
    /// it.
    ///
    /// Says nothing about whether the unit is a legal choice: liveness, team and whether it is even
    /// on the board are the caller's business. That matters more than it looks, because a dead unit
    /// has its collider disabled (see <see cref="DeathFeedback"/>) and so would be measured by the
    /// fallback radius instead — a caller that doesn't exclude the dead would find corpses easier to
    /// click than the living.
    /// </summary>
    public static bool Covers(Entity unit, Vector3 world, float headroom = DefaultHeadroom,
                              float fallbackRadius = DefaultRadius)
    {
        if (unit == null) return false;

        var body = unit.GetComponentInChildren<Collider2D>();
        if (body == null || !body.enabled)
            return Vector2.Distance(world, unit.transform.position + Vector3.up) <= fallbackRadius;

        var bounds = body.bounds;
        return world.x >= bounds.min.x && world.x <= bounds.max.x &&
               world.y >= bounds.min.y && world.y <= bounds.max.y + headroom;
    }

    /// <summary>
    /// Whether <paramref name="candidate"/> is drawn in front of <paramref name="current"/>, treating
    /// a null <paramref name="current"/> as "nothing chosen yet".
    ///
    /// Units in a column overlap, so a point can land on several at once — and the one the player
    /// believes they clicked is simply the one they can see. Extending the box over the head makes
    /// this decisive rather than a nicety: units stand a little over a unit apart, so the torso of
    /// the unit BEHIND sits at the same height as the head of the unit in front, and picking by
    /// collider alone would answer with the unit hidden behind the head being clicked.
    ///
    /// The project sorts sprites along +Y (GraphicsSettings custom axis), which draws lower units in
    /// front, so front-most is the unit with the smallest Y.
    /// </summary>
    public static bool IsInFrontOf(Entity candidate, Entity current) =>
        current == null || candidate.transform.position.y < current.transform.position.y;
}
