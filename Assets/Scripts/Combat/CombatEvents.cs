using System;

/// <summary>One landed hit, as the bus reports it: who, whom, how much, and whether it killed.</summary>
public readonly struct HitInfo
{
    public readonly Entity source;
    public readonly Entity target;
    public readonly float amount;
    public readonly bool isCrit;
    public readonly bool lethal;

    public HitInfo(Entity source, Entity target, float amount, bool isCrit, bool lethal)
    {
        this.source = source;
        this.target = target;
        this.amount = amount;
        this.isCrit = isCrit;
        this.lethal = lethal;
    }
}

/// <summary>
/// The combat bus: the four moments every reactive item wants — a hit landed, a kill, a cast, a
/// step — announced once, from the one place each happens (<see cref="Health.TakeDamage"/>,
/// <see cref="Health"/>'s death, <see cref="CombatAI"/>'s cast and move). Static, like
/// <see cref="Entity.OnAnyDied"/> before it; listeners filter by the entity they care about.
/// <see cref="Resonance"/> routes these to the engravings a hero holds, so an engraving only
/// overrides a hook and never subscribes by hand.
/// </summary>
public static class CombatEvents
{
    public static event Action<HitInfo> Hit;
    /// <summary>killer (may be null), victim.</summary>
    public static event Action<Entity, Entity> Kill;
    public static event Action<Entity, Spell> Cast;
    /// <summary>A unit moved this far this frame under its own power.</summary>
    public static event Action<Entity, float> Moved;

    public static void RaiseHit(HitInfo hit) => Hit?.Invoke(hit);
    public static void RaiseKill(Entity killer, Entity victim) => Kill?.Invoke(killer, victim);
    public static void RaiseCast(Entity caster, Spell spell) => Cast?.Invoke(caster, spell);
    public static void RaiseMoved(Entity entity, float distance) => Moved?.Invoke(entity, distance);

    /// <summary>Drop every listener. For tests, and for a domain that is starting over.</summary>
    public static void Clear()
    {
        Hit = null; Kill = null; Cast = null; Moved = null;
    }
}
