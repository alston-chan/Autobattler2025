using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// A fight to play on purpose: which heroes field, in what gear, with what stance and which spell
/// active, against which encounter. For playtesting a question — does 3v3 read better than 5v5,
/// does a ninja whose blink fires a Cannonball feel as good as it sounds — without touching the
/// scene or the run. Nothing here is saved anywhere: the scenario is applied at the start of play
/// and forgotten when play stops. Pick one in Tools > Playtest > Scenarios; none means the game as
/// authored.
/// </summary>
[CreateAssetMenu(menuName = "Data/Playtest Scenario", fileName = "Scenario")]
public class PlaytestScenario : ScriptableObject
{
    [System.Serializable]
    public class HeroKit
    {
        [Tooltip("The hero's scene object name, e.g. Hero_Daggers. Benched heroes are fielded when named.")]
        public string heroName;
        [Tooltip("What the hero wears. Weapon verbs and set engravings follow from the items.")]
        [ValueDropdown("ItemIds")] public List<string> itemIds = new List<string>();
        public Stance stance = Stance.Auto;
        [Tooltip("Which spell slot is cast. With no spellbooks, 0 is the weapon's verb.")]
        public int activeSlot = 0;
        [Tooltip("Leave the hero's authored spellbooks out, so the weapon's verb is the only ability.")]
        public bool noAuthoredSpellbooks = true;

        private static IEnumerable<ValueDropdownItem<string>> ItemIds() => Catalog.ItemIds();
    }

    [Tooltip("The heroes to field. Everyone else sits out. Empty fields the company as authored.")]
    public List<HeroKit> heroes = new List<HeroKit>();

    [Tooltip("The encounter every fight of this scenario plays, in place of the run's. Empty keeps the run's.")]
    public EncounterData encounter;
    [Tooltip("The enemy loadout for that encounter. Empty keeps the encounter's own.")]
    public EnemyLoadout loadout;

    [TextArea(2, 6), Tooltip("What this scenario is for, so the list reads.")]
    public string notes;

    public bool FieldsEveryone => heroes == null || heroes.Count == 0;

    public bool Includes(string heroName)
    {
        if (FieldsEveryone) return true;
        foreach (var h in heroes) if (h != null && h.heroName == heroName) return true;
        return false;
    }

    public HeroKit KitFor(string heroName)
    {
        if (heroes == null) return null;
        foreach (var h in heroes) if (h != null && h.heroName == heroName) return h;
        return null;
    }
}

/// <summary>
/// Which scenario is in force. A single asset in Resources, so the choice survives a domain reload
/// and a scene restart, and lives nowhere the scene or the run would pick up.
/// </summary>
[CreateAssetMenu(menuName = "Data/Playtest", fileName = "Playtest")]
public class Playtest : ScriptableObject
{
    [Tooltip("The scenario applied at the start of play. None: the game as authored.")]
    public PlaytestScenario active;

    private static Playtest _active;

    public static Playtest Active
    {
        get
        {
            if (_active == null) _active = Resources.Load<Playtest>("Playtest");
            return _active;
        }
    }

    /// <summary>The scenario in force, or null for the game as authored.</summary>
    public static PlaytestScenario Scenario => Active != null ? Active.active : null;
}
