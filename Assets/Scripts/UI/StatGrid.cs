using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>The stats a <see cref="StatGrid"/> can show, in the order a caller lists them.</summary>
public enum StatKind { Health, Mana, Damage, AttackSpeed, Armor, MagicResist, MoveSpeed, Range, KnockbackResist }

/// <summary>
/// A unit's stats as TFT or League show them: a grid of icon and number, the name and what the
/// number means on hover. It replaced two columns of text ("Magic resist ... 20 (17% less
/// magical)") that read as a spreadsheet; the icon is found faster than the word, and the meaning is
/// one hover away rather than in every line.
///
/// Numbers are plain white: every stat here comes from gear, so colouring what gear raised turned the
/// whole grid green and said nothing. Armour and magic resist carry their damage cut beside them in
/// grey, since the rating alone says nothing. Knockback resist is shown only when something grants it.
/// Each icon sits on a dark tile and is drawn larger than it, because the item art is padded and a
/// thin sword beside a round shield otherwise reads as two different sizes.
///
/// Built at runtime by <see cref="Build"/>; the caller places the returned rect and reads
/// <see cref="Height"/> to stack what follows.
/// </summary>
public class StatGrid : MonoBehaviour
{
    private static readonly Color Plain = Color.white;

    private class Cell
    {
        public StatKind kind;
        public GameObject root;
        public TextMeshProUGUI value;
        public StatHover hover;
    }

    private readonly List<Cell> _cells = new List<Cell>();
    private int _columns;
    private Vector2 _cellSize;

    /// <summary>How tall the grid is with the cells it is showing now.</summary>
    public float Height { get; private set; }

    /// <summary>
    /// A grid of <paramref name="kinds"/> under <paramref name="parent"/>, <paramref name="columns"/>
    /// wide. The root's pivot and anchors are its top-left corner; the caller positions it.
    /// </summary>
    public static StatGrid Build(Transform parent, StatKind[] kinds, int columns, Vector2 cellSize, float iconSize, float fontSize)
    {
        var go = new GameObject("StatGrid", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = new Vector2(columns * cellSize.x, 0f);

        var grid = go.AddComponent<StatGrid>();
        grid._columns = Mathf.Max(1, columns);
        grid._cellSize = cellSize;
        var icons = StatIcons.Active;
        foreach (var kind in kinds) grid._cells.Add(grid.BuildCell(kind, icons, iconSize, fontSize));
        return grid;
    }

    private Cell BuildCell(StatKind kind, StatIcons icons, float iconSize, float fontSize)
    {
        var root = new GameObject(kind.ToString(), typeof(RectTransform));
        root.transform.SetParent(transform, false);
        var rect = (RectTransform)root.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = _cellSize;

        // Invisible, but hit-testable, so the whole cell answers the hover rather than the icon alone.
        var hit = root.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);

        var tileObject = new GameObject("Tile", typeof(RectTransform));
        tileObject.transform.SetParent(root.transform, false);
        var tileRect = (RectTransform)tileObject.transform;
        tileRect.anchorMin = tileRect.anchorMax = new Vector2(0f, 0.5f);
        tileRect.pivot = new Vector2(0f, 0.5f);
        tileRect.sizeDelta = new Vector2(iconSize, iconSize);
        tileRect.anchoredPosition = Vector2.zero;
        var tile = tileObject.AddComponent<Image>();
        tile.raycastTarget = false;
        tile.sprite = icons != null ? icons.tile : null;
        tile.type = Image.Type.Sliced;
        tile.color = icons != null ? icons.tileColor : new Color(0f, 0f, 0f, 0.45f);

        var iconObject = new GameObject("Icon", typeof(RectTransform));
        iconObject.transform.SetParent(tileObject.transform, false);
        var iconRect = (RectTransform)iconObject.transform;
        iconRect.anchorMin = iconRect.anchorMax = iconRect.pivot = new Vector2(0.5f, 0.5f);
        iconRect.sizeDelta = new Vector2(iconSize * 1.45f, iconSize * 1.45f);   // the art's own padding eats the rest
        iconRect.anchoredPosition = Vector2.zero;
        var icon = iconObject.AddComponent<Image>();
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        icon.sprite = IconFor(kind, icons);
        if (kind == StatKind.Mana && icons != null) icon.color = icons.manaTint;
        if (icon.sprite == null) icon.color = new Color(1f, 1f, 1f, 0.15f);

        var valueObject = new GameObject("Value", typeof(RectTransform));
        valueObject.transform.SetParent(root.transform, false);
        var valueRect = (RectTransform)valueObject.transform;
        valueRect.anchorMin = new Vector2(0f, 0f);
        valueRect.anchorMax = new Vector2(1f, 1f);
        valueRect.offsetMin = new Vector2(iconSize + 10f, 0f);
        valueRect.offsetMax = Vector2.zero;
        var value = valueObject.AddComponent<TextMeshProUGUI>();
        value.fontSize = fontSize;
        value.fontStyle = FontStyles.Bold;
        value.alignment = TextAlignmentOptions.MidlineLeft;
        value.enableWordWrapping = false;
        value.raycastTarget = false;

        var hover = root.AddComponent<StatHover>();
        return new Cell { kind = kind, root = root, value = value, hover = hover };
    }

