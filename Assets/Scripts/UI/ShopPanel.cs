using System.Collections.Generic;
using Assets.HeroEditor.InventorySystem.Scripts;
using Assets.HeroEditor.InventorySystem.Scripts.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The shop after a won fight (Docs/ShopLoop.md): the gold the company holds, a row of items at
/// rolled rarities with their prices, a reroll, and the way on to the next fight. A card is bought by
/// clicking it; a bought card reads Sold; a card the gold does not reach shows its price in red.
///
/// It replaced the pick-one-of-three reward panel, and keeps its card: the rarity's colour, the icon,
/// the name with its grade, and what the effect does at that grade with the quest that would engrave
/// it — because choosing between items is a comparison of real numbers, and a price is one more.
///
/// It doubles as the run's status line, since the run is otherwise mute: which fight you're on, and
/// that you won it.
/// </summary>
public class ShopPanel : MonoBehaviour
{
    private static readonly Color Gold = new Color(1f, 0.82f, 0.28f, 1f);
    private static readonly Color Backdrop = new Color(0f, 0f, 0f, 0.6f);
    private static readonly Color TooDear = new Color(1f, 0.45f, 0.4f, 1f);
    private static readonly Color SoldFace = new Color(0.12f, 0.12f, 0.13f, 0.9f);
    private static readonly Color ButtonFace = new Color(0.55f, 0.42f, 0.12f, 1f);
    private static readonly Color ButtonOff = new Color(0.28f, 0.28f, 0.28f, 1f);

    private const float CardWidth = 200f, CardHeight = 300f, CardGap = 16f;

    private RunManager _runManager;
    private GameObject _root;
    private TextMeshProUGUI _heading;
    private TextMeshProUGUI _gold;
    private Transform _cardRow;
    private Button _reroll;
    private Image _rerollFace;
    private TextMeshProUGUI _rerollLabel;
    private readonly List<GameObject> _cards = new List<GameObject>();

    public void Initialize(RunManager runManager, Transform canvas)
    {
        _runManager = runManager;
        if (_runManager == null || canvas == null) return;

        Build(canvas);
        _runManager.OnShopChanged += Redraw;
        Redraw();
    }

    private void OnDestroy()
    {
        if (_runManager != null) _runManager.OnShopChanged -= Redraw;
    }

    private void Redraw()
    {
        if (_root == null) return;

        _root.SetActive(_runManager.ShopOpen);
        if (!_runManager.ShopOpen) return;

        _heading.text = _runManager.State != null ? $"Victory — {_runManager.State.Progress}  ·  the shop" : "The shop";
        _gold.text = $"{_runManager.Gold} gold";

        bool canReroll = _runManager.Gold >= _runManager.RerollCost;
        _reroll.interactable = canReroll;
        _rerollFace.color = canReroll ? ButtonFace : ButtonOff;
        _rerollLabel.text = $"Reroll  ({_runManager.RerollCost} gold)";

        foreach (var card in _cards) Destroy(card);
        _cards.Clear();

        var offers = _runManager.ShopOffers;
        for (int slot = 0; slot < offers.Count; slot++) _cards.Add(BuildCard(offers[slot], slot));
    }

