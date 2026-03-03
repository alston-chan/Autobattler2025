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

    // ── Inspector readout (read-only at runtime) ──
    [Header("Live Stats (read-only)")]
    [SerializeField] private float _damage;
    [SerializeField] private float _maxHealth;
    [SerializeField] private float _speed;
    [SerializeField] private float _blocking;

    /// <summary>Fired after any modifier is added or removed so UI can refresh.</summary>
    public event Action OnStatsChanged;

    private Entity _entity;

    public void Initialize(Entity entity)
    {
        _entity = entity;

        // Base values come from Entity (which may have been populated from UnitData)
        float baseDamage = 0f;
        if (_entity.spells != null && _entity.spells.Count > 0)
        {
            // Use the first attack spell's damage as the base
            if (_entity.spells[0] is MeleeAttackSpell melee) baseDamage = melee.damage;
            else if (_entity.spells[0] is BowAttackSpell bow) baseDamage = bow.damage;
            else if (_entity.spells[0] is ShockwaveSpell shock) baseDamage = shock.damage;
        }

        Damage = new CharacterStat(baseDamage);
        MaxHealth = new CharacterStat(_entity.maxHealth);
        Speed = new CharacterStat(_entity.unitData != null ? _entity.unitData.moveSpeed : 3f);
        Blocking = new CharacterStat(0f);

        RefreshInspector();
    }

    /// <summary>
    /// Read an item's <see cref="PropertyId"/> properties and add matching
    /// <see cref="StatModifier"/>s, using the item's Id as the source.
    /// </summary>
    public void ApplyItemModifiers(Assets.HeroEditor.InventorySystem.Scripts.Data.ItemParams itemParams, object source)
    {
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
            }
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

        RefreshInspector();
        OnStatsChanged?.Invoke();
    }

    private void RefreshInspector()
    {
        _damage = Damage?.Value ?? 0f;
        _maxHealth = MaxHealth?.Value ?? 0f;
        _speed = Speed?.Value ?? 0f;
        _blocking = Blocking?.Value ?? 0f;
    }

    /// <summary>
    /// Build a dictionary of stat names → current values for UI display.
    /// </summary>
    public Dictionary<string, float> GetDisplayStats()
    {
        return new Dictionary<string, float>
        {
            { "Damage",     Damage.Value },
            { "Max Health", MaxHealth.Value },
            { "Speed",      Speed.Value },
            { "Blocking",   Blocking.Value },
        };
    }
}
