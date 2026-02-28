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

    [Header("Combat")]
    public float separationDistance = 1.0f;
    public float separationStrength = 0.5f;

    [Header("Visual")]
    public Vector3 healthBarOffset = new Vector3(0, 3.0f, 1);

    [Header("Spells")]
    public List<Spell> spells = new List<Spell>();
}
