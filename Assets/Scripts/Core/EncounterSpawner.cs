using System.Collections.Generic;
using Assets.HeroEditor.InventorySystem.Scripts;
using UnityEngine;

/// <summary>
/// Builds the enemy side of a fight from an <see cref="EncounterData"/>, and clears it afterwards.
///
/// Enemies are spawned per encounter rather than placed in the scene, because a run needs a fresh
/// (and escalating) opposition every fight. The player's company is never touched here — it persists
/// across the whole run, carrying its equipment, spell slots and progress.
/// </summary>
public class EncounterSpawner : MonoBehaviour
{
    private readonly List<GameObject> _spawned = new List<GameObject>();

    /// <summary>Remove every enemy currently on the field, spawned or hand-placed.</summary>
    public void ClearEnemies()
    {
        // Anything this spawner made.
        for (int i = 0; i < _spawned.Count; i++)
            if (_spawned[i] != null) Destroy(_spawned[i]);
        _spawned.Clear();

        // Plus any enemy the scene started with, so a run always begins from a known board.
        var all = EntityRegistry.All;
        for (int i = all.Count - 1; i >= 0; i--)
        {
            var e = all[i];
            if (e != null && !e.isTeam) Destroy(e.gameObject);
        }
    }

    /// <summary>Spawn the encounter's enemies. Returns how many were placed.</summary>
    public int Spawn(EncounterData encounter)
    {
        if (encounter == null) return 0;

        // Spawns are built under a deactivated holder so their Awake is deferred: Entity.Awake reads
        // unitData to set up health and stats, so the override has to be in place before it runs.
        var holder = new GameObject("EncounterSpawnHolder");
        holder.SetActive(false);

        int count = 0;
        var pending = new List<Entity>();
        var loadouts = new List<EnemyLoadout>();
        foreach (var spawn in encounter.spawns)
        {
            if (spawn == null || spawn.prefab == null) continue;

            var go = Instantiate(spawn.prefab, holder.transform);
            go.transform.position = new Vector3(spawn.position.x, spawn.position.y, 0f);
            _spawned.Add(go);
            count++;

            var entity = go.GetComponent<Entity>();
            if (entity == null) continue;

            entity.isTeam = false;
            if (spawn.unitData != null) entity.unitData = spawn.unitData;

            var loadout = spawn.loadout != null ? spawn.loadout : encounter.defaultLoadout;
            if (loadout != null) ArmBeforeWake(entity, loadout);

            pending.Add(entity);
            loadouts.Add(loadout);
        }

        // Release them into the scene — this is where Awake finally runs, with the data already set.
        foreach (var go in _spawned)
            if (go != null && go.transform.parent == holder.transform) go.transform.SetParent(null, true);
        Destroy(holder);

        // Gear and looks come after: both need the character rig awake to apply.
        for (int i = 0; i < pending.Count; i++)
        {
            // Enemies muster on the right, so they face left — toward the company. CombatAI takes
            // over once the fight starts; this is what they look like while the player is still
            // deciding, when a unit staring off-screen reads as broken.
            pending[i].SetFacing(false);

            if (loadouts[i] != null) DressAfterWake(pending[i], loadouts[i]);
        }

        return count;
    }

    /// <summary>
    /// Decide how the unit fights and give it its spells — before it wakes. Order matters:
    /// EntityStats reads the first spell's <see cref="Spell.BaseDamage"/> during Awake to seed the
    /// unit's Damage stat, and CombatAI takes its attack range from the same spell, so a unit armed
    /// afterwards would wake up doing zero damage from the wrong distance.
    /// </summary>
    private void ArmBeforeWake(Entity entity, EnemyLoadout loadout)
    {
        // Monsters have no equipment rig and no bow, so they always brawl.
        bool ranged = entity.isCharacter && Random.value < loadout.rangedChance;
        entity.SetRanged(ranged);

        // Scale toughness before Awake, where Entity copies maxHealth into its Health component.
        // Skipped when a UnitData override is present, since Awake takes health from that instead
        // and would overwrite anything set here.
        if (entity.unitData == null && loadout.healthMultiplier > 0f)
            entity.maxHealth *= loadout.healthMultiplier;

        var spells = new List<Spell>();

        var basic = loadout.BasicAttackFor(ranged);
        if (basic != null) spells.Add(basic);
        else Debug.LogWarning($"[EncounterSpawner] {loadout.name} has no " +
                              (ranged ? "bow" : "melee") + " basic attack — that unit can't fight.");

        var ability = loadout.RollAbility(ranged);
        if (ability != null) spells.Add(ability);

        entity.spells = spells;
    }

    /// <summary>
    /// Roll the unit's looks and gear once it's awake. Equipment is applied through the same path and
    /// item pool the player's units use, so enemies read as part of the same world — and their stat
    /// modifiers land on the same <see cref="EntityStats"/> pipeline.
    /// </summary>
    private void DressAfterWake(Entity entity, EnemyLoadout loadout)
    {
        if (!entity.isCharacter || entity.Appearance == null) return;

        if (loadout.randomizeAppearance) entity.Appearance.SetRandomAppearance();

        if (loadout.randomizeEquipment && entity.EquipmentManagement != null)
        {
            var equipped = entity.EquipmentManagement.EquipRandomFromCollection(entity.IsRanged);

            // Gear has to reach the stat block too, or enemies look armoured but hit like civilians.
            if (entity.Stats != null && ItemCollection.Active != null)
            {
                foreach (var item in equipped)
                {
                    var itemParams = ItemCollection.Active.GetItemParams(item);
                    if (itemParams != null) entity.Stats.ApplyItemModifiers(itemParams, item.Id);
                }
            }
        }
    }
}
