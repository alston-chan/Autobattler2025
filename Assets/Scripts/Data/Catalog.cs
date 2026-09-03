using System.Collections.Generic;
using System.Linq;
using Assets.HeroEditor.InventorySystem.Scripts;
using Assets.HeroEditor.InventorySystem.Scripts.Data;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// The item catalogue as something an inspector can offer: every item id as a dropdown, grouped
/// the way the ids themselves are (type, then the id's own segments), and a yes/no for whether an
/// id is real.
///
/// Item ids are strings everywhere they are referenced — resonance entries, spellbooks, reward
/// pools, hero kits — and a string that looks right and is not in the live collection fails
/// silently: the item just never appears. This is where that class of mistake stops being
/// possible to type. Editor-only in effect: in play the collection is whatever the game loaded.
/// </summary>
public static class Catalog
{
    public const string ItemCollectionPath = "Assets/Data/ItemCollection.asset";

    /// <summary>The live collection in play, else the asset on disk in the editor.</summary>
    public static ItemCollection Items()
    {
        var collection = ItemCollection.Active;
#if UNITY_EDITOR
        if (collection == null)
            collection = UnityEditor.AssetDatabase.LoadAssetAtPath<ItemCollection>(ItemCollectionPath);
#endif
        return collection;
    }

    /// <summary>
    /// Every item id, as "Type/Family/.../Name" so the dropdown reads as a tree rather than a list
    /// of two hundred dotted strings. The leading "FantasyHeroes" segment says nothing and is dropped.
    /// </summary>
    public static IEnumerable<ValueDropdownItem<string>> ItemIds()
    {
        var collection = Items();
        if (collection == null || collection.Items == null) yield break;

        foreach (var item in collection.Items.OrderBy(i => i.Type).ThenBy(i => i.Id))
        {
            if (item == null || string.IsNullOrEmpty(item.Id)) continue;
            var segments = item.Id.Split('.');
            var tail = segments.Length > 1 ? segments.Skip(1) : segments;
            yield return new ValueDropdownItem<string>(item.Type + "/" + string.Join("/", tail), item.Id);
        }
    }

    /// <summary>True when the id is in the collection — or when there is no collection to ask.</summary>
    public static bool IsKnown(string id)
    {
        var collection = Items();
        if (collection == null || collection.Items == null) return true;
        return !string.IsNullOrEmpty(id) && collection.Items.Any(i => i != null && i.Id == id);
    }

    public static ItemParams Find(string id)
    {
        var collection = Items();
        if (collection == null || collection.Items == null || string.IsNullOrEmpty(id)) return null;
        return collection.Items.FirstOrDefault(i => i != null && i.Id == id);
    }

    // ---- sets
    //
    // Every armour id is Pack.Tier.Armor.<Set>.<part>, and the parts are exactly vest, gloves and
    // boots — 297 sets of three, each on one sprite family. Helmets are their own family; a set's
    // helmet is the one that happens to share its name, which only a handful do.

    public static readonly string[] ArmorParts = { "vest", "gloves", "boots" };

    /// <summary>
    /// True when the id is an armour part. <paramref name="setKey"/> is everything before the
    /// part ("Extensions.Epic.Armor.AngelicDress"), which is what the three pieces share.
    /// </summary>
    public static bool TryParseArmorPart(string id, out string setKey, out string part)
    {
        setKey = null; part = null;
        if (string.IsNullOrEmpty(id)) return false;
        int dot = id.LastIndexOf('.');
        if (dot <= 0) return false;
        string tail = id.Substring(dot + 1);
        if (System.Array.IndexOf(ArmorParts, tail) < 0) return false;
        string head = id.Substring(0, dot);
        if (!head.Contains(".Armor.")) return false;
        setKey = head; part = tail;
        return true;
    }

    /// <summary>The set's own name: the last segment of its key.</summary>
    public static string SetName(string setKey)
    {
        int dot = setKey.LastIndexOf('.');
        return dot >= 0 ? setKey.Substring(dot + 1) : setKey;
    }

    /// <summary>The id of one part of a set, whether or not the collection has it.</summary>
    public static string PartId(string setKey, string part) => setKey + "." + part;

    /// <summary>The helmet that shares the set's name, or null — most sets have none.</summary>
    public static ItemParams MatchingHelmet(string setKey)
    {
        var collection = Items();
        if (collection == null || collection.Items == null) return null;
        string suffix = ".Helmet." + SetName(setKey);
        return collection.Items.FirstOrDefault(i => i != null && i.Id != null && i.Id.EndsWith(suffix));
    }

    /// <summary>The item's inventory icon, or null. Looked up once per caller: the collection warns for a missing one.</summary>
    public static Sprite Icon(string id)
    {
        var item = Find(id);
        var collection = Items();
        if (item == null || collection == null || string.IsNullOrEmpty(item.IconId)) return null;
        return collection.FindIcon(item.IconId);
    }

    /// <summary>The sprite the item wears on a character, or null.</summary>
    public static Sprite Look(string id)
    {
        var item = Find(id);
        var collection = Items();
        if (item == null || collection == null || string.IsNullOrEmpty(item.SpriteId)) return null;
        return collection.FindSprite(item.SpriteId);
    }

    /// <summary>The item's English name, else the last segment of its id, else the id itself.</summary>
    public static string DisplayName(string id)
    {
        var item = Find(id);
        if (item != null)
        {
            var localized = item.GetLocalizedName("English");
            if (!string.IsNullOrEmpty(localized)) return localized;
        }
        if (string.IsNullOrEmpty(id)) return "(no item)";
        int dot = id.LastIndexOf('.');
        return dot >= 0 ? id.Substring(dot + 1) : id;
    }
}
