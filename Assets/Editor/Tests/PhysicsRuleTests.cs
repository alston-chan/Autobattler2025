using System.IO;
using Assets.HeroEditor.InventorySystem.Scripts;
using Assets.HeroEditor.InventorySystem.Scripts.Data;
using Assets.HeroEditor.InventorySystem.Scripts.Enums;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The physics damage rule as a rule: statable, printed on the verbs from the live numbers, and
/// changed only by a throw's own multiplier or a unit's resistance line.
///
/// The description tests are the ones that matter. Three spells had hand-written text quoting a
/// force and a formula that were no longer true — the text is generated now, and this is what
/// keeps it honest.
/// </summary>
public class PhysicsRuleTests
{
    [OneTimeSetUp]
    public void LoadItems()
    {
        ItemCollection.Active = AssetDatabase.LoadAssetAtPath<ItemCollection>("Assets/Data/ItemCollection.asset");
        Assert.That(ItemCollection.Active, Is.Not.Null);
    }

    // ---------- resistance ----------

    [Test]
    public void ResistanceShrugsOffThatFractionOfAThrowAndNoMore()
    {
        Assert.That(Knockback.Resisted(10f, 0f), Is.EqualTo(10f));
        Assert.That(Knockback.Resisted(10f, 0.4f), Is.EqualTo(6f).Within(1e-5f));
        Assert.That(Knockback.Resisted(10f, 1f), Is.EqualTo(0f), "immovable");
        Assert.That(Knockback.Resisted(10f, 3f), Is.EqualTo(0f), "and never negative — a throw does not pull");
    }

    // ---------- the words on the verb ----------

    [Test]
    public void AThrowHardEnoughToSlamSaysWhatASlamCosts()
    {
        var s = CombatPhysics.Active;
        string words = CombatPhysics.DescribeThrow(s.impactSpeed + 5f);
        Assert.That(words, Does.Contain("slam"));
        Assert.That(words, Does.Contain((s.bodySlamPercent * 100f).ToString("0") + "%"), "the body number");
        Assert.That(words, Does.Contain((s.wallSlamPercent * 100f).ToString("0") + "%"), "the wall number");
    }

    [Test]
    public void AThrowTooSlowToSlamSaysSo()
    {
        var s = CombatPhysics.Active;
        Assert.That(CombatPhysics.DescribeThrow(s.impactSpeed), Does.Contain("shove"));
        Assert.That(CombatPhysics.DescribeThrow(s.impactSpeed), Does.Not.Contain("%"), "no numbers for nothing");
    }

    [Test]
    public void AMultipliedThrowPrintsTheMultipliedNumbers()
    {
        var s = CombatPhysics.Active;
        string words = CombatPhysics.DescribeThrow(s.impactSpeed + 5f, 2f);
        float body = Mathf.Min(s.bodySlamPercent * 2f, s.maxImpactPercent) * 100f;
        Assert.That(words, Does.Contain("x2"));
        Assert.That(words, Does.Contain(body.ToString("0") + "%"));
    }

    [Test]
    public void TheCannonballIsTheThrowThatSlamsHarder()
    {
        var cannonball = AssetDatabase.LoadAssetAtPath<CompositeSpell>("Assets/Data/Spells/Cannonball.asset");
        Assert.That(cannonball, Is.Not.Null);
        KnockbackEffect throwEffect = null;
        foreach (var e in cannonball.effects) if (e is KnockbackEffect k) throwEffect = k;
        Assert.That(throwEffect, Is.Not.Null, "Cannonball no longer throws");
        Assert.That(throwEffect.impactMultiplier, Is.EqualTo(2f), "its whole identity is the doubled slam");
        Assert.That(throwEffect.Describe(), Does.Contain("x2"), "and the verb says so");
    }

    // ---------- the line is rare ----------

    [Test]
    public void KnockbackResistanceIsARareLine()
    {
        int rows = 0;
        foreach (var line in File.ReadAllLines("Assets/Data/Properties.csv"))
            if (line.Contains(",Resistance,")) rows++;
        Assert.That(rows, Is.GreaterThan(0), "nothing grants it");
        Assert.That(rows, Is.LessThanOrEqualTo(8), rows + " items grant it — it is meant to be rare");
    }

    [Test]
    public void TheShieldThatGrantsItCarriesItIntoTheCollection()
    {
        // The collection is generated from the CSV by an importer that REPLACES the list; a row
        // added to the CSV and never imported is a line that does not exist.
        var shield = ItemCollection.Active.Items.Find(i => i.Id == "Extensions.AbandonedWorkshop.Shield.WallKeeperShield");
        Assert.That(shield, Is.Not.Null);
        Property line = null;
        foreach (var p in shield.Properties) if (p.Id == PropertyId.Resistance) line = p;
        Assert.That(line, Is.Not.Null, "the Wall Keeper's shield lost its resistance — re-import the CSV");
        Assert.That(line.Value, Is.EqualTo("40"));
    }

    // ---------- a slam is a slam on screen ----------

    [Test]
    public void ASlamKnowsItIsOne()
    {
        var hit = new DamageInfo(10f, 90f, null, false);
        var slam = new DamageInfo(10f, 90f, null, false, 0f, DamageKind.Slam);
        Assert.That(hit.kind, Is.EqualTo(DamageKind.Hit), "the default is the ordinary number");
        Assert.That(slam.kind, Is.EqualTo(DamageKind.Slam));
    }
}
