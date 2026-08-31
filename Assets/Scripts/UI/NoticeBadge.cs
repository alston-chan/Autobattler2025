using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The little dot that says "something happened here" — on an item slot, and on the hero card of
/// whoever is carrying it.
///
/// Resonance advances quietly in the middle of a fight, so without a mark the player finds out an
/// item crossed a tier by noticing a number changed, and finds out it is ready to engrave by
/// happening to open the right window. The badge turns both into something you can see from the
/// board.
///
/// Two states, because the two events ask different things of the player. A tier-up has already
/// applied itself and is only news; being ready to engrave is a decision waiting to be made, so it
/// pulses and the other does not.
/// </summary>
public class NoticeBadge : MonoBehaviour
{
    private static readonly Color TierUpColor = new Color(1f, 0.82f, 0.28f, 1f);
    private static readonly Color ReadyColor = new Color(0.45f, 1f, 0.55f, 1f);

    [Tooltip("Seconds for one full pulse of an actionable badge.")]
    public float pulseSeconds = 1.1f;

    private Image _image;
    private bool _pulse;
    private static Sprite _dot;

    /// <summary>Add a badge dot in the top-right corner of a UI element. Starts hidden.</summary>
    public static NoticeBadge AttachTo(RectTransform host, float size, Vector2 inset)
    {
        if (host == null) return null;

        var go = new GameObject("NoticeBadge", typeof(RectTransform));
        go.transform.SetParent(host, false);

        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.sizeDelta = new Vector2(size, size);
        rect.anchoredPosition = inset;

        var image = go.AddComponent<Image>();
        image.sprite = Dot();
        image.raycastTarget = false;   // never eat a click meant for the slot underneath

        var badge = go.AddComponent<NoticeBadge>();
        badge._image = image;
        badge.Show(ResonanceNotice.None);
        return badge;
    }

    /// <summary>Set what this badge is reporting. <see cref="ResonanceNotice.None"/> hides it.</summary>
    public void Show(ResonanceNotice notice)
    {
        if (_image == null) return;

        bool visible = notice != ResonanceNotice.None;
        _image.enabled = visible;
        _pulse = notice == ResonanceNotice.EngraveReady;

        if (!visible) return;

        _image.color = _pulse ? ReadyColor : TierUpColor;
        if (!_pulse) transform.localScale = Vector3.one;
    }

    private void Update()
    {
        if (!_pulse || _image == null || !_image.enabled) return;

        // Unscaled so the badge keeps breathing while the game is paused between fights.
        float t = Mathf.PingPong(Time.unscaledTime / Mathf.Max(0.05f, pulseSeconds), 1f);
        transform.localScale = Vector3.one * Mathf.Lerp(0.82f, 1.18f, t);
    }

    /// <summary>A filled circle, generated once so this needs no art asset.</summary>
    public static Sprite Dot()
    {
        if (_dot != null) return _dot;

        const int size = 32;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp
        };

        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size - 0.5f;
                float dy = (y + 0.5f) / size - 0.5f;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);

                // Soft edge, so a small dot doesn't read as a jagged blob.
                float alpha = Mathf.Clamp01((0.46f - distance) / 0.06f);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();

        _dot = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        return _dot;
    }
}

/// <summary>
/// Keeps a hero's avatar card marked while any of their items has unread news.
///
/// Watches <see cref="Resonance.HasUnseen"/>, which is derived from the per-item set rather than
/// stored — so this dot cannot get out of step with the item dots, and clearing the last item
/// clears the hero automatically.
/// </summary>
public class HeroNoticeBadge : MonoBehaviour
{
    private Resonance _resonance;
    private NoticeBadge _badge;

    public void Initialize(Resonance resonance, RectTransform card)
    {
        _resonance = resonance;
        _badge = NoticeBadge.AttachTo(card, 18f, new Vector2(-4f, -4f));

        if (_resonance != null) _resonance.OnNoticesChanged += Refresh;
        Refresh();
    }

    private void OnDestroy()
    {
        if (_resonance != null) _resonance.OnNoticesChanged -= Refresh;
    }

    private void Refresh()
    {
        if (_badge == null || _resonance == null) return;

        // The hero shows the most urgent thing any of their items is waiting to say, so a decision
        // is never disguised as a routine tier-up.
        _badge.Show(_resonance.MostUrgentNotice);
    }
}

/// <summary>
/// The hero-level mark, floating over the unit on the board.
///
/// This lives in the world rather than only on the avatar card because the card strip is hidden
/// whenever no equipment window is open — which is exactly when the player is looking at the board
/// deciding who to check. A mark you can only see after opening the thing it is telling you to open
/// is no use. It also sits on the unit you double-click, so the notice and the gesture are in the
/// same place.
/// </summary>
public class HeroNoticeMarker : MonoBehaviour
{
    [Tooltip("Height above the unit's feet, in world units. Sits just above the health bar, which " +
             "hangs at 1.5 on a human whose head tops out around 1.35 — high enough to clear both, " +
             "low enough to still read as attached to the unit.")]
    public float height = 2.0f;

    [Tooltip("World size of the dot.")]
    public float size = 0.34f;

    private static readonly Color TierUp = new Color(1f, 0.82f, 0.28f, 1f);
    private static readonly Color Ready = new Color(0.45f, 1f, 0.55f, 1f);

    private Resonance _resonance;
    private Entity _entity;
    private SpriteRenderer _renderer;
    private bool _pulse;

    public void Initialize(Entity entity, Sprite dot)
    {
        _entity = entity;
        _resonance = entity != null ? entity.Resonance : null;

        var go = new GameObject("NoticeMarker");
        go.transform.SetParent(entity.transform, false);

        _renderer = go.AddComponent<SpriteRenderer>();
        _renderer.sprite = dot;
        // Above the units themselves, which reach ~405 on Default.
        _renderer.sortingLayerName = "UI";
        _renderer.sortingOrder = 50;

        if (_resonance != null) _resonance.OnNoticesChanged += Refresh;
        Refresh();
    }

    private void OnDestroy()
    {
        if (_resonance != null) _resonance.OnNoticesChanged -= Refresh;
    }

    private void Refresh()
    {
        if (_renderer == null || _resonance == null) return;

        var notice = _resonance.MostUrgentNotice;
        _renderer.enabled = notice != ResonanceNotice.None;
        _pulse = notice == ResonanceNotice.EngraveReady;
        _renderer.color = _pulse ? Ready : TierUp;
    }

    private void LateUpdate()
    {
        if (_renderer == null || !_renderer.enabled || _entity == null) return;

        // Parented to the unit, whose localScale flips with facing — so the marker is placed in world
        // space each frame instead, or it would mirror and drift as the hero turns around.
        _renderer.transform.position = _entity.transform.position + Vector3.up * height;

        float scale = size;
        if (_pulse)
        {
            float t = Mathf.PingPong(Time.unscaledTime / 1.1f, 1f);
            scale *= Mathf.Lerp(0.82f, 1.18f, t);
        }
        _renderer.transform.localScale = Vector3.one * scale;
        _renderer.transform.rotation = Quaternion.identity;
    }
}
