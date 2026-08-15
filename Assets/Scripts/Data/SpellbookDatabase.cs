using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Links spellbook items (by their HeroEditor <c>ItemParams.Id</c>) to the <see cref="Spell"/> they
/// teach. Kept separate from equipment's <see cref="ItemDefinition"/> because spellbooks grant an
/// ability, not stats — a spellbook equipped into a character's spell slot puts its Spell into that
/// slot (see Docs/Spells.md).
///
/// Loaded from <c>Resources/SpellbookDatabase</c>.
/// </summary>
[CreateAssetMenu(menuName = "Data/Spellbook Database", fileName = "SpellbookDatabase")]
public class SpellbookDatabase : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        [Tooltip("HeroEditor ItemParams.Id of the spellbook item, e.g. \"Spellbook.DoubleStrike\".")]
        public string itemId;
        public Spell spell;
    }

    public List<Entry> entries = new List<Entry>();

    private static SpellbookDatabase _active;

    /// <summary>The global database, loaded once from Resources.</summary>
    public static SpellbookDatabase Active
    {
        get
        {
            if (_active == null)
            {
                _active = Resources.Load<SpellbookDatabase>("SpellbookDatabase");
                if (_active == null)
                    Debug.LogWarning("[SpellbookDatabase] No asset at Resources/SpellbookDatabase — " +
                                     "spellbook items won't resolve to spells.");
            }
            return _active;
        }
    }

    /// <summary>The Spell a spellbook item teaches, or null if the id isn't a known spellbook.</summary>
    public Spell GetSpell(string itemId)
    {
        if (string.IsNullOrEmpty(itemId) || entries == null) return null;
        var e = entries.Find(x => x != null && x.itemId == itemId);
        return e != null ? e.spell : null;
    }

    /// <summary>
    /// The spellbook item Id that teaches <paramref name="spell"/>, or null if none does — the reverse
    /// of <see cref="GetSpell"/>. Lets an editor-authored spell loadout be materialized as equipped
    /// spellbooks at startup.
    /// </summary>
    public string GetItemId(Spell spell)
    {
        if (spell == null || entries == null) return null;
        var e = entries.Find(x => x != null && x.spell == spell);
        return e != null ? e.itemId : null;
    }

    /// <summary>True if the item id is a registered spellbook.</summary>
    public bool IsSpellbook(string itemId) => GetSpell(itemId) != null;
}
