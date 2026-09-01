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

        // Weapon BEFORE shield, because whether a shield is allowed at all depends on what the unit
        // ended up holding. Rolling the shield first and then a weapon from every melee class handed
        // out shield + two-handed sword, which the equipment window forbids but this path never
        // checked — the shield simply sat hidden behind the weapon until the weapon came off.
        Item weapon = null;

        if (isRanged)
        {
            // For ranged, look for Bow class weapons
            var bows = ItemCollection.Active?.Items?
                .Where(i => i.Type == ItemType.Weapon && i.Class == ItemClass.Bow).ToList();
            if (bows != null && bows.Count > 0)
            {
                var picked = bows[Random.Range(0, bows.Count)];
                weapon = new Item(picked.Id);
            }
        }
        else
        {
            // For melee, exclude bows — and firearms, which HeroEditor's CharacterInventorySetup
            // can't equip on these rigs ("Firearm equipping is not implemented"). Rolling one left
            // the unit weaponless and unable to attack.
            //
            // Wands are excluded too. They sit in this pool only because they are neither bow nor
            // firearm, and that was harmless while every non-bow weapon swung the same way. Now that
            // a wand brings its own attack, a melee roll could quietly turn a front-line fighter
            // into a caster who wants to stand at range.
            var melee = ItemCollection.Active?.Items?
                .Where(i => i.Type == ItemType.Weapon &&
                            i.Class != ItemClass.Bow && i.Class != ItemClass.Firearm &&
                            i.Class != ItemClass.Wand).ToList();
            if (melee != null && melee.Count > 0)
            {
                var picked = melee[Random.Range(0, melee.Count)];
                weapon = new Item(picked.Id);
            }
        }

        if (weapon != null)
        {
            Character.Equip(weapon);
            equipped.Add(weapon);

            // Enemies have no inventory window, so this is where their weapon class gets recorded —
            // and where the weapon gets to choose their attack. An enemy rolling a wand out of the
            // melee pool used to stand in sword range swinging it.
            var entity = GetComponent<Entity>();
            if (entity != null)
            {
                Loadout.ApplyTo(entity, weapon);
            }
        }

        // A free hand is the requirement, so this covers bows too rather than treating ranged as a
        // special case — a bow is two-handed like any greatsword, and paired blades fill both hands
        // without being tagged that way at all.
        bool handFree = weapon == null || !Loadout.OccupiesBothHands(weapon);
        if (handFree)
        {
            var shield = EquipRandomFromCollection(ItemType.Shield);
            if (shield != null) equipped.Add(shield);
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

        if (isRanged)
        {
            // No shield: a bow is two-handed. Handing out both left the shield equipped and hidden
            // behind the bow, surfacing only when the bow was taken off.
            EquipRandomBow();
        }
        else
        {
            // EquipRandomWeapon draws from the one-handed collection, so a shield is always fine here.
            EquipRandomWeapon();
            EquipRandomShield();
        }
    }
}