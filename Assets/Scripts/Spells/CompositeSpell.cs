using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// The number primitive (Docs/AbilityGrammar.md): no effect takes a raw number. A base, a share
/// of the caster's weapon damage, a share of a max health (the caster's, the target's, or the unit
/// the effect lands on), and a step per tier above the first — so a verb's tier and the hero's
/// stats both move every number without a second progression system.
/// </summary>
[Serializable]
public class ScaledValue
{
    [Tooltip("The flat part.")]
    public float baseValue = 0f;
    [Tooltip("Plus this share of the caster's Damage stat. 1 = the weapon's full damage.")]
    public float ofWeaponDamage = 0f;
    [Tooltip("Plus this share of the max health of whoever the effect lands on. 0.2 = 20%.")]
    public float ofSubjectMaxHealth = 0f;
    [Tooltip("Plus this share of the caster's max health.")]
    public float ofCasterMaxHealth = 0f;
    [Tooltip("The whole is multiplied by 1 + this per tier above the first. 0.25 = +25% at tier II, +50% at tier III.")]
    public float perTier = 0.25f;

    public ScaledValue() { }
    public ScaledValue(float baseValue, float ofWeaponDamage = 0f, float ofSubjectMaxHealth = 0f, float perTier = 0.25f)
    {
        this.baseValue = baseValue; this.ofWeaponDamage = ofWeaponDamage; this.ofSubjectMaxHealth = ofSubjectMaxHealth; this.perTier = perTier;
    }

    public float Evaluate(Entity caster, Entity subject, int tier)
    {
        float v = baseValue;
        if (ofWeaponDamage != 0f && caster != null) v += AttackRoll.DamageOf(caster, 0f) * ofWeaponDamage;
        if (ofSubjectMaxHealth != 0f && subject != null && subject.Health != null) v += subject.Health.maxHealth * ofSubjectMaxHealth;
        if (ofCasterMaxHealth != 0f && caster != null && caster.Health != null) v += caster.Health.maxHealth * ofCasterMaxHealth;
        return v * (1f + perTier * Mathf.Max(0, tier - 1));
    }

    public string Describe()
    {
        var parts = new List<string>();
        if (baseValue != 0f) parts.Add($"{baseValue:0}");
        if (ofWeaponDamage != 0f) parts.Add($"{ofWeaponDamage:0.##}× weapon");
        if (ofSubjectMaxHealth != 0f) parts.Add($"{ofSubjectMaxHealth:P0} max health");
        if (ofCasterMaxHealth != 0f) parts.Add($"{ofCasterMaxHealth:P0} your max health");
        return parts.Count > 0 ? string.Join(" + ", parts) : "0";
    }
}

/// <summary>
/// What one cast knows: who is casting, whom it chose, the spell itself, and the tier the verb is
/// held at. Effects read and write this in order — a Blink moves the caster, the Damage after it
/// lands from the new spot.
/// </summary>
public class SpellContext
{
    public Entity caster;
    public Entity target;                 // the primary target (the first the selector found)
    public List<Entity> targets = new List<Entity>();
    public CompositeSpell spell;
    public int tier = 1;                  // the verb's attunement tier; 1 when it is not a verb
    public bool targetDied;               // set by DealDamage when its target fell
}

/// <summary>
/// One step of a composite spell. Plain serializable classes, drawn by Odin as a polymorphic list,
/// so a designer composes a spell from these in the inspector instead of a programmer writing a
/// subclass. Each is small and does one thing; the order in the list is the order they run.
/// </summary>
[Serializable]
public abstract class SpellEffect
{
    public abstract IEnumerator Run(SpellContext ctx);
    /// <summary>One line for the card and the designer.</summary>
    public virtual string Describe() => GetType().Name;
}

