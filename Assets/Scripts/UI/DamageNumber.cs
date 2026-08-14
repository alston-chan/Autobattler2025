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
        transform.rotation = Quaternion.identity;   // never inherit an entity's flipped facing
        _velocity = new Vector3(Random.Range(-s.driftX, s.driftX), s.riseSpeed, 0f);

        bool crit = info.isCrit;
        _color = crit ? s.critColor : s.normalColor;
        _popScale = crit ? Mathf.Max(1f, s.critPopScale) : 1f;

        int shown = Mathf.Max(1, Mathf.RoundToInt(info.amount));
        _tmp.text = crit ? shown + s.critSuffix : shown.ToString();
        _tmp.fontSize = crit ? s.fontSize * s.critSizeMultiplier : s.fontSize;
        _tmp.color = _color;

        // Dark outline for readability on any terrain. Set through TMP's own properties rather than
        // poking the material directly: the setter recomputes the SDF scale ratios, without which the
        // outline width is read in raw distance-field units and floods the glyph into a solid block.
        // These properties instantiate a per-object material, which also isolates the outline from
        // other TMP text sharing the font — the pool is small, so the lost batching is irrelevant.
        _tmp.outlineWidth = s.outline ? s.outlineWidth : 0f;
        _tmp.outlineColor = s.outlineColor;

        transform.localScale = Vector3.one * _popScale;
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

        // Crit scale-punch settles back to 1 over the first third.
        if (_popScale > 1f)
        {
            float k = Mathf.Clamp01(_age / (_life * 0.3f));
            transform.localScale = Vector3.one * Mathf.Lerp(_popScale, 1f, k);
        }

        // Fade the tail end.
        if (t > _s.fadeStart)
        {
            float a = 1f - (t - _s.fadeStart) / (1f - _s.fadeStart);
            Color c = _color;
            c.a = a;
            _tmp.color = c;
        }
    }
}
