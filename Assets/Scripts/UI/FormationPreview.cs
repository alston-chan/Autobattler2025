using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// What a hero's engravings will do at the bell, shown over the units they will touch — a badge at
/// each one's head ("MARKED · 80%", "BULWARK -12 ×2") and a ring at its feet, gold on the company
/// and red on the enemy.
///
/// Scoped to the decision being made, the way an autobattler's pickup preview is. Every hero's
/// effects at once is a wall; one hero's effects while it is in the player's hand is an answer. So
/// the badges show for exactly one hero at a time — the one being dragged, computed from the cell
/// under it as if it were dropped there; the one just put down, lingering a moment as placement
/// feedback; or the one whose card is open — and for everyone at once for a moment at the bell, as
/// the confirmation that what was promised is what landed. The rest of the time the board is quiet.
///
/// Several grants of one engraving on one unit read as one line with the total and a count, so the
/// badge says what the unit will actually get rather than listing the pieces.
/// </summary>
public class FormationPreview : MonoBehaviour
{
    private const float PollInterval = 0.1f;
    private const float HeadHeight = 1.05f;
    private const float LingerSeconds = 1.4f;
    private const float BellSeconds = 1.3f;
    private static readonly Color AllyColor = new Color(1f, 0.88f, 0.3f, 1f);
    private static readonly Color EnemyColor = new Color(1f, 0.45f, 0.38f, 1f);

    private class Marker
    {
        public TextMeshPro text;
        public SpriteRenderer ring;
    }

    private RunManager _runManager;
    private FormationDragger _dragger;
    private UnitInspector _inspector;
    private bool _lookedForTools;

    private readonly List<Engraving.Badge> _badges = new List<Engraving.Badge>();
    private readonly Dictionary<Entity, Dictionary<System.Type, List<Engraving.Badge>>> _byTarget =
        new Dictionary<Entity, Dictionary<System.Type, List<Engraving.Badge>>>();
    private readonly Dictionary<Entity, string> _lines = new Dictionary<Entity, string>();
    private readonly Dictionary<Entity, Marker> _markers = new Dictionary<Entity, Marker>();
    private readonly List<Entity> _stale = new List<Entity>();
    private readonly List<int> _tiers = new List<int>();

    private float _nextPoll;
    private float _nextThreatPoll;
    private readonly List<SpriteRenderer> _threats = new List<SpriteRenderer>();
    private static Sprite _lineSprite;
    private static readonly Color ThreatColor = new Color(1f, 0.38f, 0.32f, 1f);
    private Entity _lingering;
    private float _lingerUntil;
    private float _bellUntil;

