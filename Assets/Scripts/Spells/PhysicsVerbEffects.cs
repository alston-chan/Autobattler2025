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
///
/// The look follows one rule: this is a game of cartoon bodies and objects, so the effects are
/// objects doing physical things (arrows that fall and stick, a blob that is lobbed and spreads,
/// bubbles that rise) rather than post-processing.
/// </summary>
public static class ShapeSprites
{
    private static Sprite _disc;
    private static readonly Dictionary<int, Sprite> _splats = new Dictionary<int, Sprite>();

    /// <summary>A soft-edged filled circle, one unit across at scale 1. Made once.</summary>
    public static Sprite Disc()
    {
        if (_disc != null) return _disc;
        _disc = Blob(0, 0f);
        return _disc;
    }

    /// <summary>
    /// A spilled shape: a disc whose edge wanders by up to a quarter of its radius, so a pool reads
    /// as liquid rather than a marker. A handful of seeds, cached, so pools differ without a texture
    /// per cast.
    /// </summary>
    public static Sprite Splat(int seed)
    {
        seed = Mathf.Abs(seed) % 6;
        Sprite s; if (_splats.TryGetValue(seed, out s) && s != null) return s;
        s = Blob(seed, 0.25f);
        _splats[seed] = s;
        return s;
    }

    private static Sprite _thinRing;

    /// <summary>
    /// A thin ring, one unit across at scale 1. The inspector's ring is a tenth of its diameter thick,
    /// which at a strike's size becomes a band a body wide; this one stays a line.
    /// </summary>
    public static Sprite ThinRing()
    {
        if (_thinRing != null) return _thinRing;
        const int size = 256; const float outer = 0.48f, inner = 0.455f, feather = 0.008f;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size - 0.5f, dy = (y + 0.5f) / size - 0.5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01((outer - d) / feather) * Mathf.Clamp01((d - inner) / feather));
            }
        texture.SetPixels(pixels); texture.Apply();
        _thinRing = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        return _thinRing;
    }

    private static Sprite Blob(int seed, float wobble)
    {
        const int size = 128; const float feather = 0.03f;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color[size * size];
        // Three sine lobes at different frequencies and phases: enough to look poured, never spiky.
        float p1 = seed * 1.7f, p2 = seed * 2.9f + 1f, p3 = seed * 0.6f + 2f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size - 0.5f, dy = (y + 0.5f) / size - 0.5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Atan2(dy, dx);
                float edge = 0.48f - wobble * 0.48f * (0.5f + 0.25f * Mathf.Sin(3f * a + p1) + 0.15f * Mathf.Sin(5f * a + p2) + 0.10f * Mathf.Sin(7f * a + p3));
                pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01((edge - d) / feather));
            }
        texture.SetPixels(pixels); texture.Apply();
        return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
    }

    /// <summary>A ring, disc or splat on the floor at a point, sized to a radius, drawn under the units.</summary>
    public static SpriteRenderer OnFloor(string name, Vector3 at, float radius, Color color, Sprite sprite)
    {
        var go = new GameObject(name);
        go.transform.position = new Vector3(at.x, at.y, 0f);
        go.transform.localScale = new Vector3(radius * 2f, radius * 2f * 0.55f, 1f);   // the floor is seen at an angle: shapes lie flat
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = color;
        sr.sortingOrder = -400;   // under the units, over the background
        return sr;
    }

    public static SpriteRenderer OnFloor(string name, Vector3 at, float radius, Color color, bool filled) =>
        OnFloor(name, at, radius, color, filled ? Disc() : ThinRing());

    /// <summary>Everything of the other side alive within a radius of a point.</summary>
    public static List<Entity> EnemiesWithin(Entity of, Vector3 point, float radius)
    {
        var found = new List<Entity>();
        foreach (var e in EntityRegistry.All)
            if (e != null && !e.isDead && e.gameObject.activeInHierarchy && e.isTeam != of.isTeam && (e.transform.position - point).magnitude <= radius) found.Add(e);
        return found;
    }

    /// <summary>A sprite in the world with nothing else on it. Sorting above the units so it reads over them.</summary>
    public static SpriteRenderer Floating(string name, Sprite sprite, Vector3 at, float scale, Color color, int order = 120)
    {
        var go = new GameObject(name);
        go.transform.position = at;
        go.transform.localScale = Vector3.one * scale;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite; sr.color = color; sr.sortingOrder = order;
        return sr;
    }
}

