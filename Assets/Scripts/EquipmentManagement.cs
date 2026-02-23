using System.Collections.Generic;
using System.Linq;
using Assets.HeroEditor.Common.Scripts.CharacterScripts;
using Assets.HeroEditor.Common.Scripts.Common;
using Assets.HeroEditor.InventorySystem.Scripts;
using Assets.HeroEditor.InventorySystem.Scripts.Data;
using Assets.HeroEditor.InventorySystem.Scripts.Enums;
using HeroEditor.Common.Enums;
using UnityEngine;

public class EquipmentManagement : MonoBehaviour
{
    private Character Character;
    private Appearance Appearance;

    public void Awake()
    {
        Character = GetComponent<Character>();
        Appearance = GetComponent<Appearance>();
    }

    /// <summary>
    /// Pick a random enabled item of the given type from ItemCollection and equip it visually.
    /// Returns the picked Item (for inventory tracking), or null if none available.
    /// </summary>
    public Item EquipRandomFromCollection(ItemType type)
    {
        var candidates = ItemCollection.Active?.Items?.Where(i => i.Type == type).ToList();

        if (candidates == null || candidates.Count == 0) return null;

        var picked = candidates[Random.Range(0, candidates.Count)];
        var item = new Item(picked.Id);

        Character.Equip(item);
        return item;
    }

    /// <summary>
    /// Equip random items from ItemCollection for all equipment slots.
    /// Returns the list of equipped items.
    /// </summary>
    public List<Item> EquipRandomFromCollection(bool isRanged = false)
    {
        var equipped = new List<Item>();

        var vest = EquipRandomFromCollection(ItemType.VestBeltPauldron);
        if (vest != null) equipped.Add(vest);

        var gloves = EquipRandomFromCollection(ItemType.Gloves);
        if (gloves != null) equipped.Add(gloves);

        var boots = EquipRandomFromCollection(ItemType.Boots);
        if (boots != null) equipped.Add(boots);

        var helmet = EquipRandomFromCollection(ItemType.Helmet);
        if (helmet != null) equipped.Add(helmet);

        var shield = EquipRandomFromCollection(ItemType.Shield);
        if (shield != null) equipped.Add(shield);

        if (isRanged)
        {
            // For ranged, look for Bow class weapons
            var bows = ItemCollection.Active?.Items?
                .Where(i => i.Type == ItemType.Weapon && i.Class == ItemClass.Bow).ToList();
            if (bows != null && bows.Count > 0)
            {
                var picked = bows[Random.Range(0, bows.Count)];
                var bow = new Item(picked.Id);
                Character.Equip(bow);
                equipped.Add(bow);
            }
        }
        else
        {
            // For melee, exclude bows
            var melee = ItemCollection.Active?.Items?
                .Where(i => i.Type == ItemType.Weapon && i.Class != ItemClass.Bow).ToList();
            if (melee != null && melee.Count > 0)
            {
                var picked = melee[Random.Range(0, melee.Count)];
                var weapon = new Item(picked.Id);
                Character.Equip(weapon);
                equipped.Add(weapon);
            }
        }

        Appearance.Refresh();
        return equipped;
    }

    // Legacy methods kept for compatibility

    public void EquipRandomArmor()
    {
        var randomIndex = Random.Range(0, Character.SpriteCollection.Armor.Count);
        var randomItem = Character.SpriteCollection.Armor[randomIndex];

        Character.Equip(randomItem, EquipmentPart.Vest);
        Character.Equip(randomItem, EquipmentPart.Gloves);
        Character.Equip(randomItem, EquipmentPart.Boots);
    }

    public void RemoveArmor()
    {
        Character.UnEquip(EquipmentPart.Vest);
        Character.UnEquip(EquipmentPart.Gloves);
        Character.UnEquip(EquipmentPart.Boots);
    }

    public void EquipRandomHelmet()
    {
        Character.Equip(Character.SpriteCollection.Helmet.Random(), EquipmentPart.Helmet);
        Appearance.Refresh();
    }

    public void RemoveHelmet()
    {
        Character.UnEquip(EquipmentPart.Helmet);
        Appearance.Refresh();
    }

    public void EquipRandomShield()
    {
        Character.Equip(Character.SpriteCollection.Shield.Random(), EquipmentPart.Shield);
    }

    public void RemoveShield()
    {
        Character.UnEquip(EquipmentPart.Shield);
    }

    public void EquipRandomWeapon()
    {
        Character.Equip(Character.SpriteCollection.MeleeWeapon1H.Random(), EquipmentPart.MeleeWeapon1H);
    }

    public void RemoveWeapon()
    {
        Character.UnEquip(EquipmentPart.MeleeWeapon1H);
    }

    public void EquipRandomBow()
    {
        Character.Equip(Character.SpriteCollection.Bow.Random(), EquipmentPart.Bow);
    }

    public void RemoveBow()
    {
        Character.UnEquip(EquipmentPart.Bow);
    }

    public void Reset()
    {
        Character.ResetEquipment();
        Appearance.CharacterAppearance = new CharacterAppearance();
        Appearance.Refresh();
    }

    // Legacy: Equip random from SpriteCollection directly
    public void EquipRandom(bool isRanged = false)
    {
        EquipRandomArmor();
        EquipRandomHelmet();
        EquipRandomShield();
        if (isRanged)
        {
            EquipRandomBow();
        }
        else
        {
            EquipRandomWeapon();
        }
    }
}