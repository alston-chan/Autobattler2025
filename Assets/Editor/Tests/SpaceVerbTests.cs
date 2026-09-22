using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The four verbs that use space (Docs/Spells.md): what their sentences say at each tier, that the
/// tier moves the number that matters for each, and that the items teaching them are listed where a
/// player can meet them.
/// </summary>
public class SpaceVerbTests
{
    [Test]
    public void ArrowRainReachesFurtherAtTierThree()
    {
        var e = new StrikeAtPointEffect { delay = 1.2f, radius = 2.5f, knockback = 6f };
        Assert.That(e.Describe(1, 1f), Does.Contain("after 1.2 s").And.Contain("a ring 5 across").And.Contain("force 6"));
        Assert.That(e.Describe(3, 1.5f), Does.Contain("a ring 7.5 across").And.Contain("force 9"));
    }

    [Test]
    public void WhirlGainsABladeATier()
    {
        var e = new OrbitEffect { blades = 3, bladesPerTier = 1, radius = 1.6f, force = 5f };
        Assert.That(e.Describe(1, 1f), Does.StartWith("3 blades").And.Contain("at 1.6"));
        Assert.That(e.Describe(3, 1.5f), Does.StartWith("5 blades").And.Contain("at 2.4").And.Contain("force 8"));
    }

    [Test]
    public void WhirlsBladesPassThroughWhereTheEnemyStands()
    {
        // A melee enemy stands between touching (1.1, two bodies) and just inside reach (1.5).
        // The blades were once drawn on the rig at half scale, a 0.8 circle that passed between the
        // caster and everyone it faced: measured, two casts in a fight, zero hits. Judged at the
        // feet, in world units, the ring must cross that whole band.
        var whirl = AssetDatabase.LoadAssetAtPath<CompositeSpell>("Assets/Data/Spells/Whirl.asset");
        OrbitEffect orbit = null;
        foreach (var e in whirl.effects) if (e is OrbitEffect o) orbit = o;
        Assert.That(orbit, Is.Not.Null);

        foreach (float standing in new[] { 1.1f, 1.35f, 1.5f })
        {
            float nearest = float.MaxValue;
            for (float angle = 0f; angle < 360f; angle += 2f)
                nearest = Mathf.Min(nearest, Vector3.Distance(OrbitRunner.GroundOffset(angle, orbit.radius), new Vector3(standing, 0f, 0f)));
            Assert.That(nearest, Is.LessThanOrEqualTo(orbit.hitRadius), "an enemy " + standing + " away is never touched");
        }
    }

    [Test]
    public void AnAreaOnTheFloorHitsWhatItDraws()
    {
        // The ring is drawn as an ellipse, FloorDepth as tall as it is wide, and was judged as a
        // circle: Arrow Rain's 2.5 reached the rows above and below its target, 1.5 away, standing
        // visibly outside the ring. The ring is the rule.
        var centre = new Vector3(0f, -2f, 0f);
        Assert.That(ShapeSprites.OnFloorWithin(centre, 2.5f, centre + new Vector3(2.4f, 0f, 0f)), Is.True, "along the floor, the full radius");
        Assert.That(ShapeSprites.OnFloorWithin(centre, 2.5f, centre + new Vector3(0f, 1.3f, 0f)), Is.True, "up the floor, inside the drawn edge at 1.375");
        Assert.That(ShapeSprites.OnFloorWithin(centre, 2.5f, centre + new Vector3(0f, 1.5f, 0f)), Is.False, "the next row up is outside the ring");
        Assert.That(ShapeSprites.OnFloorWithin(centre, 2.5f, centre + new Vector3(1.5f, -1.2f, 0f)), Is.False, "nor a corner the circle took");
    }

    [Test]
    public void TarPoolSaysItsVictimsWalkOut()
    {
        var e = new ZoneEffect { radius = 2f, seconds = 5f };
        Assert.That(e.Describe(1, 1f), Does.Contain("4 across").And.Contain("5 s").And.EndWith("they walk out"));
        Assert.That(e.Describe(2, 1.25f), Does.Contain("5 across"));
    }

    [Test]
    public void RicochetBouncesOnceMoreATier()
    {
        var e = new BounceProjectileEffect { bounces = 2, bouncesPerTier = 1, bounceRange = 4f, force = 4f };
        Assert.That(e.Describe(1, 1f), Does.Contain("within 4, 2 times"));
        Assert.That(e.Describe(3, 1.5f), Does.Contain("within 6, 4 times").And.Contain("force 6"));
    }

    [Test]
    public void NobodyIsInAPoolThatDoesNotExist()
    {
        var go = new GameObject("unit");
        try
        {
            var unit = go.AddComponent<Entity>();
            Assert.That(Zone.HostileAt(unit), Is.Null);
            Assert.That(Zone.HostileAt(null), Is.Null);
        }
        finally { Object.DestroyImmediate(go); }
    }

    private static readonly (string spell, string item)[] Verbs =
    {
        ("ArrowRain", "FantasyHeroes.Basic.Bow.RangerBow"),
        ("Whirl", "FantasyHeroes.Basic.MeleeWeapon1H.WarHammer"),
        ("TarPool", "FantasyHeroes.Basic.MeleeWeapon1H.FireWizardWand"),
        ("Ricochet", "FantasyHeroes.Basic.MeleeWeapon1H.DeserterDagger [Paint]"),
    };

    [Test]
    public void EachSpaceVerbIsTaughtByANamedItemAPlayerCanFind()
    {
        var collection = AssetDatabase.LoadAssetAtPath<Assets.HeroEditor.InventorySystem.Scripts.ItemCollection>("Assets/Data/ItemCollection.asset");
        var database = AssetDatabase.LoadAssetAtPath<ResonanceDatabase>("Assets/Resources/ResonanceDatabase.asset");
        var standard = AssetDatabase.LoadAssetAtPath<RewardPool>("Assets/Data/Run/StandardRewards.asset");
        foreach (var (spellName, itemId) in Verbs)
        {
            var spell = AssetDatabase.LoadAssetAtPath<CompositeSpell>("Assets/Data/Spells/" + spellName + ".asset");
            Assert.That(spell, Is.Not.Null, spellName + " is missing");
            Assert.That(spell.manaCost, Is.GreaterThan(0f), spellName + " costs nothing");
            Assert.That(spell.cooldown, Is.EqualTo(0f), spellName + " has a cooldown; mana is the gate");
            Assert.That(collection.Items.Exists(i => i.Id == itemId), Is.True, itemId + " is not in the collection");
            var entry = database.entries.Find(e => e.itemId == itemId);
            Assert.That(entry, Is.Not.Null, itemId + " teaches nothing");
            Assert.That(entry.engraving, Is.InstanceOf<GrantSpellEngraving>());
            Assert.That(((GrantSpellEngraving)entry.engraving).spell, Is.SameAs(spell), itemId + " teaches the wrong verb");
            Assert.That(standard.itemIds, Has.Member(itemId), itemId + " cannot drop");
        }
    }
}
