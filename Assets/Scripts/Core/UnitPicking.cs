using UnityEngine;

/// <summary>How firmly a world point landed on a unit. Higher beats lower.</summary>
public enum PickHit
{
    /// <summary>Not on the unit at all.</summary>
    None = 0,

    /// <summary>
    /// On the padding around the body — the headroom over the shoulders, or the fallback radius.
    /// Both are guesses at where the sprite is, so they must never outrank someone's actual body.
    /// </summary>
    Extended = 1,

    /// <summary>Inside the unit's body proper. The strongest claim on a click.</summary>
    Core = 2
}

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
/// at the shoulders while the sprite runs higher, so the box is extended upward; and a unit with no
/// collider falls back to a radius so an unusual prefab is never unclickable. Add a
/// <see cref="UnitPickBox"/> to a prefab to state its click area outright and skip all of that.
/// </summary>
public static class UnitPicking
{
    /// <summary>
    /// How far above the body collider a click still lands on the unit, in world units. Covers the
    /// head, which the collider excludes and which is a large part of what the player aims at.
    ///
    /// Kept modest on purpose. Units stand 1.5 apart in a column, and their bodies are about 1.5
    /// tall, so headroom much beyond this reaches into the body of the unit standing behind.
    /// </summary>
    public const float DefaultHeadroom = 0.7f;

    /// <summary>Fallback pick distance for a unit with no usable body collider, in world units.</summary>
    public const float DefaultRadius = 1.1f;

    /// <summary>
    /// How firmly a world point lands on this unit.
    ///
    /// Says nothing about whether the unit is a legal choice: liveness, team and whether it is even
    /// on the board are the caller's business. That matters more than it looks, because a dead unit
    /// has its collider disabled (see <see cref="DeathFeedback"/>) and so would be measured by the
    /// fallback radius instead — a caller that doesn't exclude the dead would find corpses easier to
    /// click than the living.
    /// </summary>
    public static PickHit Hit(Entity unit, Vector3 world, float headroom = DefaultHeadroom,
                              float fallbackRadius = DefaultRadius)
    {
        if (unit == null) return PickHit.None;

        // An authored box is a deliberate statement about this unit, so it is taken whole — no
        // headroom guessing on top of a shape somebody chose.
        var authored = unit.GetComponentInChildren<UnitPickBox>();
        if (authored != null)
            return authored.Contains(world) ? PickHit.Core : PickHit.None;

        var body = unit.GetComponentInChildren<Collider2D>();
        if (body == null || !body.enabled)
        {
            return Vector2.Distance(world, unit.transform.position + Vector3.up) <= fallbackRadius
                ? PickHit.Extended
                : PickHit.None;
        }

        var bounds = body.bounds;
        if (world.x < bounds.min.x || world.x > bounds.max.x) return PickHit.None;
        if (world.y < bounds.min.y) return PickHit.None;
        if (world.y <= bounds.max.y) return PickHit.Core;

        return world.y <= bounds.max.y + headroom ? PickHit.Extended : PickHit.None;
    }

    /// <summary>
    /// Whether a candidate should displace the best unit found so far.
    ///
    /// A body outranks padding first, and only then does draw order break the tie. Both halves are
    /// load-bearing. Units stand 1.5 apart in a column while a body is about 1.5 tall, so the
    /// headroom of the unit in front reaches into the lower body of the unit behind — and since the
    /// unit in front is also the nearer one, draw order alone would hand it every click on its
    /// neighbour's legs. Ranking the body first gives that band back to the unit whose body it is.
    ///
    /// Where the claims are equal — two bodies genuinely overlapping — the unit the player can see is
    /// the one they meant, and the project sorts sprites along +Y (GraphicsSettings custom axis),
    /// which draws lower units in front.
    /// </summary>
    public static bool Beats(PickHit hit, Entity candidate, PickHit bestHit, Entity best)
    {
        if (best == null) return hit != PickHit.None;
        if (hit != bestHit) return hit > bestHit;
        return IsInFrontOf(candidate, best);
    }

    /// <summary>Whether <paramref name="candidate"/> is drawn in front of <paramref name="current"/>.</summary>
    public static bool IsInFrontOf(Entity candidate, Entity current) =>
        current == null || candidate.transform.position.y < current.transform.position.y;

    /// <summary>
    /// The core and full (core + headroom) click areas of a unit, for drawing. False when the unit
    /// has neither an authored box nor a usable collider, and so is picked by radius instead.
    /// </summary>
    public static bool TryGetBoxes(Entity unit, float headroom, out Bounds core, out Bounds full)
    {
        core = full = default;
        if (unit == null) return false;

        var authored = unit.GetComponentInChildren<UnitPickBox>();
        if (authored != null)
        {
            core = full = authored.WorldBounds;
            return true;
        }

        var body = unit.GetComponentInChildren<Collider2D>();
        if (body == null || !body.enabled) return false;

        core = body.bounds;
        full = core;
        full.Encapsulate(new Vector3(core.center.x, core.max.y + headroom, core.center.z));
        return true;
    }
}
