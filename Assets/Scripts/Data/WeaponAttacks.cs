using System.Collections.Generic;
using Assets.HeroEditor.InventorySystem.Scripts.Enums;
using UnityEngine;

/// <summary>
/// Which basic attack a weapon brings with it (Docs/Resonance.md sits next door; this is the combat
/// half of "what is this item").
///
/// Until now the relationship ran backwards: a unit was decided to be ranged, that chose a bow
/// attack, and only then was a bow rolled to match. That works for two categories and stops working
/// at three — a wand is neither, and a unit that happened to roll one stood in melee range swinging
/// it, because the spell had already been chosen. Letting the weapon choose the attack is what makes
/// a third weapon type possible at all, and it means picking a weapon up is what changes how a hero
/// fights.
///
/// A class with no entry keeps whatever the unit already had, so ordinary swords and axes go on
/// using the melee attack without needing a row each.
/// </summary>
[CreateAssetMenu(menuName = "Data/Weapon Attacks", fileName = "WeaponAttacks")]
public class WeaponAttacks : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        public ItemClass weaponClass;
        public Spell basicAttack;
    }

    public List<Entry> entries = new List<Entry>();

    private static WeaponAttacks _active;

    public static WeaponAttacks Active
    {
        get
        {
            if (_active == null)
            {
                _active = Resources.Load<WeaponAttacks>("WeaponAttacks");
                if (_active == null)
                    Debug.LogWarning("[WeaponAttacks] No asset at Resources/WeaponAttacks — weapons " +
                                     "will not change how their bearer attacks.");
            }
            return _active;
        }
    }

    /// <summary>The basic attack this weapon class brings, or null to leave the unit's alone.</summary>
    public Spell For(ItemClass weaponClass)
    {
        if (entries == null) return null;

        for (int i = 0; i < entries.Count; i++)
            if (entries[i] != null && entries[i].weaponClass == weaponClass) return entries[i].basicAttack;

        return null;
    }

    /// <summary>Convenience for callers that just want to apply the mapping to a unit.</summary>
    public static void Apply(Entity entity, ItemClass weaponClass)
    {
        if (entity == null || Active == null) return;

        var attack = Active.For(weaponClass);
        if (attack != null) entity.SetBasicAttack(attack);
    }
}
