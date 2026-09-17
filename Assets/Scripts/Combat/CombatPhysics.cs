using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bodies. Every living unit in a fight is a circle that cannot share ground with another, and a
/// unit thrown by a knockback carries momentum: it hits what it is thrown into, both stagger, both
/// are hurt in proportion to the speed, and part of the throw passes on — so a Cannonball dominoes
/// through a line and a Repulsion Nova flings bodies into bodies. The arena's edge is a wall: a body
/// thrown into it slams, bounces off, and is hurt.
///
/// This is what makes a shove mean something. Before it, a knockback moved one sprite half a unit
/// and nothing else in the fight noticed; now the fight is a space with pressure in it, and every
/// spell that pushes or pulls has consequences the player can see and set up (Docs/Combat.md).
///
/// Positions are still written directly, as CombatAI and Knockback do — there is no Rigidbody — so
/// this runs after both each frame (LateUpdate) and resolves what they left overlapping. The
/// arithmetic lives in <see cref="BodyMath"/>, which has no scene and is tested on its own.
/// </summary>
[DefaultExecutionOrder(1000)]
public class CombatPhysics : MonoBehaviour
{
    [Serializable]
    public class Settings
    {
        [Tooltip("Bodies cannot overlap. Off, units pass through each other as they did before.")]
        public bool enableBodies = true;
        [Tooltip("A thrown body hits what it is thrown into, and the wall. Off, bodies only separate.")]
        public bool enableImpacts = true;
        [Tooltip("A unit's body radius in world units when its UnitData does not say. Two bodies touch at twice this.")]
        public float bodyRadius = 0.45f;
        [Range(0f, 1f), Tooltip("How firmly allies are kept apart per frame. 1 is a wall; less lets a unit squeeze past its own line over a few frames.")]
        public float allyPush = 0.35f;
        [Tooltip("How fast a knockback dies out. A throw travels about force / damping units.")]
        public float damping = 4f;
        [Tooltip("A throw is over once the body is slower than this (units/s) and the unit may walk again. The decay's tail is invisible drift; without a floor a unit stood still for nearly two seconds after a hard throw.")]
        public float restSpeed = 0.6f;

        [Header("Impacts")]
        [Tooltip("Closing speed (units/s) below which a collision is a shove, not a hit.")]
        public float impactSpeed = 3f;
        [Tooltip("Damage from a body-to-body impact: this fraction of the victim's max health per unit of closing speed above the threshold.")]
        public float impactPercentPerSpeed = 0.01f;
        [Tooltip("As above, for a slam into the arena wall.")]
        public float wallPercentPerSpeed = 0.015f;
        [Range(0f, 1f), Tooltip("Cap on any one impact, as a fraction of max health.")]
        public float maxImpactPercent = 0.25f;
        [Range(0f, 1f), Tooltip("How much of the closing speed the struck body inherits.")]
        public float momentumTransfer = 0.6f;
        [Range(0f, 1f), Tooltip("How much of what it would lose a charging body keeps through a hit, so a Bull Rush plows on.")]
        public float chargeRetain = 0.7f;
        [Range(0f, 1f), Tooltip("How much speed comes back off the wall.")]
        public float bounce = 0.35f;
        [Tooltip("Seconds a body is staggered by an impact.")]
        public float impactStun = 0.25f;
        [Tooltip("Freeze-frame on impact, per body.")]
        public float impactHitstop = 0.06f;
        [Tooltip("A body cannot impact again for this long, so one collision is one hit.")]
        public float impactCooldown = 0.15f;

        [Header("Stances")]
        [Tooltip("A kiting unit only backs away from an enemy that is coming for it: one whose target it is and whose reach is shorter than its own. Off, it backs away from whatever is nearest.")]
        public bool kiteOnlyWhenTargeted = true;
        [Range(0.1f, 1f), Tooltip("A kiting unit starts backing away when the threat is closer than this fraction of its reach.")]
        public float kiteFraction = 0.6f;
        [Tooltip("Once backing away, it keeps going until the threat is this much further than where it started, so it does not flicker at the line.")]
        public float kiteHysteresis = 1.5f;
        [Range(0f, 1f), Tooltip("How much a retreat leans toward the unit's own back line rather than straight away from the threat, so it falls back behind its friends instead of into a corner.")]
        public float kiteHomeBias = 0.5f;
        [Range(0.1f, 1.5f), Tooltip("Backing away, as a fraction of walking speed.")]
        public float kiteSpeed = 0.85f;
        [Tooltip("A holding unit stands its ground this long, or until it is hurt, before it advances.")]
        public float holdSeconds = 4f;
    }

    public static Settings Active => CombatFeelSettings.Active.physics;

    // For measuring the rule. Reset at each bell.
    /// <summary>Body-to-body impacts this fight.</summary>
    public static int Collisions;
    /// <summary>Slams into the arena wall this fight.</summary>
    public static int WallSlams;
    /// <summary>Damage dealt by impacts and slams this fight.</summary>
    public static float ImpactDamage;
    /// <summary>Pair-frames where two bodies were inside half their combined radius before resolution — the pile-up measure.</summary>
    public static int DeepOverlaps;

