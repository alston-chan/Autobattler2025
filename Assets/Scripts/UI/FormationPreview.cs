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

        if (focus == null) { Hide(); return; }

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
        _lines.Clear();
        foreach (var pair in _markers)
        {
            if (pair.Value.text != null && pair.Value.text.gameObject.activeSelf) pair.Value.text.gameObject.SetActive(false);
            if (pair.Value.ring != null && pair.Value.ring.gameObject.activeSelf) pair.Value.ring.gameObject.SetActive(false);
        }
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