// ---------------------------------------------------------------------------------------------
// Arrow Rain: a strike announced before it lands
// ---------------------------------------------------------------------------------------------

/// <summary>
/// A ring on the floor where the target stands and a second ring closing on it; in the last half
/// second arrows fall out of the sky and stick in the ground; when the closing ring reaches the
/// strike ring, everything still inside is hit and flung outward. The pause is the point: a diver
/// has left, a holder has not, and a pull can put a body into the ring before it lands. The caster
/// is free the moment the ring is drawn; the strike runs on its own object.
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
    [Header("Arrows")]
    [Tooltip("The arrow that falls; the bow's own. None: the strike is only the ring.")] public GameObject arrowPrefab;
    [Min(0)] public int arrows = 10;
    [Tooltip("Where the arrows start, above the ring.")] public float fallHeight = 7f;
    public float fallSpeed = 22f;
    [Tooltip("Seconds before the strike the first arrow is loosed; they arrive together with the strike.")] public float volleySeconds = 0.45f;
    [Tooltip("One arrow fired straight up from the caster at the cast, so the rain came from somewhere.")] public bool launchArrow = true;

    public override IEnumerator Run(SpellContext ctx)
    {
        var caster = ctx.caster; var target = ctx.target;
        if (caster == null || target == null) yield break;
        float reach = radius * ctx.scale;
        var ring = ShapeSprites.OnFloor("ArrowRain", target.transform.position, reach, ringColor, filled: false);
        var strike = ring.gameObject.AddComponent<DelayedStrike>();
        strike.Arm(caster, ring.transform.position, reach, delay, damage.Evaluate(caster, null, ctx.tier), knockback * ctx.scale, landingPrefab, landingScale, ringColor);
        strike.Arrows(arrowPrefab, arrows + Mathf.RoundToInt(2 * Mathf.Max(0, ctx.tier - 1)), fallHeight, fallSpeed, volleySeconds);
        if (launchArrow && arrowPrefab != null) FallingArrow.LaunchUp(arrowPrefab, Supplies.ThrowOrigin(caster), fallSpeed * 0.8f, 0.5f);
    }

    public override string Describe() => Describe(1, 1f);
    public override string Describe(int tier, float scale) => $"after {delay:0.#} s, {damage.DescribeAt(tier)} damage to enemies within {radius * scale:0.#} of where the target stood" + (knockback > 0f ? $", flung outward (force {knockback * scale:0})" : "");
}

/// <summary>The strike's clock, on the ring it drew: the closing ring, the volley, the landing.</summary>
public class DelayedStrike : MonoBehaviour
{
    private Entity _caster; private Vector3 _point; private float _radius, _at, _delay, _damage, _force, _scale; private GameObject _prefab;
    private SpriteRenderer _ring, _closing; private Color _color;
    private GameObject _arrowPrefab; private int _arrows, _loosed; private float _fallHeight, _fallSpeed, _volley;

    public void Arm(Entity caster, Vector3 point, float radius, float delay, float damage, float force, GameObject landingPrefab, float landingScale, Color color)
    {
        _caster = caster; _point = point; _radius = radius; _delay = Mathf.Max(0.05f, delay); _at = Time.time + _delay; _damage = damage; _force = force; _prefab = landingPrefab; _scale = landingScale;
        _ring = GetComponent<SpriteRenderer>(); _color = color;
        // The second ring: starts wide and closes onto the strike ring over the delay, so the pause reads as "when" as well as "where".
        // Its own object, not a child: the strike ring is scaled to its radius (and flattened), and a
        // child inherits that, so a parented closing ring came out several times too big and squashed.
        _closing = ShapeSprites.OnFloor("ArrowRainClosing", point, radius * 1.6f, new Color(color.r, color.g, color.b, color.a * 0.7f), filled: false);
    }

