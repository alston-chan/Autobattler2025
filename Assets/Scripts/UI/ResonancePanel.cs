using System.Text;
using Assets.HeroEditor.InventorySystem.Scripts.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shows a hero's resonance in their character window: how far the selected item has attuned, what
/// tier it has reached, a button to cash it out, and the engravings already banked.
///
/// Resonance is otherwise invisible — attunement, tiers and banking all happen silently, and the
/// bank-or-press decision the mechanic exists to create can't be made against numbers the player
/// can't see. The panel follows the selection so the question is always about a specific item:
/// "this one is at Tier II — cash it out and free the slot, or wear it longer?"
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
    private Button _resonateButton;
    private Image _resonateBackground;
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

        var database = ResonanceDatabase.Active;
        var entry = _selected != null && database != null ? database.Find(_selected.Id) : null;

        // Only equipped items attune, so an item sitting in the bag has nothing to show.
        bool worn = _selected != null && _inventory.Equipment.Items.Contains(_selected);
        if (entry == null || entry.engraving == null || !worn)
        {
            _block.SetActive(false);
            return;
        }

        _block.SetActive(true);

        float attunement = _hero.Resonance.AttunementFor(_selected);
        int tier = entry.TierAt(attunement);
        int next = entry.NextTierCost(attunement);

        _title.text = entry.engraving.DisplayName + (tier > 0 ? "  " + Roman(tier) : "");

        _detail.text = tier >= 3
            ? "Fully attuned — resonate to bank it and free the slot."
            : $"Attuned {attunement:0} / {next}   →   {Roman(tier + 1)}";

        // Progress within the current tier band, so the bar restarts at each threshold.
        int bandStart = tier == 0 ? 0 : (tier == 1 ? entry.tierICost : entry.tierIICost);
        float span = Mathf.Max(1f, next - bandStart);
        float fill = tier >= 3 ? 1f : Mathf.Clamp01((attunement - bandStart) / span);
        _barFill.anchorMax = new Vector2(fill, 1f);

        bool canResonate = tier >= 1;
        _resonateButton.interactable = canResonate;
        _resonateBackground.color = canResonate ? ButtonReady : ButtonBlocked;
        _resonateButton.GetComponentInChildren<TextMeshProUGUI>().text =
            canResonate ? $"Resonate  {Roman(tier)}" : "Not yet attuned";
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

        var text = new StringBuilder("Engraved: ");
        for (int i = 0; i < banked.Count; i++)
        {
            if (banked[i] == null || banked[i].engraving == null) continue;
            if (i > 0) text.Append(", ");
            text.Append(banked[i].engraving.DisplayName).Append(' ').Append(Roman(banked[i].tier));
        }
        _bankedLabel.text = text.ToString();
    }

    private void Resonate()
    {
        if (_selected == null || _hero == null || _hero.Resonance == null) return;

        if (!_hero.Resonance.Resonate(_selected)) return;

        // The item is gone, so the selection it was showing no longer exists.
        _selected = null;
        Redraw();
    }

    private static string Roman(int tier) => tier switch
    {
        1 => "I",
        2 => "II",
        3 => "III",
        _ => ""
    };

    #region Construction

    private void BuildSelectionBlock()
    {
        // The right-hand panel shows the selected item, which is exactly what this describes.
        var host = FindPanel("ItemInfo") ?? FindPanel("Equipment");
        if (host == null) return;

        // Threads a narrow gap: the item's stat lines end about 290 units up, and the window's own
        // Equip/Remove buttons start about 95 up, so the block sits between them.
        _block = NewRect("ResonanceBlock", host, new Vector2(0.5f, 0f), new Vector2(360f, 150f),
                         new Vector2(0f, 185f));

        _title = NewText("Title", _block.transform, 24f, Gold, TextAlignmentOptions.Center);
        Anchor(_title.rectTransform, new Vector2(0.5f, 1f), new Vector2(340f, 30f), new Vector2(0f, -6f));

        _detail = NewText("Detail", _block.transform, 18f, Color.white, TextAlignmentOptions.Center);
        Anchor(_detail.rectTransform, new Vector2(0.5f, 1f), new Vector2(340f, 24f), new Vector2(0f, -40f));

        // Bar: a dark trough with a gold fill stretched by anchorMax.
        var trough = NewRect("BarBack", _block.transform, new Vector2(0.5f, 1f), new Vector2(320f, 14f),
                             new Vector2(0f, -72f));
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

        BuildButton();
    }

    private void BuildButton()
    {
        var buttonObject = NewRect("ResonateButton", _block.transform, new Vector2(0.5f, 1f),
                                   new Vector2(220f, 40f), new Vector2(0f, -110f));

        _resonateBackground = buttonObject.AddComponent<Image>();
        _resonateBackground.color = ButtonReady;

        _resonateButton = buttonObject.AddComponent<Button>();
        _resonateButton.targetGraphic = _resonateBackground;
        _resonateButton.onClick.AddListener(Resonate);

        var label = NewText("Label", buttonObject.transform, 20f, Color.white, TextAlignmentOptions.Center);
        var rect = label.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private void BuildBankedLabel()
    {
        // Banked engravings belong with the hero, not with any one item — they outlive every item.
        var host = FindPanel("HeroStats") ?? FindPanel("Equipment");
        if (host == null) return;

        _bankedLabel = NewText("BankedEngravings", host, 20f, Gold, TextAlignmentOptions.Center);
        Anchor(_bankedLabel.rectTransform, new Vector2(0.5f, 0f), new Vector2(380f, 28f),
               new Vector2(0f, 40f));
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
