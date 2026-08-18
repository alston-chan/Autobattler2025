using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Click any unit on the battlefield to read its stats, the way an autobattler lets you inspect a
/// piece mid-fight.
///
/// Deliberately NOT the equipment window. That window is a workshop — it owns a doll, a bag, a spell
/// row and an engrave button, it only exists for the company, and opening one mid-fight to answer
/// "why is that thing killing me" is far too much furniture. This is a read-only card: no controls,
/// no mutation, works on enemies, and closes the moment you click away.
///
/// Live while it's open, because the interesting questions are all mid-combat ones — how much health
/// is left, is that hero actually attacking faster now — and a card frozen at the moment of clicking
/// would answer none of them.
///
/// Built at runtime like <see cref="ResonancePanel"/>, so no prefab has to be authored for it.
/// </summary>
public class UnitInspector : MonoBehaviour
{
    [Tooltip("Fallback pick distance for units with no body collider, in world units. Units that " +
             "have one are picked by that collider instead, which is shaped to the unit.")]
    public float pickRadius = 1.1f;

    [Tooltip("How far above the body collider a click still counts, in world units. The collider is " +
             "an arrow hitbox that stops at the shoulders; this covers the head, which is a large " +
             "part of what the player is aiming at.")]
    public float headroom = 0.7f;

    [Tooltip("How far the cursor may travel between press and release and still count as a click " +
             "rather than a drag, in screen pixels. Dragging a unit into formation must not also " +
             "open its card.")]
    public float clickTolerance = 12f;

    [Tooltip("Seconds between text repaints. The bars follow every frame; only the numbers wait.")]
    public float refreshInterval = 0.1f;

    [Header("Card")]
    public Vector2 cardSize = new Vector2(330f, 316f);
    [Tooltip("Inset from the bottom-right corner of the canvas. Bottom-LEFT is taken by the avatar " +
             "strip and the centre by the equipment windows, so the card lives on the right.")]
    public Vector2 cardMargin = new Vector2(-24f, 24f);

    private static readonly Color Backing = new Color(0.05f, 0.05f, 0.07f, 0.93f);
    private static readonly Color Ally = new Color(1f, 0.82f, 0.28f, 1f);
    private static readonly Color Enemy = new Color(0.95f, 0.42f, 0.36f, 1f);
    private static readonly Color Muted = new Color(0.72f, 0.72f, 0.75f, 1f);
    private static readonly Color Trough = new Color(0f, 0f, 0f, 0.55f);
    private static readonly Color HealthAlly = new Color(0.36f, 0.78f, 0.42f, 1f);
    private static readonly Color HealthEnemy = new Color(0.85f, 0.34f, 0.31f, 1f);
    private static readonly Color ManaFill = new Color(0.36f, 0.6f, 0.95f, 1f);

    private Camera _camera;
    private Entity _selected;
    private Vector3 _pressPosition;
    private bool _pressed;
    private float _nextRefresh;

    private GameObject _card;
    private TextMeshProUGUI _name;
    private TextMeshProUGUI _side;
    private RectTransform _healthFill;
    private TextMeshProUGUI _healthText;
    private GameObject _manaRow;
    private RectTransform _manaFill;
    private TextMeshProUGUI _statKeys;
    private TextMeshProUGUI _statValues;
    private TextMeshProUGUI _kit;

    private SpriteRenderer _ring;

    /// <summary>Running Y position while the card is laid out, in canvas units below its top edge.</summary>
    private float _cursor;

    /// <summary>Build the card under <paramref name="canvas"/>. Starts hidden.</summary>
    public void Initialize(Transform canvas)
    {
        _camera = Camera.main;
        if (canvas == null) return;

        BuildCard(canvas);
        BuildRing();
        Select(null);

        // A unit that dies or despawns takes its card with it — the alternative is a card describing
        // something no longer on the board.
        EntityRegistry.OnUnregistered += HandleUnregistered;
    }

    private void OnDestroy()
    {
        EntityRegistry.OnUnregistered -= HandleUnregistered;
    }

    private void HandleUnregistered(Entity entity)
    {
        if (entity == _selected) Select(null);
    }

