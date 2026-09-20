using System;
using System.Collections.Generic;
using System.Linq;
using Assets.HeroEditor.Common.Scripts.Common;
using Assets.HeroEditor.Common.Scripts.Collections;
using Assets.HeroEditor.InventorySystem.Scripts.Data;
using Assets.HeroEditor.InventorySystem.Scripts.Elements;
using Assets.HeroEditor.InventorySystem.Scripts.Enums;
using HeroEditor.Common;
using HeroEditor.Common.Data;
using HeroEditor.Common.Enums;
using UnityEngine;
using UnityEngine.UI;
using Assets.HeroEditor.InventorySystem.Scripts;
using Kryz.CharacterStats;
using TMPro;

/// <summary>
/// High-level inventory interface.
/// </summary>
public class CharacterInventory : ItemWorkspace
{
    public Equipment Equipment;
    public ScrollInventory PlayerInventory;
    public ScrollInventory Materials;
    public Button EquipButton;
    public Button RemoveButton;
    public Button CraftButton;
    public Button LearnButton;
    public Button UseButton;
    public AudioClip EquipSound;
    public AudioClip CraftSound;
    public AudioClip UseSound;
    public AudioSource AudioSource;

    public Func<Item, bool> CanEquip = i => true; // Your game can override this.
    public Action<Item> OnEquip;

    // New fields
    public SpriteCollection SpriteCollection;
    public IconCollection IconCollection;
    public Entity CharacterEntity;

    public TextMeshProUGUI statsKeys;
    public TextMeshProUGUI statsValues;

    // The window prefab carries its own stat labels as legacy UI.Text ("Health/Mana/Strength/
    // Dexterity" against a hardcoded "99/99/99/99"), and the TMP fields above were never assigned —
    // so the panel had always shown placeholder numbers. Found at runtime and written to instead.
    private TMP_Text _prefabStatKeys;
    private TMP_Text _prefabStatValues;

    // Created at runtime under the equipment panel — shows the active spell's name (B).
    private TextMeshProUGUI activeSpellLabel;

    public void Awake()
    {
        ItemCollection.Active = ItemCollection;
        ItemCollection.Active.SpriteCollections = new List<SpriteCollection> { SpriteCollection };
        ItemCollection.Active.IconCollections = new List<IconCollection> { IconCollection };
    }

    /// <summary>
    /// Initialize owned items (just for example).
    /// </summary>
    public void InitializeCharacterInventory(Entity characterEntity)
    {
        this.CharacterEntity = characterEntity;

        var equipped = new List<Item>();
        Equipment.Initialize(ref equipped);

        // Equipment.Refresh rebuilds its InventoryItems (resetting their colour), then fires OnRefresh —
        // so re-dim the reserves here to guarantee the highlight survives every rebuild.
        Equipment.OnRefresh += HighlightActiveSpellSlot;

        // Keep the avatar-card portrait in step with what's equipped. Appearance.Refresh is otherwise
        // only called from EquipmentManagement's random-equip helpers, so equipping through this
        // window changed the character but left its avatar head showing the old helmet.
        Equipment.OnRefresh += RefreshAvatar;

        // The grid rebuilds its slot objects on every Refresh, so badges have to be re-hung each
        // time — the same reason the active-spell highlight re-applies here.
        Equipment.OnRefresh += RefreshNoticeBadges;

        if (CharacterEntity != null && CharacterEntity.Resonance != null)
        {
            CharacterEntity.Resonance.OnNoticesChanged += RefreshNoticeBadges;
            CharacterEntity.Resonance.OnGrantsChanged += SyncSpellSlotsIfVerbsChanged;

            // Selecting an item IS the act of reading its news, so that is what clears it.
            OnSelectionChanged += CharacterEntity.Resonance.MarkSeen;
        }

        var statsPanel = transform.Find("HeroStats");
        if (statsPanel != null)
        {
            var keys = statsPanel.Find("Stats");
            var values = statsPanel.Find("Values");
            if (keys != null) _prefabStatKeys = keys.GetComponent<TMP_Text>();
            if (values != null) _prefabStatValues = values.GetComponent<TMP_Text>();
        }

        CreateActiveSpellLabel();
        HighlightActiveSpellSlot();   // set the initial label + dim state

        // Show initial stats
        RefreshStatsUI();
        RefreshNoticeBadges();
    }

