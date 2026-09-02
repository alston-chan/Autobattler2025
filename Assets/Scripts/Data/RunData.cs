using System.Collections.Generic;
using UnityEngine;

/// <summary>How the company is dressed when a run begins.</summary>
public enum StartingGear
{
    /// <summary>A random roll from the whole collection. The sandbox setting: every feature gets
    /// exercised against gear nobody chose.</summary>
    Randomized,

    /// <summary>Each hero's authored kit (Entity.startingItemIds), or the run's fallback kit for a
    /// hero without one. A run starts poor on purpose — the run is where gear is found.</summary>
    Kit
}

/// <summary>
/// One run: what is fought, in what shape, and how the company starts.
///
/// Two shapes. A flat list of encounters is the straight line that proved the loop, and it is what
/// the sandbox scene still plays. An <see cref="ActData"/> replaces it with a seeded branching map
/// (Docs/RunLoop.md). Which one a scene plays is decided entirely by which of these assets its
/// RunManager points at, so switching from feature-testing to a progression run is one reference.
/// </summary>
[CreateAssetMenu(menuName = "Data/Run", fileName = "Run")]
public class RunData : ScriptableObject
{
    [Tooltip("Fights in order, for a straight-line run. Clearing the last one wins. Ignored when an " +
             "act is set below.")]
    public List<EncounterData> encounters = new List<EncounterData>();

    [Header("Map")]
    [Tooltip("Play this act as a branching map instead of the list above.")]
    public ActData act;
    [Tooltip("0 rolls a fresh map every run. Any other value reproduces the same map — the same " +
             "nodes with the same fights in them — which is what makes a progression run repeatable.")]
    public int mapSeed;

    [Header("Spoils")]
    [Tooltip("Items dropped by any fight that doesn't name its own pool.")]
    public RewardPool defaultRewardPool;
    [Tooltip("How many items a victory offers to choose between.")]
    public int rewardChoices = 3;

    [Header("Starting gear")]
    public StartingGear startingGear = StartingGear.Randomized;
    [Tooltip("Under Kit: worn by any hero whose own starting kit is empty.")]
    public List<string> fallbackKitItemIds = new List<string>();

    [Tooltip("What the shared bag opens with. Workshop stocks one random item per slot plus a copy " +
             "of every designed item, so any of it can be tested at any time. Empty is a run: the bag " +
             "fills from what drops.")]
    public StartingBag bag = StartingBag.Empty;

    /// <summary>Whether there is anything here to run at all.</summary>
    public bool HasContent => act != null || (encounters != null && encounters.Count > 0);
}
