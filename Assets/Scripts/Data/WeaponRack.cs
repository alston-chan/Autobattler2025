using System.Collections.Generic;
using Assets.HeroEditor.InventorySystem.Scripts.Data;

/// <summary>
/// The rack's one rule, kept pure so it can be tested: a weapon displaced from the hand goes to the
/// back of the rack, and when the rack is full the weapon that has waited longest is handed back.
/// </summary>
public static class WeaponRack
{
    /// <summary>
    /// Put <paramref name="weapon"/> on the rack. Returns the weapon evicted to make room, or null.
    /// A weapon already on the rack is moved to the back rather than duplicated.
    /// </summary>
    public static Item Push(List<Item> rack, Item weapon, int capacity)
    {
        if (rack == null || weapon == null) return null;
        rack.Remove(weapon);
        Item evicted = null;
        if (capacity <= 0) return weapon;
        while (rack.Count >= capacity)
        {
            evicted = rack[0];
            rack.RemoveAt(0);
        }
        rack.Add(weapon);
        return evicted;
    }
}
