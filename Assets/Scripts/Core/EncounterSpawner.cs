using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds the enemy side of a fight from an <see cref="EncounterData"/>, and clears it afterwards.
///
/// Enemies are spawned per encounter rather than placed in the scene, because a run needs a fresh
/// (and escalating) opposition every fight. The player's company is never touched here — it persists
/// across the whole run, carrying its equipment, spell slots and progress.
/// </summary>
public class EncounterSpawner : MonoBehaviour
{
    private readonly List<GameObject> _spawned = new List<GameObject>();

    /// <summary>Remove every enemy currently on the field, spawned or hand-placed.</summary>
    public void ClearEnemies()
    {
        // Anything this spawner made.
        for (int i = 0; i < _spawned.Count; i++)
            if (_spawned[i] != null) Destroy(_spawned[i]);
        _spawned.Clear();

        // Plus any enemy the scene started with, so a run always begins from a known board.
        var all = EntityRegistry.All;
        for (int i = all.Count - 1; i >= 0; i--)
        {
            var e = all[i];
            if (e != null && !e.isTeam) Destroy(e.gameObject);
        }
    }

    /// <summary>Spawn the encounter's enemies. Returns how many were placed.</summary>
    public int Spawn(EncounterData encounter)
    {
        if (encounter == null) return 0;

        // Spawns are built under a deactivated holder so their Awake is deferred: Entity.Awake reads
        // unitData to set up health and stats, so the override has to be in place before it runs.
        var holder = new GameObject("EncounterSpawnHolder");
        holder.SetActive(false);

        int count = 0;
        var pending = new List<GameObject>();
        foreach (var spawn in encounter.spawns)
        {
            if (spawn == null || spawn.prefab == null) continue;

            var go = Instantiate(spawn.prefab, holder.transform);
            go.transform.position = new Vector3(spawn.position.x, spawn.position.y, 0f);

            var entity = go.GetComponent<Entity>();
            if (entity != null)
            {
                entity.isTeam = false;
                if (spawn.unitData != null) entity.unitData = spawn.unitData;
            }

            pending.Add(go);
            count++;
        }

        // Release them into the scene — this is where Awake finally runs, with the data already set.
        foreach (var go in pending)
        {
            go.transform.SetParent(null, true);
            _spawned.Add(go);
        }
        Destroy(holder);

        return count;
    }
}
