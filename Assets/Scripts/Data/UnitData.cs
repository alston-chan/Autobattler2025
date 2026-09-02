using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Data-driven unit definition. Create assets via right-click > Create > Data > UnitData.
/// Assign to Entity prefabs instead of hardcoding stats.
/// </summary>
[CreateAssetMenu(menuName = "Data/UnitData")]
public class UnitData : ScriptableObject
{
    [Header("Identity")]
    public string unitName;
    public bool isCharacter = true;
    public bool isRanged = false;

    [Header("Stats")]
    public float maxHealth = 100f;
    public float moveSpeed = 3f;
    [Tooltip("Attacks-per-second multiplier for weapon spells (melee/bow). 1 = normal speed.")]
    public float attackSpeed = 1f;
    [Tooltip("Multiplies every hit. 1 = as the weapon says. The archetype dial: a glass sniper is " +
             "2.5 at 150 health, a bulwark 1.5 at 3000. Applied as a modifier rather than a base so " +
             "it survives the weapon swap that rewrites base damage at spawn.")]
    public float damageMultiplier = 1f;

    [Header("Combat")]
    public float separationDistance = 1.0f;
    public float separationStrength = 0.5f;

    [Header("Visual")]
    public Vector3 healthBarOffset = new Vector3(0, 3.0f, 1);

    [Header("Spells")]
    public List<Spell> spells = new List<Spell>();
}
