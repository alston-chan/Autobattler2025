using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A state a unit can be in — Marked, Exposed, Burning — as data. A status is a word the whole
/// catalogue shares: items WRITE it (an effect applies it), items READ it (a selector or a rule looks
/// for it), and the status itself carries only a small intrinsic meaning so that the reading is
/// where the design lives. Duration, stacking and the intrinsic multipliers are authored here; who
/// applies it and who cares are the items' business.
/// </summary>
[CreateAssetMenu(menuName = "Data/Status", fileName = "Status")]
public class Status : ScriptableObject
{
    public enum Stacking
    {
        /// <summary>Applying again restarts the clock; there is only ever one.</summary>
        Refresh,
        /// <summary>Applying again adds a stack (to the cap) and restarts the clock.</summary>
        Stack,
        /// <summary>Applying again does nothing while it is already on.</summary>
        Ignore,
    }

    [Tooltip("The word, as the catalogue uses it: Mark, Exposed, Burn.")]
    public string displayName;
    [TextArea, Tooltip("What it means to be in this state, for the player.")]
    public string description;

    [Tooltip("A one-character glyph drawn over the unit while the status is on. Blank for none.")]
    public string glyph = "";
    public Color glyphColor = Color.white;

    [Tooltip("On an enemy this is a debuff; on an ally a buff. Only affects how it is described and coloured.")]
    public bool isDebuff = true;

    [Tooltip("Seconds it lasts when the applier does not say. Zero or less: until the fight ends.")]
    public float defaultDuration = 6f;

    public Stacking stacking = Stacking.Refresh;
    [Min(1)] public int maxStacks = 1;

    [Header("Intrinsic meaning (kept small — the items do the rest)")]
    [Tooltip("Damage taken is multiplied by this per stack. 1.1 = takes 10% more.")]
    public float damageTakenPerStack = 1f;
    [Tooltip("Damage dealt is multiplied by this per stack. 0.85 = deals 15% less.")]
    public float damageDealtPerStack = 1f;
    [Tooltip("While on, the unit cannot be picked as a target.")]
    public bool untargetable;
    [Tooltip("While on, the unit cannot move.")]
    public bool rooted;

    public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
}

/// <summary>
/// The statuses on one unit, with no Unity in it so the rules can be tested: stacking, expiry, and
/// the intrinsic multipliers. Time is passed in.
/// </summary>
public class StatusSet
{
    public class Active
    {
        public Status status;
        public int stacks;
        public float expiresAt;      // float.PositiveInfinity = until cleared
        public Entity source;
    }

    private readonly List<Active> _active = new List<Active>();
    private readonly List<Active> _expired = new List<Active>();

    public event Action<Active> OnApplied;
    public event Action<Active> OnRemoved;

    public IReadOnlyList<Active> All => _active;

    /// <summary>Put a status on. Returns the active record, or null when Ignore stacking refused it.</summary>
    public Active Apply(Status status, float now, float duration = -1f, Entity source = null, int stacks = 1)
    {
        if (status == null) return null;
        if (duration < 0f) duration = status.defaultDuration;
        float expiresAt = duration > 0f ? now + duration : float.PositiveInfinity;

        var existing = Find(status);
        if (existing != null)
        {
            switch (status.stacking)
            {
                case Status.Stacking.Ignore:
                    return null;
                case Status.Stacking.Stack:
                    existing.stacks = Mathf.Min(status.maxStacks, existing.stacks + Mathf.Max(1, stacks));
                    break;
            }
            existing.expiresAt = Mathf.Max(existing.expiresAt, expiresAt);
            if (source != null) existing.source = source;
            OnApplied?.Invoke(existing);
            return existing;
        }

        var record = new Active
        {
            status = status,
            stacks = Mathf.Clamp(stacks, 1, status.maxStacks),
            expiresAt = expiresAt,
            source = source,
        };
        _active.Add(record);
        OnApplied?.Invoke(record);
        return record;
    }

    /// <summary>Take a status off entirely. True when it was on.</summary>
    public bool Remove(Status status)
    {
        var existing = Find(status);
        if (existing == null) return false;
        _active.Remove(existing);
        OnRemoved?.Invoke(existing);
        return true;
    }

    public bool Has(Status status) => Find(status) != null;
    public int Stacks(Status status) => Find(status)?.stacks ?? 0;

    public Active Find(Status status)
    {
        for (int i = 0; i < _active.Count; i++)
            if (_active[i].status == status) return _active[i];
        return null;
    }

