using Assets.HeroEditor.Common.Scripts.CharacterScripts;
using Assets.HeroEditor.Common.Scripts.Common;
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

    public void EquipRandomArmor()
    {
        var randomIndex = Random.Range(0, Character.SpriteCollection.Armor.Count);
        var randomItem = Character.SpriteCollection.Armor[randomIndex];

        Character.Equip(randomItem, EquipmentPart.Armor);
    }

    public void RemoveArmor()
    {
        Character.UnEquip(EquipmentPart.Armor);
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

    // Equip random equipment, only bow if ranged, else melee weapon
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