    private void OnDestroy() { if (_closing != null) Destroy(_closing.gameObject); }

    public void Arrows(GameObject prefab, int count, float height, float speed, float volleySeconds)
    {
        _arrowPrefab = prefab; _arrows = prefab != null ? count : 0; _fallHeight = height; _fallSpeed = speed; _volley = volleySeconds;
    }

    private void Update()
    {
        float left = _at - Time.time;
        float t = Mathf.Clamp01(1f - left / _delay);
        if (_ring != null) _ring.color = new Color(_color.r, _color.g, _color.b, Mathf.Lerp(_color.a * 0.6f, 1f, t));
        if (_closing != null)
        {
            float r = Mathf.Lerp(_radius * 1.6f, _radius, t);
            _closing.transform.localScale = new Vector3(r * 2f, r * 2f * 0.55f, 1f);
        }

        // The volley: loosed one by one through the last stretch, each timed to arrive with the strike.
        if (_arrowPrefab != null && _loosed < _arrows && left <= _volley)
        {
            int due = Mathf.Min(_arrows, Mathf.CeilToInt((1f - left / _volley) * _arrows));
            while (_loosed < due)
            {
                Vector2 spot = UnityEngine.Random.insideUnitCircle * _radius * 0.9f;
                Vector3 land = _point + new Vector3(spot.x, spot.y * 0.55f, 0f);
                FallingArrow.Drop(_arrowPrefab, land + Vector3.up * _fallHeight, land, _fallSpeed * UnityEngine.Random.Range(0.9f, 1.15f));
                _loosed++;
            }
        }

        if (left > 0f) return;
        if (_caster != null)
            foreach (var v in ShapeSprites.EnemiesWithin(_caster, _point, _radius))
            {
                v.TakeDamage(_damage, _caster);
                if (_force > 0f) { Vector3 dir = v.transform.position - _point; v.ApplyKnockback(dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.right, _force, _caster); }
            }
        if (_prefab != null) Fx.Spawn(_prefab, _point, _scale);
        // The ring snaps white and is gone: the landing, seen.
        var flash = ShapeSprites.OnFloor("ArrowRainLanding", _point, _radius, Color.white, filled: false);
        flash.gameObject.AddComponent<FadeAway>().Begin(0.18f);
        Destroy(gameObject);
    }
}

/// <summary>An arrow in flight with nothing else to it: falls to a point and sticks, or rises and is gone.</summary>
public class FallingArrow : MonoBehaviour
{
    private Vector3 _to; private float _speed, _stuckAt = -1f; private bool _up; private float _upUntil;

    public static void Drop(GameObject prefab, Vector3 from, Vector3 to, float speed)
    {
        var go = Make(prefab, from);
        var a = go.AddComponent<FallingArrow>();
        a._to = to; a._speed = speed;
        Vector3 heading = to - from; go.transform.right = heading.sqrMagnitude > 0.0001f ? heading.normalized : Vector3.down;
    }

    public static void LaunchUp(GameObject prefab, Vector3 from, float speed, float seconds)
    {
        var go = Make(prefab, from);
        var a = go.AddComponent<FallingArrow>();
        a._up = true; a._speed = speed; a._upUntil = Time.time + seconds;
        go.transform.right = new Vector3(0.15f, 1f, 0f).normalized;
    }

