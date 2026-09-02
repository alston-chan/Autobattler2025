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
/// <summary>
/// The question a fight asks of a build (Docs/Enemies.md): what it punishes, and what it demands.
/// Named on the map so a route can be chosen for what it asks — that is the whole reason the map
/// branches. Only the problems the game can currently pose exist here; the rest of the doc's
/// roster (Mender, Warden, Stalker...) arrives with the mechanics each one needs.
/// </summary>
public enum EnemyProblem
{
    /// <summary>Many weak bodies. Punishes single-target; demands AoE.</summary>
    Swarm,
    /// <summary>One huge wall. Punishes AoE-only and slow damage; demands single-target and sustain.</summary>
    Bulwark,
    /// <summary>Backline glass cannons. Punishes slow starts and pure melee; demands reach or a dive.</summary>
    Sniper
}

[CreateAssetMenu(menuName = "Data/Encounter", fileName = "Encounter")]
public class EncounterData : ScriptableObject
{
    [Tooltip("The problems this fight poses (Docs/Enemies.md). Shown on the map so a route can be " +
             "chosen for what it asks. Empty means an ordinary fight.")]
    public List<EnemyProblem> problems = new List<EnemyProblem>();

    /// <summary>"SWARM", "BULWARK + SNIPER", or empty for an ordinary fight.</summary>
    public string ProblemLabel
    {
        get
        {
            if (problems == null || problems.Count == 0) return "";
            var names = new List<string>(problems.Count);
            foreach (var problem in problems) names.Add(problem.ToString().ToUpperInvariant());
            return string.Join(" + ", names);
        }
    }

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

    [Tooltip("Items this fight can drop. Falls back to the run's default pool when empty.")]
    public RewardPool rewardPool;

    [Tooltip("Shown to the player when the fight starts.")]
    public string encounterName = "Encounter";

    public List<Spawn> spawns = new List<Spawn>();
}
