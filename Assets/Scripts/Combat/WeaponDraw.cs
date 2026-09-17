using Assets.HeroEditor.InventorySystem.Scripts.Data;
using HeroEditor.Common.Enums;

/// <summary>
/// Drawing a racked weapon for its cast. A hero fights with the weapon in hand, but the active verb
/// may belong to a weapon on the rack: for that cast the hero draws it — sprite, grip and swing come
/// from the drawn weapon — and puts it away when the cast ends, so the next auto attack is the hand
/// weapon's again. The rig re-reads its weapon type from what is equipped, so the other weapon parts
/// are cleared first, and the paired-blade rule gets the last word on the grip. A two-hander or a
/// pair puts the shield away; the hand weapon brings it back.
/// </summary>
public static class WeaponDraw
{
    /// <summary>Draw the weapon that teaches <paramref name="spell"/>, if it is racked rather than in hand.</summary>
    public static bool Begin(Entity entity, Spell spell)
    {
        if (entity == null || entity.character == null || entity.Resonance == null || spell == null) return false;
        var weapon = entity.Resonance.WeaponTeaching(spell);
        if (weapon == null) return false;
        var hand = entity.HandWeapon;
        if (hand != null && (ReferenceEquals(weapon, hand) || weapon.Id == hand.Id)) return false;

        entity.DrawnWeapon = weapon;
        Show(entity, weapon);
        return true;
    }

    /// <summary>Put a drawn weapon away and bring the hand weapon back. Safe to call when nothing is drawn.</summary>
    public static void End(Entity entity)
    {
        if (entity == null || entity.DrawnWeapon == null) return;
        entity.DrawnWeapon = null;
        if (entity.character != null && entity.HandWeapon != null) Show(entity, entity.HandWeapon);
    }

    private static void Show(Entity entity, Item weapon)
    {
        var character = entity.character;
        // Clear every weapon part: a bow drawn over a sword would otherwise leave the sword in the
        // other hand, and the rig's type would follow whichever it read last.
        character.UnEquip(EquipmentPart.Bow);
        character.UnEquip(EquipmentPart.MeleeWeapon2H);
        character.UnEquip(EquipmentPart.MeleeWeapon1H);

        // The shield first: the rig's shield case sets the one-handed grip whether a shield is going
        // on or coming off, so the weapon's own equip has to come after it to have the last word.
        bool bothHands = Loadout.OccupiesBothHands(weapon);
        if (bothHands) character.UnEquip(EquipmentPart.Shield);
        else if (entity.HandShield != null) character.Equip(entity.HandShield);

        character.Equip(weapon);

        // Paired blades set the paired grip; coming off a pair restores the one-handed grip.
        DualWield.Apply(entity, weapon);
        character.UpdateAnimation();
    }
}
