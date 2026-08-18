using System.Collections.Generic;
using Assets.HeroEditor.InventorySystem.Scripts;
using Assets.HeroEditor.InventorySystem.Scripts.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The spoils of a won fight: pick one item of several, the rest are lost.
///
/// This is what gives resonance its opportunity cost. A freed slot is only worth what can be put in
/// it, and a fresh item is only interesting because it competes with what a hero has already sunk
/// attunement into — without drops, cashing out trades an item's stats for nothing
/// (Docs/Resonance.md).
///
/// It doubles as the run's status line, because the run was otherwise mute: which fight you're on,
/// and that you won it, appeared only in the console.
/// </summary>
public class RewardPanel : MonoBehaviour
{
    private static readonly Color Gold = new Color(1f, 0.82f, 0.28f, 1f);
    private static readonly Color CardFace = new Color(0.18f, 0.15f, 0.11f, 0.96f);
    private static readonly Color Backdrop = new Color(0f, 0f, 0f, 0.55f);

    private RunManager _runManager;
    private GameObject _root;
    private TextMeshProUGUI _heading;
    private Transform _cardRow;
    private readonly List<GameObject> _cards = new List<GameObject>();

    public void Initialize(RunManager runManager, Transform canvas)
    {
        _runManager = runManager;
        if (_runManager == null || canvas == null) return;

        Build(canvas);
        _runManager.OnRewardsChanged += Redraw;
        Redraw();
    }

    private void OnDestroy()
    {
        if (_runManager != null) _runManager.OnRewardsChanged -= Redraw;
    }

    private void Redraw()
    {
        if (_root == null) return;

        var offers = _runManager.PendingRewards;
        _root.SetActive(offers.Count > 0);
        if (offers.Count == 0) return;

        // Says outright that the next fight is waiting, since the panel now blocks it — a player
        // pressing Space and getting nothing deserves to know why.
        _heading.text = _runManager.State != null
            ? $"Victory — {_runManager.State.Progress}.   Choose your spoils to continue."
            : "Choose your spoils to continue.";

        foreach (var card in _cards) Destroy(card);
        _cards.Clear();

        foreach (var id in offers) _cards.Add(BuildCard(id));
    }

    /// <summary>One offered item: its icon, name, and the engraving it carries if any.</summary>
    private GameObject BuildCard(string itemId)
    {
        var card = new GameObject("Reward_" + itemId, typeof(RectTransform));
        card.transform.SetParent(_cardRow, false);

        var rect = card.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(240f, 300f);

        // The layout group lays children out by their *preferred* size, which a bare RectTransform
        // reports as zero — without this every card collapses onto the same point.
        var layoutElement = card.AddComponent<LayoutElement>();
        layoutElement.preferredWidth = 240f;
        layoutElement.preferredHeight = 300f;

        var face = card.AddComponent<Image>();
        face.color = CardFace;

        var button = card.AddComponent<Button>();
        button.targetGraphic = face;
        string captured = itemId;
        button.onClick.AddListener(() => _runManager.TakeReward(captured));

        var itemParams = ItemCollection.Active != null
            ? ItemCollection.Active.Items.Find(i => i.Id == itemId) : null;

        // Icon, drawn from the same collection the inventory uses so rewards look like the gear they are.
        var icon = itemParams != null ? ItemCollection.Active.GetItemIcon(new Item(itemId)) : null;
        var sprite = icon != null ? icon.Sprite : null;
        if (sprite != null)
        {
            var iconObject = NewChild("Icon", card.transform, new Vector2(0.5f, 1f),
                                      new Vector2(120f, 120f), new Vector2(0f, -80f));
            var image = iconObject.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
        }

        var name = NewText("Name", card.transform, 20f, Gold);
        Place(name.rectTransform, new Vector2(0.5f, 1f), new Vector2(220f, 50f), new Vector2(0f, -160f));
        name.text = Readable(itemParams, itemId);

        // The engraving is the reason to want this item, so it gets said plainly.
        var entry = ResonanceDatabase.Active != null ? ResonanceDatabase.Active.Find(itemId) : null;
        var detail = NewText("Detail", card.transform, 16f,
                             entry != null ? Gold : new Color(0.75f, 0.75f, 0.75f, 1f));
        Place(detail.rectTransform, new Vector2(0.5f, 0f), new Vector2(220f, 90f), new Vector2(0f, 60f));
        // Real numbers, not prose. Choosing between three items is a comparison of magnitudes, and
        // "attacks faster" gives the player nothing to compare. Tier I is quoted because that is what
        // the item is worth on the fight after it's taken.
        detail.text = entry != null && entry.engraving != null
            ? $"<b>{entry.engraving.DisplayName}</b>\n{entry.engraving.DescribeTier(1)}"
            : "No engraving.";

        return card;
    }

    /// <summary>
    /// The item's authored name, as the inventory shows it. Falling back to the tail of the id gives
    /// nonsense for multi-part ids — "FantasyHeroes.Basic.Armor.ArielDress [Paint].gloves" reads as
    /// just "gloves", which is a description of a slot rather than the name of a thing.
    /// </summary>
    private static string Readable(ItemParams itemParams, string itemId)
    {
        if (itemParams != null)
        {
            string localized = itemParams.GetLocalizedName(Application.systemLanguage.ToString());
            if (!string.IsNullOrEmpty(localized) && localized != itemId) return localized;
        }

        int dot = itemId.LastIndexOf('.');
        return dot >= 0 ? itemId.Substring(dot + 1) : itemId;
    }

    #region Construction

    private void Build(Transform canvas)
    {
        _root = new GameObject("RewardPanel", typeof(RectTransform));
        _root.transform.SetParent(canvas, false);

        var rootRect = _root.GetComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;

        // A dim backdrop so the choice reads as the thing to deal with, without blocking the board.
        var shade = _root.AddComponent<Image>();
        shade.color = Backdrop;
        shade.raycastTarget = false;

        _heading = NewText("Heading", _root.transform, 34f, Gold);
        Place(_heading.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(900f, 50f),
              new Vector2(0f, 230f));

        var row = NewChild("Cards", _root.transform, new Vector2(0.5f, 0.5f),
                           new Vector2(820f, 320f), new Vector2(0f, 20f));
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 30f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        _cardRow = row.transform;

        _root.SetActive(false);
    }

    private static GameObject NewChild(string name, Transform parent, Vector2 anchor, Vector2 size,
                                       Vector2 position)
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
        return text;
    }

    #endregion
}
