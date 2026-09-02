using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The act map: where the company is, where it can go next, and what waits at each stop.
///
/// Shown whenever the run is waiting on a path and no spoils are pending — the spoils come first,
/// so the two never compete for the screen. It gates the next fight the same way the reward panel
/// does, and for the same reason: starting a fight with no destination chosen would be starting a
/// fight with no enemies in it.
///
/// Every node names its fight and how many enemies are in it. That is the scouting step of the run
/// loop (Docs/RunLoop.md): the choice between two paths is only a choice if the player can see what
/// each one asks of them.
/// </summary>
public class MapPanel : MonoBehaviour
{
    private static readonly Color Gold = new Color(1f, 0.82f, 0.28f, 1f);
    private static readonly Color Backdrop = new Color(0f, 0f, 0f, 0.7f);
    private static readonly Color CombatFace = new Color(0.22f, 0.28f, 0.38f, 1f);
    private static readonly Color EliteFace = new Color(0.55f, 0.32f, 0.12f, 1f);
    private static readonly Color BossFace = new Color(0.55f, 0.16f, 0.16f, 1f);
    private static readonly Color Edge = new Color(0.6f, 0.6f, 0.6f, 0.6f);
    private static readonly Color Trail = new Color(1f, 0.82f, 0.28f, 0.9f);

    private const float BoardWidth = 760f;
    private const float BoardHeight = 600f;
    private const float NodeWidth = 150f;
    private const float NodeHeight = 58f;

    private RunManager _runManager;
    private GameObject _root;
    private TextMeshProUGUI _heading;
    private RectTransform _board;

    public void Initialize(RunManager runManager, Transform canvas)
    {
        _runManager = runManager;
        if (_runManager == null || canvas == null) return;

        Build(canvas);
        _runManager.OnPathChanged += Redraw;
        _runManager.OnRewardsChanged += Redraw;
        Redraw();
    }

    private void OnDestroy()
    {
        if (_runManager == null) return;
        _runManager.OnPathChanged -= Redraw;
        _runManager.OnRewardsChanged -= Redraw;
    }

    private void Redraw()
    {
        if (_root == null) return;

        bool show = _runManager.AwaitingPath && _runManager.PendingRewards.Count == 0;
        _root.SetActive(show);
        if (!show) return;

        var state = _runManager.State;
        var map = state.Map;
        _heading.text = state.CurrentNode == null
            ? "Choose where the company starts."
            : $"{state.Progress} cleared.   Choose your path.";

        for (int i = _board.childCount - 1; i >= 0; i--) Destroy(_board.GetChild(i).gameObject);

        var available = new HashSet<MapNode>(state.AvailableNext);
        var positions = new Dictionary<MapNode, Vector2>();
        foreach (var node in map.AllNodes()) positions[node] = PositionOf(node, map);

        // Edges first so nodes draw over them. A walked edge is drawn in the trail colour.
        foreach (var node in map.AllNodes())
            foreach (var next in node.Next)
                Line(positions[node], positions[next], node.Cleared && (next.Cleared || next == state.CurrentNode));

        foreach (var node in map.AllNodes())
            BuildNode(node, positions[node], node == state.CurrentNode, available.Contains(node));
    }

    /// <summary>Rows climb from the bottom; lanes spread evenly about the centre.</summary>
    private static Vector2 PositionOf(MapNode node, ActMap map)
    {
        float y = map.RowCount <= 1 ? 0f
            : -BoardHeight * 0.5f + node.Row * (BoardHeight / (map.RowCount - 1));

        int width = map.Row(node.Row).Count;
        float spacing = Mathf.Min(NodeWidth + 30f, BoardWidth / Mathf.Max(1, width));
        float x = (node.Lane - (width - 1) * 0.5f) * spacing;
        return new Vector2(x, y);
    }

