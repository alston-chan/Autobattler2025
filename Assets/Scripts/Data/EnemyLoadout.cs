using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Rules for kitting out an enemy at spawn: whether it fights at range, what gear it rolls, and
/// which ability it might bring. One pool is shared by many spawns, so a whole encounter — or a
/// whole act — can be re-tuned from a single asset.
///
/// Randomising the *loadout* rather than the *unit* keeps enemies varied without needing a prefab
/// per combination, and it means enemies draw from the same item pool the player does, so an
/// armoured silhouette telegraphs a tougher fight (Docs/Enemies.md).
/// </summary>
/// <summary>
/// What an enemy fights with. More than melee-or-bow: a dagger user lunges and a wand user stands
/// off at six units, and a company that only ever meets swords and bows never has to answer either.
/// </summary>
public enum EnemyKind { Melee = 0, Bow = 1, Dagger = 2, Wand = 3 }

[CreateAssetMenu(menuName = "Data/Enemy Loadout", fileName = "EnemyLoadout")]
public class EnemyLoadout : ScriptableObject
{
    [System.Serializable]
    public class KindWeight
    {
        public EnemyKind kind;
        [Min(0f)] public float weight = 1f;
    }

    [Header("Kits")]
    [Tooltip("Kits to draw for spawns that name none: every kit once before any repeats, in a random " +
             "order. Empty rolls gear by kind instead.")]
    public List<EnemyKit> kits = new List<EnemyKit>();
    [Tooltip("Rolled gear resonates like a hero's: the weapon teaches its class's verb and a set piece " +
             "grants its engraving. Off, an enemy's gear is only stats.")]
    public bool resonateGear = true;

    [Header("Kinds")]
    [Tooltip("What the units of this pool fight with, drawn by weight. Empty falls back to the ranged " +
             "chance below: melee or bow. Monsters are always melee whatever this says.")]
    public List<KindWeight> kinds = new List<KindWeight>();

    /// <summary>Draw a kind by weight, or by the old ranged chance when no kinds are listed.</summary>
    public EnemyKind RollKind()
    {
        float total = 0f;
        if (kinds != null) foreach (var k in kinds) if (k != null) total += Mathf.Max(0f, k.weight);
        if (total <= 0f) return Random.value < rangedChance ? EnemyKind.Bow : EnemyKind.Melee;
        float r = Random.value * total;
        foreach (var k in kinds)
        {
            if (k == null) continue;
            r -= Mathf.Max(0f, k.weight);
            if (r <= 0f) return k.kind;
        }
        return kinds[kinds.Count - 1].kind;
    }

    /// <summary>Whether a kind fights from range: a bow or a wand. Where it musters and whether it kites.</summary>
    public static bool IsRangedKind(EnemyKind kind) => kind == EnemyKind.Bow || kind == EnemyKind.Wand;

    /// <summary>The weapon classes a kind draws its weapon from.</summary>
    public static Assets.HeroEditor.InventorySystem.Scripts.Enums.ItemClass[] WeaponClassesFor(EnemyKind kind)
    {
        switch (kind)
        {
            case EnemyKind.Bow: return new[] { Assets.HeroEditor.InventorySystem.Scripts.Enums.ItemClass.Bow };
            case EnemyKind.Dagger: return new[] { Assets.HeroEditor.InventorySystem.Scripts.Enums.ItemClass.Dagger };
            case EnemyKind.Wand: return new[] { Assets.HeroEditor.InventorySystem.Scripts.Enums.ItemClass.Wand };
            default: return new[] { Assets.HeroEditor.InventorySystem.Scripts.Enums.ItemClass.Sword, Assets.HeroEditor.InventorySystem.Scripts.Enums.ItemClass.Axe,
                                    Assets.HeroEditor.InventorySystem.Scripts.Enums.ItemClass.Blunt, Assets.HeroEditor.InventorySystem.Scripts.Enums.ItemClass.Lance };
        }
    }

    /// <summary>The pre-wake basic attack for a kind: the bow's for a bow, the melee one for the rest (the weapon rewrites it after wake).</summary>
    public Spell BasicAttackFor(EnemyKind kind) => BasicAttackFor(kind == EnemyKind.Bow);

