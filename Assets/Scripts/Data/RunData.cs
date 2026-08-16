using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The sequence of fights that makes up a run. Deliberately a flat ordered list for now: the design
/// calls for a branching act map (Docs/RunLoop.md), but the branching only matters once there are
/// rewards and node types to choose *between*. A straight line proves the loop — fight, survive,
/// fight again — and the map replaces this list without touching anything downstream of
/// <see cref="RunState"/>.
/// </summary>
[CreateAssetMenu(menuName = "Data/Run", fileName = "Run")]
public class RunData : ScriptableObject
{
    [Tooltip("Fights in order. Clearing the last one wins the run.")]
    public List<EncounterData> encounters = new List<EncounterData>();
}