/// <summary>Whom a composite spell picks. The "who" of the grammar, as data.</summary>
[Serializable]
public class Selector
{
    public enum Who
    {
        /// <summary>Whoever the unit is fighting.</summary>
        CurrentTarget,
        NearestEnemy,
        LowestHealthEnemy,
        FarthestEnemy,
        /// <summary>An enemy carrying the status below, else the current target.</summary>
        WithStatusElseCurrent,
        /// <summary>An enemy carrying the status below, else the lowest-health enemy.</summary>
        WithStatusElseLowestHealth,
        AllEnemiesInRadius,
        AlliesInRadius,
        Self,
        /// <summary>Allies orthogonally next to the caster at the bell (Docs/PositionalKeywords.md).</summary>
        AlliesBeside,
        /// <summary>Allies in the caster's column at the bell.</summary>
        AlliesInRank,
        /// <summary>Allies the caster stands in front of: same lane, further back.</summary>
        AlliesCovered,
        /// <summary>The first enemy in the caster's lane at the bell, else the nearest.</summary>
        EnemyAcross,
    }

    public Who who = Who.CurrentTarget;
    [ShowIf("@who == Who.WithStatusElseCurrent || who == Who.WithStatusElseLowestHealth"), AssetsOnly]
    public Status status;
    [ShowIf("@who == Who.AllEnemiesInRadius || who == Who.AlliesInRadius"), Min(0f)]
    public float radius = 3f;
    [Tooltip("For the group pickers: at most this many, nearest first. 0 = everyone.")]
    [ShowIf("@who == Who.AllEnemiesInRadius || who == Who.AlliesInRadius"), Min(0)]
    public int count = 0;

    public List<Entity> Resolve(Entity caster, Entity current)
    {
        var result = new List<Entity>();
        if (caster == null) return result;
        Vector3 origin = caster.transform.position;

        IEnumerable<Entity> enemies = EntityRegistry.All.Where(e => e != null && !e.isDead && e.isTeam != caster.isTeam && e.gameObject.activeInHierarchy && !e.IsAggroDropped);
        IEnumerable<Entity> allies = EntityRegistry.All.Where(e => e != null && !e.isDead && e.isTeam == caster.isTeam && e.gameObject.activeInHierarchy);

        switch (who)
        {
            case Who.CurrentTarget:
                if (current != null && !current.isDead) result.Add(current);
                break;
            case Who.NearestEnemy:
                Add(result, enemies.OrderBy(e => (e.transform.position - origin).sqrMagnitude).FirstOrDefault());
                break;
            case Who.LowestHealthEnemy:
                Add(result, enemies.OrderBy(e => e.currentHealth).FirstOrDefault());
                break;
            case Who.FarthestEnemy:
                Add(result, enemies.OrderByDescending(e => (e.transform.position - origin).sqrMagnitude).FirstOrDefault());
                break;
            case Who.WithStatusElseCurrent:
            case Who.WithStatusElseLowestHealth:
            {
                var marked = enemies.Where(e => e.Statuses != null && e.Statuses.Has(status))
                                    .OrderBy(e => e.currentHealth).FirstOrDefault();
                if (marked != null) result.Add(marked);
                else if (who == Who.WithStatusElseCurrent) { if (current != null && !current.isDead) result.Add(current); }
                else Add(result, enemies.OrderBy(e => e.currentHealth).FirstOrDefault());
                break;
            }
            case Who.AllEnemiesInRadius:
            {
                var inRange = enemies.Where(e => (e.transform.position - origin).magnitude <= radius)
                                     .OrderBy(e => (e.transform.position - origin).sqrMagnitude);
                result.AddRange(count > 0 ? inRange.Take(count) : inRange);
                break;
            }
            case Who.AlliesInRadius:
            {
                var inRange = allies.Where(e => (e.transform.position - origin).magnitude <= radius)
                                    .OrderBy(e => (e.transform.position - origin).sqrMagnitude);
                result.AddRange(count > 0 ? inRange.Take(count) : inRange);
                break;
            }
            case Who.Self:
                result.Add(caster);
                break;
            case Who.AlliesBeside:
                foreach (var a in BoardSnapshot.Beside(caster)) if (a != null && !a.isDead) result.Add(a);
                break;
            case Who.AlliesInRank:
                foreach (var a in BoardSnapshot.Rank(caster)) if (a != null && !a.isDead) result.Add(a);
                break;
            case Who.AlliesCovered:
                foreach (var a in BoardSnapshot.Covered(caster)) if (a != null && !a.isDead) result.Add(a);
                break;
            case Who.EnemyAcross:
            {
                var across = BoardSnapshot.Across(caster);
                if (across != null && !across.isDead && !across.IsAggroDropped) result.Add(across);
                else Add(result, enemies.OrderBy(e => (e.transform.position - origin).sqrMagnitude).FirstOrDefault());
                break;
            }
        }
        return result;
    }

