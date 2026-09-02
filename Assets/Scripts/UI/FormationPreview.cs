using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// Badges over the units a positional engraving will touch when the fight begins — "MARKED · 80%"
/// over the enemy across from the bearer, "VANGUARD +20%" over a hero in the front rank — shown
/// while the company is being arranged and redrawn as heroes are dragged.
///
/// This is the telegraph that turns a positional effect into a decision. The effects themselves
/// resolve in one frame at the bell, before anyone can look; whatever a player learns from
/// watching that moment, they learn after the choice was made. What they need is to see, while a
/// hero is still in their hand, which enemy will be marked if they put it down here.
///
/// Polled rather than wired to events: the formation, the equipment, the encounter and the game
/// state can each change what the badges should say, and five heroes' previews cost nothing to
/// recompute ten times a second.
/// </summary>
public class FormationPreview : MonoBehaviour
{
    private const float PollInterval = 0.1f;
    private const float HeadHeight = 1.05f;
    private static readonly Color BadgeColor = new Color(1f, 0.88f, 0.3f, 1f);

    private RunManager _runManager;
    private readonly List<Engraving.Badge> _previews = new List<Engraving.Badge>();
    private readonly Dictionary<Entity, StringBuilder> _lines = new Dictionary<Entity, StringBuilder>();
    private readonly Dictionary<Entity, TextMeshPro> _badges = new Dictionary<Entity, TextMeshPro>();
    private readonly List<Entity> _stale = new List<Entity>();
    private float _nextPoll;

    public void Initialize(RunManager runManager) => _runManager = runManager;

    /// <summary>What is showing right now, as "unit: label" lines — for probes and tests.</summary>
    public List<string> Describe()
    {
        var lines = new List<string>();
        foreach (var pair in _badges)
            if (pair.Key != null && pair.Value != null && pair.Value.gameObject.activeSelf)
                lines.Add(pair.Key.name + ": " + pair.Value.text.Replace("\n", " | "));
        lines.Sort();
        return lines;
    }

    private void LateUpdate()
    {
        var game = GameManager.Instance;
        bool arranging = game != null && game.StateMachine.Current == GameState.Setup;
        if (!arranging)
        {
            HideAll();
            return;
        }

        if (Time.unscaledTime >= _nextPoll)
        {
            _nextPoll = Time.unscaledTime + PollInterval;
            Redraw();
        }

        Follow();
    }

    private void Redraw()
    {
        _previews.Clear();
        if (_runManager != null)
        {
            var all = EntityRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                var hero = all[i];
                if (hero == null || !hero.isTeam || hero.isDead || hero.Resonance == null) continue;
                hero.Resonance.CollectPreviews(_previews);
            }
        }

        // One badge per unit with its lines stacked, so two effects landing on the same enemy read
        // as two lines rather than two labels fighting for the same spot.
        _lines.Clear();
        foreach (var preview in _previews)
        {
            if (preview.target == null || string.IsNullOrEmpty(preview.label)) continue;
            if (!_lines.TryGetValue(preview.target, out var text))
                _lines[preview.target] = text = new StringBuilder();
            if (text.Length > 0) text.Append('\n');
            text.Append(preview.label);
        }

        _stale.Clear();
        foreach (var pair in _badges)
            if (pair.Key == null || !_lines.ContainsKey(pair.Key)) _stale.Add(pair.Key);
        foreach (var gone in _stale)
        {
            if (_badges[gone] != null) Destroy(_badges[gone].gameObject);
            _badges.Remove(gone);
        }

        foreach (var pair in _lines)
        {
            if (!_badges.TryGetValue(pair.Key, out var badge) || badge == null)
                _badges[pair.Key] = badge = MakeBadge();

            string wanted = pair.Value.ToString();
            if (badge.text != wanted)
            {
                badge.text = wanted;
                badge.ForceMeshUpdate();
            }
            badge.gameObject.SetActive(true);
        }
    }

    /// <summary>
    /// Badges sit at the unit's head, wherever it is this frame. Not above it: rows on the grid are
    /// only 1.25 apart, so anything much higher than the head reads as belonging to the unit in the
    /// row behind — the first draft used the health-bar offset and marked the wrong enemy to the eye.
    /// </summary>
    private void Follow()
    {
        foreach (var pair in _badges)
        {
            if (pair.Key == null || pair.Value == null) continue;
            pair.Value.transform.position = pair.Key.transform.position + Vector3.up * HeadHeight;
        }
    }

    private void HideAll()
    {
        foreach (var pair in _badges)
            if (pair.Value != null && pair.Value.gameObject.activeSelf) pair.Value.gameObject.SetActive(false);
    }

    private TextMeshPro MakeBadge()
    {
        var go = new GameObject("FormationBadge");
        go.transform.SetParent(transform, false);

        var tmp = go.AddComponent<TextMeshPro>();
        var font = TMP_Settings.defaultFontAsset;
        if (font != null) tmp.font = font;
        tmp.fontSize = 3.2f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = false;
        tmp.raycastTarget = false;
        tmp.color = BadgeColor;
        tmp.outlineWidth = 0.5f;
        tmp.outlineColor = Color.black;

        var renderer = go.GetComponent<MeshRenderer>();
        if (renderer != null) renderer.sortingOrder = 32000;   // above sprites and bars, under callouts

        return tmp;
    }
}
