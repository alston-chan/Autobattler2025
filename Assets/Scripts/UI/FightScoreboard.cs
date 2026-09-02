using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Who did what in the fight just fought: one bar per hero, sorted, for one stat at a time.
///
/// Shown between fights, while the numbers can still change what the player does next — which
/// hero gets the drop, whether the front held, whether an ult ever fired. The stats are the ones
/// that answer those questions and no others: dealt (who is carrying), taken (who is soaking),
/// blocked (is the armour earning its slot), kills (who is finishing), ults (is the mana loop
/// working). Hit counts and crit rates are interesting and not actionable, so they stay in the
/// telemetry file. Enemies are not shown: nothing about them can be changed.
///
/// One stat at a time rather than a table, because a sorted bar answers "who" at a glance and a
/// table answers it only after reading. The last fight is the default and the whole run is a
/// toggle away, for the same reason the telemetry keeps both: the fight is what to act on, the
/// run is what to trust.
/// </summary>
public class FightScoreboard : MonoBehaviour
{
    public enum Stat { Dealt, Taken, Blocked, Kills, Ults }

    private static readonly Color Gold = new Color(1f, 0.82f, 0.28f, 1f);
    private static readonly Color Panel = new Color(0.08f, 0.08f, 0.1f, 0.85f);
    private static readonly Color BarFill = new Color(0.36f, 0.6f, 0.95f, 1f);
    private static readonly Color BarTrough = new Color(0.2f, 0.22f, 0.27f, 1f);
    private static readonly Color ButtonOn = new Color(0.32f, 0.42f, 0.6f, 1f);
    private static readonly Color ButtonOff = new Color(0.18f, 0.2f, 0.25f, 1f);

    private const float Width = 300f;
    private const float RowHeight = 40f;

    private GameObject _root;
    private TextMeshProUGUI _title;
    private RectTransform _rows;
    private readonly List<GameObject> _rowObjects = new List<GameObject>();
    private readonly Dictionary<Stat, Image> _statButtons = new Dictionary<Stat, Image>();
    private Image _fightButton, _runButton;

    private Stat _stat = Stat.Dealt;
    private bool _wholeRun;

    public Stat Showing => _stat;
    public bool WholeRun => _wholeRun;

