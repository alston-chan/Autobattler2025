using UnityEngine;

/// <summary>
/// How solid a unit's bar should be this frame.
///
/// It fades out as its owner dies, which reads well. The bug is the other direction: the fade only
/// ever counted down, and nothing counted it back up. A bar that outlived its owner's death and was
/// still there when the owner came back — a revive, a death sequence that had not yet deactivated
/// the body, anything that leaves the same bar attached to a unit that is alive again — stayed at
/// whatever alpha it had reached. If the fade had finished, that is zero: a living unit with no
/// health bar, for the rest of the run, with nothing in the logs.
///
/// Stated as an invariant instead of a countdown: <b>a living unit's bar is solid</b>. That holds
/// however the bar got dimmed, including paths nobody has thought of yet.
/// </summary>
public static class BarFade
{
    /// <summary>Full opacity. A bar at this is drawn at the alpha its art was authored with.</summary>
    public const float Solid = 1f;

    public static float Step(bool ownerIsDead, bool fadeOnDeath, float current, float deltaTime, float duration)
    {
        if (!ownerIsDead) return Solid;
        if (!fadeOnDeath) return current;
        return Mathf.Max(0f, current - deltaTime / Mathf.Max(0.0001f, duration));
    }
}