    private static void Add(List<Entity> into, Entity e) { if (e != null) into.Add(e); }

    public string Describe()
    {
        switch (who)
        {
            case Who.WithStatusElseCurrent: return $"the {(status != null ? status.DisplayName : "?")} enemy, else the target";
            case Who.WithStatusElseLowestHealth: return $"the {(status != null ? status.DisplayName : "?")} enemy, else the weakest";
            case Who.AllEnemiesInRadius: return $"enemies within {radius:0.#}";
            case Who.AlliesInRadius: return $"allies within {radius:0.#}";
            case Who.AlliesBeside: return "allies Beside you";
            case Who.AlliesInRank: return "allies in your Rank";
            case Who.AlliesCovered: return "allies you Cover";
            case Who.EnemyAcross: return "the enemy Across";
            default: return who.ToString();
        }
    }
}

/// <summary>
/// A spell made of parts: a selector says whom, a motion plays, and an ordered list of effects
/// runs on the contact frame. New spells are assets, not classes — Backstab is a Blink, a Damage
/// and a Refund; Fan of Knives is a group selector and a Throw. The subclasses in this folder
/// remain for what the parts cannot yet say, and migrate here as the parts grow.
/// </summary>
[CreateAssetMenu(menuName = "Spells/Composite Spell")]
public class CompositeSpell : Spell
{
    public enum Motion { None, Slash, Jab, ThrowSupply }

    /// <summary>The spell as one sentence: whom, then what, in order. Shown first so an asset reads before it is opened.</summary>
    [ShowInInspector, ReadOnly, MultiLineProperty(2), PropertyOrder(-1), LabelText("Reads")]
    public string Reads => $"{selector.Describe()} → {DescribeEffects()}";

    [BoxGroup("Whom")] public Selector selector = new Selector();

    [BoxGroup("How it plays")]
    public Motion motion = Motion.Slash;
    [BoxGroup("How it plays"), Tooltip("Contact-frame event names: characters fire \"Hit\" for a swing and \"ThrowSupply\" for a throw; monsters fire \"Attack\".")]
    public string characterEvent = "Hit";
    [BoxGroup("How it plays")] public string monsterEvent = "Attack";
    [BoxGroup("How it plays"), Tooltip("If the clip never fires the event, land the effects after this long.")]
    public float contactFallback = 0.2f;
    [BoxGroup("How it plays")] public float contactTimeout = 1f;
    [BoxGroup("How it plays"), Tooltip("Scale with attack speed, like a weapon attack, instead of cooldown reduction.")]
    public bool scalesWithAttackSpeed = false;
    [BoxGroup("How it plays"), Tooltip("Seeds the unit's Damage stat when this is the weapon attack. Zero for anything else.")]
    public float baseDamage = 0f;

    [BoxGroup("What it does")]
    [SerializeReference, ListDrawerSettings(ShowIndexLabels = true), Tooltip("Run in order on the contact frame. Each effect says whom it touches: the primary target, every target, or the caster.")]
    public List<SpellEffect> effects = new List<SpellEffect>();

