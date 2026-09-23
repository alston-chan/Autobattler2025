using UnityEngine;

/// <summary>
/// Walking around hostile ground rather than through it, kept free of the battlefield so the rule
/// can be tested. See CombatAI.AroundPools for why.
/// </summary>
public static class PoolSteer
{
    /// <summary>
    /// <paramref name="step"/> with the part that heads into the pool taken out, given the pool's
    /// <paramref name="outward"/> normal where the unit stands. A step with no inward part is left
    /// alone; one aimed straight in goes along the rim instead, on the side of
    /// <paramref name="toTarget"/>, so the unit rounds the pool toward what it wants.
    /// </summary>
    public static Vector3 Around(Vector3 step, Vector3 outward, Vector3 toTarget)
    {
        outward.z = 0f;
        if (outward.sqrMagnitude < 1e-8f) return step;
        outward.Normalize();
        float inward = -Vector3.Dot(step, outward);
        if (inward <= 0f) return step;

        Vector3 slide = step + outward * inward;
        if (slide.sqrMagnitude > 0.04f) return slide.normalized;

        Vector3 along = new Vector3(-outward.y, outward.x, 0f);
        if (Vector3.Dot(along, toTarget) < 0f) along = -along;
        return along;
    }
}
