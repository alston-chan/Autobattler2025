using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Assets.HeroEditor.InventorySystem.Scripts.Enums;
using UnityEditor;
using UnityEngine;

/// <summary>
/// What a piece of armour grants, decided by what the piece IS.
///
/// The vendor's example CSV gave every vest the same four numbers, every boot the same speed and
/// every helmet the same health — base values wearing an item's name, so that no reward screen ever
/// asked the player a question. This writes the rows for every armour slot from a tier read off
/// the item's name: plate is armour and health, leather is a little armour and speed, cloth is
/// magic resist, a bunny ear is nothing. The table is below, in one place, and the classifier is
/// tested against the kits — so a new item lands in a tier the moment it is named, and a designer
/// who wants one item to be different edits its rows in Properties.csv after this has run.
///
/// Rows this writes REPLACE the rows for these slots; weapons and the rare knockback-resistance
/// lines are left exactly as they are. Then the collection is re-imported, because the collection
/// is generated from the CSV and a row that was never imported is a line that does not exist.
/// </summary>
public static class DefensiveStatsAuthor
{
    public enum Slot { Vest, Boots, Helmet, Shield, Cape }
    public enum Tier { Plate, Leather, Cloth, Cosmetic, Other }

    private const string ItemsCsv = "Assets/Data/Items.csv";
    private const string PropertiesCsv = "Assets/Data/Properties.csv";

    // Words, not a list of items: 1,068 pieces across five slots, and the words are what a player
    // reads on the piece anyway. Scored by how many words of each tier a name contains; ties go to
    // plate, so "ChainmailLightArmor" is mail before it is light.
    private static readonly Regex Cloth = new Regex(@"robe|dress|cloth|wizard|mage|warlock|cleric|priest|bishop|cardinal|witch|druid|sorcer|monk|mystic|shaman|silk|tunic|gown|acolyte|apprentice|scholar|sage|hood|hat\b|cowl|circlet|tiara|veil|turban|arcane|necro|occult|cult|shadow|dark|nightmare|crown|diadem|magic|elemental|hex|book|lamp", RegexOptions.IgnoreCase);
    // Not "armor": nearly every vest is named SomethingArmor, so the word says nothing about the piece —
    // with it in, WarlockArmor tied cloth against plate and a robe was written as plate.
    private static readonly Regex Plate = new Regex(@"plate|knight|guard|paladin|steel|iron|mail|chain|cataphract|crusader|champion|heavy|wall|titan|golem|gladiator|spartan|legion|imperial|royal|elite|warlord|destroyer|juggernaut|helm|full|great|tower|bulky|high|dragon|fire|blaze|thunder|storm|cataclysm|king|lord|general|captain|commander|battle|militia|landsknecht", RegexOptions.IgnoreCase);
    private static readonly Regex Leather = new Regex(@"leather|bandit|ranger|hunter|scout|thief|assassin|ninja|rogue|archer|light|agile|deserter|mercenary|pirate|outlaw|nomad|travel|explorer|bowman|dodge|dipper|cap\b|bandana|headband|mask|hide|fur|wolf|bear|drifter", RegexOptions.IgnoreCase);
    private static readonly Regex Cosmetic = new Regex(@"ears|ribbon|halo|horns|blossom|flower|brainz|candy|christmas|bunny|earpiece|arrows?\b|axe\b|dagger|wings|backpack|quiver|pot\b|ornament|fists|sweetness|glasses|goggles|beard|hair|pin\b|gear\b|forehead", RegexOptions.IgnoreCase);

    /// <summary>The name a player would use: no paint or hair tags, and a vest's or boot's set name rather than "vest".</summary>
    public static string ShortName(string itemId)
    {
        string id = Regex.Replace(itemId, @" \[(Paint|FullHair)\]", "");
        var parts = id.Split('.');
        string last = parts[parts.Length - 1];
        if ((last == "vest" || last == "boots" || last == "gloves") && parts.Length > 1) return parts[parts.Length - 2];
        return last;
    }

    public static Tier Classify(string itemId, Slot slot)
    {
        string name = ShortName(itemId);
        if ((slot == Slot.Helmet || slot == Slot.Cape) && Cosmetic.IsMatch(name)) return Tier.Cosmetic;
        int cloth = Cloth.Matches(name).Count, plate = Plate.Matches(name).Count, leather = Leather.Matches(name).Count;
        if (cloth == 0 && plate == 0 && leather == 0) return Tier.Other;
        int best = Mathf.Max(cloth, Mathf.Max(plate, leather));
        if (plate == best) return Tier.Plate;
        if (cloth == best) return Tier.Cloth;
        return Tier.Leather;
    }

