﻿using System;
using System.Collections.Generic;
using System.Linq;
using Assets.HeroEditor.Common.Scripts.Common;
using Assets.HeroEditor.InventorySystem.Scripts.Data;
using Assets.HeroEditor.InventorySystem.Scripts.Elements;
using Assets.HeroEditor.InventorySystem.Scripts.Enums;
using HeroEditor.Common;
using HeroEditor.Common.Data;
using UnityEngine;
using UnityEngine.UI;
using Assets.HeroEditor.InventorySystem.Scripts;
// using Kryz.CharacterStats;
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
    public Entity CharacterEntity;

    public TextMeshProUGUI statsKeys;
    public TextMeshProUGUI statsValues;

    public void Awake()
    {
        ItemCollection.Active = ItemCollection;
        ItemCollection.Active.SpriteCollections = new List<SpriteCollection> { SpriteCollection };
    }

    private ItemParams CreateFakeItemParams(Item item, ItemSprite itemSprite, ItemType itemType, string replaceable = null, string replacement = null)
    {
        var spriteId = itemSprite?.Id;
        var iconId = itemSprite?.Id;

        if (iconId != null && item.Id != null && replaceable != null && replacement != null)
        {
            iconId = iconId.Replace(replaceable, replacement);
        }

        return new ItemParams
        {
            Id = item.Id,
            IconId = iconId,
            SpriteId = spriteId,
            Meta = itemSprite == null ? null : Serializer.Serialize(itemSprite.Tags),
            Type = itemType,
            // PhysicalDamageBonus = 5
        };
    }

    /// <summary>
    /// Initialize owned items (just for example).
    /// </summary>
    public void InitializeCharacterInventory()
    {
        var equipped = new List<Item>();
        Equipment.Initialize(ref equipped);
    }

    public void InitializePlayerInventory()
    {
        var sprites = SpriteCollection.Helmet;
        ItemCollection.Active.Items = sprites.Select(i => CreateFakeItemParams(new Item(i.Id), i, ItemType.Helmet)).ToList();
        var inventory = ItemCollection.Active.Items.Select(i => new Item(i.Id)).ToList(); // inventory.Clear();

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
        Refresh();
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
    }

    public void Remove()
    {
        MoveItem(SelectedItem, Equipment, PlayerInventory);
        SelectItem(SelectedItem);
        AudioSource.PlayOneShot(EquipSound, SfxVolume);

        UnequipStats();
    }

    public void EquipStats()
    {
        ItemParams itemParams = ItemCollection.Active.GetItemParams(SelectedItem);

        // if (itemParams.PhysicalDamageBonus != 0)
        //     CharacterEntity.PhysicalDamage.AddModifier(new StatModifier(itemParams.PhysicalDamageBonus, StatModType.Flat, SelectedItem.Id));

        // if (itemParams.PhysicalDamagePercentBonus != 0)
        //     CharacterEntity.PhysicalDamage.AddModifier(new StatModifier(itemParams.PhysicalDamagePercentBonus, StatModType.PercentMult, SelectedItem.Id));

        // statsValues.text = CharacterEntity.PhysicalDamage.Value.ToString();
    }

    public void UnequipStats(Item item = null)
    {
        Item source = SelectedItem;
        if (item != null)
        {
            source = item;
        }

        // CharacterEntity.PhysicalDamage.RemoveAllModifiersFromSource(source.Id);

        // statsValues.text = CharacterEntity.PhysicalDamage.Value.ToString();
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