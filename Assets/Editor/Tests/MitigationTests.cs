using System.Collections.Generic;
using System.IO;
using Assets.HeroEditor.InventorySystem.Scripts;
using Assets.HeroEditor.InventorySystem.Scripts.Data;
using Assets.HeroEditor.InventorySystem.Scripts.Enums;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The damage pipeline's arithmetic, the migration from flat Blocking, and the contract that makes
/// item properties safe to add: the vendor enum's numbers never move.
/// </summary>
public class MitigationTests
{
    // ---------- the curve ----------

    [Test]
    public void EveryPointIsOnePercentMoreEffectiveHealth()
    {
        Assert.That(Mitigation.Reduce(100f, 0f), Is.EqualTo(100f));
        Assert.That(Mitigation.Reduce(100f, 100f), Is.EqualTo(50f).Within(0.01f), "a hundred halves it");
        Assert.That(Mitigation.Reduce(100f, 50f), Is.EqualTo(66.67f).Within(0.01f), "fifty takes a third");
        Assert.That(Mitigation.Reduce(100f, 300f), Is.EqualTo(25f).Within(0.01f), "and it never reaches zero");
        Assert.That(Mitigation.Reduce(100f, -40f), Is.EqualTo(100f), "a negative rating is not a weakness");
    }

    [Test]
    public void TheFractionShownIsWhatTheCurveTakes()
    {
        for (float rating = 0f; rating <= 200f; rating += 25f)
            Assert.That(Mitigation.Reduce(100f, rating), Is.EqualTo(100f * (1f - Mitigation.Fraction(rating))).Within(0.001f), "at " + rating);
    }

    // ---------- there are no default stats ----------

    [Test]
    public void NoItemStillGrantsBlocking()
    {
        foreach (var line in File.ReadAllLines("Assets/Data/Properties.csv"))
            Assert.That(line, Does.Not.Contain(",Blocking,"), "a Blocking row survived: " + line);
    }

    private static Dictionary<string, Dictionary<string, string>> Rows()
    {
        var byItem = new Dictionary<string, Dictionary<string, string>>();
        foreach (var line in File.ReadAllLines("Assets/Data/Properties.csv"))
        {
            var cols = line.Split(',');
            if (cols.Length != 3 || cols[0] == "ItemId") continue;
            if (!byItem.TryGetValue(cols[0], out var d)) byItem[cols[0]] = d = new Dictionary<string, string>();
            d[cols[1]] = cols[2];
        }
        return byItem;
    }

    private static HashSet<string> EnabledItems()
    {
        var ids = new HashSet<string>();
        foreach (var line in File.ReadAllLines("Assets/Data/Items.csv"))
        {
            var cols = line.Split(',');
            if (cols.Length > 2 && cols[0] == "TRUE") ids.Add(cols[1]);
        }
        return ids;
    }

    [Test]
    public void EveryItemTheGameHandsOutIsAuthored()
    {
        // The rule: the CSV is the design. An item a kit, a scenario, a reward pool, a loadout, an
        // engraving or the scene names must have rows a designer chose for it — nothing is granted
        // by default, so an unauthored item in play is a piece of gear that does nothing, silently.
        var enabled = EnabledItems();
        var rows = Rows();
        var sources = new List<string>();
        sources.AddRange(Directory.GetFiles("Assets/Data", "*.asset", SearchOption.AllDirectories));
        sources.Add("Assets/Resources/ResonanceDatabase.asset");
        sources.Add("Assets/Scenes/Main.unity");

        var id = new System.Text.RegularExpressions.Regex(@"((?:FantasyHeroes|Extensions)\.[A-Za-z0-9_]+\.[A-Za-z0-9_]+\.[A-Za-z0-9_\[\] ]+?(?:\.(?:vest|boots|gloves))?)\s*$",
                                                          System.Text.RegularExpressions.RegexOptions.Multiline);
        var missing = new List<string>();
        var seen = new HashSet<string>();
        foreach (var path in sources)
        {
            if (path.EndsWith("ItemCollection.asset")) continue;   // the collection names everything
            foreach (System.Text.RegularExpressions.Match m in id.Matches(File.ReadAllText(path)))
            {
                string item = m.Groups[1].Value.Trim();
                if (!enabled.Contains(item) || !seen.Add(item)) continue;
                if (!rows.ContainsKey(item)) missing.Add(item + "  (" + Path.GetFileName(path) + ")");
            }
        }
        Assert.That(seen.Count, Is.GreaterThan(40), "the scan found too few items to be trusted");
        Assert.That(missing, Is.Empty, "handed out but never designed: " + string.Join(" | ", missing));
    }

    [Test]
    public void AnItemNobodyDesignedGrantsNothing()
    {
        var rows = Rows();
        foreach (var vendorItem in new[] {
            "Extensions.Epic.Armor.AngelicDress.vest",
            "Extensions.Style.Helmet.BunnyEarsA1 [Paint] [FullHair]",
            "Extensions.Epic.Armor.ThunderguardArmor.vest",
            "FantasyHeroes.Basic.MeleeWeapon1H.ShortSword" })
            Assert.That(rows.ContainsKey(vendorItem), Is.False, vendorItem + " has rows nobody designed");

        // And the file is a design, not a table: a few hundred chosen lines, not thousands generated.
        int total = 0; foreach (var kv in rows) total += kv.Value.Count;
        Assert.That(total, Is.LessThan(400), total + " rows — something is generating defaults again");
    }

