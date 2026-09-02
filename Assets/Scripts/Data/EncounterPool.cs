using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The fights a stretch of the map can draw from, and how hard they are fought.
///
/// Difficulty is deliberately not a number on a node. An encounter is a <i>problem</i> — a back-line
/// archer behind two brawlers — and a pool is the set of problems a player might meet at a given
/// depth (Docs/Enemies.md, Docs/RunLoop.md). Toughness is a separate dial, the loadout, so the same
/// encounter can appear early at 1x health and late at 3x without being authored twice. A node
/// that has drawn from a pool holds a real encounter, which is what lets the map be scouted: the
/// player is shown what they will fight, not a difficulty rating.
/// </summary>
[CreateAssetMenu(menuName = "Data/Encounter Pool", fileName = "EncounterPool")]
public class EncounterPool : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        public EncounterData encounter;
        [Tooltip("Relative chance against the other entries. 0 removes it without deleting it.")]
        [Min(0f)] public float weight = 1f;
    }

    public List<Entry> entries = new List<Entry>();

    [Tooltip("The toughness every fight from this pool is fought at. Overrides each encounter's own " +
             "default loadout for spawns that don't name one; leave empty to keep the encounter's own.")]
    public EnemyLoadout loadout;

    public bool IsEmpty
    {
        get
        {
            if (entries == null) return true;
            foreach (var entry in entries)
                if (entry != null && entry.encounter != null && entry.weight > 0f) return false;
            return true;
        }
    }

    /// <summary>
    /// One encounter, weighted. Takes the run's own random source rather than Unity's so that a
    /// seeded map draws the same fights every time — a map that reproduces its shape but not its
    /// contents is not reproducible in any way that helps testing.
    /// </summary>
    public EncounterData Draw(System.Random rng)
    {
        if (IsEmpty || rng == null) return null;

        float total = 0f;
        foreach (var entry in entries)
            if (entry != null && entry.encounter != null && entry.weight > 0f) total += entry.weight;

        float roll = (float)(rng.NextDouble() * total);
        foreach (var entry in entries)
        {
            if (entry == null || entry.encounter == null || entry.weight <= 0f) continue;
            roll -= entry.weight;
            if (roll <= 0f) return entry.encounter;
        }

        // Floating point can leave a hair of roll past the last entry; that entry is the answer.
        for (int i = entries.Count - 1; i >= 0; i--)
            if (entries[i] != null && entries[i].encounter != null && entries[i].weight > 0f)
                return entries[i].encounter;
        return null;
    }
}
