using System.Collections.Generic;
using System.Linq;
using Assets.HeroEditor.InventorySystem.Scripts;
using Assets.HeroEditor.InventorySystem.Scripts.Data;
using UnityEngine;

/// <summary>What the shared bag holds when a run begins.</summary>
public enum StartingBag
{
    /// <summary>Nothing. A run's bag fills from what the run drops.</summary>
    Empty,

    /// <summary>A copy of every designed item — everything that carries an engraving or teaches a
    /// spell — so any of it can be tested at any time. Nothing else: plain gear is what a run finds,
    /// and in a workshop it is only clutter between the pieces worth testing.</summary>
    Workshop
}

/// <summary>
/// The designed items, and the bag a run starts with.
///
/// "Designed" has one definition here: an item is designed if the game says something about it — it
/// carries an engraving (ResonanceDatabase) or teaches a spell (SpellbookDatabase). Those two lists
/// are the whole record, so the workshop bag is derived from them rather than kept by hand. The
/// hand-kept version was three spellbooks and one bow, chosen when they were the only designed items
/// there were, and every item designed since was left out: Marked's gloves could only be met by
/// winning them in a reward roll, which is no way to test an engraving. That stock also went into
/// every run, so a progression run opened with a free engraved bow in the bag.
/// </summary>
public static class BagStock
{
    /// <summary>
    /// Every designed item id, engraved gear first, then spellbooks, each once. An id the collection
    /// does not know is skipped with a warning — a database entry for an item that does not exist is
    /// a typo nothing else would ever report.
    /// </summary>
    public static List<string> DesignedItemIds()
    {
        var ids = new List<string>();

        if (ResonanceDatabase.Active != null)
            foreach (var entry in ResonanceDatabase.Active.entries)
                if (entry != null && entry.engraving != null) Add(ids, entry.itemId, "ResonanceDatabase");

        if (SpellbookDatabase.Active != null)
            foreach (var entry in SpellbookDatabase.Active.entries)
                if (entry != null && entry.spell != null) Add(ids, entry.itemId, "SpellbookDatabase");

        return ids;
    }

    private static void Add(List<string> ids, string id, string source)
    {
        if (string.IsNullOrEmpty(id) || ids.Contains(id)) return;

        if (ItemCollection.Active != null && !ItemCollection.Active.Items.Any(i => i.Id == id))
        {
            Debug.LogWarning($"[BagStock] {source} names '{id}', which is not in the item collection — skipped.");
            return;
        }

        ids.Add(id);
    }

    /// <summary>The items a run's bag opens with.</summary>
    public static List<Item> For(StartingBag bag)
    {
        var items = new List<Item>();
        if (bag != StartingBag.Workshop || ItemCollection.Active == null) return items;

        foreach (var id in DesignedItemIds()) items.Add(new Item(id));
        return items;
    }
}
