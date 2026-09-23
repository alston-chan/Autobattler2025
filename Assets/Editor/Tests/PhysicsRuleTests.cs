using System.Collections.Generic;
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
        Assert.That(words, Does.Contain("physical"), "a slam is resisted by armour, and the verb says so");
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
            if (line.Contains(",KnockbackResist,")) rows++;
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
        foreach (var p in shield.Properties) if (p.Id == PropertyId.KnockbackResist) line = p;
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

    [Test]
    public void OnlyACritShovesOnABasicAttack()
    {
        // An ordinary hit that shoved pushed a melee pair just out of reach after nearly every swing
        // (a unit stops 0.15 inside its reach), and both stepped back in: measured as the largest
        // source of units reversing direction. So a basic attack has no ordinary-hit force at all —
        // not a zero that someone can raise in an asset, but no field to raise.
        var basics = new[] { typeof(MeleeAttackSpell), typeof(HeavyAttackSpell), typeof(DualWieldAttackSpell),
                             typeof(BowAttackSpell), typeof(WandAttackSpell), typeof(FirearmAttackSpell),
                             typeof(Assets.HeroEditor.Common.Scripts.ExampleScripts.Projectile) };
        foreach (var type in basics)
        {
            Assert.That(type.GetField("knockbackForce"), Is.Null, type.Name + " still shoves on every hit");
            Assert.That(type.GetField("normalKnockbackForce"), Is.Null, type.Name + " still shoves on an ordinary hit");
            Assert.That(type.GetField("critKnockbackForce"), Is.Not.Null, type.Name + " has no crit shove");
        }

        // And the crits kept what they had: a mace still throws on a crit.
        var blunt = AssetDatabase.LoadAssetAtPath<MeleeAttackSpell>("Assets/Data/Spells/BluntAttack.asset");
        Assert.That(blunt.critKnockbackForce, Is.EqualTo(3.2f).Within(0.001f));
        var bow = AssetDatabase.LoadAssetAtPath<BowAttackSpell>("Assets/Data/Spells/DefaultBowAttack.asset");
        Assert.That(bow.critKnockbackForce, Is.EqualTo(0.8f).Within(0.001f), "the bow's old every-hit number is its crit number");
    }

    [Test]
    public void OnlyTheVerbsThatMoveBodiesShove()
    {
        // A verb shoves when moving bodies is what it is for: a throw, a pull, a push, a charge.
        // Whirl, Arrow Rain and Ricochet shoved too, as a side effect of doing damage, and with
        // ordinary hits no longer shoving they were most of the shoving left: Whirl alone was 38
        // knockbacks in 42 seconds of a four-a-side, each one a small stun and a walk back in.
        var movers = new HashSet<string> { "Cannonball", "ChainWhip", "Singularity", "RepulsionNova", "BullRush" };
        var shoving = new List<string>();
        foreach (var guid in AssetDatabase.FindAssets("t:CompositeSpell", new[] { "Assets/Data/Spells" }))
        {
            var spell = AssetDatabase.LoadAssetAtPath<CompositeSpell>(AssetDatabase.GUIDToAssetPath(guid));
            foreach (var effect in spell.effects)
            {
                if (effect == null) continue;
                foreach (var name in new[] { "force", "knockback" })
                {
                    var field = effect.GetType().GetField(name);
                    if (field != null && field.FieldType == typeof(float) && (float)field.GetValue(effect) > 0f) { shoving.Add(spell.name); break; }
                }
            }
        }
        foreach (var name in shoving)
            Assert.That(movers, Has.Member(name), name + " shoves, but moving bodies is not what it is for");
        foreach (var name in movers)
            Assert.That(shoving, Has.Member(name), name + " is a displacement verb that no longer moves anyone");
    }
}