    /// <summary>One offer: its icon, name and grade, what its effect does, its quest, and its price.</summary>
    private GameObject BuildCard(Item offer, int slot)
    {
        var card = new GameObject(offer != null ? "Offer_" + offer.Id : "Sold", typeof(RectTransform));
        card.transform.SetParent(_cardRow, false);
        card.GetComponent<RectTransform>().sizeDelta = new Vector2(CardWidth, CardHeight);

        // The layout group lays children out by their *preferred* size, which a bare RectTransform
        // reports as zero — without this every card collapses onto the same point.
        var layoutElement = card.AddComponent<LayoutElement>();
        layoutElement.preferredWidth = CardWidth;
        layoutElement.preferredHeight = CardHeight;

        var face = card.AddComponent<Image>();
        if (offer == null)
        {
            face.color = SoldFace;
            var sold = NewText("Sold", card.transform, 22f, new Color(0.6f, 0.6f, 0.6f, 1f));
            Place(sold.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(CardWidth, 40f), Vector2.zero);
            sold.text = "Sold";
            return card;
        }

        string itemId = offer.Id;
        int rarity = Rarity.Of(offer);
        int price = _runManager.PriceOf(offer);
        bool affordable = _runManager.Gold >= price;

        // The card is the rarity's colour, as its slot will be once it is bought.
        face.color = Rarity.CardColorOf(rarity);
        var button = card.AddComponent<Button>();
        button.targetGraphic = face;
        int captured = slot;
        button.onClick.AddListener(() => _runManager.Buy(captured));

        var itemParams = ItemCollection.Active != null ? ItemCollection.Active.Items.Find(i => i.Id == itemId) : null;

        // Icon, drawn from the same collection the inventory uses so offers look like the gear they are.
        var icon = itemParams != null ? ItemCollection.Active.GetItemIcon(new Item(itemId)) : null;
        if (icon != null && icon.Sprite != null)
        {
            var iconObject = NewChild("Icon", card.transform, new Vector2(0.5f, 1f), new Vector2(96f, 96f), new Vector2(0f, -60f));
            var image = iconObject.AddComponent<Image>();
            image.sprite = icon.Sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
        }

        var name = NewText("Name", card.transform, 17f, Gold);
        Place(name.rectTransform, new Vector2(0.5f, 1f), new Vector2(CardWidth - 12f, 44f), new Vector2(0f, -132f));
        name.text = Readable(itemParams, itemId) + "  " + Rarity.Tag(rarity);

        // Real numbers, not prose, at this copy's rarity: what it will do on the next fight, and what
        // its quest will engrave.
        var entry = ResonanceDatabase.Active != null ? ResonanceDatabase.Active.FindFor(new Item(itemId)) : null;
        var detail = NewText("Detail", card.transform, 13f, entry != null ? Gold : new Color(0.75f, 0.75f, 0.75f, 1f));
        Place(detail.rectTransform, new Vector2(0.5f, 0f), new Vector2(CardWidth - 14f, 110f), new Vector2(0f, 96f));
        detail.text = entry != null && entry.engraving != null
            ? Keywords.Decorate($"<b>{entry.engraving.DisplayName}</b>\n{entry.engraving.DescribeTier(rarity)}\n" +
                                $"<size=85%>Quest: {entry.questGoal} {ResonanceRequirements.Describe(entry.requirement)}</size>")
            : "No effect — its stats only.";

        var cost = NewText("Price", card.transform, 18f, affordable ? Gold : TooDear);
        Place(cost.rectTransform, new Vector2(0.5f, 0f), new Vector2(CardWidth, 28f), new Vector2(0f, 20f));
        cost.text = $"{price} gold";

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
            if (!string.IsNullOrEmpty(localized) && localized != itemId) return DisplayNames.Item(localized);
        }

        int dot = itemId.LastIndexOf('.');
        return DisplayNames.Item(dot >= 0 ? itemId.Substring(dot + 1) : itemId);
    }

    #region Construction

    private void Build(Transform canvas)
    {
        _root = new GameObject("ShopPanel", typeof(RectTransform));
        _root.transform.SetParent(canvas, false);

        var rootRect = _root.GetComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;

        // A dim backdrop so the shop reads as the thing to deal with, without hiding the board.
        var shade = _root.AddComponent<Image>();
        shade.color = Backdrop;
        shade.raycastTarget = false;

        UiLayer.Raise(_root, UiLayer.Reward);

        _heading = NewText("Heading", _root.transform, 32f, Gold);
        Place(_heading.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(900f, 46f), new Vector2(0f, 262f));

        _gold = NewText("Gold", _root.transform, 26f, Gold);
        Place(_gold.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(400f, 36f), new Vector2(0f, 218f));

        float rowWidth = 5 * CardWidth + 4 * CardGap;
        var row = NewChild("Cards", _root.transform, new Vector2(0.5f, 0.5f), new Vector2(rowWidth, CardHeight + 10f), new Vector2(0f, 38f));
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = CardGap;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        _cardRow = row.transform;

        _reroll = NewButton("Reroll", new Vector2(-130f, -150f), () => _runManager.Reroll(), out _rerollFace, out _rerollLabel);
        NewButton("Continue", new Vector2(130f, -150f), () => _runManager.LeaveShop(), out _, out var continueLabel);
        continueLabel.text = "Continue";

        _root.SetActive(false);
    }

    private Button NewButton(string name, Vector2 position, UnityEngine.Events.UnityAction onClick, out Image face, out TextMeshProUGUI label)
    {
        var go = NewChild(name, _root.transform, new Vector2(0.5f, 0.5f), new Vector2(220f, 44f), position);
        face = go.AddComponent<Image>();
        face.color = ButtonFace;
        var button = go.AddComponent<Button>();
        button.targetGraphic = face;
        button.onClick.AddListener(onClick);

        label = NewText("Label", go.transform, 18f, Color.white);
        var rect = label.rectTransform;
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
        return button;
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
        return text;
    }

    #endregion
}
