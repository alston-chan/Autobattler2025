using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The particle helpers the verbs and the physics share. A prefab spawned in the world, sorted in
/// front of the bodies, and destroyed after its life. Cartoon FX prefabs are 3D particles authored
/// at sorting order 0, which puts them behind the sprites; every spawn here is lifted above them.
/// </summary>
public static class Fx
{
    public const int SortingOrder = 150;

    /// <summary>Spawn a prefab at a point, facing the camera, sorted over the units. Null-safe.</summary>
    public static GameObject Spawn(GameObject prefab, Vector3 at, float scale = 1f, float lifetime = 4f, Transform parent = null)
    {
        if (prefab == null) return null;
        var go = UnityEngine.Object.Instantiate(prefab, new Vector3(at.x, at.y, -1f), Quaternion.identity, parent);
        if (Mathf.Abs(scale - 1f) > 0.001f) go.transform.localScale *= scale;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.sortingOrder = SortingOrder;
        if (lifetime > 0f) UnityEngine.Object.Destroy(go, lifetime);
        return go;
    }
}

/// <summary>A particle prefab at the caster, the target, or every target: the look of a verb landing.</summary>
[Serializable]
public class SpawnEffect : SpellEffect
{
    public GameObject prefab;
    public EffectScope scope = EffectScope.PrimaryTarget;
    public float scale = 1f;
    [Tooltip("Seconds before the spawned object is destroyed.")] public float lifetime = 4f;

    public override IEnumerator Run(SpellContext ctx)
    {
        if (prefab == null) yield break;
        if (scope == EffectScope.Caster) { if (ctx.caster != null) Fx.Spawn(prefab, ctx.caster.transform.position, scale, lifetime); }
        else if (scope == EffectScope.EveryTarget) { foreach (var t in ctx.targets) if (t != null) Fx.Spawn(prefab, t.transform.position, scale, lifetime); }
        else if (ctx.target != null) Fx.Spawn(prefab, ctx.target.transform.position, scale, lifetime);
        yield break;
    }

    public override string Describe() => "";
}

/// <summary>A particle prefab carried by the caster for a moment: the dust of a charge, the glow of a cast.</summary>
[Serializable]
public class AttachEffect : SpellEffect
{
    public GameObject prefab;
    public float seconds = 0.6f;
    public float scale = 1f;

    public override IEnumerator Run(SpellContext ctx)
    {
        if (prefab == null || ctx.caster == null) yield break;
        var go = Fx.Spawn(prefab, ctx.caster.transform.position, scale, seconds, ctx.caster.transform);
        if (go != null) go.transform.localPosition = new Vector3(0f, 0f, -1f);
        yield break;
    }

    public override string Describe() => "";
}

/// <summary>A line from the caster to the target for a moment: the chain of a Chain Whip, the beam of a pull.</summary>
[Serializable]
public class ChainEffect : SpellEffect
{
    public Color color = new Color(1f, 0.85f, 0.4f, 1f);
    public float width = 0.12f;
    public float seconds = 0.35f;
    [Tooltip("The line follows the target as it is dragged.")] public bool follow = true;

    public override IEnumerator Run(SpellContext ctx)
    {
        if (ctx.caster == null || ctx.target == null) yield break;
        var go = new GameObject("Chain");
        var line = go.AddComponent<LineRenderer>();
        line.positionCount = 2;
        line.startWidth = line.endWidth = width;
        line.material = new Material(Shader.Find("Sprites/Default"));
        line.startColor = line.endColor = color;
        line.sortingOrder = Fx.SortingOrder;
        line.useWorldSpace = true;
        ChainRunner.Run(go, line, ctx.caster, ctx.target, seconds, follow);
        yield break;
    }

    public override string Describe() => "";
}

/// <summary>Keeps a chain drawn between two units for its life, then removes it. Made by <see cref="ChainEffect"/>.</summary>
public class ChainRunner : MonoBehaviour
{
    private LineRenderer _line; private Entity _from, _to; private float _until; private bool _follow;

    public static void Run(GameObject host, LineRenderer line, Entity from, Entity to, float seconds, bool follow)
    {
        var r = host.AddComponent<ChainRunner>();
        r._line = line; r._from = from; r._to = to; r._until = Time.time + seconds; r._follow = follow;
        r.Draw();
    }

    private void Update()
    {
        if (Time.time >= _until || _from == null || _to == null) { Destroy(gameObject); return; }
        if (_follow) Draw();
    }

    private void Draw()
    {
        Vector3 a = _from.transform.position + Vector3.up * 0.8f; a.z = -1f;
        Vector3 b = _to.transform.position + Vector3.up * 0.8f; b.z = -1f;
        _line.SetPosition(0, a); _line.SetPosition(1, b);
    }
}

/// <summary>
/// The look of the physics: a burst where a thrown body hits another and a slam where one hits the
/// wall, so a shove reads as a hit and not as a pinball. Listens to the bus; one per scene, made by
/// <see cref="CombatPhysics"/>. Prefabs are set from the feel settings.
/// </summary>
public class ImpactFx : MonoBehaviour
{
    [Serializable]
    public class Settings
    {
        [Tooltip("Spawned where a thrown body hits another body.")] public GameObject bodyHit;
        [Tooltip("Spawned where a thrown body slams into the wall.")] public GameObject wallSlam;
        public float scale = 1f;
    }

    private void OnEnable() { CombatEvents.Impact += OnImpact; }
    private void OnDisable() { CombatEvents.Impact -= OnImpact; }

    private void OnImpact(ImpactInfo impact)
    {
        var s = CombatFeelSettings.Active.impactFx;
        if (s == null || impact.mover == null) return;
        if (impact.struck != null)
        {
            Vector3 at = (impact.mover.transform.position + impact.struck.transform.position) * 0.5f + Vector3.up * 0.6f;
            Fx.Spawn(s.bodyHit, at, s.scale, 3f);
        }
        else Fx.Spawn(s.wallSlam, impact.mover.transform.position + Vector3.up * 0.3f, s.scale, 3f);
    }
}
