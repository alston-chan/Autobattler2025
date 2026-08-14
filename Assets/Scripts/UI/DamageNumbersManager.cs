using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Spawns floating damage numbers, driven by <see cref="Health.OnDamaged"/> via
/// <see cref="EntityRegistry"/> — the same event-driven pattern as <see cref="UnitBarsManager"/>.
/// Juice subscribes to events; it never lives inside combat logic.
///
/// The readability rule (Docs/Juice.md): visual weight scales with how rare the event is. Routine
/// hits are small and pale; crits are big, saturated, and pop. Numbers are punctuation, not a combat
/// log — they rise, fade, and recycle within a fraction of a second.
///
/// Numbers are built in code (world-space <see cref="TextMeshPro"/>) and pooled, so there is no
/// prefab to wire and no per-fight allocation. New prefabs need no setup beyond an <see cref="Entity"/>.
/// </summary>
public class DamageNumbersManager : MonoBehaviour
{
    /// <summary>All the tunables. Lives on the shared CombatFeelSettings asset by default.</summary>
    [Serializable]
    public class Settings
    {
        public bool enabled = true;

        [Header("Placement")]
        [Tooltip("Offset from the entity origin. Y sits just above the head — close enough to read " +
                 "as coming off the unit, not floating in the air above it.")]
        public Vector3 spawnOffset = new Vector3(0f, 1.6f, 0f);
        [Tooltip("Random horizontal spread so rapid hits fan out instead of stacking illegibly.")]
        public float spawnJitterX = 0.3f;

        [Header("Normal hit")]
        public float fontSize = 3.5f;
        public Color normalColor = new Color(1f, 1f, 1f, 1f);

        [Header("Crit")]
        [Tooltip("Crits must dominate — this is the moment a build 'pays off'.")]
        public Color critColor = new Color(1f, 0.75f, 0.2f, 1f);
        public float critSizeMultiplier = 1.6f;
        [Tooltip("Extra scale-punch on spawn that settles back to 1. Sells the impact.")]
        public float critPopScale = 1.45f;
        [Tooltip("Appended to crit numbers.")]
        public string critSuffix = "!";

        [Header("Outline")]
        [Tooltip("Dark edge so a number reads on any terrain — the battlefield is sage green — and so " +
                 "the face colour is later free to encode damage type.")]
        public bool outline = true;
        public Color outlineColor = new Color(0f, 0f, 0f, 1f);
        [Tooltip("SDF outline thickness, 0..1. This font's atlas caps how bold it can get, so it needs " +
                 "a fairly high value to read — ~0.2 is nearly invisible, ~0.5 is a clear edge.")]
        [Range(0f, 1f)] public float outlineWidth = 0.5f;

        [Header("Motion")]
        public float lifetime = 0.7f;
        [Tooltip("Initial upward speed, world units/sec.")]
        public float riseSpeed = 2.4f;
        [Tooltip("How fast the rise decelerates — a small arc reads better than a constant slide.")]
        public float riseDamping = 3.5f;
        [Tooltip("Sideways drift, so numbers don't travel in a rigid vertical line.")]
        public float driftX = 0.4f;
        [Tooltip("Fraction of lifetime before the number begins to fade out.")]
        [Range(0f, 1f)] public float fadeStart = 0.5f;

        [Header("Pool")]
        [Tooltip("Concurrent numbers cap. Exceeding it recycles the oldest rather than allocating.")]
        public int poolSize = 32;
    }

    [Tooltip("Tick to ignore the global CombatFeelSettings asset and use the values below.")]
    public bool overrideGlobal = false;
    public Settings localSettings = new Settings();

    private Settings S => overrideGlobal ? localSettings : CombatFeelSettings.Active.damageNumbers;

    private readonly Dictionary<Entity, Action<DamageInfo>> _handlers = new Dictionary<Entity, Action<DamageInfo>>();
    private readonly Queue<DamageNumber> _pool = new Queue<DamageNumber>();
    private readonly LinkedList<DamageNumber> _live = new LinkedList<DamageNumber>();
    private Transform _poolParent;
    private TMP_FontAsset _font;

    private void Awake()
    {
        // A root container (no parent) so pooled numbers never inherit a moved/scaled transform.
        _poolParent = new GameObject("Damage Numbers").transform;

        // AddComponent<TextMeshPro> auto-assigns the default font, but grab it explicitly so a
        // missing TMP_Settings can't leave numbers invisible.
        _font = TMP_Settings.defaultFontAsset != null
            ? TMP_Settings.defaultFontAsset
            : Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
    }

    private void OnEnable()
    {
        EntityRegistry.OnRegistered += Hook;
        EntityRegistry.OnUnregistered += Unhook;
        foreach (var e in EntityRegistry.All) Hook(e);
    }

    private void OnDisable()
    {
        // EntityRegistry is static — an un-cleared subscription would leak across scene loads.
        EntityRegistry.OnRegistered -= Hook;
        EntityRegistry.OnUnregistered -= Unhook;
        foreach (var kv in _handlers)
            if (kv.Key != null && kv.Key.Health != null) kv.Key.Health.OnDamaged -= kv.Value;
        _handlers.Clear();
    }

    private void Hook(Entity e)
    {
        if (e == null || e.Health == null || _handlers.ContainsKey(e)) return;
        // Capture the entity so the handler knows where to spawn without DamageInfo carrying it.
        Action<DamageInfo> handler = info => Spawn(e, info);
        e.Health.OnDamaged += handler;
        _handlers[e] = handler;
    }

    private void Unhook(Entity e)
    {
        if (e == null || !_handlers.TryGetValue(e, out var handler)) return;
        if (e.Health != null) e.Health.OnDamaged -= handler;
        _handlers.Remove(e);
    }

    private void Spawn(Entity entity, DamageInfo info)
    {
        var s = S;
        if (!s.enabled || entity == null || info.amount <= 0f) return;

        DamageNumber number = Rent(s);
        Vector3 pos = entity.transform.position + s.spawnOffset;
        number.Play(pos, info, s);
    }

    private DamageNumber Rent(Settings s)
    {
        DamageNumber number;
        if (_pool.Count > 0)
        {
            number = _pool.Dequeue();
        }
        else if (_live.Count >= Mathf.Max(1, s.poolSize))
        {
            // Cap reached with nothing free — steal the oldest live number.
            number = _live.First.Value;
            _live.RemoveFirst();
        }
        else
        {
            number = Create();
        }

        _live.AddLast(number);
        return number;
    }

    private DamageNumber Create()
    {
        var go = new GameObject("DamageNumber");
        var tmp = go.AddComponent<TextMeshPro>();
        if (_font != null) tmp.font = _font;
        var number = go.AddComponent<DamageNumber>();
        number.Init(this, tmp);
        return number;
    }

    /// <summary>Called by a <see cref="DamageNumber"/> when its lifetime ends.</summary>
    public void Recycle(DamageNumber number)
    {
        _live.Remove(number);
        number.transform.SetParent(_poolParent, false);
        number.gameObject.SetActive(false);
        _pool.Enqueue(number);
    }
}