    public void Initialize(RunManager runManager)
    {
        _runManager = runManager;
        if (GameManager.Instance != null) GameManager.Instance.StateMachine.OnStateChanged += HandleState;
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null) GameManager.Instance.StateMachine.OnStateChanged -= HandleState;
        if (_dragger != null) _dragger.OnDropped -= Dropped;
    }

    /// <summary>What is showing right now, as "unit: label" lines — for probes and tests.</summary>
    public List<string> Describe()
    {
        var lines = new List<string>();
        foreach (var pair in _markers)
            if (pair.Key != null && pair.Value.text != null && pair.Value.text.gameObject.activeSelf)
                lines.Add(pair.Key.name + ": " + pair.Value.text.text.Replace("\n", " | "));
        lines.Sort();
        return lines;
    }

    private void LateUpdate()
    {
        FindTools();

        var game = GameManager.Instance;
        if (game == null) { Hide(); return; }
        float now = Time.unscaledTime;

        if (game.StateMachine.Current == GameState.Combat)
        {
            // The bell: everything that landed, fading out. Collected once in HandleState.
            if (now < _bellUntil) { Apply((_bellUntil - now) / BellSeconds); Follow(); }
            else Hide();
            return;
        }

        if (game.StateMachine.Current != GameState.Setup) { Hide(); return; }

        Entity focus = null;
        bool inHand = false;
        float alpha = 1f;

        // Threat lines — enemy to the hero it will engage at the bell — show whenever the company is
        // being arranged, not only for a selected hero: they are the board's weather, and the reason
        // a lane left open is a decision rather than a gap.

        if (_dragger != null && _dragger.Held != null)
        {
            focus = _dragger.Held;
            inHand = true;
        }
        else if (_lingering != null && now < _lingerUntil)
        {
            focus = _lingering;
            alpha = (_lingerUntil - now) / LingerSeconds;
        }
        else if (_inspector != null && _inspector.Selected != null && _inspector.Selected.isTeam)
        {
            focus = _inspector.Selected;
        }

        bool held = _dragger != null && _dragger.Held != null;
        if (held || now >= _nextThreatPoll)
        {
            _nextThreatPoll = now + PollInterval;
            DrawThreats(held ? _dragger.Held : null, held, focus);
        }

        if (focus == null) { HideMarkers(); return; }

        // A unit in hand is recomputed every frame so the badges track the cursor; a still one is
        // polled, since only equipment changes could move its effects.
        if (inHand || now >= _nextPoll)
        {
            _nextPoll = now + PollInterval;
            Collect(focus, inHand);
        }

        Apply(alpha);
        Follow();
    }

    private void FindTools()
    {
        if (_lookedForTools) return;
        _lookedForTools = true;

        _dragger = FindObjectOfType<FormationDragger>();
        _inspector = FindObjectOfType<UnitInspector>();
        if (_dragger != null) _dragger.OnDropped += Dropped;
    }

    private void Dropped(Entity unit)
    {
        _lingering = unit;
        _lingerUntil = Time.unscaledTime + LingerSeconds;
        Collect(unit, inHand: false);
    }

    private void HandleState(GameState previous, GameState next)
    {
        if (next != GameState.Combat) return;

        // Runs after GameManager's own handler, so the effects have landed by now; the previews still
        // name the same units, because they read the same formation the effects did.
        _bellUntil = Time.unscaledTime + BellSeconds;
        Collect(null, inHand: false);
    }

    /// <summary>
    /// Gather the badges for one hero — or for every hero when <paramref name="focus"/> is null — and
    /// lay them out one line per engraving per unit. A unit in hand is planned onto the cell under
    /// it first, so its effects are read from where it is about to be, not where it was.
    /// </summary>
    private void Collect(Entity focus, bool inHand)
    {
        _badges.Clear();
        if (_runManager == null) { Reconcile(); return; }

        var formation = _runManager.Formation;
        if (inHand && _dragger != null && _dragger.TryGetPlannedCell(out var cell)) formation.Plan(focus, cell);
        else formation.ClearPlan();

        if (focus != null)
        {
            if (focus.Resonance != null) focus.Resonance.CollectPreviews(_badges);
        }
        else
        {
            var all = EntityRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                var hero = all[i];
                if (hero == null || !hero.isTeam || hero.isDead || hero.Resonance == null) continue;
                hero.Resonance.CollectPreviews(_badges);
            }
        }

        formation.ClearPlan();

        // Group by unit, then by engraving, so two grants of one engraving become one merged line.
        foreach (var group in _byTarget.Values) group.Clear();
        _byTarget.Clear();
        foreach (var badge in _badges)
        {
            if (badge.target == null || badge.engraving == null) continue;
            if (!_byTarget.TryGetValue(badge.target, out var byType))
                _byTarget[badge.target] = byType = new Dictionary<System.Type, List<Engraving.Badge>>();
            var type = badge.engraving.GetType();
            if (!byType.TryGetValue(type, out var list)) byType[type] = list = new List<Engraving.Badge>();
            list.Add(badge);
        }

        _lines.Clear();
        foreach (var pair in _byTarget)
        {
            var text = new StringBuilder();
            foreach (var group in pair.Value.Values)
            {
                _tiers.Clear();
                foreach (var badge in group) _tiers.Add(badge.tier);
                if (text.Length > 0) text.Append('\n');
                text.Append(group[0].engraving.MergedLabel(_tiers));
            }
            _lines[pair.Key] = text.ToString();
        }

        Reconcile();
    }

    /// <summary>Make the markers match the lines: one per unit, none for units no longer touched.</summary>
    private void Reconcile()
    {
        _stale.Clear();
        foreach (var pair in _markers)
            if (pair.Key == null || !_lines.ContainsKey(pair.Key)) _stale.Add(pair.Key);
        foreach (var gone in _stale)
        {
            var marker = _markers[gone];
            if (marker.text != null) Destroy(marker.text.gameObject);
            if (marker.ring != null) Destroy(marker.ring.gameObject);
            _markers.Remove(gone);
        }

        foreach (var pair in _lines)
        {
            if (!_markers.TryGetValue(pair.Key, out var marker) || marker.text == null)
                _markers[pair.Key] = marker = MakeMarker(pair.Key.isTeam ? AllyColor : EnemyColor);

            if (marker.text.text != pair.Value)
            {
                marker.text.text = pair.Value;
                marker.text.ForceMeshUpdate();
            }
            marker.text.gameObject.SetActive(true);
            marker.ring.gameObject.SetActive(true);
        }
    }

    private void Apply(float alpha)
    {
        alpha = Mathf.Clamp01(alpha);
        foreach (var pair in _markers)
        {
            var marker = pair.Value;
            if (marker.text == null) continue;
            var color = pair.Key != null && pair.Key.isTeam ? AllyColor : EnemyColor;
            marker.text.color = new Color(color.r, color.g, color.b, alpha);
            marker.ring.color = new Color(color.r, color.g, color.b, 0.55f * alpha);
        }
    }

    /// <summary>
    /// Badges sit at the unit's head and rings at its feet, wherever it is this frame. Not above the
    /// head: rows on the grid are only 1.25 apart, so anything much higher reads as belonging to the
    /// unit in the row behind.
    /// </summary>
    private void Follow()
    {
        foreach (var pair in _markers)
        {
            if (pair.Key == null || pair.Value.text == null) continue;
            var at = pair.Key.transform.position;
            pair.Value.text.transform.position = at + Vector3.up * HeadHeight;
            pair.Value.ring.transform.position = at + Vector3.up * 0.06f;
        }
    }

    private void Hide()
    {
        HideMarkers();
        HideThreats();
    }

    private void HideMarkers()
    {
        _lines.Clear();
        foreach (var pair in _markers)
        {
            if (pair.Value.text != null && pair.Value.text.gameObject.activeSelf) pair.Value.text.gameObject.SetActive(false);
            if (pair.Value.ring != null && pair.Value.ring.gameObject.activeSelf) pair.Value.ring.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// One line per enemy, from where it stands to the hero it will engage at the bell, computed by
    /// the same rule targeting uses — under the plan, so a hero in hand sees the lines move with it.
    /// Lines that end on the hero being looked at are drawn brighter.
    /// </summary>
    private void DrawThreats(Entity planned, bool inHand, Entity focus)
    {
        if (_runManager == null) { HideThreats(); return; }

        var formation = _runManager.Formation;
        if (inHand && _dragger != null && _dragger.TryGetPlannedCell(out var cell)) formation.Plan(planned, cell);
        else formation.ClearPlan();
        var board = BoardSnapshot.Capture(formation, planned: true);
        formation.ClearPlan();

        var grid = BattleGrid.Instance;
        int used = 0;
        foreach (var enemy in board.Units)
        {
            if (enemy == null || enemy.isTeam) continue;

            var target = BoardSnapshot.PredictOpening(board, enemy);
            if (target == null || !board.TryGet(target, out var to)) continue;

            var line = used < _threats.Count ? _threats[used] : MakeThreat();
            used++;

            // The hero's planned cell rather than its transform: a hero in hand is under the cursor,
            // and the line should point at where it will land.
            Vector3 from = enemy.transform.position + Vector3.up * 0.35f;
            Vector3 at = (grid != null ? grid.CellToWorld(true, to.column, to.row) : target.transform.position) + Vector3.up * 0.35f;
            Lay(line, from, at);

            bool onFocus = focus != null && target == focus;
            line.color = new Color(ThreatColor.r, ThreatColor.g, ThreatColor.b, onFocus ? 0.85f : 0.3f);
            line.gameObject.SetActive(true);
        }
        for (int i = used; i < _threats.Count; i++)
            if (_threats[i] != null) _threats[i].gameObject.SetActive(false);
    }

    private void HideThreats()
    {
        foreach (var line in _threats)
            if (line != null && line.gameObject.activeSelf) line.gameObject.SetActive(false);
    }

    private static void Lay(SpriteRenderer line, Vector3 from, Vector3 to)
    {
        var delta = to - from;
        line.transform.position = (from + to) * 0.5f;
        line.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        line.transform.localScale = new Vector3(delta.magnitude, 0.06f, 1f);
    }

    private SpriteRenderer MakeThreat()
    {
        var go = new GameObject("ThreatLine");
        go.transform.SetParent(transform, false);
        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = LineSprite();
        renderer.sortingLayerName = "Default";
        renderer.sortingOrder = -402;                // on the ground, under the rings
        _threats.Add(renderer);
        return renderer;
    }

    /// <summary>A one-unit white square, stretched into a line by scale.</summary>
    private static Sprite LineSprite()
    {
        if (_lineSprite != null) return _lineSprite;
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        var pixels = new Color[4];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
        texture.SetPixels(pixels);
        texture.Apply();
        _lineSprite = Sprite.Create(texture, new Rect(0f, 0f, 2f, 2f), new Vector2(0.5f, 0.5f), 2f);
        return _lineSprite;
    }

    private Marker MakeMarker(Color color)
    {
        var label = new GameObject("FormationBadge");
        label.transform.SetParent(transform, false);

        var tmp = label.AddComponent<TextMeshPro>();
        var font = TMP_Settings.defaultFontAsset;
        if (font != null) tmp.font = font;
        tmp.fontSize = 3.2f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = false;
        tmp.raycastTarget = false;
        tmp.color = color;
        tmp.outlineWidth = 0.5f;
        tmp.outlineColor = Color.black;

        var renderer = label.GetComponent<MeshRenderer>();
        if (renderer != null) renderer.sortingOrder = 32000;   // above sprites and bars, under callouts

        // The same ring the inspector draws under a selected unit, so "affected" and "selected" read
        // as the same kind of mark in two colours rather than two vocabularies.
        var ringObject = new GameObject("FormationRing");
        ringObject.transform.SetParent(transform, false);
        var ring = ringObject.AddComponent<SpriteRenderer>();
        ring.sprite = UnitInspector.RingSprite();
        ring.sortingLayerName = "Default";
        ring.sortingOrder = -401;                                // just under the inspector's ring
        ringObject.transform.localScale = new Vector3(1.6f, 0.52f, 1f);
        ring.color = new Color(color.r, color.g, color.b, 0.55f);

        return new Marker { text = tmp, ring = ring };
    }
}
