using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Four verbs that make space itself the weapon (Docs/Spells.md, "Space as the weapon"): a strike
/// that is announced before it lands, blades that orbit the caster, a pool that owns the ground it
/// covers, and a star that bounces between bodies. Each is a composite-spell effect built on the
/// same context as the others, so tiers scale its force, radius and reach, and each is fire-and-
/// forget: the cast ends when the thing is launched, and a runner on its own object sees it through.
/// </summary>
public static class ShapeSprites
{
    private static Sprite _disc;

    /// <summary>A soft-edged filled circle, one unit across at scale 1. Made once.</summary>
    public static Sprite Disc()
    {
        if (_disc != null) return _disc;
        const int size = 128; const float outer = 0.48f; const float feather = 0.03f;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size - 0.5f, dy = (y + 0.5f) / size - 0.5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01((outer - d) / feather));
            }
        texture.SetPixels(pixels); texture.Apply();
        _disc = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        return _disc;
    }

    /// <summary>A ring or disc on the floor at a point, sized to a radius, drawn under the units.</summary>
    public static SpriteRenderer OnFloor(string name, Vector3 at, float radius, Color color, bool filled)
    {
        var go = new GameObject(name);
        go.transform.position = new Vector3(at.x, at.y, 0f);
        go.transform.localScale = new Vector3(radius * 2f, radius * 2f * 0.55f, 1f);   // the floor is seen at an angle: rings lie flat
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = filled ? Disc() : UnitInspector.RingSprite();
        sr.color = color;
        sr.sortingOrder = -400;   // under the units, over the background
        return sr;
    }

    /// <summary>Everything of the other side alive within a radius of a point.</summary>
    public static List<Entity> EnemiesWithin(Entity of, Vector3 point, float radius)
    {
        var found = new List<Entity>();
        foreach (var e in EntityRegistry.All)
            if (e != null && !e.isDead && e.gameObject.activeInHierarchy && e.isTeam != of.isTeam && (e.transform.position - point).magnitude <= radius) found.Add(e);
        return found;
    }
}

// ---------------------------------------------------------------------------------------------
// Arrow Rain: a strike announced before it lands
// ---------------------------------------------------------------------------------------------

/// <summary>
/// A ring on the floor where the target stands, a pause, then damage and an outward shove to
/// everything still inside it. The pause is the point: a diver has left, a holder has not, and a
/// pull can put a body into the ring before it lands. The caster is free the moment the ring is
/// drawn; the strike runs on its own object.
/// </summary>
[Serializable]
public class StrikeAtPointEffect : SpellEffect
{
    [Tooltip("Seconds between the ring appearing and the strike landing.")] public float delay = 1.2f;
    [Min(0f)] public float radius = 2.5f;
    public ScaledValue damage = new ScaledValue(0f, ofWeaponDamage: 1.2f);
    [Tooltip("Shove each victim away from the centre with this force. Zero for none.")] public float knockback = 6f;
    public Color ringColor = new Color(1f, 0.25f, 0.2f, 0.55f);
    [Tooltip("Played at the centre when it lands. Optional.")] public GameObject landingPrefab;
    public float landingScale = 1.2f;

    public override IEnumerator Run(SpellContext ctx)
    {
        var caster = ctx.caster; var target = ctx.target;
        if (caster == null || target == null) yield break;
        float reach = radius * ctx.scale;
        var ring = ShapeSprites.OnFloor("ArrowRain", target.transform.position, reach, ringColor, filled: false);
        var strike = ring.gameObject.AddComponent<DelayedStrike>();
        strike.Arm(caster, ring.transform.position, reach, delay, damage.Evaluate(caster, null, ctx.tier), knockback * ctx.scale, landingPrefab, landingScale);
    }

    public override string Describe() => Describe(1, 1f);
    public override string Describe(int tier, float scale) => $"after {delay:0.#} s, {damage.DescribeAt(tier)} damage to enemies within {radius * scale:0.#} of where the target stood" + (knockback > 0f ? $", flung outward (force {knockback * scale:0})" : "");
}

/// <summary>The strike's clock, on the ring it drew.</summary>
public class DelayedStrike : MonoBehaviour
{
    private Entity _caster; private Vector3 _point; private float _radius, _at, _damage, _force, _scale; private GameObject _prefab;
    private SpriteRenderer _ring; private Color _color;

