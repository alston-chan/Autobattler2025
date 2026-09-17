using System.Collections.Generic;
using Assets.HeroEditor.InventorySystem.Scripts;
using Assets.HeroEditor.InventorySystem.Scripts.Data;
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

    /// <summary>
    /// Spawn the encounter's enemies. Returns how many were placed. <paramref name="loadoutOverride"/>
    /// is the toughness the map wants this fight at; a spawn that names its own loadout keeps it,
    /// since that is a designed unit rather than a tier.
    /// </summary>
    public int Spawn(EncounterData encounter, EnemyLoadout loadoutOverride = null)
    {
        if (encounter == null) return 0;

        // Spawns are built under a deactivated holder so their Awake is deferred: Entity.Awake reads
        // unitData to set up health and stats, so the override has to be in place before it runs.
        var holder = new GameObject("EncounterSpawnHolder");
        holder.SetActive(false);

        int count = 0;
        var pending = new List<Entity>();
        var loadouts = new List<EnemyLoadout>();
        var kinds = new List<EnemyKind>();
        var kits = new List<EnemyKit>();
        var authored = new List<EncounterData.Spawn>();

        // Who wears what: the scenario's draw first, then the spawn's own kit, then the loadout's
        // pool, then a random roll. Drawn once per encounter so a pool spreads across the spawns.
        int spawnCount = 0; foreach (var s in encounter.spawns) if (s != null && s.prefab != null) spawnCount++;
        var scenarioKits = Playtest.Scenario != null ? Playtest.Scenario.EnemyKitsFor(spawnCount) : null;
        var poolDraws = new Dictionary<EnemyLoadout, Queue<EnemyKit>>();

        foreach (var spawn in encounter.spawns)
        {
            if (spawn == null || spawn.prefab == null) continue;

            var go = Instantiate(spawn.prefab, holder.transform);
            go.transform.position = CellPosition(spawn);
            _spawned.Add(go);
            count++;

            var entity = go.GetComponent<Entity>();
            if (entity == null) continue;
            authored.Add(spawn);

            entity.isTeam = false;
            if (spawn.unitData != null) entity.unitData = spawn.unitData;

            var loadout = spawn.loadout != null ? spawn.loadout
                        : loadoutOverride != null ? loadoutOverride
                        : encounter.defaultLoadout;
            // A kit decides the kind (from its weapon) and brings no rolled ability.
            EnemyKit kit = scenarioKits != null && pending.Count < scenarioKits.Count ? scenarioKits[pending.Count] : null;
            if (kit == null) kit = spawn.kit;
            if (kit == null && loadout != null && loadout.kits != null && loadout.kits.Count > 0)
            {
                if (!poolDraws.TryGetValue(loadout, out var queue)) { queue = new Queue<EnemyKit>(EnemyKit.Draw(loadout.kits, spawnCount)); poolDraws[loadout] = queue; }
                if (queue.Count > 0) kit = queue.Dequeue();
            }
            var kind = loadout != null ? ArmBeforeWake(entity, loadout, kit != null ? KindOfKit(kit) : (EnemyKind?)null, kit == null) : EnemyKind.Melee;

            pending.Add(entity);
            loadouts.Add(loadout);
            kinds.Add(kind);
            kits.Add(kit);
        }

        // Now that each knows how it fights, muster: archers to the rear of their lane, brawlers to
        // the front (EnemyMuster). The authored cell is the starting point, not the answer.
        Muster(pending, authored, kinds);

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

            if (loadouts[i] != null) DressAfterWake(pending[i], loadouts[i], kinds[i], kits[i]);
        }

        return count;
    }

    /// <summary>Stand each spawn in the cell its way of fighting earns it. See <see cref="EnemyMuster"/>.</summary>
    private void Muster(List<Entity> entities, List<EncounterData.Spawn> authored, List<EnemyKind> kinds)
    {
        var grid = BattleGrid.Instance;
        if (grid == null || entities.Count != authored.Count) return;

        // By kind rather than the ranged flag: a wand user stands off like an archer without ever
        // aiming a bow arm, and the weapon that says so is not equipped until after wake.
        var units = new List<EnemyMuster.Unit>(entities.Count);
        for (int i = 0; i < entities.Count; i++)
            units.Add(new EnemyMuster.Unit(EnemyLoadout.IsRangedKind(kinds[i]), authored[i].column, authored[i].row));

        var cells = EnemyMuster.Assign(units, grid.columns, grid.rows);
        for (int i = 0; i < entities.Count; i++)
            entities[i].transform.position = grid.CellToWorld(false, cells[i].column, cells[i].row);
    }

    /// <summary>
    /// Where a spawn stands. Enemies deploy onto their half of the <see cref="BattleGrid"/> so both
    /// sides occupy the same lanes — which is what makes "the unit opposite" a meaningful target.
    /// Falls back to a spread along the right if no grid exists, so a scene without one still works.
    /// </summary>
    private Vector3 CellPosition(EncounterData.Spawn spawn)
    {
        var grid = BattleGrid.Instance;
        if (grid != null) return grid.CellToWorld(false, spawn.column, spawn.row);

        return new Vector3(3f + spawn.column * 1.6f, -3f + spawn.row * 1.2f, 0f);
    }

    /// <summary>
    /// Decide how the unit fights and give it its spells — before it wakes. Order matters:
    /// EntityStats reads the first spell's <see cref="Spell.BaseDamage"/> during Awake to seed the
    /// unit's Damage stat, and CombatAI takes its attack range from the same spell, so a unit armed
    /// afterwards would wake up doing zero damage from the wrong distance.
    /// </summary>
    /// <summary>The kind a kit's weapon makes its wearer: what musters it and what it kites with.</summary>
    private static EnemyKind KindOfKit(EnemyKit kit)
    {
        if (kit == null || kit.itemIds == null || ItemCollection.Active == null) return EnemyKind.Melee;
        foreach (var id in kit.itemIds)
        {
            var p = ItemCollection.Active.Items.Find(i => i.Id == id);
            if (p == null || p.Type != Assets.HeroEditor.InventorySystem.Scripts.Enums.ItemType.Weapon) continue;
            switch (p.Class)
            {
                case Assets.HeroEditor.InventorySystem.Scripts.Enums.ItemClass.Bow: return EnemyKind.Bow;
                case Assets.HeroEditor.InventorySystem.Scripts.Enums.ItemClass.Wand: return EnemyKind.Wand;
                case Assets.HeroEditor.InventorySystem.Scripts.Enums.ItemClass.Dagger: return EnemyKind.Dagger;
                default: return EnemyKind.Melee;
            }
        }
        return EnemyKind.Melee;
    }

    private EnemyKind ArmBeforeWake(Entity entity, EnemyLoadout loadout, EnemyKind? kindOverride = null, bool rollAbility = true)
    {
        // Monsters have no equipment rig and no bow, so they always brawl.
        var kind = !entity.isCharacter ? EnemyKind.Melee : kindOverride ?? loadout.RollKind();
        bool ranged = kind == EnemyKind.Bow;   // the flag also aims a bow arm, which only a bow has
        entity.SetRanged(ranged);

        // Scale toughness before Awake, where Entity copies maxHealth into its Health component.
        // Skipped when a UnitData override is present, since Awake takes health from that instead
        // and would overwrite anything set here.
        if (entity.unitData == null && loadout.healthMultiplier > 0f)
            entity.maxHealth *= loadout.healthMultiplier;

        var spells = new List<Spell>();

        var basic = loadout.BasicAttackFor(kind);
        if (basic != null) spells.Add(basic);
        else Debug.LogWarning($"[EncounterSpawner] {loadout.name} has no " +
                              (ranged ? "bow" : "melee") + " basic attack — that unit can't fight.");

        var ability = rollAbility ? loadout.RollAbility(kind) : null;
        if (ability != null) spells.Add(ability);

        entity.spells = spells;
        return kind;
    }

    /// <summary>
    /// Roll the unit's looks and gear once it's awake. Equipment is applied through the same path and
    /// item pool the player's units use, so enemies read as part of the same world — and their stat
    /// modifiers land on the same <see cref="EntityStats"/> pipeline.
    /// </summary>
    private void DressAfterWake(Entity entity, EnemyLoadout loadout, EnemyKind kind, EnemyKit kit)
    {
        if (!entity.isCharacter || entity.Appearance == null) return;

        if (loadout.randomizeAppearance) entity.Appearance.SetRandomAppearance();
        if (entity.EquipmentManagement == null) return;

        List<Item> worn;
        if (kit != null)
        {
            // A kit: worn like a hero's, standing and aiming as the kit says.
            worn = entity.EquipmentManagement.EquipKit(kit.itemIds);
            entity.stance = kit.stance;
            entity.targetMode = kit.targetMode;
        }
        else if (loadout.randomizeEquipment)
        {
            // The kind names the weapon class; the weapon then chooses the attack (Loadout.ApplyTo).
            worn = entity.EquipmentManagement.EquipRandomFromCollection(entity.IsRanged, EnemyLoadout.WeaponClassesFor(kind));
        }
        else return;

        // Gear has to reach the stat block too, or enemies look armoured but hit like civilians.
        if (entity.Stats != null && ItemCollection.Active != null)
            foreach (var item in worn)
            {
                var itemParams = ItemCollection.Active.GetItemParams(item);
                if (itemParams != null) entity.Stats.ApplyItemModifiers(itemParams, item.Id);
            }

        // And it resonates: the weapon's verb, the set's engravings. Every enemy archer whips, every
        // mace throws Cannonballs, and an enemy in the Ninja set substitutes. A kit always resonates;
        // rolled gear does when the loadout says so.
        if ((kit != null || loadout.resonateGear) && entity.Resonance != null)
        {
            entity.Resonance.SetWorn(worn);
            entity.Resonance.Refresh();
        }
        if (kit != null && entity.spellSlots != null && entity.spellSlots.Count > 0)
            entity.activeSpellSlot = Mathf.Clamp(kit.activeSlot, 0, entity.spellSlots.Count - 1);
        if (entity.CombatAI != null) entity.CombatAI.RefreshSpells();
    }
}
