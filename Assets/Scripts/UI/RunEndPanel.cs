using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// How a run ends: the verdict, the numbers, and the one thing left to do.
///
/// A won run used to end in silence and a lost one too — the state machine reached RunEnd and
/// nothing on screen said so, which left the player looking at a still board wondering whether
/// the game had crashed. This says what happened, shows the fight-by-fight table the telemetry
/// harness kept, and offers a new run. It is the last panel a run shows and the first thing a
/// playtest of the whole act needs, because without it the loop cannot be repeated.
/// </summary>
public class RunEndPanel : MonoBehaviour
{
    private static readonly Color Gold = new Color(1f, 0.82f, 0.28f, 1f);
    private static readonly Color Ash = new Color(0.85f, 0.45f, 0.4f, 1f);
    private static readonly Color Backdrop = new Color(0f, 0f, 0f, 0.78f);
    private static readonly Color ButtonFace = new Color(0.22f, 0.28f, 0.38f, 1f);

    private RunManager _runManager;
    private GameObject _root;
    private TextMeshProUGUI _heading;
    private TextMeshProUGUI _verdict;
    private TextMeshProUGUI _table;

    public void Initialize(RunManager runManager, Transform canvas)
    {
        _runManager = runManager;
        if (_runManager == null || canvas == null || GameManager.Instance == null) return;

        Build(canvas);
        GameManager.Instance.StateMachine.OnStateChanged += HandleState;
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null) GameManager.Instance.StateMachine.OnStateChanged -= HandleState;
    }

    private void HandleState(GameState previous, GameState next)
    {
        if (next != GameState.RunEnd || _root == null) return;
        Show();
    }

    private void Show()
    {
        var state = _runManager.State;
        bool won = state != null && state.Outcome == RunOutcome.Won;

        _heading.text = won ? "Victory" : "The company fell";
        _heading.color = won ? Gold : Ash;

        // The verdict in the run's own terms: which fight, out of how many, and which map if any.
        string where = state != null ? state.Progress : "";
        _verdict.text = won
            ? (state != null && state.IsMapRun ? $"The act is cleared — the boss fell on {where}."
                                                 : $"Every fight cleared — {where}.")
            : $"Wiped out on {where}. A run ends when the whole company falls.";

        // The harness's table, if it kept one. Monospaced so its columns line up as written.
        var telemetry = GameManager.Instance != null ? GameManager.Instance.GetComponent<CombatTelemetry>() : null;
        _table.text = telemetry != null ? "<mspace=0.58em>" + telemetry.BuildReport() + "</mspace>" : "";

        _root.SetActive(true);
    }

    #region Construction

    private void Build(Transform canvas)
    {
        _root = new GameObject("RunEndPanel", typeof(RectTransform));
        _root.transform.SetParent(canvas, false);

        var rootRect = _root.GetComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;

        // Darker than the reward backdrop: nothing on the board matters any more.
        var shade = _root.AddComponent<Image>();
        shade.color = Backdrop;
        shade.raycastTarget = true;

        _heading = NewText("Heading", _root.transform, 48f, Gold);
        Place(_heading.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(900f, 70f), new Vector2(0f, 250f));

        _verdict = NewText("Verdict", _root.transform, 22f, Color.white);
        Place(_verdict.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(900f, 40f), new Vector2(0f, 195f));

        _table = NewText("Table", _root.transform, 15f, new Color(0.85f, 0.85f, 0.85f, 1f));
        _table.alignment = TextAlignmentOptions.TopLeft;
        _table.enableWordWrapping = false;
        Place(_table.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(880f, 300f), new Vector2(0f, 0f));

        var button = NewChild("NewRun", _root.transform, new Vector2(0.5f, 0.5f), new Vector2(260f, 56f),
                              new Vector2(0f, -210f));
        var face = button.AddComponent<Image>();
        face.color = ButtonFace;
        var click = button.AddComponent<Button>();
        click.targetGraphic = face;
        click.onClick.AddListener(() => { if (GameManager.Instance != null) GameManager.Instance.RestartRun(); });

        var label = NewText("Label", button.transform, 24f, Gold);
        Place(label.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(260f, 56f), Vector2.zero);
        label.text = "New run";

        var hint = NewText("Hint", _root.transform, 14f, new Color(0.7f, 0.7f, 0.7f, 1f));
        Place(hint.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(600f, 24f), new Vector2(0f, -255f));
        hint.text = "or press R";

        _root.SetActive(false);
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
        text.enableWordWrapping = true;
        text.richText = true;
        return text;
    }

    #endregion
}