    public override bool ScalesWithAttackSpeed => scalesWithAttackSpeed;
    public override float BaseDamage => baseDamage;

    public override bool CanCast(Entity caster, Entity target)
    {
        if (caster == null) return false;
        var targets = selector.Resolve(caster, target);
        return targets.Count > 0;
    }

    public override IEnumerator Cast(Entity caster, Entity target)
    {
        var ctx = new SpellContext { caster = caster, spell = this };
        ctx.tier = caster.Resonance != null ? caster.Resonance.TierOfVerb(this) : 1;
        ctx.targets = selector.Resolve(caster, target);
        if (ctx.targets.Count == 0) yield break;
        ctx.target = ctx.targets[0];

        // Face the primary target, then play the motion at attack-speed pace if it is a weapon swing.
        if (ctx.target != caster) caster.SetFacing(ctx.target.transform.position.x > caster.transform.position.x);
        float playback = scalesWithAttackSpeed ? GetAttackSpeed(caster) : 1f;
        var animator = GetAnimator(caster);
        if (animator != null) animator.speed = playback;

        switch (motion)
        {
            case Motion.Slash: if (caster.isCharacter && caster.character != null) caster.character.Slash(); else caster.monster?.Attack(); break;
            case Motion.Jab: if (caster.isCharacter && caster.character != null) caster.character.Jab(); else caster.monster?.Attack(); break;
            case Motion.ThrowSupply: if (caster.isCharacter && caster.character != null && animator != null) animator.SetTrigger("ThrowSupply"); else caster.monster?.Attack(); break;
        }

        if (motion != Motion.None)
            yield return WaitForAnimationEvent(caster, characterEvent, monsterEvent, contactFallback / playback, contactTimeout / playback);
        if (animator != null) animator.speed = 1f;

        foreach (var effect in effects)
        {
            if (effect == null) continue;
            if (caster == null || caster.isDead) yield break;
            yield return effect.Run(ctx);
        }
    }

    public string DescribeEffects() => string.Join(" · ", effects.Where(e => e != null).Select(e => e.Describe()));
}

// ---------------------------------------------------------------------------------------------
// The effect library. Each touches the primary target, every target, or the caster, and says so.
// ---------------------------------------------------------------------------------------------

/// <summary>Where thrown things come from, and the sprite they wear. Shared by spells and engravings.</summary>
public static class Supplies
{
    public static Vector3 ThrowOrigin(Entity caster) =>
        caster.fireTransform != null ? caster.fireTransform.position : caster.transform.position + Vector3.up;

    public static Sprite FindSprite(Entity caster, string name)
    {
        var collection = caster != null && caster.character != null ? caster.character.SpriteCollection : null;
        if (collection == null || collection.Supplies == null) return null;
        var entry = collection.Supplies.FirstOrDefault(i => i != null && i.Name == name);
        return entry != null ? entry.Sprite : null;
    }

    /// <summary>A sprite in the air at the caster's hand, ready for a flight component. Null when there is no such sprite.</summary>
    public static GameObject Build(Entity caster, string spriteName, float scale, string objectName)
    {
        var sprite = FindSprite(caster, spriteName);
        if (sprite == null) return null;
        var go = new GameObject(objectName);
        go.transform.position = ThrowOrigin(caster);
        go.transform.localScale = Vector3.one * scale;
        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = 100;
        return go;
    }