    public void Arm(Entity caster, Vector3 point, float radius, float delay, float damage, float force, GameObject landingPrefab, float landingScale)
    {
        _caster = caster; _point = point; _radius = radius; _at = Time.time + delay; _damage = damage; _force = force; _prefab = landingPrefab; _scale = landingScale;
        _ring = GetComponent<SpriteRenderer>(); _color = _ring != null ? _ring.color : Color.white;
    }

    private void Update()
    {
        // The ring brightens as the strike nears, so the pause reads as a countdown.
        if (_ring != null) { float t = Mathf.Clamp01(1f - (_at - Time.time) / 1.2f); _ring.color = new Color(_color.r, _color.g, _color.b, Mathf.Lerp(_color.a * 0.6f, 1f, t)); }
        if (Time.time < _at) return;
        if (_caster != null)
            foreach (var v in ShapeSprites.EnemiesWithin(_caster, _point, _radius))
            {
                v.TakeDamage(_damage, _caster);
                if (_force > 0f) { Vector3 dir = v.transform.position - _point; v.ApplyKnockback(dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.right, _force, _caster); }
            }
        if (_prefab != null) Fx.Spawn(_prefab, _point, _scale);
        Destroy(gameObject);
    }
}

// ---------------------------------------------------------------------------------------------
// Whirl: blades that orbit the caster
// ---------------------------------------------------------------------------------------------

/// <summary>
/// Blades circle the caster for a few seconds; each hits what it passes through and shoves it
/// outward. The first verb that rewards having enemies AT you: a pull, then this.
/// </summary>
[Serializable]
public class OrbitEffect : SpellEffect
{
    [Min(1)] public int blades = 3;
    [Tooltip("One more blade per tier above the first.")] public int bladesPerTier = 1;
    public float seconds = 4f;
    [Min(0.1f)] public float radius = 1.6f;
    [Tooltip("Degrees per second.")] public float spin = 200f;
    public ScaledValue damage = new ScaledValue(0f, ofWeaponDamage: 0.5f);
    [Tooltip("Outward shove per hit.")] public float force = 5f;
    [Tooltip("A blade can hit the same victim again after this long.")] public float hitInterval = 0.6f;
    public float hitRadius = 0.55f;
    [Tooltip("A supply sprite from the caster's collection; a plain disc when it has none.")] public string spriteName = "ThrowingStar";
    public float spriteScale = 0.45f;

    public override IEnumerator Run(SpellContext ctx)
    {
        var caster = ctx.caster; if (caster == null) yield break;
        var host = new GameObject("Whirl");
        host.transform.SetParent(caster.transform, false); host.transform.localPosition = Vector3.zero;
        var runner = host.AddComponent<OrbitRunner>();
        int count = blades + bladesPerTier * Mathf.Max(0, ctx.tier - 1);
        runner.Begin(caster, count, seconds, radius * ctx.scale, spin, damage.Evaluate(caster, null, ctx.tier), force * ctx.scale, hitInterval, hitRadius, Supplies.FindSprite(caster, spriteName), spriteScale);
    }

    public override string Describe() => Describe(1, 1f);
    public override string Describe(int tier, float scale) => $"{blades + bladesPerTier * Mathf.Max(0, tier - 1)} blades circle you for {seconds:0.#} s at {radius * scale:0.#}: {damage.DescribeAt(tier)} damage a pass, shoved outward (force {force * scale:0})";
}

public class OrbitRunner : MonoBehaviour
{
    private Entity _caster; private float _until, _radius, _spin, _damage, _force, _interval, _hitRadius, _angle;
    private readonly List<Transform> _blades = new List<Transform>();
    private readonly Dictionary<Entity, float> _nextHit = new Dictionary<Entity, float>();

    public void Begin(Entity caster, int count, float seconds, float radius, float spin, float damage, float force, float interval, float hitRadius, Sprite sprite, float spriteScale)
    {
        _caster = caster; _until = Time.time + seconds; _radius = radius; _spin = spin; _damage = damage; _force = force; _interval = interval; _hitRadius = hitRadius;
        for (int i = 0; i < count; i++)
        {
            var go = new GameObject("Blade" + i);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite != null ? sprite : ShapeSprites.Disc();
            sr.sortingOrder = Fx.SortingOrder;
            go.transform.localScale = Vector3.one * (sprite != null ? spriteScale : 0.3f);
            _blades.Add(go.transform);
        }
        Place();
    }

