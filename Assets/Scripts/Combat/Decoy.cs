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
        => Spawn(owner, at, health, seconds, null, sprite, name);

    /// <summary>
    /// As above, from a prefab — the scarecrow — with a sprite as the fallback when there is none.
    /// A prefab body that carries the test-room Monster springs when it is hit.
    /// </summary>
    public static Entity Spawn(Entity owner, Vector3 at, float health, float seconds, GameObject prefab, Sprite sprite, string name = "Decoy")
    {
        if (owner == null) return null;
        GameObject go;
        if (prefab != null)
        {
            go = Object.Instantiate(prefab, at, Quaternion.identity);
            go.name = name;
            // Face the way the owner did, so it stands in for them.
            var s = go.transform.localScale;
            s.x = Mathf.Abs(s.x) * (owner.transform.localScale.x >= 0f ? 1f : -1f);
            go.transform.localScale = s;
            go.AddComponent<DecoyBody>();
        }
        else
        {
            go = new GameObject(name);
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

/// <summary>A decoy body's reactions: the scarecrow's spring when it is hit, and a fall when it dies.</summary>
public class DecoyBody : MonoBehaviour
{
    private Assets.HeroEditor.FantasyHeroes.TestRoom.Scripts.Monster _body;
    private Entity _entity;

    private void Start()
    {
        _body = GetComponent<Assets.HeroEditor.FantasyHeroes.TestRoom.Scripts.Monster>();
        _entity = GetComponent<Entity>();
        if (_entity != null && _entity.Health != null)
        {
            _entity.Health.OnDamaged += OnDamaged;
            _entity.Health.OnDied += OnDied;
        }
    }

    private void OnDestroy()
    {
        if (_entity != null && _entity.Health != null)
        {
            _entity.Health.OnDamaged -= OnDamaged;
            _entity.Health.OnDied -= OnDied;
        }
    }

    private void OnDamaged(DamageInfo hit) { if (_body != null) _body.Spring(); }
    private void OnDied() { if (_body != null) _body.Die(); }
}
