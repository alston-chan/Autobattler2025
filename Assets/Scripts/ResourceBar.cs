using System;
using UnityEngine;

/// <summary>
/// A floating resource (health / mana) bar.
///
/// The fill snaps instantly so a hit registers, while a lagging "chip" bar behind it drains a
/// moment later — the fighting-game convention, showing *what you have* and *what you just lost*
/// in one read.
///
/// Every effect is individually toggleable via <see cref="BarEffects"/> so combinations can be
/// A/B-tested; <see cref="UnitBarsManager"/> pushes a shared settings object onto each bar it spawns,
/// giving one Inspector to tune them all.
/// </summary>
public class ResourceBar : MonoBehaviour
{
    /// <summary>Per-bar juice settings. Authored on <see cref="UnitBarsManager"/>.</summary>
    [Serializable]
    public class BarEffects
    {
        [Header("Chip trail")]
        [Tooltip("A lagging bar showing damage just taken.")]
        public bool chipTrail = true;
        [Tooltip("Seconds the chip holds before draining.")]
        public float chipDelay = 0.25f;
        [Tooltip("Bar-widths per second.")]
        public float chipDrainSpeed = 1.2f;
        [Tooltip("Amber by default — must contrast with BOTH the green ally fill and the red enemy " +
                 "fill. A red chip is invisible on an enemy bar.")]
        public Color chipColor = new Color(1f, 0.82f, 0.25f, 1f);

        [Header("Hit flash")]
        public bool hitFlash = true;
        public float flashDuration = 0.1f;
        public Color flashColor = Color.white;

        [Header("Shake")]
        public bool shake = true;
        [Tooltip("Peak horizontal shake, world units.")]
        public float shakeAmount = 0.06f;
        public float shakeDuration = 0.14f;
        [Tooltip("Scales shake by the fraction of the pool lost in one hit.")]
        public float shakeDamageScale = 3f;

        [Header("Segments")]
        [Tooltip("A tick every N% of the pool (25 = quarters). 0 disables.")]
        [Range(0f, 50f)] public float segmentPercent = 0f;
        public Color segmentColor = new Color(0f, 0f, 0f, 0.55f);
    }

    public Entity entity;

    [Tooltip("World offset from the entity. Zero falls back to Entity.healthBarOffset.")]
    public Vector3 offset;

    [Tooltip("Overridden by UnitBarsManager when the bar is spawned.")]
    public BarEffects effects = new BarEffects();

    // Names must stay `bar` / `barSR` — the prefab serialises these references.
    [SerializeField] private Transform bar;
    [SerializeField] private SpriteRenderer barSR;

    // ── runtime ──
    private Transform _chipBar;
    private SpriteRenderer _chipSR;
    private float _fill = 1f, _chip = 1f, _chipHold;
    private float _flashTimer, _shakeTimer, _shakeMagnitude;
    private Color _baseColor = Color.green;
    private bool _built, _hadEntity;
    private bool _initialised;   // the first SetSize is a starting value, not a hit

    private void Awake() => Build();

    /// <summary>Push settings in before the bar builds itself. Safe to call any time.</summary>
    public void Apply(BarEffects settings)
    {
        if (settings != null) effects = settings;
        Build();
        if (_chipSR != null) _chipSR.color = effects.chipColor;
        if (_chipBar != null) _chipBar.gameObject.SetActive(effects.chipTrail);
    }

    /// <summary>World height of the visible bar, for stacking bars flush against each other.</summary>
    public float VisualHeight => barSR != null ? barSR.bounds.size.y : 0.1f;

    private void Build()
    {
        if (_built || bar == null || barSR == null) return;
        _built = true;

        _baseColor = barSR.color;

        // Chip bar: a clone of the fill sitting directly behind it.
        _chipBar = Instantiate(bar, bar.parent);
        _chipBar.name = "Chip Bar";
        _chipBar.localPosition = bar.localPosition;
        _chipSR = _chipBar.GetComponentInChildren<SpriteRenderer>();
        if (_chipSR != null)
        {
            _chipSR.color = effects.chipColor;
            _chipSR.sortingOrder = barSR.sortingOrder;   // chip takes the fill's old layer…
        }
        barSR.sortingOrder += 1;                          // …and the fill moves in front
        _chipBar.gameObject.SetActive(effects.chipTrail);

        BuildSegments();
    }