    /// <summary>The bow's arrow prefab as a picture only: its physics and its projectile script are switched off.</summary>
    private static GameObject Make(GameObject prefab, Vector3 at)
    {
        var go = UnityEngine.Object.Instantiate(prefab, at, Quaternion.identity);
        go.name = "RainArrow";
        foreach (var b in go.GetComponentsInChildren<MonoBehaviour>(true)) b.enabled = false;
        foreach (var rb in go.GetComponentsInChildren<Rigidbody2D>(true)) rb.simulated = false;
        foreach (var c in go.GetComponentsInChildren<Collider2D>(true)) c.enabled = false;
        // The bow's arrow carries a trail for its flight; falling at this speed it drew a streak the height of the arena.
        foreach (var t in go.GetComponentsInChildren<TrailRenderer>(true)) t.enabled = false;
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true)) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        foreach (var r in go.GetComponentsInChildren<SpriteRenderer>(true)) r.sortingOrder = 110;
        return go;
    }

    private void Update()
    {
        if (_up)
        {
            transform.position += transform.right * _speed * Time.deltaTime;
            if (Time.time >= _upUntil) Destroy(gameObject);
            return;
        }
        if (_stuckAt < 0f)
        {
            transform.position = Vector3.MoveTowards(transform.position, _to, _speed * Time.deltaTime);
            if (Vector3.Distance(transform.position, _to) > 0.01f) return;
            // Stuck in the ground: a little off the vertical, under the units from here on.
            _stuckAt = Time.time;
            transform.rotation = Quaternion.Euler(0f, 0f, transform.eulerAngles.z + UnityEngine.Random.Range(-8f, 8f));
            foreach (var r in GetComponentsInChildren<SpriteRenderer>(true)) r.sortingOrder = -390;
            return;
        }
        float age = Time.time - _stuckAt;
        if (age > 1.2f)
        {
            float a = Mathf.Clamp01(1f - (age - 1.2f) / 0.4f);
            foreach (var r in GetComponentsInChildren<SpriteRenderer>(true)) { var c = r.color; c.a = a; r.color = c; }
            if (a <= 0f) Destroy(gameObject);
        }
    }
}