    public void Initialize(Transform canvas)
    {
        if (canvas == null || GameManager.Instance == null) return;
        Build(canvas);
        GameManager.Instance.StateMachine.OnStateChanged += HandleState;
        Redraw();
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null) GameManager.Instance.StateMachine.OnStateChanged -= HandleState;
    }

    private void HandleState(GameState previous, GameState next) => Redraw();

    public void Show(Stat stat)
    {
        _stat = stat;
        Redraw();
    }

    public void ShowWholeRun(bool wholeRun)
    {
        _wholeRun = wholeRun;
        Redraw();
    }

    /// <summary>What is showing, as "name: value" lines in display order — for probes and tests.</summary>
    public List<string> Describe()
    {
        var lines = new List<string>();
        if (_root == null || !_root.activeSelf) return lines;
        foreach (var row in Ranked())
            lines.Add(row.Key + ": " + Format(row.Value));
        return lines;
    }

    /// <summary>How full a bar is: the value against the largest on the board. A board of zeros is all empty.</summary>
    public static float Fraction(float value, float max) => max <= 0f ? 0f : Mathf.Clamp01(value / max);

    /// <summary>Which number a stat reads from a row.</summary>
    public static float ValueOf(CombatTelemetry.Row row, Stat stat)
    {
        if (row == null) return 0f;
        switch (stat)
        {
            case Stat.Taken: return row.DamageTaken;
            case Stat.Blocked: return row.Blocked;
            case Stat.Kills: return row.Kills;
            case Stat.Ults: return row.Ults;
            default: return row.DamageDealt;
        }
    }

    private void Redraw()
    {
        if (_root == null) return;

        var game = GameManager.Instance;
        bool show = game != null && game.StateMachine.Current == GameState.Setup && CombatTelemetry.FightsRecorded > 0;
        _root.SetActive(show);
        if (!show) return;

        _title.text = (_wholeRun ? "Whole run  ·  " : "Last fight  ·  ") + Label(_stat);
        foreach (var pair in _statButtons) pair.Value.color = pair.Key == _stat ? ButtonOn : ButtonOff;
        _fightButton.color = _wholeRun ? ButtonOff : ButtonOn;
        _runButton.color = _wholeRun ? ButtonOn : ButtonOff;

        foreach (var row in _rowObjects) Destroy(row);
        _rowObjects.Clear();

        var ranked = Ranked();
        float max = 0f;
        foreach (var pair in ranked) if (pair.Value > max) max = pair.Value;

        int index = 0;
        foreach (var pair in ranked)
        {
            _rowObjects.Add(BuildRow(pair.Key, pair.Value, Fraction(pair.Value, max), index++));
        }
        _rows.sizeDelta = new Vector2(Width - 20f, Mathf.Max(1, ranked.Count) * RowHeight);

        // The panel wraps its rows rather than reserving room for a company it does not have.
        var rootRect = _root.GetComponent<RectTransform>();
        rootRect.sizeDelta = new Vector2(Width, 108f + Mathf.Max(1, ranked.Count) * RowHeight);
    }

    /// <summary>The company's heroes and their value for the current stat and scope, best first.</summary>
    private List<KeyValuePair<string, float>> Ranked()
    {
        var result = new List<KeyValuePair<string, float>>();
        var game = GameManager.Instance;
        if (game == null) return result;

        var source = _wholeRun ? CombatTelemetry.Totals : CombatTelemetry.LastFight;
        foreach (var hero in game.allyCharacters)
        {
            if (hero == null) continue;
            source.TryGetValue(hero.name, out var row);
            result.Add(new KeyValuePair<string, float>(hero.name, ValueOf(row, _stat)));
        }
        result.Sort((a, b) => b.Value.CompareTo(a.Value));
        return result;
    }

    private string Format(float value) =>
        _stat == Stat.Kills || _stat == Stat.Ults ? value.ToString("0") : value.ToString("0");

    private static string Label(Stat stat)
    {
        switch (stat)
        {
            case Stat.Taken: return "Damage taken";
            case Stat.Blocked: return "Damage blocked";
            case Stat.Kills: return "Kills";
            case Stat.Ults: return "Ultimates cast";
            default: return "Damage dealt";
        }
    }

    #region Construction

    private void Build(Transform canvas)
    {
        _root = new GameObject("FightScoreboard", typeof(RectTransform));
        _root.transform.SetParent(canvas, false);

        // A column on the right edge, clear of the spoils in the middle and the map behind them.
        var rootRect = _root.GetComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(1f, 0.5f);
        rootRect.anchorMax = new Vector2(1f, 0.5f);
        rootRect.pivot = new Vector2(1f, 0.5f);
        rootRect.sizeDelta = new Vector2(Width, 420f);
        rootRect.anchoredPosition = new Vector2(-16f, 60f);

        var face = _root.AddComponent<Image>();
        face.color = Panel;
        face.raycastTarget = false;

        _title = NewText("Title", _root.transform, 17f, Gold);
        Place(_title.rectTransform, new Vector2(0.5f, 1f), new Vector2(Width - 20f, 26f), new Vector2(0f, -18f));

        // Stat toggles: one row of small buttons.
        var statRow = NewChild("Stats", _root.transform, new Vector2(0.5f, 1f), new Vector2(Width - 20f, 26f), new Vector2(0f, -50f));
        var statLayout = statRow.AddComponent<HorizontalLayoutGroup>();
        statLayout.spacing = 4f;
        statLayout.childForceExpandWidth = true;
        statLayout.childForceExpandHeight = true;
        foreach (Stat stat in System.Enum.GetValues(typeof(Stat)))
        {
            var captured = stat;
            _statButtons[stat] = SmallButton(statRow.transform, Short(stat), () => Show(captured));
        }

        // Scope toggles.
        var scopeRow = NewChild("Scope", _root.transform, new Vector2(0.5f, 1f), new Vector2(Width - 20f, 24f), new Vector2(0f, -80f));
        var scopeLayout = scopeRow.AddComponent<HorizontalLayoutGroup>();
        scopeLayout.spacing = 4f;
        scopeLayout.childForceExpandWidth = true;
        scopeLayout.childForceExpandHeight = true;
        _fightButton = SmallButton(scopeRow.transform, "Last fight", () => ShowWholeRun(false));
        _runButton = SmallButton(scopeRow.transform, "Whole run", () => ShowWholeRun(true));

        var rows = NewChild("Rows", _root.transform, new Vector2(0.5f, 1f), new Vector2(Width - 20f, 200f), new Vector2(0f, -100f));
        _rows = rows.GetComponent<RectTransform>();
        _rows.pivot = new Vector2(0.5f, 1f);

        _root.SetActive(false);
    }

    private static string Short(Stat stat)
    {
        switch (stat)
        {
            case Stat.Taken: return "Taken";
            case Stat.Blocked: return "Blocked";
            case Stat.Kills: return "Kills";
            case Stat.Ults: return "Ults";
            default: return "Dealt";
        }
    }

    private GameObject BuildRow(string heroName, float value, float fraction, int index)
    {
        var row = NewChild("Row_" + heroName, _rows, new Vector2(0.5f, 1f), new Vector2(Width - 20f, RowHeight - 6f),
                           new Vector2(0f, -index * RowHeight - (RowHeight - 6f) * 0.5f));

        var name = NewText("Name", row.transform, 14f, Color.white);
        name.alignment = TextAlignmentOptions.Left;
        Place(name.rectTransform, new Vector2(0f, 1f), new Vector2(Width - 20f, 16f), new Vector2((Width - 20f) * 0.5f, -8f));
        name.text = Readable(heroName);

        var amount = NewText("Value", row.transform, 14f, Gold);
        amount.alignment = TextAlignmentOptions.Right;
        Place(amount.rectTransform, new Vector2(1f, 1f), new Vector2(120f, 16f), new Vector2(-60f, -8f));
        amount.text = Format(value);

        var trough = NewChild("Trough", row.transform, new Vector2(0.5f, 0f), new Vector2(Width - 20f, 10f), new Vector2(0f, 8f));
        var troughImage = trough.AddComponent<Image>();
        troughImage.color = BarTrough;
        troughImage.raycastTarget = false;

        var fill = new GameObject("Fill", typeof(RectTransform));
        fill.transform.SetParent(trough.transform, false);
        var fillRect = fill.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = new Vector2(fraction, 1f);
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        var fillImage = fill.AddComponent<Image>();
        fillImage.color = BarFill;
        fillImage.raycastTarget = false;

        return row;
    }

    /// <summary>"Hero_Melee_KnightShield" reads as "Melee KnightShield" on a bar.</summary>
    private static string Readable(string heroName)
    {
        string name = heroName.StartsWith("Hero_") ? heroName.Substring(5) : heroName;
        return name.Replace('_', ' ');
    }

    private static Image SmallButton(Transform parent, string text, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject("Button_" + text, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var face = go.AddComponent<Image>();
        face.color = ButtonOff;
        var button = go.AddComponent<Button>();
        button.targetGraphic = face;
        button.onClick.AddListener(onClick);

        var label = NewText("Label", go.transform, 12f, Color.white);
        var rect = label.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        label.text = text;
        return face;
    }

    private static GameObject NewChild(string name, Transform parent, Vector2 anchor, Vector2 size, Vector2 position)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Place(go.GetComponent<RectTransform>(), anchor, size, position);
        return go;
    }

    private static void Place(RectTransform rect, Vector2 anchor, Vector2 size, Vector2 position)
    {
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
    }

    private static TextMeshProUGUI NewText(string name, Transform parent, float size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<TextMeshProUGUI>();
        text.fontSize = size;
        text.color = color;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        text.enableWordWrapping = false;
        return text;
    }

    #endregion
}
