using System.Collections.Generic;
using System.Globalization;
using Assets.HeroEditor.InventorySystem.Scripts;
using Assets.HeroEditor.InventorySystem.Scripts.Data;
using Assets.HeroEditor.InventorySystem.Scripts.Enums;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The shop after a won fight (Docs/ShopLoop.md): a framed window with the gold the company holds,
/// a row of items at rolled rarities, and Reroll, Freeze and Continue. A card is bought by clicking
/// it; a bought card reads Sold; a card the gold does not reach shows its price in red. Frozen, the
/// unsold cards are iced over and wait for the next shop.
///
/// A card is everything needed to choose between items without opening anything: the rarity as a
/// letter and a colour, the icon, the name, what kind of item it is, its stats, what its effect does
/// at that rarity, its quest (after which it can be banked), and the price on the buy strip.
///
/// It doubles as the run's status line, since the run is otherwise mute: which fight you're on, and
/// that you won it.
/// </summary>
public class ShopPanel : MonoBehaviour
{
    private static readonly Color Gold = new Color(1f, 0.82f, 0.28f, 1f);
    private static readonly Color Muted = new Color(0.66f, 0.66f, 0.70f, 1f);
    private static readonly Color Backdrop = new Color(0f, 0f, 0f, 0.7f);
    private static readonly Color Frame = new Color(0.08f, 0.08f, 0.10f, 0.97f);
    private static readonly Color FrameEdge = new Color(0.55f, 0.45f, 0.2f, 1f);
    private static readonly Color Well = new Color(0f, 0f, 0f, 0.3f);
    private static readonly Color TooDear = new Color(1f, 0.45f, 0.4f, 1f);
    private static readonly Color SoldFace = new Color(0.11f, 0.11f, 0.12f, 0.9f);
    private static readonly Color BuyStrip = new Color(0.62f, 0.48f, 0.14f, 1f);
    private static readonly Color BuyStripOff = new Color(0.25f, 0.25f, 0.26f, 1f);
    private static readonly Color Primary = new Color(0.62f, 0.48f, 0.14f, 1f);
    private static readonly Color Steel = new Color(0.26f, 0.30f, 0.38f, 1f);
    private static readonly Color Off = new Color(0.24f, 0.24f, 0.25f, 1f);
    private static readonly Color Ice = new Color(0.62f, 0.86f, 1f, 1f);
    private static readonly Color IceFace = new Color(0.22f, 0.48f, 0.66f, 1f);

    private const float FrameWidth = 1240f, FrameHeight = 700f;
    private const float CardWidth = 216f, CardHeight = 390f, CardGap = 18f;

    private RunManager _runManager;
    private GameObject _root;
    private Transform _frame;
    private TextMeshProUGUI _heading;
    private TextMeshProUGUI _subheading;
    private TextMeshProUGUI _gold;
    private TextMeshProUGUI _hint;
    private Transform _cardRow;
    private Button _reroll;
    private Image _rerollFace;
    private TextMeshProUGUI _rerollLabel;
    private Image _freezeFace;
    private TextMeshProUGUI _freezeLabel;
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

        _heading.text = "The Shop";
        _subheading.text = _runManager.State != null ? $"Victory — {_runManager.State.Progress}" : "Victory";
        _gold.text = $"{_runManager.Gold} gold";

        bool canReroll = _runManager.Gold >= _runManager.RerollCost;
        _reroll.interactable = canReroll;
        _rerollFace.color = canReroll ? Steel : Off;
        _rerollLabel.text = $"Reroll · {_runManager.RerollCost} gold";

        bool frozen = _runManager.ShopFrozen;
        _freezeFace.color = frozen ? IceFace : Steel;
        _freezeLabel.text = frozen ? "Frozen" : "Freeze";
        _freezeLabel.color = frozen ? Color.white : Ice;

        _hint.text = frozen
            ? "<color=#9EDBFF>Frozen:</color> what you don't buy will be here after the next fight. A reroll lets it go."
            : "Bought items go to the bag; equip them from a hero's window. Freeze keeps this shelf for the next shop.";

        foreach (var card in _cards) Destroy(card);
        _cards.Clear();

