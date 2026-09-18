using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Attuning a weapon buys something: a verb's force, radius and reach grow with its tier, its
/// damage steps with it, and the card says so in the numbers. Plus the gaps this closed: every
/// weapon class in the collection teaches a verb, and a resonating enemy carries no dead ability.
/// </summary>
public class VerbTierTests
{
    [Test]
    public void ForceRadiusAndReachGrowAQuarterPerTier()
    {
        var spell = ScriptableObject.CreateInstance<CompositeSpell>();
        Assert.That(spell.ScaleAt(1), Is.EqualTo(1f));
        Assert.That(spell.ScaleAt(2), Is.EqualTo(1.25f).Within(0.0001f));
        Assert.That(spell.ScaleAt(3), Is.EqualTo(1.5f).Within(0.0001f));
        Assert.That(spell.ScaleAt(0), Is.EqualTo(1f), "an unattuned worn verb is tier I");
    }

    [Test]
    public void TheSentenceSaysTheTiersNumbers()
    {
        var spell = ScriptableObject.CreateInstance<CompositeSpell>();
        spell.selector.who = Selector.Who.AllEnemiesInRadius; spell.selector.radius = 6f;
        spell.effects.Add(new KnockbackEffect { force = 8f, pull = true, scope = EffectScope.EveryTarget });
        spell.effects.Add(new DealDamageEffect { damage = new ScaledValue(0f, ofWeaponDamage: 0.4f) });

        Assert.That(spell.ReadsAt(1), Does.Contain("within 6").And.Contain("force 8").And.Not.Contain("×1"), "tier I has no multiplier to show");
        Assert.That(spell.ReadsAt(3), Does.Contain("within 9").And.Contain("force 12").And.Contain("0.4× weapon ×1.5"));
    }

    [Test]
    public void ARadiusEffectAndACloudReachFurtherAtTierThree()
    {
        var nova = new RadiusDamageEffect { radius = 3f, knockback = 10f };
        Assert.That(nova.Describe(3, 1.5f), Does.Contain("within 4.5").And.Contain("force 15"));
        var cloud = new ApplyStatusInRadiusEffect { radius = 2.5f };
        Assert.That(cloud.Describe(2, 1.25f), Does.Contain("within 3.1"));
        var charge = new DashEffect { force = 11f };
        Assert.That(charge.Describe(2, 1.25f), Does.Contain("force 14"));
    }

    [Test]
    public void ADescriptionForAHolderWithoutResonanceIsTheBaseOne()
    {
        var spell = ScriptableObject.CreateInstance<CompositeSpell>();
        spell.description = "Throw them.";
        var go = new GameObject("unit");
        try
        {
            var unit = go.AddComponent<Entity>();
            Assert.That(spell.FullDescriptionFor(unit), Is.EqualTo(spell.FullDescription));
            Assert.That(spell.FullDescriptionFor(null), Is.EqualTo(spell.FullDescription));
        }
        finally { Object.DestroyImmediate(go); }
    }

    [Test]
    public void EveryWeaponClassInTheCollectionTeachesAVerb()
    {
        // A hero holding a weapon of a class with no default has no ability at all, and nothing on
        // screen says why. Guns and crossbows were that hero until the Firearm default.
        var collection = AssetDatabase.LoadAssetAtPath<Assets.HeroEditor.InventorySystem.Scripts.ItemCollection>("Assets/Data/ItemCollection.asset");
        var database = AssetDatabase.LoadAssetAtPath<ResonanceDatabase>("Assets/Resources/ResonanceDatabase.asset");
        var seen = new HashSet<Assets.HeroEditor.InventorySystem.Scripts.Enums.ItemClass>();
        foreach (var item in collection.Items)
        {
            if (item.Type != Assets.HeroEditor.InventorySystem.Scripts.Enums.ItemType.Weapon || !seen.Add(item.Class)) continue;
            bool taught = database.classDefaults.Exists(d => d != null && d.itemClass == item.Class && d.engraving != null);
            Assert.That(taught, Is.True, item.Class + " weapons teach no verb (first such item: " + item.Id + ")");
        }
        Assert.That(seen.Count, Is.GreaterThanOrEqualTo(7), "the collection lost weapon classes");
    }
}