    private void BuildNode(MapNode node, Vector2 position, bool current, bool available)
    {
        var go = new GameObject($"Node_{node.Row}_{node.Lane}", typeof(RectTransform));
        go.transform.SetParent(_board, false);
        var rect = go.GetComponent<RectTransform>();
        Place(rect, new Vector2(0.5f, 0.5f), new Vector2(NodeWidth, NodeHeight), position);

        var face = go.AddComponent<Image>();
        face.color = FaceFor(node.Type);

        // Cleared or unreachable nodes fade; the reachable ones are the only ones that answer a click.
        float alpha = current ? 1f : available ? 1f : node.Cleared ? 0.55f : 0.35f;
        face.color = new Color(face.color.r, face.color.g, face.color.b, alpha);

        if (current || available)
        {
            var outline = go.AddComponent<Outline>();
            outline.effectColor = current ? Gold : Color.white;
            outline.effectDistance = new Vector2(2f, -2f);
        }

        var button = go.AddComponent<Button>();
        button.targetGraphic = face;
        button.interactable = available;
        var captured = node;
        button.onClick.AddListener(() => _runManager.ChoosePath(captured));

        var label = NewText("Label", go.transform, 15f, Color.white);
        Place(label.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(NodeWidth - 10f, NodeHeight - 6f), Vector2.zero);
        label.color = new Color(1f, 1f, 1f, alpha);
        // The problem is the headline and the name is the footnote: "SWARM" is what a route is chosen
        // for, "Rat Pack" is only what it is called. An ordinary fight leads with its name.
        string kind = node.Type == NodeType.Combat ? "" : node.Type.ToString().ToUpperInvariant();
        string problem = node.Encounter != null ? node.Encounter.ProblemLabel : "";
        string tag = string.IsNullOrEmpty(kind) ? problem
                   : string.IsNullOrEmpty(problem) ? kind
                   : kind + " · " + problem;

        // A stacked elite's tag ("ELITE · BULWARK + SNIPER") is three times the width of a plain one
        // and wrapped onto three lines inside a two-line node. It shrinks instead.
        int tagSize = tag.Length > 14 ? 12 : 15;
        label.text = string.IsNullOrEmpty(tag)
            ? $"{node.Label}\n<size=12>{node.EnemyCount} enemies</size>"
            : $"<size={tagSize}><b>{tag}</b></size>\n<size=11>{node.Label} · {node.EnemyCount} enemies</size>";
    }

    private static Color FaceFor(NodeType type)
    {
        switch (type)
        {
            case NodeType.Elite: return EliteFace;
            case NodeType.Boss: return BossFace;
            default: return CombatFace;
        }
    }

    private void Line(Vector2 from, Vector2 to, bool walked)
    {
        var go = new GameObject("Edge", typeof(RectTransform));
        go.transform.SetParent(_board, false);

        var image = go.AddComponent<Image>();
        image.color = walked ? Trail : Edge;
        image.raycastTarget = false;

        var delta = to - from;
        var rect = go.GetComponent<RectTransform>();
        Place(rect, new Vector2(0.5f, 0.5f), new Vector2(delta.magnitude, walked ? 4f : 2f), (from + to) * 0.5f);
        rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
    }

    #region Construction

    private void Build(Transform canvas)
    {
        _root = new GameObject("MapPanel", typeof(RectTransform));
        _root.transform.SetParent(canvas, false);

        var rootRect = _root.GetComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;

        var shade = _root.AddComponent<Image>();
        shade.color = Backdrop;
        shade.raycastTarget = false;

        _heading = NewText("Heading", _root.transform, 30f, Gold);
        Place(_heading.rectTransform, new Vector2(0.5f, 1f), new Vector2(900f, 44f), new Vector2(0f, -40f));

        var board = new GameObject("Board", typeof(RectTransform));
        board.transform.SetParent(_root.transform, false);
        _board = board.GetComponent<RectTransform>();
        Place(_board, new Vector2(0.5f, 0.5f), new Vector2(BoardWidth, BoardHeight), new Vector2(0f, -20f));

        _root.SetActive(false);
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
