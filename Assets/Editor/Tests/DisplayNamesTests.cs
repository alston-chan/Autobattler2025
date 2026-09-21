using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// The names the player is shown. Everything here is a real name from the project, and every
/// expectation is what a card, a scoreboard or a reward card should print instead.
/// </summary>
public class DisplayNamesTests
{
    private readonly List<Object> _made = new List<Object>();

    [TearDown]
    public void TearDown()
    {
        foreach (var o in _made) if (o != null) Object.DestroyImmediate(o);
        _made.Clear();
    }

    private Entity Unit(string objectName)
    {
        var go = new GameObject(objectName); _made.Add(go);
        return go.AddComponent<Entity>();
    }

    [Test]
    public void AHeroIsCalledWhatItCarriesNotWhatTheSceneCallsIt()
    {
        Assert.That(DisplayNames.Unit(Unit("Hero_Daggers")), Is.EqualTo("Daggers"));
        Assert.That(DisplayNames.Unit(Unit("Hero_Bow")), Is.EqualTo("Bow"));
        Assert.That(DisplayNames.Unit(Unit("Hero_Greatsword_2H")), Is.EqualTo("Greatsword 2H"));

        // "Melee" says how it fights, which the silhouette already says; the shield is the hero.
        Assert.That(DisplayNames.Unit(Unit("Hero_Melee_KnightShield")), Is.EqualTo("Knight Shield"));
    }

    [Test]
    public void ASpawnedEnemyDoesNotCarryUnitysDebris()
    {
        Assert.That(DisplayNames.Unit(Unit("HumanPrefab(Clone)")), Is.EqualTo("Human"));
        Assert.That(DisplayNames.Unit(Unit("CaveRat(Clone)")), Is.EqualTo("Cave Rat"));
    }

    [Test]
    public void AGivenNameWinsOverEverything()
    {
        // What EncounterSpawner does with a kit: the fighter is a Rain Archer, not a Human.
        var enemy = Unit("HumanPrefab(Clone)");
        enemy.displayName = "Rain Archer";
        Assert.That(DisplayNames.Unit(enemy), Is.EqualTo("Rain Archer"));
    }

    [Test]
    public void ItemNamesGetTheirSpacesBack()
    {
        Assert.That(DisplayNames.Item("WarHammer"), Is.EqualTo("War Hammer"));
        Assert.That(DisplayNames.Item("AdvancedKnightShield"), Is.EqualTo("Advanced Knight Shield"));
        Assert.That(DisplayNames.Item("SpearmanHelm1"), Is.EqualTo("Spearman Helm 1"));
    }

    [Test]
    public void HeroEditorsPaintTagIsNotPartOfTheName()
    {
        Assert.That(DisplayNames.Item("ArielDress [Paint] (Lower)"), Is.EqualTo("Ariel Dress (Lower)"));
        Assert.That(DisplayNames.Item("AssassinDagger [Paint]"), Is.EqualTo("Assassin Dagger"));
    }

    [Test]
    public void AnAcronymStaysOneWord()
    {
        // "2H" and the like are how the project writes two-handed; splitting them reads as a typo.
        Assert.That(DisplayNames.Item("Sword2H"), Is.EqualTo("Sword 2H"));
        Assert.That(DisplayNames.Item("NPCGuard"), Is.EqualTo("NPC Guard"));
    }

    [Test]
    public void NothingBreaksOnNothing()
    {
        Assert.That(DisplayNames.Item(null), Is.EqualTo(""));
        Assert.That(DisplayNames.Item(""), Is.EqualTo(""));
        Assert.That(DisplayNames.Unit(null), Is.EqualTo(""));

        // A name that is nothing but noise words keeps them rather than vanishing.
        Assert.That(DisplayNames.Unit(Unit("Hero")), Is.EqualTo("Hero"));
    }
}