    private static CombatPhysics _instance;
    private readonly List<Entity> _bodies = new List<Entity>(16);

    /// <summary>Make sure the resolver exists. Called by every entity as it wakes; one is enough.</summary>
    public static void Ensure()
    {
        if (_instance != null || !Application.isPlaying) return;
        _instance = FindObjectOfType<CombatPhysics>();
        if (_instance == null) _instance = new GameObject("CombatPhysics").AddComponent<CombatPhysics>();
    }

    public static void OnFightStart()
    {
        Collisions = 0; WallSlams = 0; ImpactDamage = 0f; DeepOverlaps = 0;
        Ensure();
    }

    private void Awake() { if (_instance == null) _instance = this; }
    private void OnDestroy() { if (_instance == this) _instance = null; }

    private void LateUpdate()
    {
        var s = Active;
        if (s == null || !s.enableBodies) return;

        _bodies.Clear();
        var all = EntityRegistry.All;
        for (int i = 0; i < all.Count; i++)
        {
            var e = all[i];
            if (e != null && !e.isDead && e.IsFighting && e.gameObject.activeInHierarchy && e.Knockback != null) _bodies.Add(e);
        }

        for (int i = 0; i < _bodies.Count; i++)
        for (int j = i + 1; j < _bodies.Count; j++)
        {
            var a = _bodies[i]; var b = _bodies[j];
            Vector3 pa = a.transform.position, pb = b.transform.position;
            float ra = a.BodyRadius, rb = b.BodyRadius;
            if (!BodyMath.Overlap(pa, ra, pb, rb, out Vector3 n, out float overlap)) continue;
            if (overlap > (ra + rb) * 0.5f) DeepOverlaps++;

            // A frozen or rooted body is a pillar: the other one does all the moving.
            bool aFixed = IsFixed(a), bFixed = IsFixed(b);
            if (s.enableImpacts) TryImpact(a, b, n, aFixed, bFixed, s);

            float strength = a.isTeam == b.isTeam ? s.allyPush : 1f;
            BodyMath.Separate(ref pa, ref pb, n, overlap, aFixed, bFixed, strength);
            if (!aFixed) a.transform.position = ArenaBounds.ClampToArena(pa);
            if (!bFixed) b.transform.position = ArenaBounds.ClampToArena(pb);
        }
    }

    private static bool IsFixed(Entity e) =>
        (e.Statuses != null && e.Statuses.Rooted) || (e.Hitstop != null && e.Hitstop.IsActive);

    /// <summary>
    /// Two overlapping bodies, one of them thrown: the hit. Whichever body brings the closing speed
    /// is the mover; the other is struck. Both are hurt by the speed (a charging body is not — it is
    /// the weapon), both stagger, and the struck body inherits part of the throw and the thrower's
    /// name, so if it flies on into the wall the credit still flows.
    /// </summary>
    private static void TryImpact(Entity a, Entity b, Vector3 n, bool aFixed, bool bFixed, Settings s)
    {
        var ka = a.Knockback; var kb = b.Knockback;
        Vector3 va = aFixed ? Vector3.zero : ka.Velocity, vb = bFixed ? Vector3.zero : kb.Velocity;
        float closing = BodyMath.ClosingSpeed(va, vb, n);
        if (closing <= s.impactSpeed) return;

        bool aMoves = Vector3.Dot(va, n) >= -Vector3.Dot(vb, n);
        Entity mover = aMoves ? a : b, struck = aMoves ? b : a;
        Knockback km = mover.Knockback, ks = struck.Knockback;
        if (!km.CanImpact) return;                       // one collision is one hit
        bool struckFixed = aMoves ? bFixed : aFixed;
        Vector3 toStruck = aMoves ? n : -n;
        Vector3 vm = aMoves ? va : vb, vs = aMoves ? vb : va;

        float pct = BodyMath.ImpactPercent(closing, s.impactSpeed, s.impactPercentPerSpeed, s.maxImpactPercent);
        Entity source = km.Launcher != null ? km.Launcher : mover;
        // The thrower's own side braced for it: a Chain Whip yanks a body into the caster's line,
        // and the line is the wall — neither hurt nor staggered by its own side's pull. The body
        // still is. So a push never hurts the pusher's team; the damage of a throw is the enemy's.
        bool braced = source != null && struck.isTeam == source.isTeam;

        BodyMath.Exchange(ref vm, ref vs, toStruck, closing, s.momentumTransfer, km.Charging ? s.chargeRetain : 0f, struckFixed);
        km.SetVelocity(vm);
        if (!struckFixed) ks.Launch(vs, source);
        km.MarkImpact(); ks.MarkImpact();
        if (!km.Charging) km.Stagger(s.impactStun);
        if (!braced) ks.Stagger(s.impactStun);

        float dealt = 0f;
        if (struck.Health != null && !braced)
        {
            float dmg = pct * struck.Health.maxHealth;
            struck.TakeDamage(dmg, source);
            dealt += dmg;
        }
        if (!km.Charging && mover.Health != null)
        {
            float dmg = pct * mover.Health.maxHealth;
            mover.TakeDamage(dmg, source != mover ? source : null);
            dealt += dmg;
        }
        mover.ApplyHitstop(s.impactHitstop);
        struck.ApplyHitstop(s.impactHitstop);

        Collisions++;
        ImpactDamage += dealt;
        CombatEvents.RaiseImpact(new ImpactInfo(mover, struck, closing, source));
    }

