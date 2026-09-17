using UnityEngine;

/// <summary>
/// A combo: when the wearer does one thing, a spell follows for free. Blink into a Cannonball, a
/// kill into a Nova, a Bull Rush into a Singularity. The follow-up is cast at the nearest enemy,
/// costs no mana, and waits out its own cooldown, so a helmet can turn a movement into a play
/// without the wearer's kit knowing. Tier shortens the cooldown.
/// </summary>
[CreateAssetMenu(menuName = "Data/Engravings/Cast On Event", fileName = "CastOnEvent")]
public class CastOnEventEngraving : Engraving
{
    public enum Trigger
    {
        /// <summary>The wearer moved in one step: a blink, a substitute, a throw.</summary>
        Blink,
        /// <summary>The wearer cast a spell (optionally one by name).</summary>
        Cast,
        /// <summary>The wearer killed something.</summary>
        Kill,
        /// <summary>The wearer was hit.</summary>
        Damaged,
    }

    public Trigger trigger = Trigger.Blink;
    [Tooltip("For the Cast trigger: only this spell (its display name) sets it off. Empty means any cast.")]
    public string castNamed = "";
    [Tooltip("The spell that follows, cast at the nearest enemy for free.")]
    public Spell spell;
    [Tooltip("Seconds before it can follow again at tier I; each tier takes a fifth off.")]
    public float cooldown = 4f;

    [System.NonSerialized] private float _readyAt;

    public override void OnCombatStart(Entity owner, int tier) => _readyAt = 0f;

    public override void OnBlink(Entity owner, Vector3 from, Vector3 to, int tier)
    {
        if (trigger == Trigger.Blink) Follow(owner, tier);
    }

    public override void OnCast(Entity owner, Spell cast, int tier)
    {
        if (trigger != Trigger.Cast || cast == spell) return;
        if (!string.IsNullOrEmpty(castNamed) && (cast == null || cast.DisplayName != castNamed)) return;
        Follow(owner, tier);
    }

    public override void OnKill(Entity owner, Entity victim, int tier)
    {
        if (trigger == Trigger.Kill) Follow(owner, tier);
    }

    public override void OnDamaged(Entity owner, HitInfo hit, int tier)
    {
        if (trigger == Trigger.Damaged) Follow(owner, tier);
    }

    private void Follow(Entity owner, int tier)
    {
        if (owner == null || owner.isDead || !owner.IsFighting || spell == null) return;
        if (Time.time < _readyAt) return;
        var target = Targeting.Pick(owner, TargetMode.Nearest);
        if (target == null || !spell.CanCast(owner, target)) return;

        _readyAt = Time.time + cooldown * (1f - 0.2f * (Mathf.Max(1, tier) - 1));
        AbilityFeedback.Announce(owner, engravingName);
        CombatEvents.RaiseCast(owner, spell);
        owner.StartCoroutine(spell.Cast(owner, target));
    }

    public override string DescribeTier(int tier)
    {
        string when = trigger == Trigger.Blink ? "After you blink"
                    : trigger == Trigger.Kill ? "After a kill"
                    : trigger == Trigger.Damaged ? "When you are hit"
                    : string.IsNullOrEmpty(castNamed) ? "After any cast" : $"After {castNamed}";
        float cd = cooldown * (1f - 0.2f * (Mathf.Max(1, tier) - 1));
        return $"{when}: {(spell != null ? spell.DisplayName : "(no spell)")} at the nearest enemy, free. Every {cd:0.#} s.";
    }
}