    private void BuildSegments()
    {
        if (effects.segmentPercent <= 0f || barSR == null) return;

        int count = Mathf.FloorToInt(100f / effects.segmentPercent) - 1;   // interior ticks only
        for (int i = 1; i <= count; i++)
        {
            var tick = new GameObject($"Tick{i}");
            tick.transform.SetParent(bar.parent, false);
            var sr = tick.AddComponent<SpriteRenderer>();
            sr.sprite = barSR.sprite;
            sr.color = effects.segmentColor;
            sr.sortingLayerID = barSR.sortingLayerID;
            sr.sortingOrder = barSR.sortingOrder + 1;

            float t = i * (effects.segmentPercent / 100f);
            tick.transform.localPosition = bar.localPosition + new Vector3(t, 0f, -0.01f);
            tick.transform.localScale = new Vector3(0.012f, 1f, 1f);
        }
    }

    /// <summary>Set fill 0..1. A drop triggers whichever of chip / flash / shake are enabled.</summary>
    public void SetSize(float sizeNormalized)
    {
        Build();
        float next = Mathf.Clamp01(sizeNormalized);

        // First call establishes the starting value — snap to it silently. Without this a bar that
        // starts empty (mana) or part-full (a wounded summon) would read as a full-bar hit and
        // play the whole chip drain on spawn.
        if (!_initialised)
        {
            _initialised = true;
            _fill = _chip = next;
            ApplyFill();
            return;
        }

        float lost = _fill - next;

        if (lost > 0.0001f)
        {
            if (effects.chipTrail) _chipHold = effects.chipDelay;
            else _chip = next;

            if (effects.hitFlash) _flashTimer = effects.flashDuration;

            if (effects.shake && effects.shakeAmount > 0f)
            {
                // Take the stronger — a chip hit landing during a big hit's shake must not weaken it.
                _shakeTimer = Mathf.Max(_shakeTimer, effects.shakeDuration);
                _shakeMagnitude = Mathf.Max(_shakeMagnitude,
                    effects.shakeAmount * Mathf.Clamp01(lost * effects.shakeDamageScale));
            }
        }
        else if (next > _fill)
        {
            _chip = next;   // gains never trail
        }

        _fill = next;
        ApplyFill();
    }

    public void SetColor(Color color)
    {
        _baseColor = color;
        if (barSR != null && _flashTimer <= 0f) barSR.color = color;
    }

    private void Update()
    {
        if (_chip > _fill)
        {
            if (_chipHold > 0f) _chipHold -= Time.deltaTime;
            else
            {
                _chip = Mathf.Max(_fill, _chip - effects.chipDrainSpeed * Time.deltaTime);
                ApplyFill();
            }
        }

        if (_flashTimer > 0f)
        {
            _flashTimer -= Time.deltaTime;
            if (barSR != null) barSR.color = _flashTimer > 0f ? effects.flashColor : _baseColor;
        }

        if (_shakeTimer > 0f)
        {
            _shakeTimer -= Time.deltaTime;
            if (_shakeTimer <= 0f) _shakeMagnitude = 0f;   // clear, or the next small hit inherits this peak
        }
    }

    private void ApplyFill()
    {
        if (bar != null) bar.localScale = new Vector3(_fill, 1f, 1f);
        if (_chipBar != null) _chipBar.localScale = new Vector3(Mathf.Max(_chip, _fill), 1f, 1f);
    }

    private void LateUpdate()
    {
        // Self-clean: once the owner is gone the bar has no reason to exist.
        if (entity == null)
        {
            if (_hadEntity) Destroy(gameObject);
            return;
        }
        _hadEntity = true;

        Vector3 off = offset == Vector3.zero ? entity.healthBarOffset : offset;
        Vector3 pos = entity.transform.position + off;

        if (_shakeTimer > 0f)
        {
            float falloff = _shakeTimer / Mathf.Max(0.0001f, effects.shakeDuration);
            pos.x += Mathf.Sin(Time.time * 90f) * _shakeMagnitude * falloff;
        }

        transform.position = pos;
    }
}
