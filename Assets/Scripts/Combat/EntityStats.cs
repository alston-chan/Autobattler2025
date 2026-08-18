using System;
using System.Collections.Generic;
using Kryz.CharacterStats;
using UnityEngine;

/// <summary>
/// Holds all <see cref="CharacterStat"/> instances for an entity.
/// Equipment modifiers are applied/removed through this component.
/// Spells and other systems read <c>.Value</c> to get the final modified stat.
/// </summary>
public class EntityStats : MonoBehaviour
{
    // ── Core stats ──
    public CharacterStat Damage { get; private set; }
    public CharacterStat MaxHealth { get; private set; }
    public CharacterStat Speed { get; private set; }
    public CharacterStat Blocking { get; private set; }
    public CharacterStat AttackSpeed { get; private set; }

    // ── Inspector readout (read-only at runtime) ──
    [Header("Live Stats (read-only)")]
    [SerializeField] private float _damage;
    [SerializeField] private float _maxHealth;
    [SerializeField] private float _speed;
    [SerializeField] private float _blocking;
    [SerializeField] private float _attackSpeed;

    /// <summary>Fired after any modifier is added or removed so UI can refresh.</summary>
    public event Action OnStatsChanged;

    private Entity _entity;

    public void Initialize(Entity entity)
    {
        _entity = entity;

        // Base values come from Entity (which may have been populated from UnitData)
        // Use the first (weapon basic attack) spell's damage as the base. Each Spell reports its own
        // via Spell.BaseDamage, so adding a spell type never means editing this file.
        float baseDamage = 0f;
        if (_entity.spells != null && _entity.spells.Count > 0 && _entity.spells[0] != null)
            baseDamage = _entity.spells[0].BaseDamage;

        Damage = new CharacterStat(baseDamage);
        MaxHealth = new CharacterStat(_entity.maxHealth);
        Speed = new CharacterStat(_entity.unitData != null ? _entity.unitData.moveSpeed : 3f);
        Blocking = new CharacterStat(0f);
        AttackSpeed = new CharacterStat(_entity.attackSpeed);

        RefreshInspector();
    }

    /// <summary>
    /// Read an item's <see cref="PropertyId"/> properties and add matching
    /// <see cref="StatModifier"/>s, using the item's Id as the source.
    /// </summary>
    public void ApplyItemModifiers(Assets.HeroEditor.InventorySystem.Scripts.Data.ItemParams itemParams, object source)
    {
        bool authoredSpeed = false;

        foreach (var prop in itemParams.Properties)
        {
            if (!float.TryParse(prop.Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float val))
                continue;

            switch (prop.Id)
            {
                case Assets.HeroEditor.InventorySystem.Scripts.Enums.PropertyId.Damage:
                    Damage.AddModifier(new StatModifier(val, StatModType.Flat, source));
                    break;
                case Assets.HeroEditor.InventorySystem.Scripts.Enums.PropertyId.HealthMax:
                    MaxHealth.AddModifier(new StatModifier(val, StatModType.Flat, source));
                    break;
                case Assets.HeroEditor.InventorySystem.Scripts.Enums.PropertyId.Speed:
                    Speed.AddModifier(new StatModifier(val, StatModType.Flat, source));
                    break;
                case Assets.HeroEditor.InventorySystem.Scripts.Enums.PropertyId.Blocking:
                    Blocking.AddModifier(new StatModifier(val, StatModType.Flat, source));
                    break;

                // A weapon's own handling speed, authored as a fraction (+0.2 = 20% faster). Percent
                // rather than flat so it compounds with engravings like Swift instead of racing them.
                case Assets.HeroEditor.InventorySystem.Scripts.Enums.PropertyId.ChargeSpeed:
                    authoredSpeed = true;
                    AttackSpeed.AddModifier(new StatModifier(val, StatModType.PercentAdd, source));
                    break;
            }
        }

        // The vendor catalogue authors nothing but Damage on weapons, so without a fallback every
        // weapon would swing at exactly the same rate and equipping one would move no visible number.
        // An item that does author ChargeSpeed keeps its own value — the table is only the default.
        if (!authoredSpeed &&
            itemParams.Type == Assets.HeroEditor.InventorySystem.Scripts.Enums.ItemType.Weapon)
        {
            float handling = WeaponSpeeds.HandlingFor(itemParams.Class);
            if (handling != 0f)
                AttackSpeed.AddModifier(new StatModifier(handling, StatModType.PercentAdd, source));
        }

        RefreshInspector();
        OnStatsChanged?.Invoke();
    }

    /// <summary>
    /// Remove every modifier that was applied by <paramref name="source"/>.
    /// </summary>
    public void RemoveItemModifiers(object source)
    {
        Damage.RemoveAllModifiersFromSource(source);
        MaxHealth.RemoveAllModifiersFromSource(source);
        Speed.RemoveAllModifiersFromSource(source);
        Blocking.RemoveAllModifiersFromSource(source);
        AttackSpeed.RemoveAllModifiersFromSource(source);

        RefreshInspector();
        OnStatsChanged?.Invoke();
    }

    private void RefreshInspector()
    {
        _damage = Damage?.Value ?? 0f;
        _maxHealth = MaxHealth?.Value ?? 0f;
        _speed = Speed?.Value ?? 0f;
        _blocking = Blocking?.Value ?? 0f;
        _attackSpeed = AttackSpeed?.Value ?? 0f;
    }

    /// <summary>
    /// Build a dictionary of stat names → current values for UI display.
    /// </summary>
    public Dictionary<string, float> GetDisplayStats()
    {
        return new Dictionary<string, float>
        {
            { "Damage",       Damage.Value },
            { "Max Health",   MaxHealth.Value },
            { "Speed",        Speed.Value },
            { "Blocking",     Blocking.Value },
            { "Attack Speed", AttackSpeed.Value },
        };
    }
}
