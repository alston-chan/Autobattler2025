using System.Collections.Generic;

/// <summary>
/// Central registry of all alive entities. Eliminates FindObjectsOfType calls.
/// Entities register themselves in OnEnable and unregister in OnDisable.
/// </summary>
public static class EntityRegistry
{
    private static readonly List<Entity> _entities = new List<Entity>();

    public static IReadOnlyList<Entity> All => _entities;

    public static void Register(Entity entity)
    {
        if (!_entities.Contains(entity))
            _entities.Add(entity);
    }

    public static void Unregister(Entity entity)
    {
        _entities.Remove(entity);
    }

    public static void Clear()
    {
        _entities.Clear();
    }
}