    private static Sprite IconFor(StatKind kind, StatIcons icons)
    {
        if (icons == null) return null;
        switch (kind)
        {
            case StatKind.Health: return icons.health;
            case StatKind.Mana: return icons.mana;
            case StatKind.Damage: return icons.damage;
            case StatKind.AttackSpeed: return icons.attackSpeed;
            case StatKind.Armor: return icons.armor;
            case StatKind.MagicResist: return icons.magicResist;
            case StatKind.MoveSpeed: return icons.moveSpeed;
            case StatKind.Range: return icons.range;
            case StatKind.KnockbackResist: return icons.knockbackResist;
            default: return null;
        }
    }

    /// <summary>Fill every cell from <paramref name="unit"/>, hide the ones that do not apply, and re-flow.</summary>
    public void Show(Entity unit)
    {
        int shown = 0;
        foreach (var cell in _cells)
        {
            bool visible = unit != null && Paint(cell, unit);
            cell.root.SetActive(visible);
            if (!visible) continue;

            var rect = (RectTransform)cell.root.transform;
            rect.anchoredPosition = new Vector2(shown % _columns * _cellSize.x, -(shown / _columns) * _cellSize.y);
            shown++;
        }
        int rows = (shown + _columns - 1) / _columns;
        Height = rows * _cellSize.y;
        var root = (RectTransform)transform;
        root.sizeDelta = new Vector2(_columns * _cellSize.x, Height);
    }

    /// <summary>Write one cell. Returns false when the stat does not apply to this unit.</summary>
    private static bool Paint(Cell cell, Entity unit)
    {
        var stats = unit.Stats;
        switch (cell.kind)
        {
            case StatKind.Health:
            {
                if (unit.Health == null) return false;
                float max = stats != null ? stats.MaxHealth.Value : unit.Health.maxHealth;
                float now = unit.Health.currentHealth;
                bool hurt = now > 0f && now < max - 0.5f;
                Set(cell, hurt ? $"{Mathf.CeilToInt(now)}/{Mathf.CeilToInt(max)}" : Mathf.CeilToInt(max).ToString(),
                    Plain,
                    "Health", hurt ? $"{Mathf.CeilToInt(now)} of {Mathf.CeilToInt(max)}." : "Refilled before every fight.");
                return true;
            }
            case StatKind.Mana:
            {
                if (unit.Mana == null || unit.Mana.maxMana <= 0f) return false;
                Set(cell, unit.Mana.maxMana.ToString("0"), Plain,
                    "Mana", $"Casts its ability when mana reaches {unit.Mana.maxMana:0}. Basic attacks fill it.");
                return true;
            }
            case StatKind.Damage:
                if (stats == null) return false;
                Set(cell, stats.Damage.Value.ToString("0"), Plain,
                    "Damage", "Each basic attack's damage, and what abilities scale from.");
                return true;
            case StatKind.AttackSpeed:
                if (stats == null) return false;
                Set(cell, stats.AttacksPerSecond.ToString("0.00"), Plain,
                    "Attack speed", $"{stats.AttacksPerSecond:0.00} basic attacks a second.");
                return true;
            case StatKind.Armor:
            {
                if (stats == null) return false;
                float cut = Mitigation.Fraction(stats.Armor.Value) * 100f;
                Set(cell, stats.Armor.Value.ToString("0") + Cut(cut), Plain,
                    "Armour", $"Takes {cut:0}% less physical damage — hits, slams and physical abilities.");
                return true;
            }
            case StatKind.MagicResist:
            {
                if (stats == null) return false;
                float cut = Mitigation.Fraction(stats.MagicResist.Value) * 100f;
                Set(cell, stats.MagicResist.Value.ToString("0") + Cut(cut), Plain,
                    "Magic resist", $"Takes {cut:0}% less magical damage — wands and magical abilities.");
                return true;
            }
            case StatKind.MoveSpeed:
                if (stats == null) return false;
                Set(cell, stats.Speed.Value.ToString("0.#"), Plain,
                    "Move speed", "How fast it walks to its target.");
                return true;
            case StatKind.Range:
            {
                if (unit.CombatAI == null) return false;
                float reach = unit.CombatAI.AttackRange;
                Set(cell, reach.ToString("0.#"), Plain,
                    "Range", reach > 3f ? $"Attacks from {reach:0.#} away." : $"Melee: reaches {reach:0.#}.");
                return true;
            }
            case StatKind.KnockbackResist:
            {
                float resist = stats != null && stats.KnockbackResistance != null ? stats.KnockbackResistance.Value : 0f;
                if (resist <= 0f) return false;
                Set(cell, (resist * 100f).ToString("0") + "%", Plain,
                    "Knockback resist", $"Thrown {resist * 100f:0}% less far by shoves and throws.");
                return true;
            }
        }
        return false;
    }