    private void Update()
    {
        if (Time.time >= _until || _caster == null || _caster.isDead) { Destroy(gameObject); return; }
        _angle += _spin * Time.deltaTime;
        Place();
        foreach (var blade in _blades)
        {
            foreach (var v in ShapeSprites.EnemiesWithin(_caster, blade.position, _hitRadius))
            {
                float next; if (_nextHit.TryGetValue(v, out next) && Time.time < next) continue;
                _nextHit[v] = Time.time + _interval;
                v.TakeDamage(_damage, _caster);
                Vector3 dir = v.transform.position - _caster.transform.position;
                if (_force > 0f) v.ApplyKnockback(dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.right, _force, _caster);
            }
        }
    }

    private void Place()
    {
        for (int i = 0; i < _blades.Count; i++)
        {
            float a = (_angle + 360f * i / _blades.Count) * Mathf.Deg2Rad;
            _blades[i].localPosition = new Vector3(Mathf.Cos(a) * _radius, Mathf.Sin(a) * _radius * 0.55f + 0.6f, -0.5f);
            _blades[i].localRotation = Quaternion.Euler(0f, 0f, _angle * 3f);
        }
    }
}

// ---------------------------------------------------------------------------------------------
// Tar Pool: ground that is owned
// ---------------------------------------------------------------------------------------------

/// <summary>
/// A pool on the floor at the target for a few seconds. Enemies inside take damage every half
/// second and wear a status; they walk out when they can (CombatAI leaves hostile ground before it
/// does anything else), which is what a pull, a throw or a charge into the pool is for.
/// </summary>
[Serializable]
public class ZoneEffect : SpellEffect
{
    [Min(0.1f)] public float radius = 2f;
    public float seconds = 5f;
    public ScaledValue damagePerSecond = new ScaledValue(0f, ofWeaponDamage: 0.5f);
    [Tooltip("Worn while inside and a moment after. Optional.")] public Status status;
    public float statusDuration = 1.5f;
    public Color color = new Color(0.12f, 0.06f, 0.02f, 0.7f);

    public override IEnumerator Run(SpellContext ctx)
    {
        var caster = ctx.caster; var target = ctx.target;
        if (caster == null || target == null) yield break;
        var disc = ShapeSprites.OnFloor("TarPool", target.transform.position, radius * ctx.scale, color, filled: true);
        var zone = disc.gameObject.AddComponent<Zone>();
        zone.Begin(caster, radius * ctx.scale, seconds, damagePerSecond.Evaluate(caster, null, ctx.tier), status, statusDuration);
    }

    public override string Describe() => Describe(1, 1f);
    public override string Describe(int tier, float scale) => $"a pool {radius * scale:0.#} wide under the target for {seconds:0.#} s: {damagePerSecond.DescribeAt(tier)} damage a second to enemies in it" + (status != null ? $", {status.DisplayName} while inside" : "") + "; they walk out";
}

/// <summary>A live pool. The AI asks <see cref="HostileAt"/> before it moves anywhere else.</summary>
public class Zone : MonoBehaviour
{
    public static readonly List<Zone> All = new List<Zone>();

    public Entity Owner { get; private set; }
    public float Radius { get; private set; }
    private float _until, _dps, _nextTick, _statusDuration; private Status _status;
    private const float Tick = 0.5f;

    public void Begin(Entity owner, float radius, float seconds, float damagePerSecond, Status status, float statusDuration)
    {
        Owner = owner; Radius = radius; _until = Time.time + seconds; _dps = damagePerSecond; _status = status; _statusDuration = statusDuration; _nextTick = Time.time + Tick;
    }

    private void OnEnable() { All.Add(this); }
    private void OnDisable() { All.Remove(this); }

    public bool Contains(Entity e) => e != null && (e.transform.position - transform.position).magnitude <= Radius;

    /// <summary>The pool this unit is standing in that belongs to the other side, or null.</summary>
    public static Zone HostileAt(Entity e)
    {
        if (e == null) return null;
        for (int i = 0; i < All.Count; i++)
        {
            var z = All[i];
            if (z == null || z.Owner == null || z.Owner.isTeam == e.isTeam) continue;
            if (z.Contains(e)) return z;
        }
        return null;
    }

    private void Update()
    {
        if (Time.time >= _until || Owner == null) { Destroy(gameObject); return; }
        if (Time.time < _nextTick) return;
        _nextTick += Tick;
        foreach (var v in ShapeSprites.EnemiesWithin(Owner, transform.position, Radius))
        {
            v.TakeDamage(_dps * Tick, Owner);
            if (_status != null && v.Statuses != null) v.Statuses.Apply(_status, _statusDuration, Owner);
        }
    }
}