    /// <summary>Throw a star from the caster at a point; on a hit, optionally put a status on the victim.</summary>
    public static ThrownStar ThrowStar(Entity caster, Entity target, float damage, float speed, float hitRadius, float spin, float critChance,
                                       Status onHitStatus = null, float statusDuration = -1f, string spriteName = "ThrowingStar", float scale = 0.5f, float overshoot = 1.5f)
    {
        if (target == null) return null;
        var go = Build(caster, spriteName, scale, "ThrownStar");
        if (go == null) return null;
        // Aimed through the target rather than at it, so a target that steps aside is still crossed and a miss flies past.
        Vector3 origin = go.transform.position;
        Vector3 direction = target.transform.position - origin;
        direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.right;
        Vector3 at = target.transform.position + direction * overshoot;
        var star = go.AddComponent<ThrownStar>();
        Action<Entity> onHit = null;
        if (onHitStatus != null)
            onHit = victim => { if (victim != null && victim.Statuses != null) victim.Statuses.Apply(onHitStatus, statusDuration, caster); };
        star.Launch(caster, at, speed, damage, hitRadius, spin, critChance, onHit);
        return star;
    }
}

public enum EffectScope { PrimaryTarget, EveryTarget, Caster }

[Serializable]
public class DealDamageEffect : SpellEffect
{
    public EffectScope scope = EffectScope.PrimaryTarget;
    public ScaledValue damage = new ScaledValue(0f, ofWeaponDamage: 1f);
    [Range(0f, 1f)] public float critChance = 0.1f;
    public bool alwaysCrit = false;
    [Tooltip("Freeze the victim this long on impact. Zero for none.")]
    public float hitstop = 0f;

    public override IEnumerator Run(SpellContext ctx)
    {
        foreach (var victim in Targets(ctx))
        {
            if (victim == null || victim.isDead) continue;
            float dmg = damage.Evaluate(ctx.caster, victim, ctx.tier);
            bool crit = alwaysCrit || AttackRoll.IsCrit(critChance);
            victim.TakeDamage(dmg, ctx.caster, crit);
            if (hitstop > 0f) victim.ApplyHitstop(hitstop);
            if (victim.isDead) ctx.targetDied = true;
        }
        yield break;
    }

    private IEnumerable<Entity> Targets(SpellContext ctx)
    {
        switch (scope)
        {
            case EffectScope.EveryTarget: return ctx.targets;
            case EffectScope.Caster: return new[] { ctx.caster };
            default: return new[] { ctx.target };
        }
    }

    public override string Describe() => $"{damage.Describe()} damage" + (alwaysCrit ? ", a crit" : "") + (scope == EffectScope.EveryTarget ? " to each" : "");
}

[Serializable]
public class ThrowStarEffect : SpellEffect
{
    public EffectScope scope = EffectScope.PrimaryTarget;
    public string spriteName = "ThrowingStar";
    public float scale = 0.5f;
    public ScaledValue damage = new ScaledValue(0f, ofWeaponDamage: 0.6f);
    public float speed = 16f;
    public float hitRadius = 0.6f;
    public float spin = 540f;
    [Range(0f, 1f)] public float critChance = 0.1f;
    [Tooltip("Stagger the throws slightly so a fan reads as a fan.")]
    public float secondsBetween = 0.05f;
    [AssetsOnly] public Status applyOnHit;
    [ShowIf("@applyOnHit != null")] public float statusDuration = -1f;

    public override IEnumerator Run(SpellContext ctx)
    {
        var targets = scope == EffectScope.EveryTarget ? ctx.targets : new List<Entity> { ctx.target };
        foreach (var victim in targets)
        {
            if (victim == null || victim.isDead) continue;
            Supplies.ThrowStar(ctx.caster, victim, damage.Evaluate(ctx.caster, victim, ctx.tier), speed, hitRadius, spin, critChance, applyOnHit, statusDuration, spriteName, scale);
            if (secondsBetween > 0f && targets.Count > 1) yield return new WaitForSeconds(secondsBetween);
        }
    }

    public override string Describe() => $"throw a star{(scope == EffectScope.EveryTarget ? " at each" : "")}{(applyOnHit != null ? ", " + applyOnHit.DisplayName + " on hit" : "")}";
}