    private void Update()
    {
        if (_card == null) return;

        if (Input.GetKeyDown(KeyCode.Escape) && _selected != null) Select(null);

        // Press and release are tracked separately so a formation drag doesn't also open a card.
        if (Input.GetMouseButtonDown(0))
        {
            _pressed = !IsPointerOverUI();
            _pressPosition = Input.mousePosition;
        }
        else if (Input.GetMouseButtonUp(0) && _pressed)
        {
            _pressed = false;
            if (Vector3.Distance(Input.mousePosition, _pressPosition) <= clickTolerance)
                Select(UnitUnderCursor());
        }
    }

    private void LateUpdate()
    {
        if (_selected == null) return;

        // A dead unit's card would go on reporting stats for a corpse.
        if (_selected.isDead)
        {
            Select(null);
            return;
        }

        FollowBars();
        if (Time.unscaledTime < _nextRefresh) return;
        _nextRefresh = Time.unscaledTime + refreshInterval;
        Repaint();
    }

    private static bool IsPointerOverUI() =>
        EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

    /// <summary>Nearest living unit to the cursor, or null if the click landed on empty ground.</summary>
    private Entity UnitUnderCursor()
    {
        if (_camera == null) _camera = Camera.main;
        if (_camera == null) return null;

        Vector3 mouse = _camera.ScreenToWorldPoint(Input.mousePosition);
        mouse.z = 0f;
        return UnitAt(mouse);
    }

    /// <summary>
    /// The living unit at a world point, front-most first — see <see cref="UnitPicking"/> for how a
    /// click is matched to a unit. Split out from the cursor lookup so the hit-test can be exercised
    /// without synthesising mouse input.
    ///
    /// Any unit on the board may be inspected, enemies included, so this searches the whole registry
    /// rather than the company.
    /// </summary>
    public Entity UnitAt(Vector3 world)
    {
        Entity best = null;

        var all = EntityRegistry.All;
        for (int i = 0; i < all.Count; i++)
        {
            var unit = all[i];
            if (unit == null || unit.isDead || !unit.gameObject.activeInHierarchy) continue;
            if (!UnitPicking.Covers(unit, world, headroom, pickRadius)) continue;
            if (UnitPicking.IsInFrontOf(unit, best)) best = unit;
        }

        return best;
    }

    private void Select(Entity unit)
    {
        // Clicking the selected unit again closes the card, so dismissing it never needs a hunt for
        // empty ground.
        if (unit == _selected) unit = null;

        _selected = unit;
        _card.SetActive(unit != null);
        if (_ring != null) _ring.gameObject.SetActive(unit != null);

        if (unit == null) return;

        _nextRefresh = 0f;
        Repaint();
        FollowBars();
    }

    #region Painting

    /// <summary>Per-frame work: the ring tracks a moving unit and the bars track live values.</summary>
    private void FollowBars()
    {
        var health = _selected.Health;
        if (health != null && _healthFill != null)
            _healthFill.anchorMax = new Vector2(Fraction(health.currentHealth, MaxHealthOf(_selected)), 1f);

        var mana = _selected.Mana;
        if (mana != null && _manaFill != null)
            _manaFill.anchorMax = new Vector2(Mathf.Clamp01(mana.Normalized), 1f);

        if (_ring != null)
            _ring.transform.position = _selected.transform.position + Vector3.up * 0.06f;
    }

    private static float Fraction(float value, float max) =>
        max <= 0f ? 0f : Mathf.Clamp01(value / max);

    private static float MaxHealthOf(Entity unit) =>
        unit.Stats != null ? unit.Stats.MaxHealth.Value : unit.maxHealth;

    private void Repaint()
    {
        bool ally = _selected.isTeam;
        Color accent = ally ? Ally : Enemy;

        _name.text = DisplayName(_selected);
        _name.color = accent;
        _side.text = ally ? "Company" : "Enemy";

        if (_ring != null) _ring.color = new Color(accent.r, accent.g, accent.b, 0.55f);

        var health = _selected.Health;
        float max = MaxHealthOf(_selected);
        _healthText.text = health != null
            ? $"{Mathf.CeilToInt(health.currentHealth)} / {Mathf.CeilToInt(max)}"
            : "—";
        _healthFill.GetComponent<Image>().color = ally ? HealthAlly : HealthEnemy;

        _manaRow.SetActive(_selected.Mana != null);

        PaintStats();
        PaintKit();
    }