    /// <summary>The table. What each tier of each slot grants; empty is a real answer.</summary>
    public static (PropertyId id, string value)[] Grants(Slot slot, Tier tier)
    {
        switch (slot)
        {
            case Slot.Vest:
                switch (tier)
                {
                    case Tier.Plate:   return new[] { (PropertyId.Armor, "24"), (PropertyId.HealthMax, "20") };
                    case Tier.Leather: return new[] { (PropertyId.Armor, "10"), (PropertyId.HealthMax, "10"), (PropertyId.Speed, "1"), (PropertyId.Damage, "2") };
                    case Tier.Cloth:   return new[] { (PropertyId.MagicResist, "24"), (PropertyId.HealthMax, "5"), (PropertyId.Speed, "1") };
                    default:           return new[] { (PropertyId.Armor, "12"), (PropertyId.MagicResist, "6"), (PropertyId.HealthMax, "10"), (PropertyId.Speed, "0.5") };
                }
            case Slot.Boots:
                switch (tier)
                {
                    case Tier.Plate:   return new[] { (PropertyId.Armor, "4") };
                    case Tier.Leather: return new[] { (PropertyId.Speed, "1.5") };
                    case Tier.Cloth:   return new[] { (PropertyId.Speed, "1") };
                    default:           return new[] { (PropertyId.Speed, "0.5") };
                }
            case Slot.Helmet:
                switch (tier)
                {
                    case Tier.Plate:    return new[] { (PropertyId.Armor, "8"), (PropertyId.HealthMax, "10") };
                    case Tier.Leather:  return new[] { (PropertyId.MagicResist, "10"), (PropertyId.Speed, "0.5") };
                    case Tier.Cloth:    return new[] { (PropertyId.MagicResist, "30") };
                    case Tier.Cosmetic: return new (PropertyId, string)[0];
                    default:            return new[] { (PropertyId.MagicResist, "12"), (PropertyId.HealthMax, "5") };
                }
            case Slot.Shield:
                switch (tier)
                {
                    case Tier.Plate:   return new[] { (PropertyId.Armor, "60") };
                    case Tier.Leather: return new[] { (PropertyId.Armor, "30") };
                    case Tier.Cloth:   return new[] { (PropertyId.MagicResist, "40") };
                    default:           return new[] { (PropertyId.Armor, "45") };
                }
            default: // Cape: HeroEditor calls the slot "Armor"
                switch (tier)
                {
                    case Tier.Plate:    return new[] { (PropertyId.HealthMax, "10") };
                    case Tier.Leather:  return new[] { (PropertyId.Speed, "0.5") };
                    case Tier.Cosmetic: return new (PropertyId, string)[0];
                    default:            return new[] { (PropertyId.MagicResist, "10"), (PropertyId.HealthMax, "5") };
                }
        }
    }

    /// <summary>HeroEditor's Type column, as a slot; null for anything this does not author (weapons).</summary>
    public static Slot? SlotOf(string itemType)
    {
        switch (itemType)
        {
            case "VestBeltPauldron": return Slot.Vest;
            case "Boots": return Slot.Boots;
            case "Helmet": return Slot.Helmet;
            case "Shield": return Slot.Shield;
            case "Armor": return Slot.Cape;
            default: return null;
        }
    }

    /// <summary>Kept exactly as they are through a rewrite: rare lines authored by hand.</summary>
    private static bool Preserved(PropertyId id) => id == PropertyId.KnockbackResist;

    [MenuItem("Tools/Item Database/Author Armour Rows By Tier")]
    public static void AuthorFromMenu()
    {
        Debug.Log("[DefensiveStatsAuthor] " + Author());
        EditorApplication.ExecuteMenuItem("Tools/Item Database/Import CSV into ItemCollection");
    }

    /// <summary>Rewrite the armour slots' rows in Properties.csv. Returns a summary; does not import.</summary>
    public static string Author()
    {
        var slots = new Dictionary<string, Slot>();
        foreach (var line in File.ReadAllLines(ItemsCsv))
        {
            var cols = line.Split(',');
            if (cols.Length < 3 || cols[0] != "TRUE") continue;
            var slot = SlotOf(cols[2]);
            if (slot.HasValue) slots[cols[1]] = slot.Value;
        }

        var kept = new List<string>();
        string header = null;
        foreach (var line in File.ReadAllLines(PropertiesCsv))
        {
            if (header == null) { header = line; continue; }
            if (line.Trim().Length == 0) continue;
            var cols = line.Split(',');
            bool authoredSlot = cols.Length == 3 && slots.ContainsKey(cols[0]);
            bool preserved = cols.Length == 3 && System.Enum.TryParse(cols[1], out PropertyId id) && Preserved(id);
            if (!authoredSlot || preserved) kept.Add(line);
        }

        var counts = new Dictionary<string, int>();
        var written = new List<string>();
        foreach (var kv in slots)
        {
            var tier = Classify(kv.Key, kv.Value);
            string key = kv.Value + "/" + tier;
            counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;
            foreach (var g in Grants(kv.Value, tier)) written.Add(kv.Key + "," + g.id + "," + g.value);
        }

        var sb = new StringBuilder();
        sb.Append(header).Append("\r\n");
        foreach (var l in kept) sb.Append(l).Append("\r\n");
        foreach (var l in written) sb.Append(l).Append("\r\n");
        File.WriteAllText(PropertiesCsv, sb.ToString());

        var summary = new StringBuilder();
        summary.Append("wrote ").Append(written.Count).Append(" rows for ").Append(slots.Count).Append(" pieces, kept ").Append(kept.Count).Append(" others; ");
        foreach (var kv in counts) summary.Append(kv.Key).Append('=').Append(kv.Value).Append(' ');
        return summary.ToString();
    }
}