    /// <summary>Let time pass: anything past its expiry comes off.</summary>
    public void Tick(float now)
    {
        _expired.Clear();
        for (int i = 0; i < _active.Count; i++)
            if (now >= _active[i].expiresAt) _expired.Add(_active[i]);
        foreach (var record in _expired)
        {
            _active.Remove(record);
            OnRemoved?.Invoke(record);
        }
    }

    public void Clear()
    {
        _expired.Clear();
        _expired.AddRange(_active);
        _active.Clear();
        foreach (var record in _expired) OnRemoved?.Invoke(record);
    }

    public float DamageTakenMultiplier
    {
        get
        {
            float m = 1f;
            for (int i = 0; i < _active.Count; i++)
                m *= Mathf.Pow(_active[i].status.damageTakenPerStack, _active[i].stacks);
            return m;
        }
    }

    public float DamageDealtMultiplier
    {
        get
        {
            float m = 1f;
            for (int i = 0; i < _active.Count; i++)
                m *= Mathf.Pow(_active[i].status.damageDealtPerStack, _active[i].stacks);
            return m;
        }
    }

    public bool Untargetable
    {
        get { for (int i = 0; i < _active.Count; i++) if (_active[i].status.untargetable) return true; return false; }
    }

    public bool Rooted
    {
        get { for (int i = 0; i < _active.Count; i++) if (_active[i].status.rooted) return true; return false; }
    }
}

/// <summary>
/// The statuses on this unit, in the scene: a <see cref="StatusSet"/> ticked on game time, cleared
/// when the fight ends, and drawn as glyphs over the head so a Mark is something the player sees.
/// </summary>
public class StatusController : MonoBehaviour
{
    private Entity _entity;
    private readonly StatusSet _set = new StatusSet();
    private readonly Dictionary<StatusSet.Active, TextMesh> _glyphs = new Dictionary<StatusSet.Active, TextMesh>();

    public StatusSet Set => _set;

    public void Initialize(Entity entity)
    {
        _entity = entity;
        _set.OnApplied += ShowGlyph;
        _set.OnRemoved += HideGlyph;
    }

    public StatusSet.Active Apply(Status status, float duration = -1f, Entity source = null, int stacks = 1)
        => _set.Apply(status, Time.time, duration, source, stacks);
    public bool Remove(Status status) => _set.Remove(status);
    public bool Has(Status status) => status != null && _set.Has(status);
    public int Stacks(Status status) => _set.Stacks(status);
    public float DamageTakenMultiplier => _set.DamageTakenMultiplier;
    public float DamageDealtMultiplier => _set.DamageDealtMultiplier;
    public bool Untargetable => _set.Untargetable;
    public bool Rooted => _set.Rooted;

    public void Tick() => _set.Tick(Time.time);
    public void ClearAll() => _set.Clear();

    private void OnDisable() => _set.Clear();

    // ---- the glyphs: one small TextMesh per active status, stacked above the health bar

    private void ShowGlyph(StatusSet.Active record)
    {
        if (string.IsNullOrEmpty(record.status.glyph)) return;
        if (!_glyphs.TryGetValue(record, out var mesh) || mesh == null)
        {
            var go = new GameObject("Status " + record.status.DisplayName);
            go.transform.SetParent(transform, false);
            mesh = go.AddComponent<TextMesh>();
            mesh.fontSize = 48;
            mesh.characterSize = 0.08f;
            mesh.anchor = TextAnchor.LowerCenter;
            mesh.alignment = TextAlignment.Center;
            var renderer = mesh.GetComponent<MeshRenderer>();
            renderer.sortingOrder = 500;
            _glyphs[record] = mesh;
        }
        mesh.color = record.status.glyphColor;
        mesh.text = record.stacks > 1 ? $"{record.status.glyph}{record.stacks}" : record.status.glyph;
        Layout();
    }

    private void HideGlyph(StatusSet.Active record)
    {
        if (_glyphs.TryGetValue(record, out var mesh))
        {
            if (mesh != null) Destroy(mesh.gameObject);
            _glyphs.Remove(record);
        }
        Layout();
    }

    private void Layout()
    {
        float y = (_entity != null ? _entity.healthBarOffset.y : 1.6f) + 0.35f;
        int i = 0;
        foreach (var pair in _glyphs)
        {
            if (pair.Value == null) continue;
            var t = pair.Value.transform;
            t.localPosition = new Vector3((i - (_glyphs.Count - 1) * 0.5f) * 0.3f, y, 0f);
            t.localRotation = Quaternion.identity;
            // The body flips by scale to face; the glyph should not read mirrored.
            var parentScale = transform.lossyScale;
            t.localScale = new Vector3(Mathf.Sign(parentScale.x), 1f, 1f);
            i++;
        }
    }
}
