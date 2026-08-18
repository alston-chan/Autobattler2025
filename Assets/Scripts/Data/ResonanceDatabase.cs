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
/// <summary>
/// What an item counts to attune. Each is an event-driven counter, so progress arrives as the hero
/// plays rather than in a lump when the fight ends — a shield that counts damage blocked should tick
/// on the blow that gets blocked.
///
/// The requirement is also characterisation: a shield attuning through <see cref="DamageBlocked"/>
/// asks to be put where blows land, which is a different instruction to the player than one counting
/// kills.
/// </summary>
public enum ResonanceRequirement
{
    /// <summary>Fights survived while worn. The simple default; credited when a fight ends.</summary>
    CombatsWorn,
    EnemiesKilled,
    DamageDealt,
    DamageBlocked,
    AbilitiesCast
}

public static class ResonanceRequirements
{
    /// <summary>
    /// What the counter is counting, for display. "1 / 2" alone is meaningless — the player can't
    /// tell whether that's fights, kills or damage, and so can't tell whether it's nearly done or
    /// barely started.
    /// </summary>
    public static string Describe(ResonanceRequirement requirement) => requirement switch
    {
        ResonanceRequirement.CombatsWorn => "fights worn",
        ResonanceRequirement.EnemiesKilled => "enemies slain",
        ResonanceRequirement.DamageDealt => "damage dealt",
        ResonanceRequirement.DamageBlocked => "damage blocked",
        ResonanceRequirement.AbilitiesCast => "abilities cast",
        _ => "progress"
    };
}

[CreateAssetMenu(menuName = "Data/Resonance Database", fileName = "ResonanceDatabase")]
public class ResonanceDatabase : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        [Tooltip("HeroEditor ItemParams.Id of the item that carries this engraving.")]
        public string itemId;
        public Engraving engraving;

        [Tooltip("What this item counts to attune. Pick something the item's own fantasy implies — " +
                 "a shield that counts blocked damage tells the player where to stand it.")]
        public ResonanceRequirement requirement = ResonanceRequirement.CombatsWorn;

        [Tooltip("Attunement needed to reach Tier II and Tier III. Tier I costs nothing — an item's " +
                 "engraving is its identity and works the moment it is worn. Attunement only makes it " +
                 "stronger, and the second tier costs more than the first so each is a longer " +
                 "commitment than the last.")]
        public int tierIICost = 3;
        public int tierIIICost = 6;

        [Tooltip("Attunement needed before the engraving can be banked permanently. Separate from the " +
                 "worn tiers on purpose: wearing an item grants its engraving at once, but KEEPING it " +
                 "forever has to be earned — otherwise cashing out costs nothing and the bank-or-press " +
                 "decision disappears.")]
        public int engraveCost = 3;

        /// <summary>True once the engraving has been attuned enough to bank permanently.</summary>
        public bool CanEngrave(float attunement) => attunement >= engraveCost;

        /// <summary>
        /// Tier reached at a given attunement: 1 through 3. Never 0 — a worn engraving is always at
        /// least Tier I, so equipping an item is never a dead period waiting for it to switch on.
        /// </summary>
        public int TierAt(float attunement)
        {
            if (attunement >= tierIIICost) return 3;
            if (attunement >= tierIICost) return 2;
            return 1;
        }

        /// <summary>Attunement required for the next tier, or 0 once maxed.</summary>
        public int NextTierCost(float attunement)
        {
            int tier = TierAt(attunement);
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