    /// <summary>
    /// A body carried into the arena's edge. Slow, it slides along the wall; fast, it slams: hurt by
    /// the speed, staggered, bounced back a little. Called by <see cref="Knockback"/> as it moves.
    /// </summary>
    public static void WallSlam(Entity e, Knockback k, Vector3 inward)
    {
        var s = Active;
        Vector3 v = k.Velocity;
        float into = -Vector3.Dot(v, inward);
        if (s == null || !s.enableImpacts || into <= s.impactSpeed || !k.CanImpact)
        {
            k.SetVelocity(BodyMath.Slide(v, inward));
            return;
        }

        float pct = BodyMath.ImpactPercent(into, s.impactSpeed, s.wallPercentPerSpeed, s.maxImpactPercent);
        Entity source = k.Launcher;
        k.SetVelocity(BodyMath.Bounce(v, inward, s.bounce));
        k.MarkImpact();
        k.Stagger(s.impactStun);
        e.ApplyHitstop(s.impactHitstop);

        float dmg = e.Health != null ? pct * e.Health.maxHealth : 0f;
        if (dmg > 0f) e.TakeDamage(dmg, source);

        WallSlams++;
        ImpactDamage += dmg;
        CombatEvents.RaiseImpact(new ImpactInfo(e, null, into, source));
    }
}

/// <summary>The arithmetic of bodies, with no scene in it. Everything works in the XY plane; Z is left alone.</summary>
public static class BodyMath
{
    /// <summary>Whether two circles overlap; the unit normal from a to b and the overlap depth when they do.</summary>
    public static bool Overlap(Vector3 pa, float ra, Vector3 pb, float rb, out Vector3 normal, out float overlap)
    {
        Vector3 d = pb - pa; d.z = 0f;
        float dist = d.magnitude;
        overlap = ra + rb - dist;
        if (overlap <= 0f) { normal = Vector3.zero; return false; }
        // Two bodies on one point part along x, the axis the sides face each other on.
        normal = dist > 1e-4f ? d / dist : Vector3.right;
        return true;
    }

    /// <summary>How fast a is closing on b along the normal from a to b. Negative when they part.</summary>
    public static float ClosingSpeed(Vector3 va, Vector3 vb, Vector3 n) => Vector3.Dot(va - vb, n);

    /// <summary>Damage as a fraction of max health for a closing speed: nothing under the threshold, linear above, capped.</summary>
    public static float ImpactPercent(float closing, float threshold, float perSpeed, float cap) =>
        Mathf.Clamp(Mathf.Max(0f, closing - threshold) * perSpeed, 0f, cap);

    /// <summary>
    /// Push two overlapping bodies apart along the normal. A fixed body does not move; the other
    /// takes the whole push. <paramref name="strength"/> is the fraction of the overlap closed this
    /// frame: 1 resolves it at once, less lets bodies squeeze past each other over a few frames.
    /// </summary>
    public static void Separate(ref Vector3 pa, ref Vector3 pb, Vector3 n, float overlap, bool aFixed, bool bFixed, float strength)
    {
        if (overlap <= 0f || (aFixed && bFixed)) return;
        Vector3 push = n * overlap * Mathf.Clamp01(strength);
        if (aFixed) pb += push;
        else if (bFixed) pa -= push;
        else { pa -= push * 0.5f; pb += push * 0.5f; }
    }

    /// <summary>
    /// Momentum passing from the mover to the struck body along the normal from mover to struck.
    /// The struck body gains <paramref name="transfer"/> of the closing speed; the mover loses what
    /// it gave — all of it against a fixed body — except the <paramref name="retain"/> fraction a
    /// charging body keeps.
    /// </summary>
    public static void Exchange(ref Vector3 mover, ref Vector3 struck, Vector3 n, float closing, float transfer, float retain, bool struckFixed)
    {
        float lost = struckFixed ? closing : closing * transfer;
        mover -= n * lost * (1f - Mathf.Clamp01(retain));
        if (!struckFixed) struck += n * closing * transfer;
    }

    /// <summary>Remove the part of a velocity that points into a wall, leaving the slide along it.</summary>
    public static Vector3 Slide(Vector3 v, Vector3 inward)
    {
        float into = -Vector3.Dot(v, inward);
        return into > 0f ? v + inward * into : v;
    }

    /// <summary>Reflect the part of a velocity that points into a wall, keeping <paramref name="bounce"/> of it.</summary>
    public static Vector3 Bounce(Vector3 v, Vector3 inward, float bounce)
    {
        float into = -Vector3.Dot(v, inward);
        return into > 0f ? v + inward * into * (1f + Mathf.Clamp01(bounce)) : v;
    }
}
