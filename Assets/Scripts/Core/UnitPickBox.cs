using UnityEngine;

/// <summary>
/// States a unit's click area outright, instead of letting <see cref="UnitPicking"/> infer one from
/// the body collider.
///
/// Add this to a unit prefab when the inferred area is wrong for it. The collider it would otherwise
/// use is an arrow hitbox — sized for what a projectile should strike, which is not the same as what
/// a player is aiming a cursor at, and not something that can be retuned for clicking without
/// changing how combat plays. A unit whose art sits oddly in its hitbox, or a boss that should be as
/// easy to click as it is large, wants its own answer.
///
/// Authored in WORLD units around the unit's feet, so the numbers mean what they look like on the
/// board and don't have to be read through the prefab's root scale. Select the unit to see the box
/// drawn in the Scene view.
/// </summary>
public class UnitPickBox : MonoBehaviour
{
    [Tooltip("Size of the click area in world units. For reference, a human's body collider is " +
             "about 0.63 wide by 1.5 tall, and units stand 1.5 apart in a column — a box much " +
             "taller than that reaches into the unit standing behind.")]
    public Vector2 size = new Vector2(0.8f, 2f);

    [Tooltip("Centre of the click area relative to the unit's feet, in world units.")]
    public Vector2 offset = new Vector2(0f, 1f);

    /// <summary>The click area in world space.</summary>
    public Bounds WorldBounds
    {
        get
        {
            Vector3 centre = transform.position + new Vector3(offset.x, offset.y, 0f);
            return new Bounds(centre, new Vector3(size.x, size.y, 1f));
        }
    }

    public bool Contains(Vector3 world)
    {
        var bounds = WorldBounds;
        return world.x >= bounds.min.x && world.x <= bounds.max.x &&
               world.y >= bounds.min.y && world.y <= bounds.max.y;
    }

    private void OnDrawGizmosSelected()
    {
        var bounds = WorldBounds;
        Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.9f);
        Gizmos.DrawWireCube(bounds.center, new Vector3(bounds.size.x, bounds.size.y, 0f));
    }
}
