using System.Collections.Generic;
using Assets.HeroEditor.InventorySystem.Scripts;
using Assets.HeroEditor.InventorySystem.Scripts.Data;
using NUnit.Framework;
using UnityEditor;

/// <summary>The rack keeps the newest, hands back the oldest (<see cref="WeaponRack"/>).</summary>
public class WeaponRackTests
{
    [OneTimeSetUp]
    public void LoadCollection()
    {
        ItemCollection.Active = AssetDatabase.LoadAssetAtPath<ItemCollection>("Assets/Data/ItemCollection.asset");
        Assert.That(ItemCollection.Active, Is.Not.Null);
    }

    private static Item Weapon(string id) => new Item(id);

    [Test]
    public void ADisplacedWeaponGoesToTheBackOfTheRack()
    {
        var rack = new List<Item>();
        var sword = Weapon("FantasyHeroes.Basic.MeleeWeapon1H.Bilbo");
        Assert.That(WeaponRack.Push(rack, sword, 2), Is.Null);
        Assert.That(rack, Has.Member(sword));
    }

    [Test]
    public void AFullRackHandsBackTheWeaponThatWaitedLongest()
    {
        var rack = new List<Item>();
        var first = Weapon("FantasyHeroes.Basic.MeleeWeapon1H.Bilbo");
        var second = Weapon("FantasyHeroes.Basic.MeleeWeapon1H.BlacksmithHammer");
        var third = Weapon("FantasyHeroes.Basic.Bow.HunterBow");
        WeaponRack.Push(rack, first, 2);
        WeaponRack.Push(rack, second, 2);
        var evicted = WeaponRack.Push(rack, third, 2);
        Assert.That(evicted, Is.SameAs(first));
        Assert.That(rack, Is.EqualTo(new List<Item> { second, third }));
    }

    [Test]
    public void AWeaponAlreadyOnTheRackIsMovedNotDuplicated()
    {
        var rack = new List<Item>();
        var a = Weapon("FantasyHeroes.Basic.MeleeWeapon1H.Bilbo");
        var b = Weapon("FantasyHeroes.Basic.MeleeWeapon1H.BlacksmithHammer");
        WeaponRack.Push(rack, a, 2);
        WeaponRack.Push(rack, b, 2);
        WeaponRack.Push(rack, a, 2);
        Assert.That(rack, Is.EqualTo(new List<Item> { b, a }));
    }

    [Test]
    public void NoCapacityHandsTheWeaponStraightBack()
    {
        var rack = new List<Item>();
        var a = Weapon("FantasyHeroes.Basic.MeleeWeapon1H.Bilbo");
        Assert.That(WeaponRack.Push(rack, a, 0), Is.SameAs(a));
        Assert.That(rack, Is.Empty);
    }
}
