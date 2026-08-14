using UnityEngine;

/// <summary>
/// Per-character hit feedback: colour flash, positional shake, and squash.
///
/// Replaces HeroEditor's <c>HitAsRed</c>, whose flash colour is hard-coded to <see cref="Color.red"/>
/// inside a third-party script we don't want to fork. Everything here is configurable so
/// combinations can be A/B tested — same approach as <see cref="ResourceBar.BarEffects"/>.
///
/// The shake moves the <b>visual child</b> (the Animator's transform), never the entity root, so it
/// can't fight movement from <c>CombatAI</c> or <c>Knockback</c>.
/// </summary>
public class HitFeedback : MonoBehaviour
{
    /// <summary>All the tunables. Lives on the shared CombatFeelSettings asset by default.</summary>
    [System.Serializable]
    public class Settings
    {
        [Header("Flash")]
        public bool enableFlash = true;
        [Tooltip("HeroEditor's built-in flash was red. White reads as a cleaner impact on most " +
                 "sprites — red can be muddy on already-warm characters.")]
        public Color flashColor = Color.white;
        public float flashDuration = 0.08f;

        [Header("Shake")]
        public bool enableShake = true;
        [Tooltip("Peak offset in world units, at full damage scaling.")]
        public float shakeAmount = 0.09f;
        public float shakeDuration = 0.16f;
        [Tooltip("Scales with the fraction of max health lost — chip hits barely move, big hits jolt.")]
        public float shakeDamageScale = 4f;
        [Tooltip("Shake sideways only. Vertical shake on a grounded character reads as floating.")]
        public bool horizontalOnly = true;

        [Header("Squash")]
        [Tooltip("HeroEditor's spring scale-punch (Character.HitAsScale / Monster.Spring).")]
        public bool enableSquash = true;
    }

    [Tooltip("Tick to ignore the global CombatFeelSettings asset and use the values below — handy " +
             "for side-by-side tests, e.g. allies flashing white while enemies flash red.")]
    public bool overrideGlobal = false;
    public Settings localSettings = new Settings();

    /// <summary>Live reference, so edits to the shared asset apply immediately — even mid-play.</summary>
    private Settings S => overrideGlobal ? localSettings : CombatFeelSettings.Active.hitFeedback;

    private Entity _entity;
    private Transform _flashRoot;   // where sprite renderers are gathered — may be the entity root
    private Transform _shakeRoot;   // MUST be a child; null disables shake
    private SpriteRenderer[] _renderers;
    private MaterialPropertyBlock _mpb;
    private static readonly int FlashColorId = Shader.PropertyToID("_FlashColor");
    private static readonly int FlashAmountId = Shader.PropertyToID("_FlashAmount");

    private float _flashTimer, _flashDuration;
    private Color _flashColor = Color.white;   // held, so a death flash isn't recoloured mid-decay
    private float _shakeTimer, _shakeMagnitude, _shakeDuration;
    private Vector3 _restLocalPos;

    public void Initialize(Entity entity)
    {
        _entity = entity;
        _flashRoot = transform;

        // Prefer the Animator's transform ("Animation" on characters, sometimes a "Body" child on
        // monsters). On some prefabs the Animator sits on the ROOT — shaking that would overwrite
        // the position CombatAI/Knockback are driving and snap the unit back to spawn, so in that
        // case fall back to a child, and disable shake entirely if there isn't one.
        Transform candidate = null;
        if (entity.isCharacter && entity.character != null && entity.character.Animator != null)
            candidate = entity.character.Animator.transform;
        else if (entity.monster != null && entity.monster.Animator != null)
            candidate = entity.monster.Animator.transform;

        if (candidate == null || candidate == transform)
            candidate = transform.childCount > 0 ? transform.GetChild(0) : null;

        _shakeRoot = (candidate != null && candidate != transform) ? candidate : null;
        if (_shakeRoot != null) _restLocalPos = _shakeRoot.localPosition;
    }

    /// <summary>Play the configured feedback. <paramref name="damageFraction"/> is damage / maxHealth.</summary>
    public void Play(float damageFraction)
    {
        if (S.enableSquash && _entity != null) _entity.HitScale();

        if (S.enableFlash) Flash(S.flashColor, S.flashDuration);

        if (S.enableShake && S.shakeAmount > 0f && _shakeRoot != null)
        {
            float mag = S.shakeAmount * Mathf.Clamp01(damageFraction * S.shakeDamageScale);
            // Take the STRONGER of the two — a chip hit must never cancel a heavy hit's shake.
            _shakeMagnitude = Mathf.Max(_shakeMagnitude, mag);
            _shakeTimer = Mathf.Max(_shakeTimer, S.shakeDuration);
            _shakeDuration = Mathf.Max(_shakeDuration, S.shakeDuration);
        }
    }