    /// <summary>
    /// Hang an unread dot on every equipped slot whose item has news, and clear the rest.
    ///
    /// Worn slots only. Only worn items attune, so they are the ones with anything to report — and
    /// the bag is a scrolling list that recycles its slot objects, which would carry a badge from
    /// the item that just scrolled away onto whatever took its place.
    /// </summary>
    private void RefreshNoticeBadges()
    {
        if (CharacterEntity == null || CharacterEntity.Resonance == null || Equipment == null) return;

        foreach (var slot in Equipment.InventoryItems)
        {
            if (slot == null) continue;

            var rect = slot.transform as RectTransform;
            if (rect == null) continue;

            var badge = slot.GetComponentInChildren<NoticeBadge>(true);
            if (badge == null) badge = NoticeBadge.AttachTo(rect, 14f, new Vector2(-3f, -3f));
            if (badge == null) continue;

            badge.Show(CharacterEntity.Resonance.NoticeFor(slot.Item));
        }
    }

    /// <summary>
    /// Rebuild this character's avatar head so the portrait matches the equipped gear. Runs after
    /// Equipment.Refresh has already pushed the new items onto the character, so the helmet it reads
    /// is the current one.
    /// </summary>
    private void RefreshAvatar()
    {
        if (CharacterEntity != null && CharacterEntity.Appearance != null)
            CharacterEntity.Appearance.Refresh();
    }

    private TextMeshProUGUI spellDescriptionLabel;

    /// <summary>
    /// What the selected spellbook's spell does, under the item panel — with its real numbers. A
    /// spellbook's own info panel is blank, because HeroEditor items describe themselves through
    /// stat properties and a book has none; a player choosing between three books was choosing
    /// between three names. Hidden for anything that isn't a spellbook.
    /// </summary>
    private void UpdateSpellDescription()
    {
        // A weapon's verb, from the database: what this weapon would teach if worn.
        Spell spell = null;
        if (SelectedItem != null && SelectedItem.IsWeapon && ResonanceDatabase.Active != null)
        {
            var entry = ResonanceDatabase.Active.FindFor(SelectedItem);
            if (entry != null && entry.engraving is GrantSpellEngraving grant) spell = grant.spell;
        }
        bool show = spell != null && !string.IsNullOrEmpty(spell.FullDescription);

        if (spellDescriptionLabel == null)
        {
            if (!show || ItemInfo == null) return;

            var go = new GameObject("SpellDescription", typeof(RectTransform));
            go.transform.SetParent(ItemInfo.transform, false);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = 17;
            tmp.alignment = TextAlignmentOptions.Top;
            tmp.color = new Color(0.92f, 0.92f, 0.92f, 1f);
            tmp.enableWordWrapping = true;
            tmp.raycastTarget = false;

            var rt = tmp.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 0f);   // hangs below the item panel
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(320f, 110f);
            rt.anchoredPosition = new Vector2(0f, -6f);

            spellDescriptionLabel = tmp;
        }

