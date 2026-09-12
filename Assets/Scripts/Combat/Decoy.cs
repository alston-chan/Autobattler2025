using UnityEngine;

/// <summary>
/// A decoy: a stand-in body that enemies fight instead of the unit that left it. A real
/// <see cref="Entity"/> with health on the owner's side, rooted so it never walks, taunting every
/// enemy that was on the owner so the lock turns to it, and gone when its time or its health
/// runs out. Made in code from a sprite because the only thing that varies is the sprite.
/// </summary>
public static class Decoy
{
    /// <summary>
    /// Stand a decoy where the owner is. Enemies currently targeting the owner are Taunted onto it
    /// for <paramref name="seconds"/>; it lasts that long or until killed.
    /// </summary>
    public static Entity Spawn(Entity owner, Vector3 at, float health, float seconds, Sprite sprite, string name = "Decoy")
    {
        if (owner == null) return null;
        var go = new GameObject(name);
        go.transform.position = at;

        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = 50;
        if (sprite != null)
        {
            // A supply icon is small; stand it up to roughly a body's height.
            float h = sprite.bounds.size.y;
            float scale = h > 0.01f ? Mathf.Clamp(1.6f / h, 0.3f, 6f) : 1f;
            go.transform.localScale = Vector3.one * scale;
        }

        var entity = go.AddComponent<Entity>();     // Awake adds Health, CombatAI, Statuses… and initialises them
        entity.isTeam = owner.isTeam;
        entity.isCharacter = false;
        entity.maxHealth = health;
        if (entity.Health != null)
        {
            entity.Health.maxHealth = health;
            entity.Health.currentHealth = health;
            entity.Health.healthBarOffset = new Vector3(0f, 1.9f, 0f);
        }
        entity.SetFighting(true);

        var rooted = StatusLibrary.Rooted;
        if (rooted != null && entity.Statuses != null) entity.Statuses.Apply(rooted, 0f);

        var taunt = StatusLibrary.Taunted;
        if (taunt != null)
            foreach (var e in EntityRegistry.All)
                if (e != null && !e.isDead && e.isTeam != owner.isTeam && e.Statuses != null && e.CombatAI != null && e.CombatAI.CurrentTarget == owner)
                    e.Statuses.Apply(taunt, seconds, entity);

        Object.Destroy(go, seconds);
        return entity;
    }
}