/// <summary>Fade every sprite on this object out and destroy it.</summary>
public class FadeAway : MonoBehaviour
{
    private float _seconds, _start;
    public void Begin(float seconds) { _seconds = Mathf.Max(0.01f, seconds); _start = Time.time; }
    private void Update()
    {
        float a = Mathf.Clamp01(1f - (Time.time - _start) / _seconds);
        foreach (var r in GetComponentsInChildren<SpriteRenderer>(true)) { var c = r.color; c.a = a; r.color = c; }
        if (a <= 0f) Destroy(gameObject);
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
                v.TakeDamage(_damage, _caster, quiet: true);   // a pass every half second; a flash each would hold the body white
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
/// A blob lobbed from the caster; where it lands a pool spreads and lives a few seconds. Enemies
/// inside take damage every half second, wear a status and are stained dark; they walk out when
/// they can (CombatAI leaves hostile ground before it does anything else), which is what a pull, a
/// throw or a charge into the pool is for.
/// </summary>
[Serializable]
public class ZoneEffect : SpellEffect
{
    [Min(0.1f)] public float radius = 2f;
    public float seconds = 5f;
    public ScaledValue damagePerSecond = new ScaledValue(0f, ofWeaponDamage: 0.5f);
    [Tooltip("What resists the ticks. Tar from a wand is magical.")] public DamageType damageType = DamageType.Physical;
    [Tooltip("Worn while inside and a moment after. Optional.")] public Status status;
    public float statusDuration = 1.5f;
    public Color color = new Color(0.12f, 0.06f, 0.02f, 0.78f);
    [Tooltip("How long the blob is in the air from the caster to the target.")] public float lobSeconds = 0.35f;
    [Tooltip("Units inside are stained this colour.")] public Color stain = new Color(0.45f, 0.32f, 0.22f, 1f);

    public override IEnumerator Run(SpellContext ctx)
    {
        var caster = ctx.caster; var target = ctx.target;
        if (caster == null || target == null) yield break;
        float reach = radius * ctx.scale; float dps = damagePerSecond.Evaluate(caster, null, ctx.tier);
        var status = this.status; float statusSeconds = statusDuration; float life = seconds; Color poolColor = color; Color stainColor = stain;
        var type = damageType;
        LobbedBlob.Throw(Supplies.ThrowOrigin(caster), target.transform.position, lobSeconds, poolColor, reach * 0.35f, at =>
        {
            // Six splat shapes and a random mirror stand in for rotation: a floor shape is flattened by
            // its scale, and rotating that transform skewed the pool into a tall smear.
            var pool = ShapeSprites.OnFloor("TarPool", at, reach, poolColor, ShapeSprites.Splat(UnityEngine.Random.Range(0, 6)));
            if (UnityEngine.Random.value < 0.5f) pool.flipX = true;
            if (UnityEngine.Random.value < 0.5f) pool.flipY = true;
            var zone = pool.gameObject.AddComponent<Zone>();
            zone.Begin(caster, reach, life, dps, status, statusSeconds, stainColor);
            zone.DamageType = type;
        });
    }

    public override string Describe() => Describe(1, 1f);
    public override string Describe(int tier, float scale) => $"a pool {radius * scale:0.#} wide under the target for {seconds:0.#} s: {damagePerSecond.DescribeAt(tier)} damage a second to enemies in it" + (status != null ? $", {status.DisplayName} while inside" : "") + "; they walk out";
}

/// <summary>A blob in an arc from hand to floor; what it does when it lands is the caller's.</summary>
public class LobbedBlob : MonoBehaviour
{
    private Vector3 _from, _to; private float _start, _seconds; private Action<Vector3> _onLand;

    public static void Throw(Vector3 from, Vector3 to, float seconds, Color color, float size, Action<Vector3> onLand)
    {
        var sr = ShapeSprites.Floating("TarBlob", ShapeSprites.Splat(3), from, size, new Color(color.r, color.g, color.b, 1f), 130);
        var b = sr.gameObject.AddComponent<LobbedBlob>();
        b._from = from; b._to = to; b._start = Time.time; b._seconds = Mathf.Max(0.05f, seconds); b._onLand = onLand;
    }

    private void Update()
    {
        float t = Mathf.Clamp01((Time.time - _start) / _seconds);
        Vector3 p = Vector3.Lerp(_from, _to, t);
        p.y += Mathf.Sin(t * Mathf.PI) * 2.2f;   // the arc
        transform.position = p;
        transform.Rotate(0f, 0f, 240f * Time.deltaTime);
        if (t < 1f) return;
        _onLand?.Invoke(_to);
        Destroy(gameObject);
    }
}

/// <summary>A live pool. The AI asks <see cref="HostileAt"/> before it moves anywhere else.</summary>
public class Zone : MonoBehaviour
{
    public static readonly List<Zone> All = new List<Zone>();

    public Entity Owner { get; private set; }
    public float Radius { get; private set; }
    private float _born, _until, _dps, _nextTick, _statusDuration, _nextBubble; private Status _status; private Color _stain;
    private Vector3 _fullScale; private SpriteRenderer _sr; private Color _color;
    private readonly HashSet<Entity> _stained = new HashSet<Entity>();
    private const float Tick = 0.5f, Spread = 0.25f, Fade = 1f;

    /// <summary>What resists the ticks; the effect that made the pool says.</summary>
    public DamageType DamageType = DamageType.Physical;

    public void Begin(Entity owner, float radius, float seconds, float damagePerSecond, Status status, float statusDuration, Color stain)
    {
        Owner = owner; Radius = radius; _born = Time.time; _until = Time.time + seconds; _dps = damagePerSecond; _status = status; _statusDuration = statusDuration; _stain = stain;
        _nextTick = Time.time + Tick; _nextBubble = Time.time + 0.3f;
        _sr = GetComponent<SpriteRenderer>(); _color = _sr != null ? _sr.color : Color.black;
        _fullScale = transform.localScale;
        transform.localScale = _fullScale * 0.2f;
        // The splash of the landing: the blob's own shape, larger and thinner, fading fast.
        var splash = ShapeSprites.OnFloor("TarSplash", transform.position, radius * 1.4f, new Color(_color.r, _color.g, _color.b, 0.45f), ShapeSprites.Splat(5));
        splash.gameObject.AddComponent<FadeAway>().Begin(0.35f);
    }

    private void OnEnable() { All.Add(this); }
    private void OnDisable() { All.Remove(this); foreach (var e in _stained) Unstain(e); _stained.Clear(); }

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
        float age = Time.time - _born; float left = _until - Time.time;
        if (left <= 0f || Owner == null) { Destroy(gameObject); return; }

        // Spreads out from the landing, sits, then sinks away over its last second.
        float grow = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(age / Spread));
        float sink = Mathf.Clamp01(left / Fade);
        transform.localScale = _fullScale * Mathf.Lerp(0.2f, 1f, grow) * Mathf.Lerp(0.6f, 1f, sink);
        if (_sr != null) _sr.color = new Color(_color.r, _color.g, _color.b, _color.a * sink);

        // Bubbles: a small dark disc that rises a little, grows and pops.
        if (Time.time >= _nextBubble && sink > 0.5f)
        {
            _nextBubble = Time.time + UnityEngine.Random.Range(0.25f, 0.5f);
            Vector2 spot = UnityEngine.Random.insideUnitCircle * Radius * 0.7f;
            var bubble = ShapeSprites.Floating("TarBubble", ShapeSprites.Disc(), transform.position + new Vector3(spot.x, spot.y * 0.55f, 0f), 0.12f, new Color(_color.r + 0.12f, _color.g + 0.08f, _color.b + 0.05f, 0.9f), -395);
            bubble.gameObject.AddComponent<Bubble>();
        }

        // Who is in it: stained while inside, clean again on the way out.
        var inside = ShapeSprites.EnemiesWithin(Owner, transform.position, Radius);
        foreach (var e in inside) if (_stained.Add(e)) Stain(e);
        if (_stained.Count > inside.Count)
        {
            var gone = new List<Entity>();
            foreach (var e in _stained) if (e == null || e.isDead || !inside.Contains(e)) gone.Add(e);
            foreach (var e in gone) { _stained.Remove(e); Unstain(e); }
        }

        if (Time.time < _nextTick) return;
        _nextTick += Tick;
        foreach (var v in inside)
        {
            v.TakeDamage(_dps * Tick, Owner, quiet: true, type: DamageType);   // it ticks; the stain is the feedback
            if (_status != null && v.Statuses != null) v.Statuses.Apply(_status, _statusDuration, Owner);
        }
    }

    private void Stain(Entity e) { if (e != null && e.HitFeedback != null) e.HitFeedback.SetTint(_stain); }
    private void Unstain(Entity e) { if (e != null && e.HitFeedback != null) e.HitFeedback.SetTint(Color.white); }
}

/// <summary>A tar bubble: rises a little, swells, and is gone.</summary>
public class Bubble : MonoBehaviour
{
    private float _start = -1f; private SpriteRenderer _sr; private Color _color; private float _size;
    private void Update()
    {
        if (_start < 0f) { _start = Time.time; _sr = GetComponent<SpriteRenderer>(); _color = _sr != null ? _sr.color : Color.black; _size = transform.localScale.x; }
        float t = (Time.time - _start) / 0.6f;
        if (t >= 1f) { Destroy(gameObject); return; }
        transform.position += Vector3.up * 0.25f * Time.deltaTime;
        transform.localScale = Vector3.one * _size * (1f + t * 0.8f);
        if (_sr != null) _sr.color = new Color(_color.r, _color.g, _color.b, _color.a * (1f - t * t));
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
