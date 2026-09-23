using System.Text;
using Assets.HeroEditor.InventorySystem.Scripts.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shows a hero's resonance in their character window: the selected item's rarity and what its
/// effect does at it, how far its quest has come, and the engravings the hero has kept.
///
/// There is nothing to decide here any more — a quest completes and is engraved on its own when the
/// fight ends (Docs/ShopLoop.md) — so the panel reports rather than asks. It used to carry a tier bar
/// and a cash-out button for the three-tier loop that rarity replaced.
///
/// Built at runtime rather than authored into the window prefab, so the vendor inventory prefab is
/// left untouched.
/// </summary>
public class ResonancePanel : MonoBehaviour
{
    private static readonly Color Gold = new Color(1f, 0.82f, 0.28f, 1f);
    private static readonly Color BarBack = new Color(0f, 0f, 0f, 0.45f);
    private static readonly Color ButtonReady = new Color(0.55f, 0.42f, 0.12f, 1f);
    private static readonly Color ButtonBlocked = new Color(0.28f, 0.28f, 0.28f, 1f);

    private CharacterInventory _inventory;
    private Entity _hero;

    private GameObject _block;
    private TextMeshProUGUI _title;
    private TextMeshProUGUI _detail;
    private RectTransform _barFill;
    private TextMeshProUGUI _bankedLabel;

    private Item _selected;

    public void Initialize(CharacterInventory inventory, Entity hero)
    {
        _inventory = inventory;
        _hero = hero;
        if (_inventory == null || _hero == null) return;

        BuildSelectionBlock();
        BuildBankedLabel();

        _inventory.OnSelectionChanged += HandleSelection;
        _inventory.Equipment.OnRefresh += Redraw;
        _hero.Resonance.OnAttunementChanged += MarkDirty;

        Redraw();
    }

    private void OnDestroy()
    {
        if (_hero != null && _hero.Resonance != null) _hero.Resonance.OnAttunementChanged -= MarkDirty;
        if (_inventory == null) return;
        _inventory.OnSelectionChanged -= HandleSelection;
        _inventory.Equipment.OnRefresh -= Redraw;
    }

    // Counters can tick many times per second in a busy fight — several hits, a kill, a cast — so the
    // panel coalesces them into one repaint per frame rather than rebuilding text on every event.
    private bool _dirty;
    private void MarkDirty() => _dirty = true;

    private void LateUpdate()
    {
        if (!_dirty) return;
        _dirty = false;
        Redraw();
    }

    private void HandleSelection(Item item)
    {
        _selected = item;
        Redraw();
    }

    /// <summary>Repaint from current state — attunement only changes between fights, so this is cheap.</summary>
    private void Redraw()
    {
        if (_block == null || _hero == null || _hero.Resonance == null) return;

        UpdateBanked();

        // Through the hero's Resonance rather than the database directly, so this inherits its view
        // of what still resonates — in particular, a hollow item has already given up its engraving
        // and answers null, which is what stops the panel offering to engrave a spent item again.
        var entry = _hero.Resonance.EntryFor(_selected);

        if (entry == null || entry.engraving == null)
        {
            _block.SetActive(false);
            return;
        }

        _block.SetActive(true);

        int rarity = Rarity.Of(_selected);
        _title.text = entry.engraving.DisplayName + "  " + Rarity.Tag(rarity);
        string effect = entry.engraving.DescribeTier(rarity);

        // Name what the counter counts. "525 / 900" alone doesn't say whether that's fights, kills or
        // damage, so the player can't tell how close they are or what to do to get there.
        string unit = ResonanceRequirements.Describe(entry.requirement);

        // An item in the bag shows what it carries and what its quest asks — deciding whether to equip
        // it is exactly when the player needs both — with its progress paused where it was left.
        bool worn = _inventory.Equipment.Items.Contains(_selected);
        float progress = _hero.Resonance.AttunementFor(_selected);
        bool complete = entry.IsComplete(progress);

        string quest = !worn ? $"Quest: {progress:0} / {entry.questGoal} {unit} while worn (paused) — then engraved for good at {Rarity.Letter(rarity)}."
                     : complete ? "<b>Quest complete</b> — engraved for good when this fight ends; the item is spent."
                     : $"Quest: {progress:0} / {entry.questGoal} {unit} — then engraved for good at {Rarity.Letter(rarity)}.";

        _detail.text = Keywords.Decorate(effect + "\n" + quest);
        _barFill.anchorMax = new Vector2(Mathf.Clamp01(progress / Mathf.Max(1f, entry.questGoal)), 1f);
    }

