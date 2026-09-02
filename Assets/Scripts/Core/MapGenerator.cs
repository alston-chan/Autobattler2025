using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Rolls an <see cref="ActMap"/> from an <see cref="ActData"/> recipe and a seed.
///
/// Three promises, each pinned by a test:
///
///   * <b>Reproducible.</b> Same recipe and seed, same map — nodes, edges and the fight inside every
///     node. A progression run has to be replayable or nothing learned from it can be checked.
///   * <b>No dead ends.</b> Every node is reachable from the bottom row and every node leads to the
///     boss. Edges only ever go one row up, and every node gets at least one edge in each direction,
///     which by induction is the whole proof.
///   * <b>Elites are placed, not forced.</b> The guaranteed count lands on separate rows where
///     possible, so a single path cannot skip all of them by accident, but the player may still
///     route around one on purpose — that choice is the point (Docs/RunLoop.md).
///
/// Edges are drawn so they never cross: each node's main link goes to the node at the same
/// proportional position on the row above, and side links are added only where they stay between
/// the neighbours' main links. Crossing edges are not a correctness problem, but a map that reads
/// as a tangle cannot be planned on.
/// </summary>
public static class MapGenerator
{
    public static ActMap Generate(ActData act, int seed)
    {
        var map = new ActMap { Seed = seed };
        if (act == null) return map;

        var rng = new System.Random(seed);
        int rows = Mathf.Max(2, act.rows);
        int minWide = Mathf.Max(1, Mathf.Min(act.minNodesPerRow, act.maxNodesPerRow));
        int maxWide = Mathf.Max(minWide, act.maxNodesPerRow);

        // Shape: every row but the top is a spread of ordinary nodes; the top is the boss alone.
        for (int r = 0; r < rows - 1; r++)
        {
            int width = rng.Next(minWide, maxWide + 1);
            var row = new List<MapNode>(width);
            for (int lane = 0; lane < width; lane++)
                row.Add(new MapNode { Row = r, Lane = lane, Type = NodeType.Combat });
            map.Rows.Add(row);
        }
        map.Rows.Add(new List<MapNode> { new MapNode { Row = rows - 1, Lane = 0, Type = NodeType.Boss } });

        for (int r = 0; r < rows - 1; r++) Link(map.Rows[r], map.Rows[r + 1], rng);

        PlaceElites(map, act, rng);
        FillFights(map, act, rng);

        return map;
    }

    /// <summary>Connect one row to the next so nothing on either side is left out.</summary>
    private static void Link(List<MapNode> from, List<MapNode> to, System.Random rng)
    {
        int n = from.Count, m = to.Count;
        var primary = new int[n];
        for (int i = 0; i < n; i++)
            primary[i] = Mathf.Clamp((int)((i + 0.5f) * m / n), 0, m - 1);

        for (int i = 0; i < n; i++)
        {
            Connect(from[i], to[primary[i]]);

            // A second link to a neighbour, when it would not cross the next node's main link.
            int right = primary[i] + 1;
            bool rightSafe = right < m && (i == n - 1 || right <= primary[i + 1]);
            if (rightSafe && rng.NextDouble() < 0.45) Connect(from[i], to[right]);

            int left = primary[i] - 1;
            bool leftSafe = left >= 0 && (i == 0 || left >= primary[i - 1]);
            if (leftSafe && rng.NextDouble() < 0.3) Connect(from[i], to[left]);
        }

        // Anyone above with no way in gets a link from whichever node below sits closest.
        for (int j = 0; j < m; j++)
        {
            if (HasIncoming(from, to[j])) continue;
            int best = 0;
            for (int i = 1; i < n; i++)
                if (Mathf.Abs(primary[i] - j) < Mathf.Abs(primary[best] - j)) best = i;
            Connect(from[best], to[j]);
        }
    }

    private static void Connect(MapNode from, MapNode to)
    {
        if (!from.Next.Contains(to)) from.Next.Add(to);
    }

    private static bool HasIncoming(List<MapNode> from, MapNode node)
    {
        foreach (var candidate in from)
            if (candidate.Next.Contains(node)) return true;
        return false;
    }

    /// <summary>
    /// Turn some ordinary nodes into elites. Rows are drawn without replacement first, so the
    /// guaranteed elites spread out; only once every eligible row has one do two share a row.
    /// </summary>
    private static void PlaceElites(ActMap map, ActData act, System.Random rng)
    {
        int wanted = Mathf.Max(0, act.guaranteedElites);
        if (wanted == 0) return;

        int first = Mathf.Max(0, act.eliteEarliestRow);
        int last = map.RowCount - 2;                       // never the boss row
        var eligibleRows = new List<int>();
        for (int r = first; r <= last; r++) eligibleRows.Add(r);
        if (eligibleRows.Count == 0)
        {
            Debug.LogWarning($"[MapGenerator] {act.name} wants {wanted} elites but no row between " +
                             $"{first} and {last} can hold one — none placed.");
            return;
        }

        var pool = new List<MapNode>();
        foreach (int r in eligibleRows) pool.AddRange(map.Rows[r]);

        int placed = 0;
        while (placed < wanted && pool.Count > 0)
        {
            // Prefer a row that has no elite yet.
            var candidates = pool.FindAll(node => !RowHasElite(map, node.Row));
            if (candidates.Count == 0) candidates = pool;

            var chosen = candidates[rng.Next(candidates.Count)];
            chosen.Type = NodeType.Elite;
            pool.Remove(chosen);
            placed++;
        }
    }

    private static bool RowHasElite(ActMap map, int row)
    {
        foreach (var node in map.Rows[row])
            if (node.Type == NodeType.Elite) return true;
        return false;
    }

    /// <summary>Give every node its fight, drawn from the pool its type and depth call for.</summary>
    private static void FillFights(ActMap map, ActData act, System.Random rng)
    {
        foreach (var node in map.AllNodes())
        {
            EncounterPool pool;
            switch (node.Type)
            {
                case NodeType.Boss: pool = act.bossPool; break;
                case NodeType.Elite: pool = act.elitePool; break;
                default: pool = act.PoolForCombatRow(node.Row); break;
            }

            if (pool == null || pool.IsEmpty)
            {
                // A node with no fight is a node the player cannot pass. Say which rule left it empty,
                // because the map itself will look perfectly normal.
                Debug.LogWarning($"[MapGenerator] {act.name}: no {node.Type} pool covers row " +
                                 $"{node.Row} — that node has no fight.");
                continue;
            }

            node.Encounter = pool.Draw(rng);
            node.Loadout = pool.loadout;
        }
    }
}
