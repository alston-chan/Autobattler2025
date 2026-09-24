using TMPro;
using UnityEngine;

/// <summary>
/// A single floating damage number. Built and pooled by <see cref="DamageNumbersManager"/> — never
/// placed in a scene by hand. Rises in a short arc, fades, and hands itself back to the pool.
///
/// World-space <see cref="TextMeshPro"/> (not the UGUI variant) so it lives in the battlefield next
/// to the sprites, and its mesh renderer is forced to the top of the sprite draw order so a number
/// is never swallowed by a character or a bar.
/// </summary>
public class DamageNumber : MonoBehaviour
{
    private DamageNumbersManager _pool;
    private TextMeshPro _tmp;
    private MeshRenderer _renderer;

    private DamageNumbersManager.Settings _s;
    private float _age, _life;
    private Vector3 _velocity;
    private Color _color;
    private float _popScale;

    /// <summary>Called once by the pool right after construction.</summary>
    public void Init(DamageNumbersManager pool, TextMeshPro tmp)
    {
        _pool = pool;
        _tmp = tmp;
        _renderer = GetComponent<MeshRenderer>();

        _tmp.alignment = TextAlignmentOptions.Center;
        _tmp.enableWordWrapping = false;
        _tmp.raycastTarget = false;

        // Draw over sprites. Sprites sit on the Default sorting layer; a large order wins the tie.
        if (_renderer != null) _renderer.sortingOrder = 32000;
    }

    /// <summary>Configure and launch. <paramref name="worldPos"/> is the pre-jitter spawn point.</summary>
    public void Play(Vector3 worldPos, DamageInfo info, DamageNumbersManager.Settings s)
    {
        _s = s;
        _age = 0f;
        _life = Mathf.Max(0.01f, s.lifetime);

        transform.position = worldPos + new Vector3(Random.Range(-s.spawnJitterX, s.spawnJitterX), 0f, 0f);
        // Never inherit an entity's flipped facing; a small random tilt so a flurry doesn't line up.
        transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(-s.tiltRange, s.tiltRange));
        _velocity = new Vector3(Random.Range(-s.driftX, s.driftX), s.riseSpeed, 0f);

        bool crit = info.isCrit;
        bool slam = info.kind == DamageKind.Slam;
        _color = slam ? s.slamColor : crit ? s.critColor : s.normalColor;
        _popScale = crit ? Mathf.Max(1f, s.critPopScale) : 1f;

        // The font first: every material setting below lands on the font's material instance.
        if (s.font != null && _tmp.font != s.font) _tmp.font = s.font;

        int shown = Mathf.Max(1, Mathf.RoundToInt(info.amount));
        _tmp.text = slam ? s.slamPrefix + shown : crit ? shown + s.critSuffix : shown.ToString();
        _tmp.fontSize = crit ? s.fontSize * s.critSizeMultiplier : slam ? s.fontSize * s.slamSizeMultiplier : s.fontSize;

        // Painted lettering: the colour at the top, shading darker to the bottom. The face colour
        // stays white so the gradient shows as authored, and fading only has to touch its alpha.
        Color bottom = Color.Lerp(_color, Color.black, s.gradientDarken);
        bottom.a = _color.a;
        _tmp.enableVertexGradient = true;
        _tmp.colorGradient = new VertexGradient(_color, _color, bottom, bottom);
        _tmp.color = Color.white;

        // Dark outline for readability on any terrain. Set through TMP's own properties rather than
        // poking the material directly: the setter recomputes the SDF scale ratios, without which the
        // outline width is read in raw distance-field units and floods the glyph into a solid block.
        // These properties instantiate a per-object material, which also isolates the outline from
        // other TMP text sharing the font — the pool is small, so the lost batching is irrelevant.
        _tmp.outlineWidth = s.outline ? s.outlineWidth : 0f;
        _tmp.outlineColor = s.outlineColor;

        // A hard drop shadow (TMP's underlay) behind the outline, on the same per-object material.
        var mat = _tmp.fontMaterial;
        if (s.shadow)
        {
            mat.EnableKeyword(ShaderUtilities.Keyword_Underlay);
            mat.SetColor(ShaderUtilities.ID_UnderlayColor, s.shadowColor);
            mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, s.shadowOffset.x);
            mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, s.shadowOffset.y);
            mat.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0f);
        }
        else
        {
            mat.DisableKeyword(ShaderUtilities.Keyword_Underlay);
        }
        mat.SetFloat(ShaderUtilities.ID_FaceDilate, s.faceDilate);
        // Dilate and the shadow grow the glyph past its quad; without new padding their edges clip.
        _tmp.UpdateMeshPadding();

        transform.localScale = Vector3.one * (_popScale * PopIn(0f));
        gameObject.SetActive(true);

        // A runtime-created / pooled 3D TextMeshPro only marks itself dirty when text changes; the
        // actual mesh is built later by TMP's update manager, which never ran for these (verts=0,
        // nothing drawn). Force the build now so the number is visible the frame it spawns.
        _tmp.ForceMeshUpdate();
    }

    private void Update()
    {
        _age += Time.deltaTime;
        if (_age >= _life)
        {
            _pool.Recycle(this);
            return;
        }

        float t = _age / _life;

        // Rise in a decelerating arc — reads better than a constant slide.
        transform.position += _velocity * Time.deltaTime;
        _velocity.y = Mathf.Max(0f, _velocity.y - _s.riseDamping * Time.deltaTime);

        // Every number pops in with a cartoon overshoot; a crit's extra punch settles back to 1 over
        // the first third; the tail end shrinks as it fades.
        float scale = PopIn(_age);
        if (_popScale > 1f)
            scale *= Mathf.Lerp(_popScale, 1f, Mathf.Clamp01(_age / (_life * 0.3f)));
        if (t > _s.fadeStart)
        {
            float k = (t - _s.fadeStart) / (1f - _s.fadeStart);
            scale *= Mathf.Lerp(1f, _s.shrinkTo, k);
            _tmp.color = new Color(1f, 1f, 1f, 1f - k);   // the gradient carries the colour
        }
        transform.localScale = Vector3.one * scale;
    }

    /// <summary>
    /// Ease-out-back from a small start to full size over <c>popDuration</c>: overshoots, then
    /// settles. After the pop it is simply 1.
    /// </summary>
    private float PopIn(float age)
    {
        float d = Mathf.Max(0.0001f, _s.popDuration);
        if (age >= d) return 1f;
        float x = age / d - 1f;
        float c1 = Mathf.Max(0f, _s.popOvershoot), c3 = c1 + 1f;
        float eased = 1f + c3 * x * x * x + c1 * x * x;
        return Mathf.Lerp(0.25f, 1f, eased);
    }
}