[Serializable]
public class BlinkEffect : SpellEffect
{
    public enum Destination { BehindTarget, AwayFromTarget, ToRear }
    public Destination destination = Destination.BehindTarget;
    [Tooltip("How far behind (or away from) the target the caster lands.")]
    public float offset = 0.9f;
    [Tooltip("Beat between arriving and the next effect, so the eye can follow.")]
    public float settle = 0.12f;

    public override IEnumerator Run(SpellContext ctx)
    {
        var caster = ctx.caster; var target = ctx.target;
        Vector3 to = caster.transform.position;
        switch (destination)
        {
            case Destination.BehindTarget:
                if (target == null) yield break;
                float facing = target.transform.localScale.x >= 0f ? 1f : -1f;
                if (target.monster != null) facing = -facing;
                to = target.transform.position - new Vector3(facing * offset, 0f, 0f);
                break;
            case Destination.AwayFromTarget:
                if (target == null) yield break;
                to = caster.transform.position + (caster.transform.position - target.transform.position).normalized * offset;
                break;
            case Destination.ToRear:
                // The back line: the rear cell of the caster's lane. With no board, away from the nearest enemy.
                var nearest = EntityRegistry.All.Where(e => e != null && !e.isDead && e.isTeam != caster.isTeam)
                    .OrderBy(e => (e.transform.position - caster.transform.position).sqrMagnitude).FirstOrDefault();
                Vector3 away = nearest != null ? (caster.transform.position - nearest.transform.position).normalized : (caster.isTeam ? Vector3.left : Vector3.right);
                to = BoardSnapshot.RearOf(caster, caster.transform.position + away * offset);
                break;
        }
        caster.Relocate(to);   // a blink is movement: it counts for everything that listens for steps
        if (target != null && target != caster) caster.SetFacing(target.transform.position.x > caster.transform.position.x);
        if (settle > 0f) yield return new WaitForSeconds(settle);
    }

    public override string Describe() => destination == Destination.BehindTarget ? "blink behind the target" : destination == Destination.ToRear ? "blink to the rear" : "blink away";
}

[Serializable]
public class ApplyStatusEffect : SpellEffect
{
    public EffectScope scope = EffectScope.PrimaryTarget;
    [Required, AssetsOnly] public Status status;
    [Tooltip("Seconds. Negative: the status's own default.")]
    public float duration = -1f;
    [Min(1)] public int stacks = 1;

    public override IEnumerator Run(SpellContext ctx)
    {
        IEnumerable<Entity> targets = scope == EffectScope.EveryTarget ? ctx.targets : scope == EffectScope.Caster ? new[] { ctx.caster } : new[] { ctx.target };
        foreach (var t in targets)
            if (t != null && !t.isDead && t.Statuses != null) t.Statuses.Apply(status, duration, ctx.caster, stacks);
        yield break;
    }

    public override string Describe() => $"{(status != null ? status.DisplayName : "?")}{(scope == EffectScope.EveryTarget ? " on each" : scope == EffectScope.Caster ? " on self" : "")}";
}

[Serializable]
public class DropAggroEffect : SpellEffect
{
    public EffectScope scope = EffectScope.Caster;
    public float seconds = 2.5f;

    public override IEnumerator Run(SpellContext ctx)
    {
        IEnumerable<Entity> targets = scope == EffectScope.EveryTarget ? ctx.targets : scope == EffectScope.Caster ? new[] { ctx.caster } : new[] { ctx.target };
        foreach (var t in targets) t?.DropAggro(seconds);
        yield break;
    }

    public override string Describe() => $"out of sight {seconds:0.#} s{(scope == EffectScope.EveryTarget ? " for each" : "")}";
}

[Serializable]
public class HitstopEffect : SpellEffect
{
    public float duration = 0.1f;
    public bool wholeBattlefield = false;

    public override IEnumerator Run(SpellContext ctx)
    {
        if (wholeBattlefield) { foreach (var e in EntityRegistry.All) if (e != null && !e.isDead) e.ApplyHitstop(duration); }
        else { ctx.target?.ApplyHitstop(duration); ctx.caster?.ApplyHitstop(duration); }
        yield break;
    }

