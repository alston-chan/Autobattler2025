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

    // ---------- what a piece grants depends on what it is ----------

    [Test]
    public void NoItemStillGrantsBlocking()
    {
        foreach (var line in File.ReadAllLines("Assets/Data/Properties.csv"))
            Assert.That(line, Does.Not.Contain(",Blocking,"), "a Blocking row survived: " + line);
    }

    [Test]
    public void TheKitsClassifyWhereADesignerWouldPutThem()
    {
        var V = DefensiveStatsAuthor.Slot.Vest; var H = DefensiveStatsAuthor.Slot.Helmet;
        Assert.That(DefensiveStatsAuthor.Classify("Extensions.AbandonedWorkshop.Armor.WallKeeperArmor.vest", V), Is.EqualTo(DefensiveStatsAuthor.Tier.Plate));
        Assert.That(DefensiveStatsAuthor.Classify("FantasyHeroes.Basic.Armor.DestroyerArmor.vest", V), Is.EqualTo(DefensiveStatsAuthor.Tier.Plate));
        Assert.That(DefensiveStatsAuthor.Classify("FantasyHeroes.Basic.Armor.ChainmailLightArmor [Paint].vest", V), Is.EqualTo(DefensiveStatsAuthor.Tier.Plate), "mail before light");
        Assert.That(DefensiveStatsAuthor.Classify("FantasyHeroes.Basic.Armor.SiegeArcherArmor.vest", V), Is.EqualTo(DefensiveStatsAuthor.Tier.Leather));
        Assert.That(DefensiveStatsAuthor.Classify("FantasyHeroes.Basic.Armor.ThiefArmor.vest", V), Is.EqualTo(DefensiveStatsAuthor.Tier.Leather));
        Assert.That(DefensiveStatsAuthor.Classify("FantasyHeroes.Basic.Armor.NinjaOutfit.vest", V), Is.EqualTo(DefensiveStatsAuthor.Tier.Leather));
        Assert.That(DefensiveStatsAuthor.Classify("FantasyHeroes.Basic.Armor.WarlockArmor.vest", V), Is.EqualTo(DefensiveStatsAuthor.Tier.Cloth));
        Assert.That(DefensiveStatsAuthor.Classify("FantasyHeroes.Basic.Armor.Cleric [Paint].vest", V), Is.EqualTo(DefensiveStatsAuthor.Tier.Cloth));
        Assert.That(DefensiveStatsAuthor.Classify("FantasyHeroes.Basic.Helmet.WarlockHood", H), Is.EqualTo(DefensiveStatsAuthor.Tier.Cloth));
        Assert.That(DefensiveStatsAuthor.Classify("Extensions.AbandonedWorkshop.Helmet.WallKeeperHelm", H), Is.EqualTo(DefensiveStatsAuthor.Tier.Plate));
        Assert.That(DefensiveStatsAuthor.Classify("FantasyHeroes.Basic.Helmet.BunnyEarsA1 [Paint] [FullHair]", H), Is.EqualTo(DefensiveStatsAuthor.Tier.Cosmetic));
        Assert.That(DefensiveStatsAuthor.Classify("FantasyHeroes.Basic.Helmet.Horns3", H), Is.EqualTo(DefensiveStatsAuthor.Tier.Cosmetic));
    }

    [Test]
    public void ATierGrantsWhatItIsAndNothingItIsNot()
    {
        var plate = DefensiveStatsAuthor.Grants(DefensiveStatsAuthor.Slot.Vest, DefensiveStatsAuthor.Tier.Plate);
        var cloth = DefensiveStatsAuthor.Grants(DefensiveStatsAuthor.Slot.Vest, DefensiveStatsAuthor.Tier.Cloth);
        Assert.That(Has(plate, PropertyId.Armor), Is.True, "plate is armour");
        Assert.That(Has(plate, PropertyId.MagicResist), Is.False, "and not magic resist");
        Assert.That(Has(cloth, PropertyId.MagicResist), Is.True, "cloth is magic resist");
        Assert.That(Has(cloth, PropertyId.Armor), Is.False, "and not armour");
        Assert.That(DefensiveStatsAuthor.Grants(DefensiveStatsAuthor.Slot.Helmet, DefensiveStatsAuthor.Tier.Cosmetic), Is.Empty, "a bunny ear is nothing");
    }

    private static bool Has((PropertyId id, string value)[] grants, PropertyId id)
    {
        foreach (var g in grants) if (g.id == id) return true;
        return false;
    }

    [Test]
    public void ArmourIsNotABaseValue()
    {
        // The rule behind the table: not every vest grants the same armour, some grant none, and
        // some head pieces grant nothing at all. If every row reads alike again, this fails.
        var byItem = new Dictionary<string, Dictionary<string, string>>();
        foreach (var line in File.ReadAllLines("Assets/Data/Properties.csv"))
        {
            var cols = line.Split(',');
            if (cols.Length != 3) continue;
            if (!byItem.TryGetValue(cols[0], out var d)) byItem[cols[0]] = d = new Dictionary<string, string>();
            d[cols[1]] = cols[2];
        }
        var armourValues = new HashSet<string>();
        int vests = 0, vestsWithoutArmour = 0;
        foreach (var kv in byItem)
        {
            if (!kv.Key.EndsWith(".vest")) continue;
            vests++;
            if (kv.Value.TryGetValue("Armor", out var a)) armourValues.Add(a); else vestsWithoutArmour++;
        }
        Assert.That(vests, Is.GreaterThan(200));
        Assert.That(armourValues.Count, Is.GreaterThanOrEqualTo(3), "every vest reads the same armour again: " + string.Join(",", armourValues));
        Assert.That(vestsWithoutArmour, Is.GreaterThan(0), "a robe should grant no armour");
        Assert.That(byItem.ContainsKey("FantasyHeroes.Basic.Helmet.BunnyEarsA1 [Paint] [FullHair]"), Is.False, "a cosmetic grants nothing, so it has no rows");
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
        var helm = ItemCollection.Active.Items.Find(i => i.Id == "Extensions.AbandonedWorkshop.Helmet.WallKeeperHelm");
        Assert.That(Of(plate, PropertyId.Armor), Is.EqualTo("24"), "plate — re-import the CSV if this is null");
        Assert.That(Of(plate, PropertyId.MagicResist), Is.Null, "plate resists no magic");
        Assert.That(Of(robe, PropertyId.MagicResist), Is.EqualTo("24"), "a robe");
        Assert.That(Of(robe, PropertyId.Armor), Is.Null, "a robe is no armour");
        Assert.That(Of(shield, PropertyId.Armor), Is.EqualTo("60"), "a wall keeper's shield");
        Assert.That(Of(shield, PropertyId.KnockbackResist), Is.EqualTo("40"), "and its rare line survived the rewrite");
        Assert.That(Of(hood, PropertyId.MagicResist), Is.EqualTo("30"), "a hood");
        Assert.That(Of(helm, PropertyId.Armor), Is.EqualTo("8"), "a helm");
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