        spellDescriptionLabel.gameObject.SetActive(show);
        if (show)
            spellDescriptionLabel.text = Keywords.Decorate($"<b>{spell.DisplayName}</b>\n{spell.FullDescription}");
    }

    /// <summary>(B) Spawn the "Active Spell: …" label under the equipment grid.</summary>
    private void CreateActiveSpellLabel()
    {
        if (activeSpellLabel != null || Equipment == null) return;

        var go = new GameObject("ActiveSpellLabel", typeof(RectTransform));
        go.transform.SetParent(Equipment.transform, false);

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = "Active Spell: —";
        tmp.fontSize = 22;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(1f, 0.82f, 0.28f, 1f);
        tmp.raycastTarget = false;

        var rt = tmp.rectTransform;
        rt.anchorMin = new Vector2(0.5f, 0f);   // bottom-centre of the equipment panel
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(340f, 32f);
        rt.anchoredPosition = new Vector2(0f, -8f);

        activeSpellLabel = tmp;
    }

    /// <summary>
    /// Open the shared bag with <paramref name="contents"/>. What goes in is the run's decision
    /// (RunData.bag, built by <see cref="BagStock"/>); this only owns the wiring. The contents used
    /// to be composed here by hand — a random item per slot, one engraved bow, three spellbooks —
    /// which meant every run opened with the same test stock and no item designed later ever made it in.
    /// </summary>
    public void InitializePlayerInventory(List<Item> contents)
    {
        ItemCollection.Active.Reset();

        var inventory = contents ?? new List<Item>();
        RegisterCallbacks();
        PlayerInventory.Initialize(ref inventory);
    }

    public void Initialize(ref List<Item> playerItems, ref List<Item> equippedItems, int bagSize, Action onRefresh)
    {
        RegisterCallbacks();
        PlayerInventory.Initialize(ref playerItems);
        Equipment.SetBagSize(bagSize);
        Equipment.Initialize(ref equippedItems);
        Equipment.OnRefresh = onRefresh;

        if (!Equipment.SelectAny() && !PlayerInventory.SelectAny())
        {
            ItemInfo.Reset();
        }
    }

    public void RegisterCallbacks()
    {
        InventoryItem.OnLeftClick = SelectItem;
        InventoryItem.OnRightClick = InventoryItem.OnDoubleClick = QuickAction;
    }

    /// <summary>Fired after the player selects an item, so panels can follow the selection.</summary>
    public event Action<Item> OnSelectionChanged;

    public void SelectItem(Item item)
    {
        SelectedItem = item;
        ItemInfo.Initialize(SelectedItem, SelectedItem.Params.Price);

        // Clear all green highlights, then highlight only the clicked item
        PlayerInventory.HighlightOnly(item);
        Equipment.HighlightOnly(item);

        Refresh();
        OnSelectionChanged?.Invoke(item);
    }

    private void QuickAction(Item item)
    {
        SelectItem(item);

        if (Equipment.Items.Contains(item))
        {
            Remove();
        }
        else if (CanEquipSelectedItem())
        {
            Equip();
        }
    }

    private static readonly Color ReserveBookDim = new Color(0.4f, 0.4f, 0.4f, 1f);

    /// <summary>
    /// Make the active spellbook obvious: the active book stays full-bright, the reserves are dimmed
    /// (A). Targets the equipped book (an <see cref="InventoryItem"/> drawn on top of the slot) and
    /// DIMS the reserves rather than trying to brighten the active one — Image.color multiplies, so it
    /// can darken but not brighten. Also updates the active-spell name label (B). Spellbooks fill the
    /// spell slots in order, so the active book is the one at index <see cref="Entity.activeSpellSlot"/>
    /// among the equipped spellbooks.
    /// </summary>
    private void HighlightActiveSpellSlot()
    {
        if (CharacterEntity == null) return;
        UpdateActiveSpellLabel();
        RefreshRackStrip();
    }

    // The rack strip: one row per racked weapon under the active label, with the two things a
    // player does with a racked weapon — draw it into the hand, or put it back in the bag.
    private readonly List<GameObject> _rackRows = new List<GameObject>();

    private void RefreshRackStrip()
    {
        foreach (var row in _rackRows) if (row != null) Destroy(row);
        _rackRows.Clear();
        if (Equipment == null || CharacterEntity == null) return;

        for (int i = 0; i < CharacterEntity.carriedWeapons.Count; i++)
        {
            var weapon = CharacterEntity.carriedWeapons[i];
            if (weapon == null) continue;
            var row = new GameObject("Rack" + i, typeof(RectTransform));
            row.transform.SetParent(Equipment.transform, false);
            var rect = row.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(400f, 26f);
            rect.anchoredPosition = new Vector2(0f, -66f - i * 30f);

            string verb = "";
            if (ResonanceDatabase.Active != null)
            {
                var entry = ResonanceDatabase.Active.FindFor(weapon);
                if (entry != null && entry.engraving is GrantSpellEngraving grant && grant.spell != null) verb = grant.spell.DisplayName;
            }
            RackText(row.transform, Catalog.ShortName(weapon.Id) + (verb != "" ? "  ·  " + verb : ""), new Vector2(-80f, 0f), new Vector2(236f, 26f), TextAlignmentOptions.Left);
            var captured = weapon;
            RackButton(row.transform, "Hand", new Vector2(78f, 0f), () => DrawToHand(captured));
            RackButton(row.transform, "Bag", new Vector2(140f, 0f), () => DropFromRack(captured));
            _rackRows.Add(row);
        }
    }

    private static TextMeshProUGUI RackText(Transform parent, string text, Vector2 at, Vector2 size, TextAlignmentOptions align)
    {
        var go = new GameObject("Label", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text; tmp.fontSize = 17; tmp.alignment = align; tmp.color = new Color(0.92f, 0.92f, 0.92f, 1f); tmp.raycastTarget = false;
        tmp.enableWordWrapping = false; tmp.overflowMode = TextOverflowModes.Ellipsis;
        var rt = tmp.rectTransform; rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f); rt.sizeDelta = size; rt.anchoredPosition = at;
        return tmp;
    }

    private static void RackButton(Transform parent, string label, Vector2 at, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject(label, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>(); rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f); rt.sizeDelta = new Vector2(56f, 24f); rt.anchoredPosition = at;
        var back = go.AddComponent<Image>(); back.color = new Color(0.2f, 0.32f, 0.55f, 1f);
        var button = go.AddComponent<Button>(); button.targetGraphic = back; button.onClick.AddListener(onClick);
        RackText(go.transform, label, Vector2.zero, new Vector2(56f, 24f), TextAlignmentOptions.Center).fontSize = 15;
    }

    /// <summary>Draw a racked weapon into the hand; the hand weapon takes its place on the rack.</summary>
    public void DrawToHand(Item racked)
    {
        if (CharacterEntity == null || racked == null || !CharacterEntity.carriedWeapons.Remove(racked)) return;
        PlayerInventory.Items.Add(racked);
        SelectItem(racked);
        Equip();   // racks the weapon that was in hand, and resyncs slots
    }

    /// <summary>Put a racked weapon back in the bag; its verb leaves the slots.</summary>
    public void DropFromRack(Item racked)
    {
        if (CharacterEntity == null || racked == null || !CharacterEntity.carriedWeapons.Remove(racked)) return;
        PlayerInventory.Items.Add(racked);
        PlayerInventory.Refresh(racked, true);
        if (CharacterEntity.Resonance != null) CharacterEntity.Resonance.Refresh();
        SelectItem(racked);
        SyncSpellSlots();
    }

    /// <summary>The active verb and the weapon it comes from, and what else is on the rack.</summary>
    private void UpdateActiveSpellLabel()
    {
        if (activeSpellLabel == null) return;

        var spell = CharacterEntity.ActiveSpell;
        var from = spell != null && CharacterEntity.Resonance != null ? CharacterEntity.Resonance.WeaponTeaching(spell) : null;
        string rack = "";
        foreach (var w in CharacterEntity.carriedWeapons) if (w != null) rack += (rack.Length > 0 ? ", " : "") + Catalog.ShortName(w.Id);
        activeSpellLabel.text = (spell != null ? "Active: " + spell.DisplayName + (from != null ? " (" + Catalog.ShortName(from.Id) + ")" : "") : "Active: —")
                              + (rack.Length > 0 ? "\nRack: " + rack : "");
    }

    public void Equip()
    {
        if (!CanEquip(SelectedItem)) return;

        var equipped = SelectedItem.IsFirearm
            ? Equipment.Items.Where(i => i.IsFirearm).ToList()
            : Equipment.Items.Where(i => i.Params.Type == SelectedItem.Params.Type && !i.IsFirearm).ToList();

        // A new weapon goes to the hand and the old one to the rack, so a hero keeps every verb it
        // has picked up until the rack is full; then the oldest racked weapon goes back to the bag.
        if (SelectedItem.IsWeapon && !SelectedItem.IsFirearm && CharacterEntity != null)
        {
            foreach (var old in equipped)
            {
                if (!old.IsWeapon) continue;
                Equipment.Items.Remove(old);
                var evicted = WeaponRack.Push(CharacterEntity.carriedWeapons, old, Entity.RackSize - 1);
                if (evicted != null) PlayerInventory.Items.Add(evicted);
            }
            equipped.RemoveAll(i => i.IsWeapon);
        }

        if (equipped.Any())
        {
            AutoRemove(equipped, Equipment.Slots.Count(i => i.Supports(SelectedItem)));
        }

        // Which pieces cannot share a body with the one being put on — the same rules the random
        // roll and signature items answer to, rather than a third opinion. AutoRemove rather than a
        // plain removal because the displaced gear goes back to the bag, which is a window concern.
        var conflicts = new List<Item>();
        foreach (var worn in Equipment.Items)
        {
            if (Loadout.Conflicts(SelectedItem, worn)) conflicts.Add(worn);
        }
        if (conflicts.Count > 0) AutoRemove(conflicts, conflicts.Count);

        MoveItem(SelectedItem, PlayerInventory, Equipment);
        AudioSource.PlayOneShot(EquipSound, SfxVolume);
        OnEquip?.Invoke(SelectedItem);

        EquipStats();
        SyncSpellSlots();
    }

    /// <summary>
    /// Spend an equipped item on its engraving: it stays worn but is hollowed out, keeping its class
    /// and losing everything else (<see cref="HollowItems"/>).
    ///
    /// It stays equipped because a weapon is not only its numbers — it is what lets an archer shoot.
    /// Destroying the bow that made a hero an archer could strand them with a kit they can no longer
    /// use until another bow happens to drop, which punishes the player for engaging with the
    /// mechanic. Leaving the husk keeps them functional while making the loss plain: the slot is
    /// occupied by something that now does nothing for them.
    ///
    /// Deliberately not <see cref="Remove"/>, which hands the item back intact.
    /// </summary>
    public void HollowItem(Item item)
    {
        if (item == null || !Equipment.Items.Contains(item)) return;

        // Take the old stats off under the item's PREVIOUS identity — modifiers are sourced by item
        // id, and hollowing does not change the id, but the removal must happen before the re-apply
        // below or the two would race over the same source.
        UnequipStats(item);

        HollowItems.Hollow(item);

        // Re-apply so the hollow item is accounted for like any other worn thing. It now contributes
        // nothing, but going through the same path keeps one code path for "what is this item worth".
        ItemParams itemParams = ItemCollection.Active.GetItemParams(item);
        CharacterEntity.Stats.ApplyItemModifiers(itemParams, item.Id, hollow: true);

        Equipment.Refresh(null);

        if (CharacterEntity.Resonance != null) CharacterEntity.Resonance.Refresh();

        RefreshStatsUI();
        SyncSpellSlots();
    }

    public void Remove()
    {
        var removed = SelectedItem;
        MoveItem(SelectedItem, Equipment, PlayerInventory);
        SelectItem(SelectedItem);
        AudioSource.PlayOneShot(EquipSound, SfxVolume);

        UnequipStats();

        // The hand emptied: the first racked weapon steps into it, so a hero is never left swinging
        // nothing while it still carries something.
        if (removed != null && removed.IsWeapon && CharacterEntity != null && CharacterEntity.carriedWeapons.Count > 0)
        {
            var next = CharacterEntity.carriedWeapons[0];
            CharacterEntity.carriedWeapons.RemoveAt(0);
            PlayerInventory.Items.Add(next);
            SelectItem(next);
            Equip();
            return;
        }

        SyncSpellSlots();
    }

    /// <summary>
    /// Make the equipped weapon decide how this character fights: what class they are holding, which
    /// basic attack that brings, and whether it fills both hands.
    ///
    /// Separate from <see cref="SyncSpellSlots"/> because that only runs at startup for characters
    /// who materialised authored spellbooks. A hero with no books never had this applied, so their
    /// weapon was ignored — a duelist stood there swinging one dagger with the ordinary melee attack
    /// while every hero who happened to carry a spellbook worked correctly.
    /// </summary>
    public void ApplyWeaponLoadout()
    {
        if (CharacterEntity == null || Equipment == null) return;

        Loadout.ApplyTo(CharacterEntity, Loadout.WeaponIn(Equipment.Items));
    }

    /// <summary>
    /// The slots follow the verbs the hero holds. A tier-up revokes and re-grants the same verb, and
    /// that must not rebuild the slots: rebuilding interrupts the cast in progress (the very cast
    /// that crossed the tier) and can reorder the slots under the player's chosen one. So the slots
    /// are only rebuilt when the set of verbs is actually different.
    /// </summary>
    private void SyncSpellSlotsIfVerbsChanged()
    {
        if (CharacterEntity == null || CharacterEntity.Resonance == null) return;
        if (SameVerbs(CharacterEntity.spellSlots, CharacterEntity.Resonance.GrantedVerbs())) return;
        SyncSpellSlots();
    }

    /// <summary>Whether the slots already hold exactly these verbs, in any order, ignoring what the slot cap left out.</summary>
    public static bool SameVerbs(List<Spell> slots, List<Spell> verbs)
    {
        var wanted = new List<Spell>();
        if (verbs != null) foreach (var v in verbs) if (v != null && !wanted.Contains(v) && wanted.Count < Entity.MaxSpellSlots) wanted.Add(v);
        var held = new List<Spell>();
        if (slots != null) foreach (var s in slots) if (s != null && !held.Contains(s)) held.Add(s);
        if (held.Count != wanted.Count) return false;
        foreach (var v in wanted) if (!held.Contains(v)) return false;
        return true;
    }

    /// <summary>
    /// The player picked which verb is cast. Only the index moves: the slots are what they were,
    /// so nothing is rebuilt and nothing mid-swing is interrupted; CombatAI re-reads its kit and
    /// the mana bar resizes to the new verb's cost.
    /// </summary>
    public void SetActiveSlot(int index)
    {
        if (CharacterEntity == null || CharacterEntity.spellSlots == null) return;
        CharacterEntity.activeSpellSlot = Mathf.Clamp(index, 0, Mathf.Max(0, CharacterEntity.spellSlots.Count - 1));
        if (CharacterEntity.CombatAI != null) CharacterEntity.CombatAI.RefreshSpells();
        HighlightActiveSpellSlot();
    }

    /// <summary>
    /// Rebuild the character's spell slots from the verbs its weapons teach (Docs/Spells.md), apply
    /// the weapon loadout, clamp the active slot, and refresh CombatAI so the change takes effect.
    ///
    /// The rule for who calls this: the equipment paths in this class (equip, remove, hollow, the
    /// rack), because the hand changed; and the resonance books through
    /// <see cref="SyncSpellSlotsIfVerbsChanged"/>, because the verbs changed. Nobody else. It
    /// interrupts a cast in progress, so a caller that only wants a different active slot uses
    /// <see cref="SetActiveSlot"/> and a caller that only granted a verb does nothing — the books
    /// announce it.
    /// </summary>
    public void SyncSpellSlots()
    {
        if (CharacterEntity == null) return;

        // Equipment just changed, so anything mid-swing is now being performed with a weapon the
        // hero is no longer holding. Drop it rather than let it finish.
        if (CharacterEntity.CombatAI != null) CharacterEntity.CombatAI.InterruptCast();

        ApplyWeaponLoadout();

        // The slots are verbs: the hand weapon's first, then the rack's, then any banked. Weapons are
        // verbs (Docs/Spells.md); there is no other ability system.
        var spells = new List<Spell>();
        if (CharacterEntity.Resonance != null)
            foreach (var verb in CharacterEntity.Resonance.GrantedVerbs())
                if (!spells.Contains(verb) && spells.Count < Entity.MaxSpellSlots) spells.Add(verb);

        CharacterEntity.HandShield = Equipment.Items.FirstOrDefault(i => i != null && i.IsShield);

        CharacterEntity.spellSlots = spells;
        if (CharacterEntity.activeSpellSlot >= spells.Count)
            CharacterEntity.activeSpellSlot = Mathf.Max(0, spells.Count - 1);

        if (CharacterEntity.CombatAI != null) CharacterEntity.CombatAI.RefreshSpells();
        HighlightActiveSpellSlot();
    }

    public void EquipStats()
    {
        if (CharacterEntity == null || CharacterEntity.Stats == null) return;

        ItemParams itemParams = ItemCollection.Active.GetItemParams(SelectedItem);
        CharacterEntity.Stats.ApplyItemModifiers(itemParams, SelectedItem.Id,
                                                 HollowItems.IsHollow(SelectedItem));

        // An item's engraving is part of what equipping it does, so it lands now rather than at the
        // next fight — otherwise the stat sits unchanged and the item looks like it did nothing.
        if (CharacterEntity.Resonance != null) CharacterEntity.Resonance.Refresh();

        RefreshStatsUI();
    }

    public void UnequipStats(Item item = null)
    {
        if (CharacterEntity == null || CharacterEntity.Stats == null) return;

        Item source = item ?? SelectedItem;
        CharacterEntity.Stats.RemoveItemModifiers(source.Id);

        // Taking the item off takes its engraving with it, for the same reason.
        if (CharacterEntity.Resonance != null) CharacterEntity.Resonance.Refresh();

        RefreshStatsUI();
    }

    /// <summary>
    /// Update the stats panel text from the entity's current stat values.
    /// </summary>
    public void RefreshStatsUI()
    {
        if (CharacterEntity == null || CharacterEntity.Stats == null) return;

        var stats = CharacterEntity.Stats.GetDisplayStats();
        var keys = new System.Text.StringBuilder();
        var vals = new System.Text.StringBuilder();

        foreach (var kvp in stats)
        {
            keys.AppendLine(kvp.Key);
            vals.AppendLine(kvp.Value.ToString("0.##"));
        }

        if (statsKeys != null) statsKeys.text = keys.ToString();
        if (statsValues != null) statsValues.text = vals.ToString();

        // The window's own labels, which are legacy UI.Text and were showing hardcoded placeholders.
        if (_prefabStatKeys != null) _prefabStatKeys.text = keys.ToString();
        if (_prefabStatValues != null) _prefabStatValues.text = vals.ToString();
    }

    public void Craft()
    {
        var materials = MaterialList;

        if (CanCraft(materials))
        {
            materials.ForEach(i => PlayerInventory.Items.Single(j => j.Hash == i.Hash).Count -= i.Count);
            PlayerInventory.Items.RemoveAll(i => i.Count == 0);

            var itemId = SelectedItem.Params.FindProperty(PropertyId.Craft).Value;
            var existed = PlayerInventory.Items.SingleOrDefault(i => i.Id == itemId && i.Modifier == null);

            if (existed == null)
            {
                PlayerInventory.Items.Add(new Item(itemId));
            }
            else
            {
                existed.Count++;
            }

            PlayerInventory.Refresh(SelectedItem);
            CraftButton.interactable = CanCraft(materials);
            AudioSource.PlayOneShot(CraftSound, SfxVolume);
        }
        else
        {
            Debug.Log("No materials.");
        }
    }

    public void Learn()
    {
        // Implement your logic here!
    }

    public void Use()
    {
        var sound = SelectedItem.Params.Type == ItemType.Coupon ? EquipSound : UseSound;

        if (SelectedItem.Count == 1)
        {
            PlayerInventory.Items.Remove(SelectedItem);
            SelectedItem = PlayerInventory.Items.FirstOrDefault();

            if (SelectedItem == null)
            {
                PlayerInventory.Refresh(null);
                SelectedItem = Equipment.Items.FirstOrDefault();

                if (SelectedItem != null)
                {
                    Equipment.Refresh(SelectedItem);
                }
            }
            else
            {
                PlayerInventory.Refresh(SelectedItem);
            }
        }
        else
        {
            SelectedItem.Count--;
            PlayerInventory.Refresh(SelectedItem);
        }

        Equipment.OnRefresh?.Invoke();
        AudioSource.PlayOneShot(sound, SfxVolume);
    }

    public override void Refresh()
    {
        if (SelectedItem == null)
        {
            ItemInfo.Reset();
            EquipButton.SetActive(false);
            RemoveButton.SetActive(false);
        }
        else
        {
            var equipped = Equipment.Items.Contains(SelectedItem);

            EquipButton.SetActive(!equipped && CanEquipSelectedItem());
            RemoveButton.SetActive(equipped);
            UseButton.SetActive(CanUse());
        }

        UpdateSpellDescription();

        var receipt = SelectedItem != null && SelectedItem.Params.Type == ItemType.Recipe;

        if (CraftButton != null) CraftButton.SetActive(false);
        if (LearnButton != null) LearnButton.SetActive(false);

        if (receipt)
        {
            if (LearnButton == null)
            {
                var materialSelected = !PlayerInventory.Items.Contains(SelectedItem) && !Equipment.Items.Contains(SelectedItem);

                CraftButton.SetActive(true);
                Materials.SetActive(materialSelected);
                Equipment.Scheme.SetActive(!materialSelected);

                var materials = MaterialList;

                Materials.Initialize(ref materials);
            }
            else
            {
                LearnButton.SetActive(true);
            }
        }

        // Keep the active-spell-slot highlight current after any UI refresh.
        HighlightActiveSpellSlot();
    }

    private List<Item> MaterialList => SelectedItem.Params.FindProperty(PropertyId.Materials).Value.Split(',').Select(i => i.Split(':')).Select(i => new Item(i[0], int.Parse(i[1]))).ToList();

    private bool CanEquipSelectedItem()
    {
        return PlayerInventory.Items.Contains(SelectedItem) && Equipment.Slots.Any(i => i.Supports(SelectedItem)) && SelectedItem.Params.Class != ItemClass.Booster;
    }

    private bool CanUse()
    {
        return SelectedItem.Params.Class == ItemClass.Booster || SelectedItem.Params.Type == ItemType.Coupon;
    }

    private bool CanCraft(List<Item> materials)
    {
        return materials.All(i => PlayerInventory.Items.Any(j => j.Hash == i.Hash && j.Count >= i.Count));
    }

    /// <summary>
    /// Automatically removes items if target slot is busy.
    /// </summary>
    private void AutoRemove(List<Item> items, int max = 1)
    {
        long sum = 0;

        foreach (var p in items)
        {
            sum += p.Count;
        }

        if (sum == max)
        {
            Item item = items.LastOrDefault(i => i.Id != SelectedItem.Id) ?? items.Last();
            MoveItemSilent(item, Equipment, PlayerInventory);
            UnequipStats(item);
        }
    }
}