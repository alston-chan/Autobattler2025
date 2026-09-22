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

    // ---------- the migration ----------

    [Test]
    public void AVestReducesAMedianHitByAsMuchAsItsBlockingDid()
    {
        // Old: blocking 3 on a 20 hit took 3 (15%). New: the armour it became must take about that.
        float armour = Mitigation.ArmorFromBlocking(3f);
        Assert.That(armour, Is.EqualTo(18f));
        float taken = 20f - Mitigation.Reduce(20f, armour);
        Assert.That(taken / 20f, Is.EqualTo(0.15f).Within(0.02f), "the ladder must not quietly get harder or easier");
    }

    [Test]
    public void AShieldIsALargeArmourItemNotAWall()
    {
        // Old: blocking 12 was half of every hit up to 24 — the cap, not the stat. New: capped at 60,
        // which is 37.5% of everything: less than the old half on small hits, more on big ones.
        float armour = Mitigation.ArmorFromBlocking(12f);
        Assert.That(armour, Is.EqualTo(60f));
        Assert.That(Mitigation.Fraction(armour), Is.EqualTo(0.375f).Within(0.001f));
    }

    [Test]
    public void NoItemStillGrantsBlocking()
    {
        foreach (var line in File.ReadAllLines("Assets/Data/Properties.csv"))
            Assert.That(line, Does.Not.Contain(",Blocking,"), "a Blocking row survived the migration: " + line);
    }

    [Test]
    public void TheCollectionCarriesArmourWhereBlockingWas()
    {
        ItemCollection.Active = AssetDatabase.LoadAssetAtPath<ItemCollection>("Assets/Data/ItemCollection.asset");
        var vest = ItemCollection.Active.Items.Find(i => i.Id == "Extensions.AbandonedWorkshop.Armor.WallKeeperArmor.vest");
        var shield = ItemCollection.Active.Items.Find(i => i.Id == "Extensions.AbandonedWorkshop.Shield.WallKeeperShield");
        Assert.That(Of(vest, PropertyId.Armor), Is.EqualTo("18"), "a vest — re-import the CSV if this is null");
        Assert.That(Of(shield, PropertyId.Armor), Is.EqualTo("60"), "a shield");
        Assert.That(Of(shield, PropertyId.Blocking), Is.Null, "and nothing grants Blocking any more");
    }

    private static string Of(ItemParams item, PropertyId id)
    {
        Assert.That(item, Is.Not.Null);
        foreach (var p in item.Properties) if (p.Id == id) return p.Value;
        return null;
    }

    // ---------- magic resist lives on the head ----------

    [Test]
    public void EveryHelmetResistsMagicAndNothingElseDoes()
    {
        // The rule the player can hold: body armour for blades, the head for spells. A hood or a
        // hat is 30, a helm is 12, and no vest or shield grants any — one stat, one slot.
        var items = new Dictionary<string, string>();
        foreach (var line in File.ReadAllLines("Assets/Data/Items.csv"))
        {
            var cols = line.Split(',');
            if (cols.Length > 2 && cols[0] == "TRUE") items[cols[1]] = cols[2];
        }

        var resist = new Dictionary<string, int>();
        foreach (var line in File.ReadAllLines("Assets/Data/Properties.csv"))
        {
            var cols = line.Split(',');
            if (cols.Length == 3 && cols[1] == "MagicResist") resist[cols[0]] = int.Parse(cols[2]);
        }

        int helmets = 0;
        foreach (var kv in items)
        {
            if (kv.Value == "Helmet")
            {
                helmets++;
                Assert.That(resist.ContainsKey(kv.Key), Is.True, kv.Key + " is a helmet that resists nothing");
                Assert.That(resist[kv.Key], Is.EqualTo(12).Or.EqualTo(30), kv.Key + " has an off-rule value");
            }
            else
            {
                Assert.That(resist.ContainsKey(kv.Key), Is.False, kv.Key + " is not a helmet and grants magic resist");
            }
        }
        Assert.That(helmets, Is.GreaterThan(100), "the helmet set is missing");
    }

    [Test]
    public void AHoodResistsMoreThanAHelmAndTheCollectionCarriesBoth()
    {
        ItemCollection.Active = AssetDatabase.LoadAssetAtPath<ItemCollection>("Assets/Data/ItemCollection.asset");
        var hood = ItemCollection.Active.Items.Find(i => i.Id == "FantasyHeroes.Basic.Helmet.WarlockHood");
        var helm = ItemCollection.Active.Items.Find(i => i.Id == "Extensions.AbandonedWorkshop.Helmet.WallKeeperHelm");
        Assert.That(Of(hood, PropertyId.MagicResist), Is.EqualTo("30"), "a warlock's hood — re-import the CSV if this is null");
        Assert.That(Of(helm, PropertyId.MagicResist), Is.EqualTo("12"), "a wall keeper's helm");
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