    /// <summary>An ability the kind can use: bows take Bow ones, wands Wand ones, blades Melee ones, everyone Any.</summary>
    public Spell RollAbility(EnemyKind kind)
    {
        if (abilities == null || abilities.Count == 0) return null;
        if (Random.value > abilityChance) return null;
        var usable = new List<Spell>();
        foreach (var spell in abilities)
        {
            if (spell == null) continue;
            bool fits = spell.weaponRequirement == WeaponClass.Any
                        || (kind == EnemyKind.Bow && spell.weaponRequirement == WeaponClass.Bow)
                        || (kind == EnemyKind.Wand && spell.weaponRequirement == WeaponClass.Wand)
                        || ((kind == EnemyKind.Melee || kind == EnemyKind.Dagger) && spell.weaponRequirement == WeaponClass.Melee);
            if (fits) usable.Add(spell);
        }
        return usable.Count > 0 ? usable[Random.Range(0, usable.Count)] : null;
    }

    [Header("Basic attacks")]
    [Tooltip("Weapon basic attack given to melee units. This is also what sets their base damage " +
             "and reach, so it must be present or the unit can't fight.")]
    public Spell meleeBasicAttack;
    [Tooltip("Weapon basic attack given to units that roll ranged.")]
    public Spell bowBasicAttack;

    [Header("Toughness")]
    [Tooltip("Multiplies the prefab's max health. The main dial for making later acts survive long " +
             "enough to be a fight — enemy prefabs are shared with the player's units, so their own " +
             "health can't be raised without buffing the company too.")]
    public float healthMultiplier = 1f;

    [Header("Ranged mix")]
    [Range(0f, 1f)]
    [Tooltip("Chance a humanoid rolls ranged (bow) instead of melee. Monsters are always melee.")]
    public float rangedChance = 0.35f;

    [Header("Abilities")]
    [Range(0f, 1f)]
    [Tooltip("Chance the unit also brings an ultimate from the pool below. 1 = every unit does, " +
             "which is the point: an enemy with nothing but a basic attack is one the player never " +
             "has to read. Lower it to make a pool feel like conscripts.")]
    public float abilityChance = 1f;
    [Tooltip("Ultimates to draw from. Ones whose weapon requirement doesn't match what the unit " +
             "ended up holding are skipped, so a bow user never rolls a melee-only ability.")]
    public List<Spell> abilities = new List<Spell>();

    [Header("Appearance")]
    public bool randomizeAppearance = true;
    [Tooltip("Roll armour, helmet, gloves, boots and a weapon from the shared item collection.")]
    public bool randomizeEquipment = true;

    /// <summary>The basic attack matching how this unit fights.</summary>
    public Spell BasicAttackFor(bool ranged) => ranged ? bowBasicAttack : meleeBasicAttack;

    /// <summary>
    /// Roll one ability the unit will be able to cast, or null. Filtered against the weapon the unit
    /// is *about* to carry rather than <see cref="Spell.MeetsWeaponRequirement"/>, because abilities
    /// are chosen before the unit wakes and equips — at that point it is still empty-handed, so
    /// asking the rig what it holds would answer "nothing" and reject every weapon-gated ability.
    /// </summary>
    public Spell RollAbility(bool ranged)
    {
        if (abilities == null || abilities.Count == 0) return null;
        if (Random.value > abilityChance) return null;

        var usable = new List<Spell>();
        foreach (var spell in abilities)
        {
            if (spell == null) continue;

            bool fits = spell.weaponRequirement == WeaponClass.Any
                        || (ranged && spell.weaponRequirement == WeaponClass.Bow)
                        || (!ranged && spell.weaponRequirement == WeaponClass.Melee);
            if (fits) usable.Add(spell);
        }

        if (usable.Count == 0)
        {
            // Silent here means the unit walks out with a basic attack and nothing else, which looks
            // exactly like a deliberately plain enemy. Say it instead: the pool needs an entry this
            // unit's weapon can satisfy, or an 'Any' one to cover both branches.
            Debug.LogWarning("[EnemyLoadout] " + name + " has no ability a " +
                             (ranged ? "bow" : "melee") + " unit can use — that unit spawns with " +
                             "only a basic attack. Add one requiring " +
                             (ranged ? "Bow" : "Melee") + " or Any.");
            return null;
        }

        return usable[Random.Range(0, usable.Count)];
    }
}
