using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The recipe for one act's map: how tall and wide it is, and which fights live at which depth.
///
/// A recipe rather than an authored map, because a run should differ each time but a *test* run
/// should not — so the shape is rolled from a seed (<see cref="MapGenerator"/>) and this asset holds
/// only the rules. Depth is expressed as row bands each pointing at an <see cref="EncounterPool"/>:
/// "rows 0–2 draw from the early pool, rows 3–5 from the late one". That is the whole difficulty
/// curve, and it is curated rather than computed, because a computed curve cannot tell the player
/// what problem is coming — only that it will be worse (Docs/RunLoop.md, Docs/Enemies.md).
/// </summary>
[CreateAssetMenu(menuName = "Data/Act", fileName = "Act")]
public class ActData : ScriptableObject
{
    [Header("Shape")]
    [Tooltip("Rows from the bottom of the map to the boss, the boss row included. Seven is six " +
             "fights and a boss.")]
    [Min(2)] public int rows = 7;
    [Range(1, 6)] public int minNodesPerRow = 2;
    [Range(1, 6)] public int maxNodesPerRow = 4;

    [System.Serializable]
    public class Band
    {
        [Tooltip("First row this pool covers, 0 at the bottom.")]
        public int fromRow;
        [Tooltip("Last row this pool covers, inclusive.")]
        public int toRow;
        public EncounterPool pool;
    }

    [Header("Fights")]
    [Tooltip("Which pool an ordinary fight at each depth draws from. A row nobody covers gets no " +
             "fight, and the generator says so.")]
    public List<Band> combatBands = new List<Band>();

    [Tooltip("Harder fights, better spoils. Placed on separate rows where possible so a run cannot " +
             "route around all of them in one step.")]
    public EncounterPool elitePool;
    [Tooltip("How many elite nodes every map has. They are placed, not forced — a player may still " +
             "path around one.")]
    [Min(0)] public int guaranteedElites = 2;
    [Tooltip("The lowest row an elite may sit on. Keeps the first fights honest.")]
    [Min(0)] public int eliteEarliestRow = 2;

    [Tooltip("The act-ender. One node alone on the top row.")]
    public EncounterPool bossPool;

    [Header("Spoils")]
    [Tooltip("What an elite drops. Weight this toward the items worth routing for; the run's " +
             "default pool is used when empty.")]
    public RewardPool eliteRewardPool;
    [Tooltip("What the boss drops. Falls back to the elite pool, then the run's default.")]
    public RewardPool bossRewardPool;

    /// <summary>The pool an ordinary fight on <paramref name="row"/> draws from, or null if no band covers it.</summary>
    public EncounterPool PoolForCombatRow(int row)
    {
        if (combatBands == null) return null;
        foreach (var band in combatBands)
            if (band != null && row >= band.fromRow && row <= band.toRow) return band.pool;
        return null;
    }
}
