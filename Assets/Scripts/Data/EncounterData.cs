using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One fight: which enemies show up and where. The unit of content a run is built from — a
/// <see cref="RunData"/> is an ordered list of these (Docs/RunLoop.md).
///
/// Positions are authored rather than random so an encounter is a designed *problem* (a back-line
/// archer behind two brawlers reads differently from three brawlers), which is what makes scouting
/// and countering meaningful later.
/// </summary>
[CreateAssetMenu(menuName = "Data/Encounter", fileName = "Encounter")]
public class EncounterData : ScriptableObject
{
    [System.Serializable]
    public class Spawn
    {
        [Tooltip("Enemy prefab to instantiate. Its Entity is forced onto the enemy team.")]
        public GameObject prefab;
        [Tooltip("Cell on the enemy half of the grid. Column 0 is the front rank (nearest the " +
                 "company); row 0 is the bottom.")]
        public int column;
        public int row;
        [Tooltip("Optional stat override. Leave empty to use the prefab's own values.")]
        public UnitData unitData;
        [Tooltip("Optional. Rolls this unit's gear, appearance and ability at spawn. Falls back to " +
                 "the encounter's default loadout when empty.")]
        public EnemyLoadout loadout;
    }

    [Tooltip("Loadout used by any spawn that doesn't name its own. Without one, spawned units keep " +
             "whatever their prefab has — which for the stock prefabs is no spells, so they can't fight.")]
    public EnemyLoadout defaultLoadout;

    [Tooltip("Shown to the player when the fight starts.")]
    public string encounterName = "Encounter";

    public List<Spawn> spawns = new List<Spawn>();
}
