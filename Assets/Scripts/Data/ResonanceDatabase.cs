using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Which items carry an Engraving, and what it costs to attune them (Docs/Resonance.md).
///
/// Keyed by HeroEditor's <c>ItemParams.Id</c>, the same bridge <see cref="SpellbookDatabase"/> uses,
/// so design data can hang off the vendor item catalogue without modifying it. An item absent from
/// here simply doesn't resonate — most gear is still plain armour.
///
/// Loaded once from <c>Resources/ResonanceDatabase</c>.
/// </summary>
[CreateAssetMenu(menuName = "Data/Resonance Database", fileName = "ResonanceDatabase")]
public class ResonanceDatabase : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        [Tooltip("HeroEditor ItemParams.Id of the item that carries this engraving.")]
        public string itemId;
        public Engraving engraving;

        [Tooltip("Attunement needed for Tier I / II / III. Costs escalate, so each tier is a longer " +
                 "commitment than the last — that's what gives a reason to wait, and a reason to stop.")]
        public int tierICost = 1;
        public int tierIICost = 3;
        public int tierIIICost = 6;

        /// <summary>Tier reached at a given attunement: 0 (none) through 3.</summary>
        public int TierAt(float attunement)
        {
            if (attunement >= tierIIICost) return 3;
            if (attunement >= tierIICost) return 2;
            if (attunement >= tierICost) return 1;
            return 0;
        }

        /// <summary>Attunement required for the next tier, or 0 once maxed.</summary>
        public int NextTierCost(float attunement)
        {
            int tier = TierAt(attunement);
            if (tier == 0) return tierICost;
            if (tier == 1) return tierIICost;
            if (tier == 2) return tierIIICost;
            return 0;
        }
    }

    public List<Entry> entries = new List<Entry>();

    private static ResonanceDatabase _active;

    public static ResonanceDatabase Active
    {
        get
        {
            if (_active == null)
            {
                _active = Resources.Load<ResonanceDatabase>("ResonanceDatabase");
                if (_active == null)
                    Debug.LogWarning("[ResonanceDatabase] No asset at Resources/ResonanceDatabase — " +
                                     "no item will resonate.");
            }
            return _active;
        }
    }

    /// <summary>The resonance entry for an item id, or null if that item doesn't resonate.</summary>
    public Entry Find(string itemId)
    {
        if (string.IsNullOrEmpty(itemId) || entries == null) return null;
        return entries.Find(e => e != null && e.itemId == itemId);
    }
}
