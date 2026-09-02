using System;
using System.Collections.Generic;
using System.IO;
using Assets.HeroEditor.InventorySystem.Scripts.Data;
using Assets.HeroEditor.InventorySystem.Scripts.Enums;
using UnityEngine;

/// <summary>An item as a save knows it: enough to make the same one again.</summary>
[Serializable]
public class SavedItem
{
    public string id;
    public int count = 1;
    public int modifierId;
    public int modifierLevel;

    public static SavedItem From(Item item) => new SavedItem
    {
        id = item.Id,
        count = Mathf.Max(1, item.Count),
        modifierId = item.Modifier != null ? (int)item.Modifier.Id : 0,
        modifierLevel = item.Modifier != null ? item.Modifier.Level : 0
    };

    public Item ToItem() =>
        modifierId != 0 || modifierLevel != 0
            ? new Item(id, new Modifier((ItemModifier)modifierId, modifierLevel), count)
            : new Item(id, count);
}

/// <summary>One hero: where it stands, what it wears, and what it has resonated.</summary>
[Serializable]
public class SavedHero
{
    public string name;
    public int column;
    public int row;
    public List<SavedItem> equipped = new List<SavedItem>();
    public Resonance.State resonance = new Resonance.State();
}

/// <summary>
/// A run at a safe point: everything needed to put it back exactly, and nothing that can be rebuilt.
///
/// The map is not stored — its seed and the path taken across it are, and the map is rolled again
/// and the path replayed (RunState.Replay). Enemies are not stored either: a save is only written
/// between fights, and a fight interrupted is fought again from its start (Docs/Architecture.md,
/// "save between fights; replay the fight if it was interrupted").
/// </summary>
[Serializable]
public class RunSnapshot
{
    public string runAsset;
    public string progress;
    public string savedAt;

    public int mapSeed;
    public List<int> pathRows = new List<int>();
    public List<int> pathLanes = new List<int>();
    public bool awaitingPath;
    public int encounterIndex;

    public List<SavedHero> heroes = new List<SavedHero>();
    public List<SavedItem> bag = new List<SavedItem>();
}

/// <summary>
/// The single run save: one file, written at every safe point, deleted when the run ends.
///
/// One slot on purpose. A run is precious enough — a wipe ends it — that losing one to a crash is
/// unacceptable, but it is not a thing a player chooses between versions of; the newest state is
/// the only state. Writing at every safe point rather than on quit means there is no quit to get
/// wrong.
/// </summary>
public static class RunSave
{
    public static string FilePath => Path.Combine(Application.persistentDataPath, "run.json");

    public static bool Exists => File.Exists(FilePath);

    public static void Write(RunSnapshot snapshot)
    {
        if (snapshot == null) return;
        snapshot.savedAt = DateTime.Now.ToString("s");
        File.WriteAllText(FilePath, JsonUtility.ToJson(snapshot, true));
    }

    /// <summary>The saved run, or null if there is none or it cannot be read.</summary>
    public static RunSnapshot Read()
    {
        if (!Exists) return null;
        try
        {
            return JsonUtility.FromJson<RunSnapshot>(File.ReadAllText(FilePath));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[RunSave] Could not read {FilePath}: {e.Message} — starting fresh.");
            return null;
        }
    }

    public static void Delete()
    {
        if (Exists) File.Delete(FilePath);
    }

    public static List<Item> ToItems(List<SavedItem> saved)
    {
        var items = new List<Item>();
        if (saved == null) return items;
        foreach (var record in saved)
            if (record != null && !string.IsNullOrEmpty(record.id)) items.Add(record.ToItem());
        return items;
    }

    public static List<SavedItem> FromItems(IEnumerable<Item> items)
    {
        var saved = new List<SavedItem>();
        if (items == null) return saved;
        foreach (var item in items)
            if (item != null && !string.IsNullOrEmpty(item.Id)) saved.Add(SavedItem.From(item));
        return saved;
    }
}
