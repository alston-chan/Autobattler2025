using System.Collections.Generic;

/// <summary>What happens at a node. Only the kinds the game can currently resolve — a fight — exist;
/// Shop, Rest, Event and Treasure (Docs/RunLoop.md) arrive with the systems they need.</summary>
public enum NodeType
{
    Combat,
    Elite,
    Boss
}

/// <summary>
/// One stop on the map. Holds a real encounter from the moment the map is generated, so the player
/// can scout it and a seeded run replays it exactly.
/// </summary>
public class MapNode
{
    public int Row;
    public int Lane;
    public NodeType Type;

    /// <summary>The fight here. Null only when the act has no pool covering this row.</summary>
    public EncounterData Encounter;

    /// <summary>The toughness the pool said to fight it at; null keeps the encounter's own.</summary>
    public EnemyLoadout Loadout;

    /// <summary>Nodes on the row above this one can move to.</summary>
    public readonly List<MapNode> Next = new List<MapNode>();

    public bool Cleared;

    public string Label => Encounter != null ? Encounter.encounterName : Type.ToString();

    public int EnemyCount => Encounter != null && Encounter.spawns != null ? Encounter.spawns.Count : 0;
}

/// <summary>
/// A generated act: rows of nodes from the bottom up, the boss alone on top. Pure data with no Unity
/// dependencies, so <see cref="RunState"/> can walk it and tests can inspect it.
/// </summary>
public class ActMap
{
    public readonly List<List<MapNode>> Rows = new List<List<MapNode>>();

    public int Seed;

    public int RowCount => Rows.Count;

    public List<MapNode> Row(int index) =>
        index >= 0 && index < Rows.Count ? Rows[index] : new List<MapNode>();

    public MapNode Boss => Rows.Count > 0 && Rows[Rows.Count - 1].Count > 0 ? Rows[Rows.Count - 1][0] : null;

    public IEnumerable<MapNode> AllNodes()
    {
        foreach (var row in Rows)
            foreach (var node in row)
                yield return node;
    }
}
