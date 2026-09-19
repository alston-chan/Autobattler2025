using NUnit.Framework;
using UnityEditor;

/// <summary>
/// What a card is allowed to call an ability. Weapon attacks are not one; anything that costs mana
/// is; and a spell nobody named still has something to be called. (The enemy ability roll this file
/// used to cover is gone: an enemy's ability is the verb its weapon teaches, like a hero's.)
/// </summary>
public class EnemyAbilityTests
{
    private const string SpellDir = "Assets/Data/Spells/";

    private static Spell Load(string file) =>
        AssetDatabase.LoadAssetAtPath<Spell>(SpellDir + file + ".asset");

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
        Assert.That(Load("Cannonball").IsAbility, Is.True);
        Assert.That(Load("Singularity").IsAbility, Is.True);
        Assert.That(Load("NinjaBackstab").IsAbility, Is.True);
    }

    [Test]
    public void AnUnnamedSpellStillHasSomethingToCallIt()
    {
        var named = Load("Cannonball");
        Assert.That(named.DisplayName, Is.EqualTo("Cannonball"));

        var unnamed = Load("DefaultMeleeAttack");
        Assert.That(unnamed.spellName, Is.Empty, "test assumes this one is unnamed");
        Assert.That(unnamed.DisplayName, Is.EqualTo(unnamed.name));
    }
}
