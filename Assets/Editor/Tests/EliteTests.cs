using NUnit.Framework;
using UnityEditor;

/// <summary>
/// The Act 1 elites are rules as much as numbers (Docs/Enemies.md). Their fights are tuned in the
/// OgreVs* scenarios; what is checked here is the data a tuning pass could quietly break.
/// </summary>
public class EliteTests
{
    private const string Ogre = "Assets/Data/Run/Archetypes/Elite_OgreWarchief.asset";

    [Test]
    public void TheOgreWarchiefCannotBeShovedAndRages()
    {
        var encounter = AssetDatabase.LoadAssetAtPath<EncounterData>(Ogre);
        Assert.That(encounter, Is.Not.Null);
        Assert.That(encounter.problems, Has.Member(EnemyProblem.Bloodrager), "the map would not say what it asks");
        Assert.That(encounter.spawns.Count, Is.EqualTo(1), "a lone Ogre: area damage has nothing else to hit");

        var spawn = encounter.spawns[0];
        Assert.That(spawn.prefab.GetComponent<Entity>(), Is.Not.Null);
        Assert.That(spawn.unitData.knockbackResist, Is.EqualTo(1f), "the company's displacement verbs would move it");
        Assert.That(spawn.unitData.traits.Exists(t => t is BloodrageEngraving), Is.True, "without Bloodrage it is only a wall");
    }

    [Test]
    public void TheOgreReachesPastItsOwnBodyAndIsReachedBySwords()
    {
        // Reach is measured centre to centre, and bodies stop at the sum of their radii. A big body
        // with the rats' 1.5 reach could not touch a hero; a much bigger one could not be touched.
        var spawn = AssetDatabase.LoadAssetAtPath<EncounterData>(Ogre).spawns[0];
        float wall = spawn.unitData.bodyRadius + CombatPhysics.Active.bodyRadius;

        Assert.That(spawn.loadout, Is.Not.Null,
                    "a spawn without its own loadout takes the map's elite loadout, and its 1.5-reach attack with it");
        Assert.That(spawn.loadout.meleeBasicAttack.range, Is.GreaterThan(wall + 0.2f), "the Ogre stands at its target and cannot hit it");
        Assert.That(wall, Is.LessThan(1.5f - 0.05f), "a hero's 1.5 melee reach cannot get past the Ogre's body");
    }

    [Test]
    public void TheOgreIsInTheAct1ElitePool()
    {
        var act = AssetDatabase.LoadAssetAtPath<ActData>("Assets/Data/Run/Act1/Act1.asset");
        var ogre = AssetDatabase.LoadAssetAtPath<EncounterData>(Ogre);
        Assert.That(act.elitePool.entries.Exists(e => e.encounter == ogre), Is.True);
    }
}