// ---------------------------------------------------------------------------------------------
// Ricochet: a star that bounces between bodies
// ---------------------------------------------------------------------------------------------

/// <summary>
/// A thrown star that, on a hit, shoves the victim and flies on to the nearest enemy it has not hit
/// yet, so a clump is a chain of hits. Bounces grow with the tier.
/// </summary>
[Serializable]
public class BounceProjectileEffect : SpellEffect
{
    [Min(0)] public int bounces = 2;
    public int bouncesPerTier = 1;
    [Tooltip("How far the star will look for its next body.")] public float bounceRange = 4f;
    public ScaledValue damage = new ScaledValue(0f, ofWeaponDamage: 0.9f);
    [Tooltip("Shove on each hit, along the star's flight.")] public float force = 4f;
    public float speed = 14f;
    public float hitRadius = 0.6f;
    public float spin = 720f;
    public string spriteName = "ThrowingStar";
    public float spriteScale = 0.5f;

    public override IEnumerator Run(SpellContext ctx)
    {
        var caster = ctx.caster; var target = ctx.target;
        if (caster == null || target == null) yield break;
        var go = Supplies.Build(caster, spriteName, spriteScale, "Ricochet");
        if (go == null) { go = new GameObject("Ricochet"); go.transform.position = Supplies.ThrowOrigin(caster); var sr = go.AddComponent<SpriteRenderer>(); sr.sprite = ShapeSprites.Disc(); sr.sortingOrder = 100; go.transform.localScale = Vector3.one * 0.3f; }
        var star = go.AddComponent<BouncingStar>();
        star.Launch(caster, target, speed, damage.Evaluate(caster, null, ctx.tier), force * ctx.scale, hitRadius, spin, bounces + bouncesPerTier * Mathf.Max(0, ctx.tier - 1), bounceRange * ctx.scale);
    }

    public override string Describe() => Describe(1, 1f);
    public override string Describe(int tier, float scale) => $"a star for {damage.DescribeAt(tier)} damage that shoves (force {force * scale:0}) and bounces to the nearest enemy within {bounceRange * scale:0.#}, {bounces + bouncesPerTier * Mathf.Max(0, tier - 1)} times";
}

public class BouncingStar : MonoBehaviour
{
    private Entity _thrower, _target; private float _speed, _damage, _force, _hitRadius, _spin, _range; private int _bouncesLeft;
    private readonly HashSet<Entity> _hit = new HashSet<Entity>();

    public void Launch(Entity thrower, Entity target, float speed, float damage, float force, float hitRadius, float spin, int bounces, float range)
    {
        _thrower = thrower; _target = target; _speed = speed; _damage = damage; _force = force; _hitRadius = hitRadius; _spin = spin; _bouncesLeft = bounces; _range = range;
        Destroy(gameObject, 6f);
    }

    private void Update()
    {
        if (_target == null || _target.isDead) { _target = Next(transform.position); if (_target == null) { Destroy(gameObject); return; } }
        Vector3 aim = _target.transform.position + Vector3.up * 0.6f;
        Vector3 before = transform.position;
        transform.position = Vector3.MoveTowards(before, aim, _speed * Time.deltaTime);
        transform.Rotate(0f, 0f, _spin * Time.deltaTime);
        if (Vector3.Distance(transform.position, aim) > _hitRadius) return;

        _target.TakeDamage(_damage, _thrower);
        Vector3 dir = aim - before; dir.z = 0f;
        if (_force > 0f) _target.ApplyKnockback(dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.right, _force, _thrower);
        _hit.Add(_target);
        if (_bouncesLeft-- <= 0) { Destroy(gameObject); return; }
        _target = Next(_target.transform.position);
        if (_target == null) Destroy(gameObject);
    }

    private Entity Next(Vector3 from)
    {
        Entity best = null; float bestD = _range;
        foreach (var e in EntityRegistry.All)
        {
            if (e == null || e.isDead || !e.gameObject.activeInHierarchy || _thrower == null || e.isTeam == _thrower.isTeam || _hit.Contains(e)) continue;
            float d = Vector3.Distance(from, e.transform.position);
            if (d <= bestD) { bestD = d; best = e; }
        }
        return best;
    }
}