    private void PaintStats()
    {
        var stats = _selected.Stats;
        if (stats == null)
        {
            _statKeys.text = _statValues.text = "";
            return;
        }

        // Max Health is deliberately absent — the bar above already carries it, in more detail.
        var keys = new StringBuilder();
        var values = new StringBuilder();

        Line(keys, values, "Damage", stats.Damage.Value.ToString("0.##"));
        Line(keys, values, "Attack Speed", stats.AttackSpeed.Value.ToString("0.##"));
        Line(keys, values, "Move Speed", stats.Speed.Value.ToString("0.##"));
        Line(keys, values, "Blocking", stats.Blocking.Value.ToString("0.##"));

        _statKeys.text = keys.ToString();
        _statValues.text = values.ToString();
    }

    private static void Line(StringBuilder keys, StringBuilder values, string key, string value)
    {
        keys.AppendLine(key);
        values.AppendLine(value);
    }

    /// <summary>
    /// What this unit is carrying into the fight: the spell it casts and the engravings acting on
    /// it. Engravings are the reason two units with identical stat lines behave differently, so a
    /// card that listed only numbers would hide the most important thing about a unit.
    /// </summary>
    private void PaintKit()
    {
        var text = new StringBuilder();

        var spell = _selected.ActiveSpell;
        if (spell != null)
            text.Append("<color=#BFC6D4>Ability</color>  ").Append(spell.name).Append('\n');

        AppendEngravings(text);

        _kit.text = text.Length > 0 ? text.ToString().TrimEnd('\n') : "";
    }

    private void AppendEngravings(StringBuilder text)
    {
        var resonance = _selected.Resonance;
        if (resonance == null) return;

        // Worn engravings first — they leave when the item does, which is worth seeing separately
        // from the banked ones, which never leave.
        var inventory = _selected.characterInventory;
        if (inventory != null && inventory.Equipment != null)
        {
            foreach (var item in inventory.Equipment.Items)
            {
                var entry = resonance.EntryFor(item);
                if (entry == null || entry.engraving == null) continue;
                Engraving(text, entry.engraving.DisplayName, resonance.TierFor(item), worn: true);
            }
        }

        if (resonance.banked == null) return;
        foreach (var mark in resonance.banked)
        {
            if (mark == null || mark.engraving == null) continue;
            Engraving(text, mark.engraving.DisplayName, mark.tier, worn: false);
        }
    }

    private static void Engraving(StringBuilder text, string name, int tier, bool worn)
    {
        text.Append("<color=#FFD147>").Append(name).Append(' ').Append(Roman(tier))
            .Append("</color>")
            .Append(worn ? "" : "  <color=#8A8F99>engraved</color>")
            .Append('\n');
    }

    private static string Roman(int tier) => tier switch
    {
        1 => "I",
        2 => "II",
        3 => "III",
        _ => ""
    };

    private static string DisplayName(Entity unit)
    {
        if (unit.unitData != null && !string.IsNullOrEmpty(unit.unitData.unitName))
            return unit.unitData.unitName;

        // Spawned units carry Unity's instantiation debris in their name — "HumanPrefab(Clone)" is
        // not something to show a player.
        string name = unit.name.Replace("(Clone)", "").Trim();
        if (name.EndsWith("Prefab")) name = name.Substring(0, name.Length - "Prefab".Length);
        return name;
    }

    #endregion

    #region Construction

    private void BuildCard(Transform canvas)
    {
        _card = NewRect("UnitInspectorCard", canvas, new Vector2(1f, 0f), cardSize, cardMargin);
        _card.GetComponent<RectTransform>().pivot = new Vector2(1f, 0f);

        var backing = _card.AddComponent<Image>();
        backing.color = Backing;
        // Swallows clicks so dismissing the card by clicking it isn't possible — and, more usefully,
        // so a click meant for the card never falls through to select whatever unit is behind it.
        backing.raycastTarget = true;

        _cursor = -14f;

        _name = NewText("Name", _card.transform, 23f, Ally, TextAlignmentOptions.Left);
        Stack(_name.rectTransform, 28f, 0f);

        _side = NewText("Side", _card.transform, 14f, Muted, TextAlignmentOptions.Left);
        Stack(_side.rectTransform, 18f, 10f);

        _healthFill = BuildBar("Health", out _healthText, HealthAlly, 18f, 4f);
        _manaRow = BuildManaRow();

        // Keys and values are two full-width blocks sharing one row, left- and right-aligned, so the
        // numbers line up on the right edge without a layout group.
        float statsTop = _cursor;
        _statKeys = NewText("StatKeys", _card.transform, 16f, Muted, TextAlignmentOptions.TopLeft);
        Stack(_statKeys.rectTransform, 88f, 0f);

        _cursor = statsTop;
        _statValues = NewText("StatValues", _card.transform, 16f, Color.white,
                              TextAlignmentOptions.TopRight);
        Stack(_statValues.rectTransform, 88f, 10f);

        _kit = NewText("Kit", _card.transform, 15f, Color.white, TextAlignmentOptions.TopLeft);
        _kit.enableWordWrapping = true;
        Stack(_kit.rectTransform, 90f, 0f);
    }

