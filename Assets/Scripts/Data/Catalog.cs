using System.Collections.Generic;
using System.Linq;
using Assets.HeroEditor.InventorySystem.Scripts;
using Assets.HeroEditor.InventorySystem.Scripts.Data;
using Assets.HeroEditor.InventorySystem.Scripts.Enums;
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
    // boots — 297 sets of three, each on one sprite family. Helmets and capes are their own
    // families, named by theme: AngelicDress goes with AngelicRibbon and AngelicCape,
    // ArmorOfCorruption with HelmetOfCorruption and CapeOfCorruption. The theme is the name with
    // its garment word taken off, matched within the same pack and tier. Measured on the
    // collection: 165 of the 297 sets find a helmet this way and 35 a cape, where exact names
    // found 7. Several can match (a TypeB, a Helm1/Helm2), and all of them are the set's.

    // Two pieces since the waist split: the upper keeps the vest's id and dresses pauldrons and
    // gloves with it; the lower keeps the boots' id and dresses the belt. Gloves rows are disabled.
    public static readonly string[] ArmorParts = { "vest", "boots" };

    /// <summary>What a part is called on a card: the vest id is the Upper, the boots id the Lower.</summary>
    public static string PartLabel(string part) => part == "vest" ? "Upper" : part == "boots" ? "Lower" : part;

    /// <summary>
    /// What an item type is called anywhere a player or designer reads it. The vendor's enum names
    /// are its own: VestBeltPauldron is the upper armour now (vest, pauldrons, gloves) and Boots the
    /// lower (boots, belt); a Gloves item only exists in saves from before the split.
    /// </summary>
    public static string TypeLabel(ItemType type)
    {
        switch (type)
        {
            case ItemType.VestBeltPauldron: return "Upper armor";
            case ItemType.Boots: return "Lower armor";
            case ItemType.Gloves: return "Gloves (old)";
            case ItemType.Armor: return "Cape";
            default: return type.ToString();
        }
    }

    private static readonly string[] GarmentWords =
    {
        "Armor", "Armour", "Dress", "Robe", "Outfit", "Costume", "Suit", "Mail", "Plate", "Garb", "Tunic",
        "Coat", "Gown", "Uniform", "Attire", "Clothes", "Cloth", "Vest", "Leather", "Chainmail", "Loincloth",
        "Helmet", "Helm", "Hat", "Hood", "Mask", "Crown", "Ribbon", "Headband", "Halo", "Earpiece", "Cap",
        "Circlet", "Tiara", "Veil", "Bandana", "Turban", "Cowl", "Hair", "Wreath", "Horns", "Headdress",
        "Visor", "Bonnet", "Beret", "Hairpin", "Ears", "Glasses", "Goggles", "Eyeguard",
        "Cape", "Wings", "Cloak", "Mantle", "Scarf", "Backpack",
        // Weapons and shields share the theme too — SwordOfCryingDemon, BennuBow, BlacksmithHammer.
        "Greatsword", "Sword", "Blade", "Saber", "Sabre", "Katana", "Dagger", "Knife", "Axe", "Hatchet",
        "Hammer", "Mace", "Club", "Flail", "Scepter", "Sceptre", "Staff", "Wand", "Rod", "Spear", "Lance",
        "Pike", "Halberd", "Scythe", "Glaive", "Crossbow", "Bow", "Gun", "Rifle", "Pistol", "Musket",
        "Shotgun", "Blaster", "Cannon", "Shield", "Buckler", "Claws", "Claw", "Whip", "Trident", "Stake",
        "Torch", "Fan", "Boomerang", "Star",
    };

    private static bool IsCompanionType(ItemType type) => type == ItemType.Weapon || type == ItemType.Shield;

    /// <summary>
    /// The weapons and shields on the set's theme, in its pack. Not pieces of the set — a set is
    /// something any hero can wear whatever they swing — but what goes with it: the weapon often
    /// says what the set is for (BennuRobe and BennuBow; ArmorOfAncestors and StaffOfAncestors).
    /// Measured: 115 of the 297 sets have a weapon on their theme, 41 a shield.
    /// </summary>
    public static List<ItemParams> Companions(string setKey)
    {
        var found = new List<ItemParams>();
        var collection = Items();
        if (collection == null || collection.Items == null || string.IsNullOrEmpty(setKey)) return found;

        string family = Family(setKey);
        string theme = Theme(SetName(setKey));
        foreach (var item in collection.Items)
        {
            if (item == null || string.IsNullOrEmpty(item.Id) || !IsCompanionType(item.Type)) continue;
            if (Family(item.Id) != family || Theme(Tail(item.Id)) != theme) continue;
            found.Add(item);
        }
        return found.OrderBy(i => i.Type).ThenBy(i => i.Id).ToList();
    }

    /// <summary>"Extensions.Epic" — the pack and tier an id belongs to, which is where its set lives.</summary>
    public static string Family(string id)
    {
        var segments = id.Split('.');
        return segments.Length >= 2 ? segments[0] + "." + segments[1] : id;
    }

    /// <summary>
    /// What a name is about once its garment word is taken off: "AngelicDress" and "AngelicRibbon"
    /// are both "Angelic"; "ArmorOfCorruption" and "HelmetOfCorruption" are both "OfCorruption".
    /// Editions ("TypeB", "Helm2") and tags ("[FullHair]") are ignored.
    /// </summary>
    public static string Theme(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        int tag = name.IndexOf('[');
        if (tag >= 0) name = name.Substring(0, tag);
        name = name.Trim();
        foreach (var edition in new[] { "TypeA", "TypeB", "TypeC" })
            if (name.EndsWith(edition)) { name = name.Substring(0, name.Length - edition.Length); break; }
        while (name.Length > 0 && char.IsDigit(name[name.Length - 1])) name = name.Substring(0, name.Length - 1);

        // "ArmorOfCorruption", "HelmOfSunwalker", "CrownOfJerome": the theme is the "Of…" part.
        int of = name.IndexOf("Of", System.StringComparison.Ordinal);
        if (of > 0 && of + 2 < name.Length && char.IsUpper(name[of + 2]) &&
            System.Array.IndexOf(GarmentWords, name.Substring(0, of)) >= 0)
        {
            string rest = name.Substring(of + 2);
            if (rest.StartsWith("The")) rest = rest.Substring(3);
            return "Of" + rest;
        }

        foreach (var word in GarmentWords.OrderByDescending(w => w.Length))
            if (name.EndsWith(word) && name.Length - word.Length >= 3)
                return name.Substring(0, name.Length - word.Length);
        return name;
    }

    /// <summary>
    /// The helmets and capes on the set's theme, in its pack — all of them, since a theme can have
    /// a TypeB helmet or three numbered helms and every one is a fit.
    /// </summary>
    public static List<ItemParams> MatchingPieces(string setKey)
    {
        var found = new List<ItemParams>();
        var collection = Items();
        if (collection == null || collection.Items == null || string.IsNullOrEmpty(setKey)) return found;

        string family = Family(setKey);
        string theme = Theme(SetName(setKey));
        foreach (var item in collection.Items)
        {
            if (item == null || string.IsNullOrEmpty(item.Id)) continue;
            if (item.Type != ItemType.Helmet && item.Type != ItemType.Armor) continue;   // Armor here is the capes
            if (Family(item.Id) != family) continue;
            if (Theme(Tail(item.Id)) != theme) continue;
            found.Add(item);
        }
        return found.OrderBy(i => i.Type).ThenBy(i => i.Id).ToList();
    }

    /// <summary>
    /// The set an item belongs to — or goes with. Its own for an armour part; for a helmet, cape,
    /// weapon or shield, the armour set in the same pack on the same theme, if there is one. Null
    /// for anything else.
    /// </summary>
    public static string SetKeyFor(string id)
    {
        if (TryParseArmorPart(id, out var own, out _)) return own;
        var item = Find(id);
        if (item == null || (item.Type != ItemType.Helmet && item.Type != ItemType.Armor && !IsCompanionType(item.Type))) return null;

        var collection = Items();
        string family = Family(id);
        string theme = Theme(Tail(id));
        foreach (var other in collection.Items)
        {
            if (other == null || !TryParseArmorPart(other.Id, out var key, out var part) || part != "vest") continue;
            if (Family(key) == family && Theme(SetName(key)) == theme) return key;
        }
        return null;
    }

    private static string Tail(string id)
    {
        int dot = id.LastIndexOf('.');
        return dot >= 0 ? id.Substring(dot + 1) : id;
    }

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
