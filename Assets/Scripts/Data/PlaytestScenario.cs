using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// A fight to play on purpose: which heroes field, in what gear and with which spell active,
/// against which encounter. Tactics come from the gear, as always. For playtesting a question — does 3v3 read better than 5v5,
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
        [Tooltip("Which spell slot is cast. With no spellbooks, 0 is the weapon's verb.")]
        public int activeSlot = 0;
        [Tooltip("Leave the hero's authored spellbooks out, so the weapon's verb is the only ability.")]
        public bool noAuthoredSpellbooks = true;

        private static IEnumerable<ValueDropdownItem<string>> ItemIds() => Catalog.ItemIds();
    }

    [Tooltip("The heroes to field. Everyone else sits out. Empty fields the company as authored.")]
    public List<HeroKit> heroes = new List<HeroKit>();

    [Tooltip("Scales the fielded heroes' max health, so a scenario can be paced: 0.5 halves it.")]
    public float heroHealthScale = 1f;

    [Tooltip("The encounter every fight of this scenario plays, in place of the run's. Empty keeps the run's.")]
    public EncounterData encounter;
    [Tooltip("The enemy loadout for that encounter. Empty keeps the encounter's own.")]
    public EnemyLoadout loadout;

    [Tooltip("Kits for the encounter's spawns. Drawn at random, every kit once before any repeats, " +
             "so the enemy side is a different team each fight; or in order, first spawn first kit. " +
             "Empty leaves the spawns to the encounter and loadout.")]
    public List<EnemyKit> enemyKits = new List<EnemyKit>();
    [Tooltip("Draw the enemy kits at random rather than in order.")]
    public bool randomEnemyKits = true;

    /// <summary>The kits for an encounter's spawns, one per spawn (null where none is given).</summary>
    public List<EnemyKit> EnemyKitsFor(int spawnCount)
    {
        var result = new List<EnemyKit>(spawnCount);
        if (enemyKits == null || enemyKits.Count == 0) { for (int i = 0; i < spawnCount; i++) result.Add(null); return result; }
        if (randomEnemyKits) return EnemyKit.Draw(enemyKits, spawnCount);
        for (int i = 0; i < spawnCount; i++) result.Add(i < enemyKits.Count ? enemyKits[i] : null);
        return result;
    }

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
