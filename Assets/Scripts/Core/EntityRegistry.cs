using System;
using System.Collections.Generic;

/// <summary>
/// Central registry of all alive entities. Eliminates FindObjectsOfType calls.
/// Entities register themselves in OnEnable and unregister in OnDisable.
///
/// The lifecycle events let systems provision per-entity things (health/mana bars, VFX,
/// combat log hooks) for <i>any</i> entity that ever appears — including units summoned
/// mid-combat — instead of a one-shot loop at startup that misses them.
/// </summary>
public static class EntityRegistry
{
    private static readonly List<Entity> _entities = new List<Entity>();

    public static IReadOnlyList<Entity> All => _entities;

    /// <summary>Fired after an entity joins the registry.</summary>
    public static event Action<Entity> OnRegistered;

    /// <summary>Fired after an entity leaves the registry (death, despawn, scene teardown).</summary>
    public static event Action<Entity> OnUnregistered;

    public static void Register(Entity entity)
    {
        if (entity == null || _entities.Contains(entity)) return;
        _entities.Add(entity);
        OnRegistered?.Invoke(entity);
    }

    public static void Unregister(Entity entity)
    {
        if (entity != null && _entities.Remove(entity))
            OnUnregistered?.Invoke(entity);
    }

    /// <summary>Drop every entry. Call on scene teardown — this class is static and survives reloads.</summary>
    public static void Clear()
    {
        _entities.Clear();
    }
}
