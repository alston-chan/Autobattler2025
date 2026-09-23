using System.Collections.Generic;
using Assets.HeroEditor.InventorySystem.Scripts;
using Assets.HeroEditor.InventorySystem.Scripts.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Abilities row under a hero's Worn panel: one slot per banked weapon verb, up to
/// <see cref="Entity.MaxBankedAbilities"/>, each drawn as the weapon it was banked from, on its
/// rarity's background, with the verb's name under it. Banked verbs are the one kind of bank the
/// player uses directly — they join the skill slots to pick between — so they are shown as things
/// rather than as lines of text. Clicking one before a fight makes it the verb the hero casts; the
/// active one is framed in green, as a selected item is.
///
/// Built at runtime from the window's own panel, title and slot art, so the vendor prefab is untouched.
/// </summary>
public class BankedAbilityBar : MonoBehaviour
{
    private static readonly Color FrameIdle = new Color(0.408f, 0.373f, 0.31f, 1f);
    private static readonly Color FrameActive = new Color(0.62f, 0.86f, 0.25f, 1f);
    private static readonly Color Gold = new Color(1f, 0.82f, 0.28f, 1f);
    private static readonly Color Light = new Color(0.9f, 0.88f, 0.82f, 1f);
    private static readonly Color Muted = new Color(0.55f, 0.52f, 0.47f, 1f);
    private static readonly Color EmptyWell = new Color(0f, 0f, 0f, 0.35f);

    private const float PanelHeight = 172f, SlotSize = 64f, SlotSpacing = 118f, SlotY = -82f;

    private CharacterInventory _inventory;
    private Entity _hero;
    private RectTransform _panel;
    private Sprite _frameSprite;
    private readonly List<GameObject> _slots = new List<GameObject>();

    // What the row was last drawn from; it is redrawn only when one of these moves.
    private int _drawnCount = -1;
    private Spell _drawnActive;
    private bool _drawnSetup;

    public void Initialize(CharacterInventory inventory, Entity hero)
    {
        _inventory = inventory;
        _hero = hero;
        if (_inventory == null || _hero == null || _hero.Resonance == null) return;
        Build();
    }

    private void LateUpdate()
    {
        if (_panel == null) return;
        int count = _hero.Resonance.BankedAbilities().Count;
        var active = _hero.ActiveSpell;
        bool setup = InSetup;
        if (count == _drawnCount && active == _drawnActive && setup == _drawnSetup) return;
        _drawnCount = count;
        _drawnActive = active;
        _drawnSetup = setup;
        Redraw();
    }

    private static bool InSetup
    {
        get
        {
            var game = GameManager.Instance;
            return game == null || game.StateMachine.Current == GameState.Setup;
        }
    }

    private void Redraw()
    {
        foreach (var slot in _slots) Destroy(slot);
        _slots.Clear();

        var abilities = _hero.Resonance.BankedAbilities();
        for (int i = 0; i < Entity.MaxBankedAbilities; i++)
        {
            var x = (i - (Entity.MaxBankedAbilities - 1) / 2f) * SlotSpacing;
            _slots.Add(i < abilities.Count ? BuildSlot(abilities[i], x) : BuildEmpty(x));
        }
    }

    private GameObject BuildSlot(Resonance.Banked mark, float x)
    {
        var spell = ((GrantSpellEngraving)mark.engraving).spell;
        bool active = _hero.ActiveSpell == spell;

        var slot = NewRect("Ability_" + spell.name, _panel, new Vector2(0.5f, 1f), new Vector2(SlotSize, SlotSize), new Vector2(x, SlotY));

        // The weapon it came from, on its rarity's background, as it looked in the slot it was banked from.
        var copy = !string.IsNullOrEmpty(mark.itemId) ? Rarity.Make(mark.itemId, mark.tier) : null;
        var background = NewImage(slot.transform, "Background", copy != null ? Rarity.Background(copy) : null, EmptyWell, 0f);
        if (background.sprite != null) background.color = Color.white;

        var icon = copy != null && ItemCollection.Active != null ? ItemCollection.Active.GetItemIcon(new Item(mark.itemId)) : null;
        if (icon != null && icon.Sprite != null)
        {
            var iconImage = NewImage(slot.transform, "Icon", icon.Sprite, Color.white, 5f);
            iconImage.preserveAspect = true;
        }

        var frame = NewImage(slot.transform, "Frame", _frameSprite, active ? FrameActive : FrameIdle, 0f);
        if (_frameSprite != null) frame.type = UnityEngine.UI.Image.Type.Sliced;

        var grade = Text(slot.transform, "Grade", Rarity.Letter(mark.tier), 15f, Color.white, TextAlignmentOptions.TopLeft);
        grade.fontStyle = FontStyles.Bold;
        Fill(grade.rectTransform, 5f);

        var name = Text(slot.transform, "Name", spell.DisplayName, 14f, active ? Gold : Light, TextAlignmentOptions.Center);
        Place(name.rectTransform, new Vector2(0.5f, 0f), new Vector2(SlotSpacing - 4f, 20f), new Vector2(0f, -16f));
        name.enableAutoSizing = true;
        name.fontSizeMin = 10f;
        name.fontSizeMax = 14f;

        // Pick it as the verb to cast — before a fight, as the unit card's Cast row allows.
        frame.raycastTarget = true;
        var button = slot.AddComponent<Button>();
        button.targetGraphic = frame;
        button.interactable = InSetup;
        var colors = button.colors;
        colors.disabledColor = Color.white;   // mid-fight the row is read-only, not greyed out
        button.colors = colors;
        button.onClick.AddListener(() => Pick(spell));
        return slot;
    }

