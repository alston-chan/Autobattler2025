using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Which items carry an Engraving, and what it costs to attune them (Docs/Resonance.md).
///
/// Keyed by HeroEditor's <c>ItemParams.Id</c>,
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
/// <remarks>
/// Values are explicit and permanent: the database stores them as integers, so renumbering would
/// quietly re-point every authored entry at a different requirement.
/// </remarks>
public enum ResonanceRequirement
{
    /// <summary>Fights survived while worn. The simple default; credited when a fight ends.</summary>
    CombatsWorn = 0,
    EnemiesKilled = 1,
    DamageDealt = 2,
    DamageBlocked = 3,

    /// <summary>
    /// Casts of a real ability — an ultimate or any spell that isn't the weapon's own attack.
    /// Deliberately excludes auto-attacks: an item asking for abilities is asking the player to use
    /// their kit, and counting the swings that happen anyway would make that goal meaningless.
    /// </summary>
    AbilitiesCast = 4,

    /// <summary>
    /// Weapon auto-attacks. The busiest counter by far, so thresholds want to be much larger than
    /// they would be for <see cref="AbilitiesCast"/>.
    /// </summary>
    BasicAttacks = 5
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
        ResonanceRequirement.BasicAttacks => "auto-attacks",
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
        [ValueDropdown("ItemIds"), ValidateInput("KnownItem", "Not in ItemCollection", InfoMessageType.Error)]
        [TableColumnWidth(260, Resizable = true)]
        public string itemId;
        [Required, AssetsOnly]
        public Engraving engraving;

        [Tooltip("What this item counts to attune. Pick something the item's own fantasy implies — " +
                 "a shield that counts blocked damage tells the player where to stand it.")]
        public ResonanceRequirement requirement = ResonanceRequirement.CombatsWorn;

        [Tooltip("The quest: how much of the requirement completes it. Wear the item until it has, and " +
                 "its effect is engraved on the hero at the item's rarity when the fight ends; the item " +
                 "is spent. One goal — the three tiers this used to climb are the item's rarity now, " +
                 "decided in the shop (Docs/ShopLoop.md).")]
        [UnityEngine.Serialization.FormerlySerializedAs("engraveCost")]
        public int questGoal = 3;

        /// <summary>True once enough has been done while wearing it to engrave it.</summary>
        public bool IsComplete(float progress) => progress >= questGoal;

        private static IEnumerable<ValueDropdownItem<string>> ItemIds() => Catalog.ItemIds();
        private static bool KnownItem(string id) => Catalog.IsKnown(id);
    }

    [TableList(AlwaysExpanded = true, DrawScrollView = false)]
    public List<Entry> entries = new List<Entry>();

    /// <summary>
    /// A verb for a whole weapon class. Weapons are verbs (Docs/Spells.md), and the sandbox rolls
    /// weapons at random, so every weapon must teach something: a class default is what a weapon
    /// teaches when no entry names it. An entry for the item itself always wins, which is how the
    /// assassin dagger keeps Backstab while every other dagger lunges.
    /// </summary>
    [System.Serializable]
    public class ClassDefault
    {
        public Assets.HeroEditor.InventorySystem.Scripts.Enums.ItemClass itemClass;
        [Required, AssetsOnly] public Engraving engraving;
        public ResonanceRequirement requirement = ResonanceRequirement.AbilitiesCast;
        [UnityEngine.Serialization.FormerlySerializedAs("engraveCost")]
        public int questGoal = 6;
    }

    [Tooltip("What a weapon class teaches when no entry names the item. An item's own entry wins.")]
    [TableList(AlwaysExpanded = true, DrawScrollView = false)]
    public List<ClassDefault> classDefaults = new List<ClassDefault>();

    // One Entry per item id for class defaults, so attunement and tiers read the same object each time.
    private readonly Dictionary<string, Entry> _classEntries = new Dictionary<string, Entry>();

    /// <summary>The entry for an item: its own, else its weapon class's default, else null.</summary>
    public Entry FindFor(Assets.HeroEditor.InventorySystem.Scripts.Data.Item item)
    {
        if (item == null) return null;
        var own = Find(item.Id);
        if (own != null) return own;
        if (item.Params == null || classDefaults == null) return null;

        if (_classEntries.TryGetValue(item.Id, out var cached)) return cached;
        foreach (var d in classDefaults)
        {
            if (d == null || d.engraving == null || d.itemClass != item.Params.Class) continue;
            var entry = new Entry
            {
                itemId = item.Id, engraving = d.engraving, requirement = d.requirement, questGoal = d.questGoal
            };
            _classEntries[item.Id] = entry;
            return entry;
        }
        return null;
    }

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

    /// <summary>
    /// An engraving by asset name. A save file can't hold a reference to a ScriptableObject, so
    /// banked marks are written out by name and resolved back through here on load. Every engraving
    /// a hero can bank came from an entry in this database, so this can always find it again.
    /// </summary>
    public Engraving FindEngraving(string engravingName)
    {
        if (string.IsNullOrEmpty(engravingName) || entries == null) return null;

        for (int i = 0; i < entries.Count; i++)
        {
            var engraving = entries[i] != null ? entries[i].engraving : null;
            if (engraving != null && engraving.name == engravingName) return engraving;
        }
        if (classDefaults != null)
            for (int i = 0; i < classDefaults.Count; i++)
            {
                var engraving = classDefaults[i] != null ? classDefaults[i].engraving : null;
                if (engraving != null && engraving.name == engravingName) return engraving;
            }
        return null;
    }

    /// <summary>The resonance entry for an item id, or null if that item doesn't resonate.</summary>
    public Entry Find(string itemId)
    {
        if (string.IsNullOrEmpty(itemId) || entries == null) return null;
        return entries.Find(e => e != null && e.itemId == itemId);
    }
}

/// <summary>
/// What an item is waiting to tell the player. Ordered by urgency — a higher value outranks a lower
/// one when both apply to the same item, so a decision is never hidden behind a piece of news.
/// </summary>
public enum ResonanceNotice
{
    None = 0,

    /// <summary>
    /// The item's quest completed and its effect was engraved on the hero; the item is spent. News,
    /// not a decision — there is no cash-out to make any more (Docs/ShopLoop.md).
    /// </summary>
    Engraved = 1
}