    public override string Describe() => $"hitstop {duration:0.##} s";
}

/// <summary>If the primary target fell to this cast, the spell is ready again at once.</summary>
[Serializable]
public class RefundCooldownOnKillEffect : SpellEffect
{
    public override IEnumerator Run(SpellContext ctx)
    {
        if (ctx.targetDied && ctx.caster != null && ctx.caster.CombatAI != null) ctx.caster.CombatAI.RefundCooldown();
        yield break;
    }

    public override string Describe() => "a kill resets the cooldown";
}

[Serializable]
public class WaitEffect : SpellEffect
{
    public float seconds = 0.1f;
    public override IEnumerator Run(SpellContext ctx) { if (seconds > 0f) yield return new WaitForSeconds(seconds); }
    public override string Describe() => $"wait {seconds:0.##} s";
}

[Serializable]
public class ShieldEffect : SpellEffect
{
    public EffectScope scope = EffectScope.Caster;
    public ScaledValue amount = new ScaledValue(0f, ofSubjectMaxHealth: 0.2f);
    [Tooltip("Multiply by how many targets the selector found — a Roar that shields per enemy taunted.")]
    public bool perTargetFound = false;
    [Tooltip("Seconds. Zero: until broken or the fight ends.")] public float duration = 0f;

    public override IEnumerator Run(SpellContext ctx)
    {
        IEnumerable<Entity> targets = scope == EffectScope.EveryTarget ? ctx.targets : scope == EffectScope.Caster ? new[] { ctx.caster } : new[] { ctx.target };
        float mult = perTargetFound ? Mathf.Max(1, ctx.targets.Count) : 1f;
        foreach (var t in targets)
        {
            if (t == null || t.isDead || t.Health == null) continue;
            t.Health.AddShield(amount.Evaluate(ctx.caster, t, ctx.tier) * mult, duration);
        }
        yield break;
    }

    public override string Describe() => $"shield {amount.Describe()}{(perTargetFound ? " per target" : "")}{(scope == EffectScope.EveryTarget ? " on each" : scope == EffectScope.Caster ? " on self" : "")}";
}

[Serializable]
public class HealEffect : SpellEffect
{
    public EffectScope scope = EffectScope.PrimaryTarget;
    public ScaledValue amount = new ScaledValue(0f, ofSubjectMaxHealth: 0.15f);

    public override IEnumerator Run(SpellContext ctx)
    {
        IEnumerable<Entity> targets = scope == EffectScope.EveryTarget ? ctx.targets : scope == EffectScope.Caster ? new[] { ctx.caster } : new[] { ctx.target };
        foreach (var t in targets)
            if (t != null && !t.isDead && t.Health != null) t.Health.Heal(amount.Evaluate(ctx.caster, t, ctx.tier), ctx.caster);
        yield break;
    }

    public override string Describe() => $"heal {amount.Describe()}{(scope == EffectScope.EveryTarget ? " each" : "")}";
}

[Serializable]
public class KnockbackEffect : SpellEffect
{
    public EffectScope scope = EffectScope.PrimaryTarget;
    public float force = 8f;
    [Tooltip("Away from the caster (default) or toward it (a pull).")] public bool pull = false;

    public override IEnumerator Run(SpellContext ctx)
    {
        IEnumerable<Entity> targets = scope == EffectScope.EveryTarget ? ctx.targets : new[] { ctx.target };
        foreach (var t in targets)
        {
            if (t == null || t.isDead || ctx.caster == null) continue;
            Vector3 dir = (t.transform.position - ctx.caster.transform.position);
            dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.right;
            t.ApplyKnockback(pull ? -dir : dir, force, ctx.caster);
        }
        yield break;
    }

    public override string Describe() => (pull ? "pull " : "knock back ") + (scope == EffectScope.EveryTarget ? "each" : "the target");
}