        var offers = _runManager.ShopOffers;
        for (int slot = 0; slot < offers.Count; slot++) _cards.Add(BuildCard(offers[slot], slot, frozen));
    }

    /// <summary>One offer: its rarity, icon, name, kind, stats, effect, quest, and price.</summary>
    private GameObject BuildCard(Item offer, int slot, bool frozen)
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
            var sold = NewText("Sold", card.transform, 22f, Muted);
            Place(sold.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(CardWidth, 40f), Vector2.zero);
            sold.text = "Sold";
            return card;
        }

        string itemId = offer.Id;
        int rarity = Rarity.Of(offer);
        int price = _runManager.PriceOf(offer);
        bool affordable = _runManager.Gold >= price;
        Color edge = frozen ? Ice : Rarity.ColorOf(rarity);

        // The face is the rarity's dark shade, edged in its bright one, as its slot will be once bought.
        var shade = Rarity.CardColorOf(rarity);
        face.color = new Color(shade.r, shade.g, shade.b, 1f);   // opaque, or the edge bleeds through the face
        var outline = card.AddComponent<Outline>();
        outline.effectColor = edge;
        outline.effectDistance = new Vector2(2f, -2f);

        var strip = NewChild("RarityStrip", card.transform, new Vector2(0.5f, 1f), new Vector2(CardWidth, 6f), new Vector2(0f, -3f));
        Paint(strip, edge);

        // The grade, big enough to read across the row at a glance.
        var badge = NewChild("Grade", card.transform, new Vector2(0f, 1f), new Vector2(34f, 34f), new Vector2(26f, -28f));
        Paint(badge, Rarity.ColorOf(rarity));
        var letter = NewText("Letter", badge.transform, 22f, new Color(0.08f, 0.08f, 0.1f, 1f));
        Fill(letter.rectTransform);
        letter.fontStyle = FontStyles.Bold;
        letter.text = Rarity.Letter(rarity);

        if (frozen)
        {
            var tag = NewChild("FrozenTag", card.transform, new Vector2(1f, 1f), new Vector2(74f, 22f), new Vector2(-45f, -24f));
            Paint(tag, IceFace);
            var tagText = NewText("Label", tag.transform, 12f, Color.white);
            Fill(tagText.rectTransform);
            tagText.fontStyle = FontStyles.Bold;
            tagText.text = "FROZEN";
        }

        var itemParams = ItemCollection.Active != null ? ItemCollection.Active.Items.Find(i => i.Id == itemId) : null;

        // Icon, on a dark well, from the same collection the inventory uses so offers look like the gear they are.
        var well = NewChild("Well", card.transform, new Vector2(0.5f, 1f), new Vector2(100f, 100f), new Vector2(0f, -84f));
        Paint(well, Well);
        var icon = itemParams != null ? ItemCollection.Active.GetItemIcon(new Item(itemId)) : null;
        if (icon != null && icon.Sprite != null)
        {
            var iconObject = NewChild("Icon", well.transform, new Vector2(0.5f, 0.5f), new Vector2(86f, 86f), Vector2.zero);
            var image = iconObject.AddComponent<Image>();
            image.sprite = icon.Sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
        }

        var name = NewText("Name", card.transform, 16f, Color.white);
        Place(name.rectTransform, new Vector2(0.5f, 1f), new Vector2(CardWidth - 16f, 40f), new Vector2(0f, -158f));
        name.fontStyle = FontStyles.Bold;
        name.enableAutoSizing = true;
        name.fontSizeMin = 12f;
        name.fontSizeMax = 16f;
        name.text = Readable(itemParams, itemId);

        var kind = NewText("Kind", card.transform, 12f, Muted);
        Place(kind.rectTransform, new Vector2(0.5f, 1f), new Vector2(CardWidth - 16f, 18f), new Vector2(0f, -186f));
        kind.text = Rarity.Tag(rarity) + " · " + KindOf(itemParams);

        var stats = NewText("Stats", card.transform, 12f, new Color(0.86f, 0.9f, 1f, 1f));
        Place(stats.rectTransform, new Vector2(0.5f, 1f), new Vector2(CardWidth - 16f, 20f), new Vector2(0f, -206f));
        stats.enableAutoSizing = true;
        stats.fontSizeMin = 9f;
        stats.fontSizeMax = 12f;
        stats.text = StatLine(itemParams);

        var rule = NewChild("Rule", card.transform, new Vector2(0.5f, 1f), new Vector2(CardWidth - 36f, 1f), new Vector2(0f, -222f));
        Paint(rule, new Color(1f, 1f, 1f, 0.15f));

        // Real numbers, not prose, at this copy's rarity: what it does while worn, and
        // the quest after which it can be banked (a weapon's, as a skill).
        var entry = ResonanceDatabase.Active != null ? ResonanceDatabase.Active.FindFor(new Item(itemId)) : null;
        var effect = NewText("Effect", card.transform, 13f, entry != null ? Gold : Muted);
        Place(effect.rectTransform, new Vector2(0.5f, 1f), new Vector2(CardWidth - 20f, 90f), new Vector2(0f, -272f));
        effect.alignment = TextAlignmentOptions.Top;
        effect.enableAutoSizing = true;
        effect.fontSizeMin = 10f;
        effect.fontSizeMax = 13f;
        effect.text = entry != null && entry.engraving != null
            ? Keywords.Decorate($"<b>{entry.engraving.DisplayName}</b>\n{entry.engraving.DescribeTier(rarity)}")
            : "No effect — its stats only.";

        bool bankable = entry != null && entry.engraving != null;
        if (bankable)
        {
            var quest = NewText("Quest", card.transform, 11f, new Color(0.85f, 0.75f, 0.5f, 1f));
            Place(quest.rectTransform, new Vector2(0.5f, 0f), new Vector2(CardWidth - 16f, 30f), new Vector2(0f, 62f));
            quest.text = $"Bank after {entry.questGoal} {ResonanceRequirements.Describe(entry.requirement)}" + (offer.IsWeapon ? " — as a skill" : "");
        }

        // The buy strip is the button's face, so hovering it lights up what a click will do.
        var buy = NewChild("Buy", card.transform, new Vector2(0.5f, 0f), new Vector2(CardWidth - 16f, 36f), new Vector2(0f, 26f));
        var buyFace = Paint(buy, affordable ? BuyStrip : BuyStripOff);
        buyFace.raycastTarget = true;
        var buyLabel = NewText("Price", buy.transform, 17f, affordable ? Color.white : TooDear);
        Fill(buyLabel.rectTransform);
        buyLabel.fontStyle = FontStyles.Bold;
        buyLabel.text = affordable ? $"Buy · {price} gold" : $"{price} gold";

        face.raycastTarget = true;
        var button = card.AddComponent<Button>();
        button.targetGraphic = buyFace;
        var colors = button.colors;
        colors.normalColor = new Color(0.88f, 0.88f, 0.88f, 1f);
        colors.highlightedColor = Color.white;
        colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
        colors.selectedColor = colors.normalColor;
        button.colors = colors;
        int captured = slot;
        button.onClick.AddListener(() => _runManager.Buy(captured));

        if (frozen)
        {
            var frost = NewChild("Frost", card.transform, new Vector2(0.5f, 0.5f), new Vector2(CardWidth, CardHeight), Vector2.zero);
            Paint(frost, new Color(Ice.r, Ice.g, Ice.b, 0.08f));
        }

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

    /// <summary>What kind of thing it is: a weapon by its class, anything else by its slot.</summary>
    private static string KindOf(ItemParams itemParams)
    {
        if (itemParams == null) return "Item";
        switch (itemParams.Type)
        {
            case ItemType.Weapon: return itemParams.Class.ToString() + " (weapon)";
            case ItemType.Armor: return "Armour";
            case ItemType.VestBeltPauldron: return "Vest";
            default: return itemParams.Type.ToString();
        }
    }

    /// <summary>The item's stats in one line, in the words the hero panel uses.</summary>
    private static string StatLine(ItemParams itemParams)
    {
        if (itemParams == null || itemParams.Properties == null) return "";
        var parts = new List<string>();
        foreach (var property in itemParams.Properties)
        {
            if (!float.TryParse(property.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || value == 0f) continue;
            switch (property.Id)
            {
                case PropertyId.Damage: parts.Add($"Damage {value:0.#}"); break;
                case PropertyId.HealthMax: parts.Add($"Health {value:+0;-0}"); break;
                case PropertyId.Armor: parts.Add($"Armour {value:0}"); break;
                case PropertyId.MagicResist: parts.Add($"Magic resist {value:0}"); break;
                case PropertyId.Speed: parts.Add($"Speed {value:+0.#;-0.#}"); break;
                case PropertyId.KnockbackResist: parts.Add($"Knockback resist {value:0}%"); break;
                case PropertyId.ChargeSpeed: parts.Add($"Attack speed {value * 100f:+0;-0}%"); break;
            }
        }
        return string.Join("  ·  ", parts);
    }

    #region Construction

    private void Build(Transform canvas)
    {
        _root = new GameObject("ShopPanel", typeof(RectTransform));
        _root.transform.SetParent(canvas, false);
        Fill(_root.GetComponent<RectTransform>());

        // A dim backdrop so the shop reads as the thing to deal with, without blocking the heroes'
        // windows: what is bought is equipped from them while the shop is still open.
        var shade = _root.AddComponent<Image>();
        shade.color = Backdrop;
        shade.raycastTarget = false;

        UiLayer.Raise(_root, UiLayer.Reward);

        var frame = NewChild("Frame", _root.transform, new Vector2(0.5f, 0.5f), new Vector2(FrameWidth, FrameHeight), Vector2.zero);
        Paint(frame, Frame).raycastTarget = true;
        var edge = frame.AddComponent<Outline>();
        edge.effectColor = FrameEdge;
        edge.effectDistance = new Vector2(2f, -2f);
        _frame = frame.transform;

        float left = -FrameWidth / 2f + 36f;

        _heading = NewText("Heading", _frame, 34f, Gold);
        _heading.alignment = TextAlignmentOptions.Left;
        _heading.fontStyle = FontStyles.Bold;
        Place(_heading.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(500f, 44f), new Vector2(left + 250f, 304f));

        _subheading = NewText("Subheading", _frame, 17f, Muted);
        _subheading.alignment = TextAlignmentOptions.Left;
        Place(_subheading.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(500f, 24f), new Vector2(left + 250f, 270f));

        var purse = NewChild("Purse", _frame, new Vector2(0.5f, 0.5f), new Vector2(210f, 50f), new Vector2(FrameWidth / 2f - 141f, 290f));
        Paint(purse, new Color(0.04f, 0.04f, 0.05f, 1f));   // opaque, or the gold edge shows through as a fill
        var purseEdge = purse.AddComponent<Outline>();
        purseEdge.effectColor = new Color(Gold.r, Gold.g, Gold.b, 0.6f);
        purseEdge.effectDistance = new Vector2(1f, -1f);
        _gold = NewText("Gold", purse.transform, 26f, Gold);
        _gold.fontStyle = FontStyles.Bold;
        Fill(_gold.rectTransform);

        float rowWidth = 5 * CardWidth + 4 * CardGap;
        var row = NewChild("Cards", _frame, new Vector2(0.5f, 0.5f), new Vector2(rowWidth, CardHeight + 10f), new Vector2(0f, 14f));
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = CardGap;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        _cardRow = row.transform;

        _hint = NewText("Hint", _frame, 15f, Muted);
        _hint.alignment = TextAlignmentOptions.Left;
        Place(_hint.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(560f, 50f), new Vector2(left + 280f, -290f));

        float buttonsY = -290f, right = FrameWidth / 2f - 36f;
        NewButton("Continue", new Vector2(right - 100f, buttonsY), () => _runManager.LeaveShop(), Primary, out _, out var continueLabel);
        continueLabel.text = "Continue";
        NewButton("Freeze", new Vector2(right - 314f, buttonsY), () => _runManager.ToggleFreeze(), Steel, out _freezeFace, out _freezeLabel);
        _reroll = NewButton("Reroll", new Vector2(right - 528f, buttonsY), () => _runManager.Reroll(), Steel, out _rerollFace, out _rerollLabel);

        _root.SetActive(false);
    }

    private Button NewButton(string name, Vector2 position, UnityEngine.Events.UnityAction onClick, Color color,
                             out Image face, out TextMeshProUGUI label)
    {
        var go = NewChild(name, _frame, new Vector2(0.5f, 0.5f), new Vector2(200f, 48f), position);
        face = Paint(go, color);
        face.raycastTarget = true;
        var button = go.AddComponent<Button>();
        button.targetGraphic = face;
        button.onClick.AddListener(onClick);

        label = NewText("Label", go.transform, 19f, Color.white);
        label.fontStyle = FontStyles.Bold;
        Fill(label.rectTransform);
        return button;
    }

    private static Image Paint(GameObject go, Color color)
    {
        var image = go.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
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

    private static void Fill(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
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