    [Test]
    public void TheCollectionCarriesTheAuthoredRows()
    {
        // The collection is generated from the CSV by an importer that REPLACES the list; a row
        // written and never imported is a line that does not exist.
        ItemCollection.Active = AssetDatabase.LoadAssetAtPath<ItemCollection>("Assets/Data/ItemCollection.asset");
        var plate = ItemCollection.Active.Items.Find(i => i.Id == "Extensions.AbandonedWorkshop.Armor.WallKeeperArmor.vest");
        var robe = ItemCollection.Active.Items.Find(i => i.Id == "FantasyHeroes.Basic.Armor.WarlockArmor.vest");
        var shield = ItemCollection.Active.Items.Find(i => i.Id == "Extensions.AbandonedWorkshop.Shield.WallKeeperShield");
        var hood = ItemCollection.Active.Items.Find(i => i.Id == "FantasyHeroes.Basic.Helmet.WarlockHood");
        var nothing = ItemCollection.Active.Items.Find(i => i.Id == "Extensions.Style.Helmet.BunnyEarsA1 [Paint] [FullHair]");
        Assert.That(Of(plate, PropertyId.Armor), Is.EqualTo("30"), "plate — re-import the CSV if this is null");
        Assert.That(Of(plate, PropertyId.MagicResist), Is.Null, "plate resists no magic");
        Assert.That(Of(robe, PropertyId.MagicResist), Is.EqualTo("24"), "a robe");
        Assert.That(Of(robe, PropertyId.Armor), Is.Null, "a robe is no armour");
        Assert.That(Of(shield, PropertyId.Armor), Is.EqualTo("60"), "a wall keeper's shield");
        Assert.That(Of(shield, PropertyId.KnockbackResist), Is.EqualTo("40"), "and its rare line");
        Assert.That(Of(hood, PropertyId.MagicResist), Is.EqualTo("30"), "a hood");
        Assert.That(nothing, Is.Not.Null); Assert.That(nothing.Properties.Count, Is.EqualTo(0), "a cosmetic grants nothing");
    }

    private static string Of(ItemParams item, PropertyId id)
    {
        Assert.That(item, Is.Not.Null);
        foreach (var p in item.Properties) if (p.Id == id) return p.Value;
        return null;
    }

    // ---------- who is magical ----------

    [Test]
    public void TheWandsVerbsAndTheBurnAreMagical()
    {
        var singularity = AssetDatabase.LoadAssetAtPath<CompositeSpell>("Assets/Data/Spells/Singularity.asset");
        DealDamageEffect strike = null;
        foreach (var e in singularity.effects) if (e is DealDamageEffect d) strike = d;
        Assert.That(strike, Is.Not.Null);
        Assert.That(strike.damageType, Is.EqualTo(DamageType.Magical), "Singularity");

        var burn = AssetDatabase.LoadAssetAtPath<Status>("Assets/Data/Statuses/Burn.asset");
        Assert.That(burn.damageType, Is.EqualTo(DamageType.Magical), "Burn");

        var tarPool = AssetDatabase.LoadAssetAtPath<CompositeSpell>("Assets/Data/Spells/TarPool.asset");
        ZoneEffect pool = null;
        foreach (var e in tarPool.effects) if (e is ZoneEffect z) pool = z;
        Assert.That(pool, Is.Not.Null);
        Assert.That(pool.damageType, Is.EqualTo(DamageType.Magical), "Tar Pool");

        var cannonball = AssetDatabase.LoadAssetAtPath<CompositeSpell>("Assets/Data/Spells/Cannonball.asset");
        DealDamageEffect blow = null;
        foreach (var e in cannonball.effects) if (e is DealDamageEffect d) blow = d;
        Assert.That(blow.damageType, Is.EqualTo(DamageType.Physical), "a hammer is a hammer");
    }

    [Test]
    public void ASlamIsPhysicalAndAHitIsPhysicalByDefault()
    {
        Assert.That(new DamageInfo(1f, 1f, null, false).type, Is.EqualTo(DamageType.Physical));
        Assert.That(new DamageInfo(1f, 1f, null, false, 0f, DamageKind.Slam).type, Is.EqualTo(DamageType.Physical));
    }
}

/// <summary>
/// ItemCollection.asset stores a property as its integer. These numbers are therefore a contract:
/// a member inserted anywhere but the end of the vendor enum renumbers every property on every
/// item, silently, and the first sign is a knight whose armour reads as ammo.
/// </summary>
public class PropertyIdContractTests
{
    [Test]
    public void TheVendorsNumbersDidNotMove()
    {
        Assert.That((int)PropertyId.Blocking, Is.EqualTo(4));
        Assert.That((int)PropertyId.Damage, Is.EqualTo(13));
        Assert.That((int)PropertyId.HealthMax, Is.EqualTo(22));
        Assert.That((int)PropertyId.Resistance, Is.EqualTo(30));
        Assert.That((int)PropertyId.Shock, Is.EqualTo(38), "the vendor's last member");
    }

    [Test]
    public void TheProjectsMembersComeAfterTheVendors()
    {
        Assert.That((int)PropertyId.Armor, Is.EqualTo(39));
        Assert.That((int)PropertyId.MagicResist, Is.EqualTo(40));
        Assert.That((int)PropertyId.KnockbackResist, Is.EqualTo(41));
    }
}
