using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// The promise that every unit walks into a fight with something to do, and that a card can say
/// what it is.
///
/// Both halves used to fail silently. An enemy that rolled no ability was indistinguishable from one
/// designed to be plain, and an ability the unit's weapon could not satisfy sat in the list being
/// skipped every pass by CombatAI while the inspector listed it as if it would fire. Neither showed
/// up as an error anywhere, so both are pinned here instead.
/// </summary>
public class EnemyAbilityTests
{
    private const string PoolPath = "Assets/Data/Run/StandardEnemies.asset";
    private const string SpellDir = "Assets/Data/Spells/";

    private EnemyLoadout _standard;

    [OneTimeSetUp]
    public void LoadPool()
    {
        _standard = AssetDatabase.LoadAssetAtPath<EnemyLoadout>(PoolPath);
        Assert.That(_standard, Is.Not.Null, PoolPath + " is missing — the enemies have no rules.");
    }

    private static Spell Load(string file) =>
        AssetDatabase.LoadAssetAtPath<Spell>(SpellDir + file + ".asset");

    // ---------- the pool the game actually ships ----------

    // Rolled many times rather than once: the roll is random, and a pool that only *usually* arms a
    // unit is the exact bug this is here to catch.
    private const int Rolls = 60;

    [Test]
    public void EveryMeleeEnemyGetsAnAbility()
    {
        for (int i = 0; i < Rolls; i++)
            Assert.That(_standard.RollAbility(ranged: false), Is.Not.Null,
                        "a melee enemy rolled no ability on attempt " + i);
    }

    [Test]
    public void EveryRangedEnemyGetsAnAbility()
    {
        for (int i = 0; i < Rolls; i++)
            Assert.That(_standard.RollAbility(ranged: true), Is.Not.Null,
                        "a ranged enemy rolled no ability on attempt " + i);
    }

    [Test]
    public void EnemyAbilitiesSuitTheWeaponTheirCarrierWillHold()
    {
        // A melee unit is handed a melee weapon and a ranged one a bow, so an ability demanding the
        // other is dead on arrival — it would be listed, charged toward, and never cast.
        for (int i = 0; i < Rolls; i++)
        {
            var melee = _standard.RollAbility(ranged: false);
            Assert.That(melee, Is.Not.Null, "a melee enemy rolled no ability at all");
            Assert.That(melee.weaponRequirement, Is.Not.EqualTo(WeaponClass.Bow),
                        melee.name + " needs a bow but was given to a melee enemy");

            var ranged = _standard.RollAbility(ranged: true);
            Assert.That(ranged, Is.Not.Null, "a ranged enemy rolled no ability at all");
            Assert.That(ranged.weaponRequirement, Is.Not.EqualTo(WeaponClass.Melee),
                        ranged.name + " needs a melee weapon but was given to an archer");
        }
    }

    // ---------- the dials still work ----------

    [Test]
    public void ChanceOfZeroArmsNobody()
    {
        var pool = ScriptableObject.CreateInstance<EnemyLoadout>();
        pool.abilityChance = 0f;
        pool.abilities = new List<Spell> { Load("ShockwaveSpell") };

        for (int i = 0; i < Rolls; i++)
            Assert.That(pool.RollAbility(ranged: false), Is.Null);
    }

    [Test]
    public void APoolWithNothingTheUnitCanUseSaysSo()
    {
        // The failure that prompted the warning: a pool full of bow abilities hands every melee unit
        // a basic attack and nothing else, and says nothing about it.
        var pool = ScriptableObject.CreateInstance<EnemyLoadout>();
        pool.name = "BowOnlyPool";
        pool.abilityChance = 1f;
        pool.abilities = new List<Spell> { Load("MultiShot") };

        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("BowOnlyPool"));
        Assert.That(pool.RollAbility(ranged: false), Is.Null);
    }

    // ---------- what a card is allowed to call an ability ----------

    [Test]
    public void WeaponAttacksAreNotAbilities()
    {
        // The inspector leaves these out. If a basic attack ever started counting as an ability,
        // every card would lead with "Ability  DefaultMeleeAttack" and bury the line that matters.
        Assert.That(Load("DefaultMeleeAttack").IsAbility, Is.False);
        Assert.That(Load("DefaultBowAttack").IsAbility, Is.False);
        Assert.That(Load("DefaultWandAttack").IsAbility, Is.False);
    }

    [Test]
    public void CostAbilitiesAreAbilities()
    {
        Assert.That(Load("ShockwaveSpell").IsAbility, Is.True);
        Assert.That(Load("DoubleStrike").IsAbility, Is.True);
        Assert.That(Load("Backstab").IsAbility, Is.True);
    }

    [Test]
    public void AnUnnamedSpellStillHasSomethingToCallIt()
    {
        var named = Load("ShockwaveSpell");
        Assert.That(named.DisplayName, Is.EqualTo("Shockwave"));

        var unnamed = Load("DefaultMeleeAttack");
        Assert.That(unnamed.spellName, Is.Empty, "test assumes this one is unnamed");
        Assert.That(unnamed.DisplayName, Is.EqualTo(unnamed.name));
    }
}
