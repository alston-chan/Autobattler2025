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

        // Show initial stats
        RefreshStatsUI();
    }

    public void InitializePlayerInventory()
    {
        ItemCollection.Active.Reset();

        // For testing: add 1 random item of each equipment type
        var equipmentTypes = new[]
        {
            ItemType.VestBeltPauldron, ItemType.Gloves, ItemType.Boots,
            ItemType.Helmet, ItemType.Shield, ItemType.Weapon
        };

        var inventory = new List<Item>();
        foreach (var type in equipmentTypes)
        {
            var candidates = ItemCollection.Active.Items.Where(i => i.Type == type).ToList();
            if (candidates.Count > 0)
            {
                var picked = candidates[UnityEngine.Random.Range(0, candidates.Count)];
                inventory.Add(new Item(picked.Id));
            }
        }

        // Spellbooks, so they can be equipped into the spell row (weapon-gating decides if they fire).
        foreach (var bookId in new[] { "Spellbook.DoubleStrike", "Spellbook.MultiShot" })
        {
            if (ItemCollection.Active.Items.Any(i => i.Id == bookId))
                inventory.Add(new Item(bookId));
        }

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

    public void SelectItem(Item item)
    {
        SelectedItem = item;
        ItemInfo.Initialize(SelectedItem, SelectedItem.Params.Price);

        // Clear all green highlights, then highlight only the clicked item
        PlayerInventory.HighlightOnly(item);
        Equipment.HighlightOnly(item);

        Refresh();
    }

    private void QuickAction(Item item)
    {
        SelectItem(item);

        if (Equipment.Items.Contains(item))
        {
            // An equipped spellbook: double-click makes it the ACTIVE slot (the one cast in combat)
            // instead of unequipping — the 1-active / 2-reserve model. Remove via the Remove button.
            if (item.Params.Type == ItemType.Spellbook)
                SetActiveSpellbook(item);
            else
                Remove();
        }
        else if (CanEquipSelectedItem())
        {
            Equip();
        }
    }

    /// <summary>Make an equipped spellbook the character's active spell, and refresh combat + highlight.</summary>
    private void SetActiveSpellbook(Item item)
    {
        if (CharacterEntity == null) return;

        var books = Equipment.Items.Where(i => i.Params.Type == ItemType.Spellbook).ToList();
        int index = books.IndexOf(item);
        if (index < 0) return;

        CharacterEntity.activeSpellSlot = index;
        if (CharacterEntity.CombatAI != null) CharacterEntity.CombatAI.RefreshSpells();
        HighlightActiveSpellSlot();
    }

    private static readonly Color ActiveSlotTint = new Color(1f, 0.82f, 0.28f, 1f);

    /// <summary>
    /// Tint the spell slot holding the active spellbook so the player can see which of their three
    /// is cast. Spellbooks fill the spell slots in order, so the active book sits in the slot at
    /// index <see cref="Entity.activeSpellSlot"/>.
    /// </summary>
    private void HighlightActiveSpellSlot()
    {
        if (CharacterEntity == null) return;

        var spellSlots = Equipment.Slots.Where(s => s.Types.Contains(ItemType.Spellbook)).ToList();
        int equippedBooks = Equipment.Items.Count(i => i.Params.Type == ItemType.Spellbook);
        int active = CharacterEntity.activeSpellSlot;

        for (int i = 0; i < spellSlots.Count; i++)
        {
            if (spellSlots[i].Background == null || spellSlots[i].Locked) continue;
            bool isActive = i == active && i < equippedBooks;
            spellSlots[i].Background.color = isActive ? ActiveSlotTint : Color.white;
        }
    }

    public void Equip()
    {
        if (!CanEquip(SelectedItem)) return;

        var equipped = SelectedItem.IsFirearm
            ? Equipment.Items.Where(i => i.IsFirearm).ToList()
            : Equipment.Items.Where(i => i.Params.Type == SelectedItem.Params.Type && !i.IsFirearm).ToList();

        if (equipped.Any())
        {
            AutoRemove(equipped, Equipment.Slots.Count(i => i.Supports(SelectedItem)));
        }

        if (SelectedItem.IsTwoHanded) AutoRemove(Equipment.Items.Where(i => i.IsShield).ToList());
        if (SelectedItem.IsShield) AutoRemove(Equipment.Items.Where(i => i.IsWeapon && i.IsTwoHanded).ToList());

        if (SelectedItem.IsFirearm) AutoRemove(Equipment.Items.Where(i => i.IsShield).ToList());
        if (SelectedItem.IsFirearm) AutoRemove(Equipment.Items.Where(i => i.IsWeapon && i.IsTwoHanded).ToList());
        if (SelectedItem.IsTwoHanded || SelectedItem.IsShield) AutoRemove(Equipment.Items.Where(i => i.IsWeapon && i.IsFirearm).ToList());

        MoveItem(SelectedItem, PlayerInventory, Equipment);
        AudioSource.PlayOneShot(EquipSound, SfxVolume);
        OnEquip?.Invoke(SelectedItem);

        EquipStats();
        SyncSpellSlots();
    }

    public void Remove()
    {
        MoveItem(SelectedItem, Equipment, PlayerInventory);
        SelectItem(SelectedItem);
        AudioSource.PlayOneShot(EquipSound, SfxVolume);

        UnequipStats();
        SyncSpellSlots();
    }

    /// <summary>
    /// Mirror the equipped spellbook items onto the character's spell slots. Spellbooks fill the
    /// slots in order; each resolves to its Spell via <see cref="SpellbookDatabase"/>. The active
    /// slot is clamped, and CombatAI is refreshed so the change takes effect. Called after any
    /// equip / unequip — equipment is set up after Awake, so this is what actually gets slotted
    /// spells into the combat kit.
    /// </summary>
    public void SyncSpellSlots()
    {
        if (CharacterEntity == null) return;

        var spells = new List<Spell>();
        foreach (var item in Equipment.Items)
        {
            if (item.Params.Type != ItemType.Spellbook) continue;
            var spell = SpellbookDatabase.Active != null ? SpellbookDatabase.Active.GetSpell(item.Id) : null;
            if (spell != null) spells.Add(spell);
            if (spells.Count >= Entity.MaxSpellSlots) break;
        }

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
        CharacterEntity.Stats.ApplyItemModifiers(itemParams, SelectedItem.Id);
        RefreshStatsUI();
    }

    public void UnequipStats(Item item = null)
    {
        if (CharacterEntity == null || CharacterEntity.Stats == null) return;

        Item source = item ?? SelectedItem;
        CharacterEntity.Stats.RemoveItemModifiers(source.Id);
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