/// <summary>
/// Throw the caster at the target: a charge. The caster is the weapon — it is not stunned or hurt
/// by what it hits, and it plows through with most of its speed (<see cref="CombatPhysics"/>). The
/// damage is the collision's, so there is nothing to set here but how hard.
/// </summary>
[Serializable]
public class DashEffect : SpellEffect
{
    [Tooltip("Launch speed. The charge travels about force / damping units.")] public float force = 14f;

    public override IEnumerator Run(SpellContext ctx)
    {
        var caster = ctx.caster; var target = ctx.target;
        if (caster == null || target == null) yield break;
        Vector3 dir = target.transform.position - caster.transform.position; dir.z = 0f;
        dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : (caster.isTeam ? Vector3.right : Vector3.left);
        caster.ApplyKnockback(dir, force, caster, charging: true);
        yield break;
    }

    public override string Describe() => "charge at the target, knocking aside everything hit";
}

/// <summary>Damage everything of one side within a radius of the caster — Shockwave's heart.</summary>
[Serializable]
public class RadiusDamageEffect : SpellEffect
{
    [Min(0f)] public float radius = 3f;
    public bool enemies = true;
    public ScaledValue damage = new ScaledValue(10f, ofWeaponDamage: 0.5f);
    [Range(0f, 1f)] public float critChance = 0f;
    [Tooltip("Knock each victim away from the caster with this force. Zero for none.")] public float knockback = 0f;
    public float hitstop = 0f;

    public override IEnumerator Run(SpellContext ctx)
    {
        var caster = ctx.caster; if (caster == null) yield break;
        Vector3 origin = caster.transform.position;
        var victims = new List<Entity>();
        foreach (var e in EntityRegistry.All)
            if (e != null && !e.isDead && e.gameObject.activeInHierarchy && (e.isTeam != caster.isTeam) == enemies && e != caster && (e.transform.position - origin).magnitude <= radius) victims.Add(e);
        foreach (var v in victims)
        {
            float dmg = damage.Evaluate(caster, v, ctx.tier);
            v.TakeDamage(dmg, caster, AttackRoll.IsCrit(critChance));
            if (knockback > 0f) { Vector3 dir = v.transform.position - origin; v.ApplyKnockback(dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.right, knockback, caster); }
            if (hitstop > 0f) v.ApplyHitstop(hitstop);
        }
        ctx.targets = victims;
        if (victims.Count > 0) ctx.target = victims[0];
    }

    public override string Describe() => $"{damage.Describe()} damage to {(enemies ? "enemies" : "allies")} within {radius:0.#}{(knockback > 0 ? ", knocked back" : "")}";
}

/// <summary>Put a status on everyone of one side within a radius of the caster — a cloud, a shout, a ring.</summary>
[Serializable]
public class ApplyStatusInRadiusEffect : SpellEffect
{
    [Min(0f)] public float radius = 2.5f;
    [Tooltip("Enemies of the caster, or allies (the caster included).")]
    public bool enemies = true;
    [Required, AssetsOnly] public Status status;
    [Tooltip("Seconds. Negative: the status's own default.")]
    public float duration = -1f;

    public override IEnumerator Run(SpellContext ctx)
    {
        var caster = ctx.caster;
        if (caster == null || status == null) yield break;
        Vector3 origin = caster.transform.position;
        foreach (var e in EntityRegistry.All)
        {
            if (e == null || e.isDead || !e.gameObject.activeInHierarchy || e.Statuses == null) continue;
            if ((e.isTeam != caster.isTeam) != enemies) continue;
            if ((e.transform.position - origin).magnitude > radius) continue;
            e.Statuses.Apply(status, duration, caster);
        }
    }

    public override string Describe() => $"{(status != null ? status.DisplayName : "?")} on {(enemies ? "enemies" : "allies")} within {radius:0.#}";
}