    private static string Cut(float percent) => percent >= 0.5f ? $" <size=70%><color=#8A93A6>-{percent:0}%</color></size>" : "";

    private static void Set(Cell cell, string value, Color color, string name, string meaning)
    {
        cell.value.text = value;
        cell.value.color = color;
        cell.hover.Title = name;
        cell.hover.Body = meaning;
    }
}

/// <summary>Shows its cell's name and meaning in the shared <see cref="StatTooltip"/> while hovered.</summary>
public class StatHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public string Title;
    public string Body;
    private bool _over;

    public void OnPointerEnter(PointerEventData eventData)
    {
        _over = true;
        StatTooltip.Show(this, Title, Body);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _over = false;
        StatTooltip.Hide(this);
    }

    private void OnDisable()
    {
        if (_over) StatTooltip.Hide(this);
        _over = false;
    }
}

/// <summary>
/// One small tooltip for every stat cell: the stat's name and what its number means, beside the
/// cursor, above every panel.
/// </summary>
public static class StatTooltip
{
    private const int Order = 950;
    private static RectTransform _panel;
    private static TextMeshProUGUI _text;
    private static Canvas _canvas;
    private static StatHover _owner;

    public static void Show(StatHover owner, string title, string body)
    {
        if (owner == null) return;
        if (!Ensure(owner)) return;
        _owner = owner;
        _text.text = $"<b>{title}</b>\n<size=85%><color=#C9CED8>{body}</color></size>";
        _panel.sizeDelta = new Vector2(260f, _text.GetPreferredValues(_text.text, 240f, 0f).y + 16f);
        Place(owner);
        _panel.gameObject.SetActive(true);
    }

    public static void Hide(StatHover owner)
    {
        if (_panel == null || owner != _owner) return;
        _panel.gameObject.SetActive(false);
        _owner = null;
    }

    /// <summary>Beside the hovered cell, to its right and a little below, kept on the screen.</summary>
    private static void Place(StatHover owner)
    {
        var cell = (RectTransform)owner.transform;
        var corners = new Vector3[4];
        cell.GetWorldCorners(corners);
        var camera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
        Vector2 screen = RectTransformUtility.WorldToScreenPoint(camera, corners[3]);   // bottom-right
        var canvasRect = (RectTransform)_canvas.transform;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, camera, out var local)) return;

        var half = canvasRect.rect.size * 0.5f;
        float x = Mathf.Min(local.x + 6f, half.x - _panel.sizeDelta.x - 8f);
        float y = Mathf.Max(local.y - 4f, -half.y + _panel.sizeDelta.y + 8f);
        _panel.anchoredPosition = new Vector2(x, y);
    }

    private static bool Ensure(StatHover owner)
    {
        var canvas = owner.GetComponentInParent<Canvas>();
        if (canvas == null) return false;
        canvas = canvas.rootCanvas;
        if (_panel != null && _canvas == canvas) return true;

        _canvas = canvas;
        var go = new GameObject("StatTooltip", typeof(RectTransform));
        go.transform.SetParent(canvas.transform, false);
        _panel = (RectTransform)go.transform;
        _panel.anchorMin = _panel.anchorMax = new Vector2(0.5f, 0.5f);
        _panel.pivot = new Vector2(0f, 1f);
        var face = go.AddComponent<Image>();
        face.color = new Color(0.06f, 0.06f, 0.08f, 0.96f);
        face.raycastTarget = false;
        var edge = go.AddComponent<Outline>();
        edge.effectColor = new Color(1f, 0.82f, 0.28f, 0.5f);
        edge.effectDistance = new Vector2(1f, -1f);
        UiLayer.Raise(go, Order);

        var textObject = new GameObject("Text", typeof(RectTransform));
        textObject.transform.SetParent(go.transform, false);
        var textRect = (RectTransform)textObject.transform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(10f, 8f);
        textRect.offsetMax = new Vector2(-10f, -8f);
        _text = textObject.AddComponent<TextMeshProUGUI>();
        _text.fontSize = 16f;
        _text.color = Color.white;
        _text.enableWordWrapping = true;
        _text.alignment = TextAlignmentOptions.TopLeft;
        _text.raycastTarget = false;
        go.SetActive(false);
        return true;
    }
}
