using Assets.HeroEditor.InventorySystem.Scripts.Data;
using Assets.HeroEditor.InventorySystem.Scripts.Enums;
using NUnit.Framework;
using UnityEditor;

/// <summary>
/// Rarity on an item copy (Docs/ShopLoop.md): C is the plain item, the grade survives the moves the
/// inventory makes, the odds climb through a run, and the database asks for one quest goal.
/// </summary>
public class RarityTests
{
    private const string Sword = "FantasyHeroes.Basic.MeleeWeapon1H.WarHammer";

    [Test]
    public void CIsThePlainItemSoEverythingAuthoredBeforeRarityIsAC()
    {
        Assert.That(Rarity.Of(new Item(Sword)), Is.EqualTo(Rarity.C));
        Assert.That(Rarity.Make(Sword, Rarity.C).Modifier, Is.Null, "a C carries no modifier, so kits and loadouts need no change");
        Assert.That(Rarity.Of(null), Is.EqualTo(Rarity.C));
    }

    [Test]
    public void AGradeSurvivesTheCopyTheInventoryMakesOnEveryMove()
    {
        // ItemWorkspace.MoveItemSilent adds new Item(item.Id, item.Modifier, amount) and drops the
        // original, so a grade kept anywhere but on the modifier would be lost on the first move.
        for (int grade = Rarity.C; grade <= Rarity.S; grade++)
        {
            var copy = Rarity.Make(Sword, grade);
            var moved = new Item(copy.Id, copy.Modifier, 1);
            Assert.That(Rarity.Of(moved), Is.EqualTo(grade), Rarity.Letter(grade));
        }
    }

    [Test]
    public void TwoGradesOfOneItemAreTwoThingsToTheInventory()
    {
        // The vendor stacks and compares by Hash; a B and an S of the same sword must not stack, and
        // their quests progress apart.
        Assert.That(Rarity.Make(Sword, Rarity.B).Hash, Is.Not.EqualTo(Rarity.Make(Sword, Rarity.S).Hash));
    }

    [Test]
    public void AHollowItemIsNoLongerAnyGrade()
    {
        var spent = Rarity.Make(Sword, Rarity.A);
        HollowItems.Hollow(spent);
        Assert.That(Rarity.Of(spent), Is.EqualTo(Rarity.C));
        Assert.That(spent.Modifier.Id, Is.EqualTo(ItemModifier.Hollow));
    }

    [Test]
    public void TheOddsSumToOneAndClimbThroughTheRun()
    {
        foreach (float at in new[] { 0f, 0.5f, 1f })
        {
            float sum = 0f; foreach (var p in Rarity.OddsAt(at)) sum += p;
            Assert.That(sum, Is.EqualTo(1f).Within(0.0001f), "at " + at);
        }
        Assert.That(Rarity.OddsAt(0f)[3], Is.EqualTo(0f), "no S on the first fight");
        Assert.That(Rarity.OddsAt(1f)[3], Is.GreaterThan(0f), "and some by the end");
        Assert.That(Rarity.OddsAt(1f)[0], Is.LessThan(Rarity.OddsAt(0f)[0]), "fewer C as the run goes on");
    }

    [Test]
    public void ARollLandsInTheBandItFallsIn()
    {
        // First fight: C below 0.70, B below 0.95, A to the end, never S.
        Assert.That(Rarity.Roll(0f, 0.10f), Is.EqualTo(Rarity.C));
        Assert.That(Rarity.Roll(0f, 0.80f), Is.EqualTo(Rarity.B));
        Assert.That(Rarity.Roll(0f, 0.97f), Is.EqualTo(Rarity.A));
        Assert.That(Rarity.Roll(1f, 0.99f), Is.EqualTo(Rarity.S));
    }

    [Test]
    public void EveryEntryHasOneQuestGoal()
    {
        // The three tier costs are gone; what is left is the goal, and a zero would complete on the
        // first hit and engrave before the item had done anything.
        var database = AssetDatabase.LoadAssetAtPath<ResonanceDatabase>("Assets/Resources/ResonanceDatabase.asset");
        foreach (var entry in database.entries)
            Assert.That(entry.questGoal, Is.GreaterThan(0), entry.itemId + " has no quest");
        foreach (var d in database.classDefaults)
            Assert.That(d.questGoal, Is.GreaterThan(0), d.itemClass + " default has no quest");
        Assert.That(database.entries[0].IsComplete(database.entries[0].questGoal), Is.True);
        Assert.That(database.entries[0].IsComplete(database.entries[0].questGoal - 1), Is.False);
    }
}