    /// <summary>
    /// Spike the colour flash to full and let it decay. Exposed so other systems can borrow the
    /// renderer cache and the shader plumbing — <see cref="DeathFeedback"/> uses it for the longer,
    /// louder flash on a killing blow.
    /// </summary>
    public void Flash(Color color, float duration)
    {
        CacheRenderers();
        // Re-spike to full. Because the flash DECAYS (see Update), overlapping hits read as
        // distinct pulses instead of fusing into one permanently-white blob.
        _flashColor = color;
        _flashDuration = Mathf.Max(0.0001f, duration);
        _flashTimer = _flashDuration;
        SetFlash(1f, _flashColor);
    }

    /// <summary>
    /// Grab the sprite renderers. Includes inactive ones so equipment that is hidden at cache time
    /// still flashes once shown, and re-caches when the count changes (equipment swaps).
    /// </summary>
    private void CacheRenderers()
    {
        Transform root = _flashRoot != null ? _flashRoot : transform;
        var found = root.GetComponentsInChildren<SpriteRenderer>(true);
        if (_renderers != null && _renderers.Length == found.Length) return;
        _renderers = found;

        // The flash lives in the shader, so the renderers must be using it. One shared material
        // keeps batching intact — the per-renderer flash amount rides on a MaterialPropertyBlock.
        Material mat = FlashMaterial;
        if (mat == null) return;
        for (int i = 0; i < _renderers.Length; i++)
            if (_renderers[i] != null && _renderers[i].sharedMaterial != mat)
                _renderers[i].sharedMaterial = mat;
    }

    private static Material _flashMaterial;
    private static bool _flashMaterialMissing;

    /// <summary>Shared Sprites/Flash material, loaded from Resources once.</summary>
    private static Material FlashMaterial
    {
        get
        {
            if (_flashMaterial == null && !_flashMaterialMissing)
            {
                _flashMaterial = Resources.Load<Material>("SpriteFlash");
                if (_flashMaterial == null)
                {
                    _flashMaterialMissing = true;
                    Debug.LogWarning("[HitFeedback] Resources/SpriteFlash.mat not found — the flash " +
                                     "will do nothing, because SpriteRenderer.color cannot brighten.");
                }
            }
            return _flashMaterial;
        }
    }

    /// <summary>
    /// Drive the flash through the shader rather than SpriteRenderer.color.
    /// Tinting cannot brighten — white multiplies to a no-op — so a white flash needs
    /// _FlashAmount on the Sprites/Flash shader. A MaterialPropertyBlock keeps this
    /// batching-friendly and avoids leaking material instances.
    /// </summary>
    private void SetFlash(float amount, Color color)
    {
        if (_renderers == null) return;
        if (_mpb == null) _mpb = new MaterialPropertyBlock();

        for (int i = 0; i < _renderers.Length; i++)
        {
            var r = _renderers[i];
            if (r == null) continue;
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(FlashColorId, color);
            _mpb.SetFloat(FlashAmountId, amount);
            r.SetPropertyBlock(_mpb);
        }
    }
    private void Update()
    {
        if (_flashTimer > 0f)
        {
            _flashTimer -= Time.deltaTime;
            // Fade the flash out rather than holding it solid, so a second hit visibly re-spikes.
            SetFlash(_flashTimer > 0f ? _flashTimer / _flashDuration : 0f, _flashColor);
        }

        if (_shakeRoot == null) return;

        if (_shakeTimer > 0f)
        {
            _shakeTimer -= Time.deltaTime;
            float falloff = Mathf.Clamp01(_shakeTimer / Mathf.Max(0.0001f, _shakeDuration));
            float x = Mathf.Sin(Time.time * 110f) * _shakeMagnitude * falloff;
            float y = S.horizontalOnly ? 0f : Mathf.Cos(Time.time * 97f) * _shakeMagnitude * falloff * 0.6f;
            _shakeRoot.localPosition = _restLocalPos + new Vector3(x, y, 0f);
        }
        else if (_shakeMagnitude != 0f || _shakeRoot.localPosition != _restLocalPos)
        {
            _shakeMagnitude = 0f;   // clear, or the next small hit would inherit this peak
            _shakeRoot.localPosition = _restLocalPos;
        }
    }
}