    private GameObject BuildEmpty(float x)
    {
        var slot = NewRect("Empty", _panel, new Vector2(0.5f, 1f), new Vector2(SlotSize, SlotSize), new Vector2(x, SlotY));
        NewImage(slot.transform, "Background", null, EmptyWell, 0f);
        var frame = NewImage(slot.transform, "Frame", _frameSprite, new Color(FrameIdle.r, FrameIdle.g, FrameIdle.b, 0.5f), 0f);
        if (_frameSprite != null) frame.type = UnityEngine.UI.Image.Type.Sliced;

        var label = Text(slot.transform, "Name", "Empty", 13f, Muted, TextAlignmentOptions.Center);
        Place(label.rectTransform, new Vector2(0.5f, 0f), new Vector2(SlotSpacing - 4f, 20f), new Vector2(0f, -16f));
        return slot;
    }

    private void Pick(Spell spell)
    {
        if (!InSetup || _hero.spellSlots == null) return;
        int index = _hero.spellSlots.IndexOf(spell);
        if (index >= 0) _inventory.SetActiveSlot(index);
    }

    #region Construction

    private void Build()
    {
        var equipment = _inventory.Equipment != null ? _inventory.Equipment.transform as RectTransform : null;
        if (equipment == null) return;

        // Directly under the Worn panel, as wide as it, down to where the panels beside it end.
        var go = new GameObject("BankedAbilities", typeof(RectTransform));
        go.transform.SetParent(equipment.parent, false);
        _panel = (RectTransform)go.transform;
        _panel.anchorMin = _panel.anchorMax = equipment.anchorMin;
        _panel.pivot = new Vector2(0.5f, 1f);
        _panel.sizeDelta = new Vector2(equipment.sizeDelta.x, PanelHeight);
        _panel.anchoredPosition = new Vector2(equipment.anchoredPosition.x,
                                              equipment.anchoredPosition.y - equipment.sizeDelta.y * (1f - equipment.pivot.y) - 2f);

        var panelArt = equipment.GetComponent<Image>();
        var face = go.AddComponent<Image>();
        if (panelArt != null)
        {
            face.sprite = panelArt.sprite;
            face.type = panelArt.type;
            face.color = panelArt.color;
        }
        face.raycastTarget = true;

        // The window's own panel title, so the row reads as part of the same window.
        var title = equipment.Find("PanelTitle");
        if (title != null)
        {
            var copy = Instantiate(title.gameObject, _panel, false);
            copy.name = "PanelTitle";
            var text = copy.GetComponentInChildren<TMP_Text>();
            if (text != null) text.text = "Abilities";
        }

        var frameSource = _inventory.transform.Find("ItemInfo/SelectedItem/Frame");
        var frameImage = frameSource != null ? frameSource.GetComponent<Image>() : null;
        _frameSprite = frameImage != null ? frameImage.sprite : null;
    }

    private static Image NewImage(Transform parent, string name, Sprite sprite, Color color, float inset)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Fill((RectTransform)go.transform, inset);
        var image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static TextMeshProUGUI Text(Transform parent, string name, string value, float size, Color color, TextAlignmentOptions alignment)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.raycastTarget = false;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        return text;
    }

    private static GameObject NewRect(string name, Transform parent, Vector2 anchor, Vector2 size, Vector2 position)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Place((RectTransform)go.transform, anchor, size, position);
        return go;
    }

    private static void Place(RectTransform rect, Vector2 anchor, Vector2 size, Vector2 position)
    {
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
    }

    private static void Fill(RectTransform rect, float inset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    #endregion
}