    /// <summary>
    /// Place a full-width element with its TOP at the cursor, then move the cursor below it. Rects
    /// here are centre-pivoted, so the half-height offset is what actually puts the top where the
    /// cursor says — without it every block creeps upward into the one above.
    /// </summary>
    private void Stack(RectTransform rect, float height, float gap)
    {
        Anchor(rect, new Vector2(0.5f, 1f), new Vector2(cardSize.x - 28f, height),
               new Vector2(0f, _cursor - height * 0.5f));
        _cursor -= height + gap;
    }

    /// <summary>A labelled trough + fill, stacked below whatever came before it.</summary>
    private RectTransform BuildBar(string name, out TextMeshProUGUI label, Color fillColor,
                                   float height, float gap)
    {
        var trough = NewRect(name + "Bar", _card.transform, new Vector2(0.5f, 1f),
                             new Vector2(cardSize.x - 28f, height),
                             new Vector2(0f, _cursor - height * 0.5f));
        _cursor -= height + gap;
        var troughImage = trough.AddComponent<Image>();
        troughImage.color = Trough;
        troughImage.raycastTarget = false;

        var fill = NewRect(name + "Fill", trough.transform, new Vector2(0f, 0.5f), Vector2.zero,
                           Vector2.zero);
        var fillRect = fill.GetComponent<RectTransform>();
        fillRect.anchorMin = new Vector2(0f, 0f);
        fillRect.anchorMax = new Vector2(1f, 1f);
        fillRect.offsetMin = fillRect.offsetMax = Vector2.zero;
        var fillImage = fill.AddComponent<Image>();
        fillImage.color = fillColor;
        fillImage.raycastTarget = false;

        label = NewText(name + "Text", trough.transform, 13f, Color.white, TextAlignmentOptions.Center);
        var labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;

        return fillRect;
    }

    private GameObject BuildManaRow()
    {
        var fill = BuildBar("Mana", out var unused, ManaFill, 10f, 12f);
        unused.gameObject.SetActive(false);   // mana reads fine as a bar; the number is noise
        _manaFill = fill;
        return fill.parent.gameObject;
    }

    /// <summary>
    /// A ring on the ground under the selected unit. Without it the card names a unit the player then
    /// has to find again by eye — which in a 5v5 of similarly-dressed enemies is genuinely hard.
    /// Drawn procedurally so this needs no art asset.
    /// </summary>
    private void BuildRing()
    {
        var go = new GameObject("UnitInspectorRing");
        _ring = go.AddComponent<SpriteRenderer>();
        _ring.sprite = RingSprite();
        // Above the background (-1000) and the formation grid (-500), below the units themselves.
        _ring.sortingLayerName = "Default";
        _ring.sortingOrder = -400;
        // Flattened, so it reads as lying on the ground rather than standing up in a side view.
        go.transform.localScale = new Vector3(1.9f, 0.62f, 1f);
        go.SetActive(false);
    }

    private static Sprite RingSprite()
    {
        const int size = 128;
        const float outer = 0.47f;
        const float inner = 0.36f;
        const float feather = 0.02f;

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

                // Soft on both edges so the ring doesn't alias into a jagged polygon.
                float alpha = Mathf.Clamp01((outer - distance) / feather) *
                              Mathf.Clamp01((distance - inner) / feather);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
    }

    private static GameObject NewRect(string name, Transform parent, Vector2 anchor, Vector2 size,
                                      Vector2 position)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Anchor(go.GetComponent<RectTransform>(), anchor, size, position);
        return go;
    }

    private static void Anchor(RectTransform rect, Vector2 anchor, Vector2 size, Vector2 position)
    {
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
    }

    private static TextMeshProUGUI NewText(string name, Transform parent, float size, Color color,
                                           TextAlignmentOptions alignment)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var text = go.AddComponent<TextMeshProUGUI>();
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.raycastTarget = false;
        return text;
    }

    #endregion
}
