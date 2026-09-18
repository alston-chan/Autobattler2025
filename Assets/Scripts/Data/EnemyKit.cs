using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// An enemy dressed on purpose: what it wears and which slot it casts. Worn gear resonates as a
/// hero's does, so the kit's weapon teaches its verb, its set pieces grant their engravings, and its
/// tactics item says how it fights — a kit authors no stance of its own, exactly like a hero. An encounter can name a kit per spawn, a loadout can hold a pool the
/// spawner draws from, and a playtest scenario can do either. Kits are what make enemies read as a
/// team with a plan rather than a crowd with weapons.
/// </summary>
[CreateAssetMenu(menuName = "Data/Enemy Kit", fileName = "EnemyKit")]
public class EnemyKit : ScriptableObject
{
    [Tooltip("What the player will call it.")]
    public string kitName;
    [Tooltip("Everything worn, weapon included. Unknown ids are skipped with a warning.")]
    [ValueDropdown("ItemIds")] public List<string> itemIds = new List<string>();
    [Tooltip("Which spell slot is cast. With no spellbooks, 0 is the weapon's verb.")]
    public int activeSlot = 0;
    [TextArea(1, 4)] public string notes;

    public string DisplayName => string.IsNullOrEmpty(kitName) ? name : kitName;

    private static IEnumerable<ValueDropdownItem<string>> ItemIds() => Catalog.ItemIds();

    /// <summary>
    /// Draw <paramref name="count"/> kits from a pool: every kit once before any repeats, in a random
    /// order, so three spawns from seven kits are three different fighters.
    /// </summary>
    public static List<EnemyKit> Draw(IList<EnemyKit> pool, int count)
    {
        var result = new List<EnemyKit>(count);
        if (pool == null || pool.Count == 0 || count <= 0) return result;
        var bag = new List<EnemyKit>();
        while (result.Count < count)
        {
            if (bag.Count == 0) { bag.AddRange(pool); bag.RemoveAll(k => k == null); if (bag.Count == 0) break; }
            int i = Random.Range(0, bag.Count);
            result.Add(bag[i]);
            bag.RemoveAt(i);
        }
        return result;
    }
}
