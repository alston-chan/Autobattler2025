using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Provisions the floating health and mana bars for every entity, driven by
/// <see cref="EntityRegistry"/> lifecycle events.
///
/// Why bars are NOT children of the entity prefab: <c>CombatAI.FaceTarget()</c> turns a unit by
/// negating <c>localScale.x</c>, so a parented bar would mirror — draining right-to-left — every
/// time it changed facing. Bars therefore live under a neutral parent and follow in LateUpdate.
///
/// Because provisioning is event-driven, anything that ever appears gets bars: units placed in the
/// scene, minions summoned mid-fight, revives. New prefabs need no setup beyond an
/// <see cref="Entity"/> component.
/// </summary>
public class UnitBarsManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject resourceBarPrefab;
    [SerializeField] private Transform barsParent;

    [Header("Placement")]
    [Tooltip("Above the head (traditional) or below the feet (Cookie Run style).\n\n" +
             "Below keeps tall headgear visible — equipment is the game — and reserves the space " +
             "above the character for rising damage numbers.")]
    [SerializeField] private BarPlacement placement = BarPlacement.BelowFeet;
    [Tooltip("Offset used when placement is BelowFeet. Overrides Entity.healthBarOffset.")]
    [SerializeField] private Vector3 belowOffset = new Vector3(0f, -0.35f, 1f);

    public enum BarPlacement { AboveHead, BelowFeet }

    [Header("Health bar")]
    [SerializeField] private Color allyColor = Color.green;
    [SerializeField] private Color enemyColor = Color.red;

    [Header("Mana bar")]
    [Tooltip("TODO: gate this on the unit actually having a mana-costing ability. Until ults exist, " +
             "every unit shows a permanently-empty second bar, which is pure visual noise.")]
    [SerializeField] private bool showManaBar = true;
    [Tooltip("Slimmer than the health bar, so it reads as secondary.")]
    [SerializeField] private float manaBarHeightScale = 0.6f;
    [SerializeField] private Color manaBarColor = new Color(0.29f, 0.56f, 1f, 1f);
    [Tooltip("Extra space between the two bars. 0 = flush/touching. Negative overlaps them.")]
    [SerializeField] private float barGap = 0f;

    [Header("Effects")]
    [Tooltip("Bar juice lives on the shared Resources/CombatFeelSettings asset so it can be tuned in " +
             "one place — and, because it's a ScriptableObject, edits made during Play mode persist.\n\n" +
             "Tick this to ignore the asset and use per-scene values instead.")]
    [SerializeField] private bool overrideGlobalEffects = false;
    [SerializeField] private ResourceBar.BarEffects localHealthEffects = new ResourceBar.BarEffects();
    [SerializeField] private ResourceBar.BarEffects localManaEffects = new ResourceBar.BarEffects
    {
        chipColor = new Color(0.65f, 0.85f, 1f, 1f),
        hitFlash = false,
        shake = false,
    };

    // Live references — bars hold the same object, so edits to the asset apply to bars already spawned.
    private ResourceBar.BarEffects HealthEffects =>
        overrideGlobalEffects ? localHealthEffects : CombatFeelSettings.Active.healthBar;
    private ResourceBar.BarEffects ManaEffects =>
        overrideGlobalEffects ? localManaEffects : CombatFeelSettings.Active.manaBar;

    private readonly Dictionary<Entity, List<ResourceBar>> _bars = new Dictionary<Entity, List<ResourceBar>>();
    private bool _configured;

    /// <summary>
    /// Bootstrap hook — lets <see cref="GameManager"/> hand over the scene references it already
    /// holds, so no manual wiring is needed when this component is added at runtime.
    /// </summary>
    public void Configure(GameObject barPrefab, Transform parent)
    {
        if (barPrefab != null) resourceBarPrefab = barPrefab;
        if (parent != null) barsParent = parent;
        _configured = true;
        SweepExisting();
    }

    private void OnEnable()
    {
        EntityRegistry.OnRegistered += Provision;
        EntityRegistry.OnUnregistered += Despawn;
        if (_configured) SweepExisting();
    }

    private void OnDisable()
    {
        // EntityRegistry is static — failing to unsubscribe would leak this object across scene loads.
        EntityRegistry.OnRegistered -= Provision;
        EntityRegistry.OnUnregistered -= Despawn;
    }

    /// <summary>Catch entities that registered before this manager woke up.</summary>
    private void SweepExisting()
    {
        var existing = new List<Entity>(EntityRegistry.All);
        foreach (var e in existing) Provision(e);
    }

    private void Provision(Entity entity)
    {
        if (entity == null || resourceBarPrefab == null) return;
        if (_bars.ContainsKey(entity)) return;                 // already has bars

        var created = new List<ResourceBar>(2);

        bool below = placement == BarPlacement.BelowFeet;
        Vector3 healthOffset = below ? belowOffset : entity.healthBarOffset;

        // ── Health ──
        ResourceBar health = Spawn(entity, healthOffset, Vector3.one,
                                   entity.isTeam ? allyColor : enemyColor, HealthEffects);
        if (health != null)
        {
            entity.healthBar = health;
            if (entity.Health != null && entity.Health.maxHealth > 0f)
                health.SetSize(entity.Health.currentHealth / entity.Health.maxHealth);
            created.Add(health);
        }

        // ── Mana ──
        if (showManaBar && entity.Mana != null)
        {
            ResourceBar mana = Spawn(entity, healthOffset,
                                     new Vector3(1f, manaBarHeightScale, 1f), manaBarColor,
                                     ManaEffects);
            if (mana != null)
            {
                // Stack flush against the health bar using real sprite heights, so the two touch
                // regardless of prefab art or the mana bar's height scale.
                float step = 0f;
                if (health != null) step = (health.VisualHeight + mana.VisualHeight) * 0.5f + barGap;
                // Health always sits closest to the character; mana stacks outward from it.
                mana.offset = healthOffset + Vector3.up * (below ? -step : step);

                entity.Mana.manaBar = mana;
                mana.SetSize(entity.Mana.Normalized);
                created.Add(mana);
            }
        }

        _bars[entity] = created;
    }

    private ResourceBar Spawn(Entity entity, Vector3 offset, Vector3 scaleMul, Color color,
                              ResourceBar.BarEffects effects)
    {
        GameObject go = Instantiate(resourceBarPrefab, barsParent);
        var bar = go.GetComponent<ResourceBar>();
        if (bar == null) { Destroy(go); return null; }

        go.transform.localScale = Vector3.Scale(entity.transform.localScale, scaleMul);
        bar.entity = entity;
        bar.offset = offset;
        bar.Apply(effects);     // settings must land before the first SetSize
        bar.SetColor(color);
        return bar;
    }

    private void Despawn(Entity entity)
    {
        if (entity == null || !_bars.TryGetValue(entity, out var list)) return;
        foreach (var bar in list)
            if (bar != null) Destroy(bar.gameObject);
        _bars.Remove(entity);
    }
}