    private void UpdateBanked()
    {
        if (_bankedLabel == null) return;

        var banked = _hero.Resonance.banked;
        if (banked == null || banked.Count == 0)
        {
            _bankedLabel.text = "Engraved: —";
            return;
        }

        // Each mark with what it actually does — a list of names alone doesn't tell the player what
        // their hero has become, which is the whole point of banking them.
        var text = new StringBuilder("<b>Engraved</b>");
        foreach (var mark in banked)
        {
            if (mark == null || mark.engraving == null) continue;
            text.Append("\n<color=#FFD147>")
                .Append(mark.engraving.DisplayName).Append("</color> ")
                .Append(Rarity.Tag(mark.tier)).Append("  ")
                .Append(mark.engraving.DescribeTier(mark.tier));
        }
        _bankedLabel.text = Keywords.Decorate(text.ToString());
    }

    #region Construction

    private void BuildSelectionBlock()
    {
        // The right-hand panel shows the selected item, which is exactly what this describes.
        var host = FindPanel("ItemInfo") ?? FindPanel("Equipment");
        if (host == null) return;

        // Threads a narrow gap: the item's stat lines end about 290 units up, and the window's own
        // Equip/Remove buttons start about 95 up, so the block sits between them.
        _block = NewRect("ResonanceBlock", host, new Vector2(0.5f, 0f), new Vector2(360f, 168f),
                         new Vector2(0f, 190f));

        _title = NewText("Title", _block.transform, 24f, Gold, TextAlignmentOptions.Center);
        Anchor(_title.rectTransform, new Vector2(0.5f, 1f), new Vector2(340f, 30f), new Vector2(0f, -6f));

        _detail = NewText("Detail", _block.transform, 15f, Color.white, TextAlignmentOptions.Top);
        _detail.enableWordWrapping = true;
        Anchor(_detail.rectTransform, new Vector2(0.5f, 1f), new Vector2(340f, 78f), new Vector2(0f, -58f));

        // Bar: a dark trough with a gold fill stretched by anchorMax.
        var trough = NewRect("BarBack", _block.transform, new Vector2(0.5f, 1f), new Vector2(320f, 14f),
                             new Vector2(0f, -104f));
        var troughImage = trough.AddComponent<Image>();
        troughImage.color = BarBack;
        troughImage.raycastTarget = false;

        var fill = NewRect("BarFill", trough.transform, new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
        _barFill = fill.GetComponent<RectTransform>();
        _barFill.anchorMin = new Vector2(0f, 0f);
        _barFill.anchorMax = new Vector2(0f, 1f);
        _barFill.offsetMin = Vector2.zero;
        _barFill.offsetMax = Vector2.zero;
        var fillImage = fill.AddComponent<Image>();
        fillImage.color = Gold;
        fillImage.raycastTarget = false;
    }

    private void BuildBankedLabel()
    {
        // Banked engravings belong with the hero, not with any one item — they outlive every item.
        var host = FindPanel("HeroStats") ?? FindPanel("Equipment");
        if (host == null) return;

        _bankedLabel = NewText("BankedEngravings", host, 15f, Color.white, TextAlignmentOptions.Top);
        _bankedLabel.enableWordWrapping = true;
        Anchor(_bankedLabel.rectTransform, new Vector2(0.5f, 0f), new Vector2(380f, 110f),
               new Vector2(0f, 55f));
    }

    private Transform FindPanel(string named)
    {
        var found = _inventory.transform.Find(named);
        return found != null ? found : null;
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
