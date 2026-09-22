using System;
using System.Collections;
using Assets.HeroEditor.Common.Scripts.CharacterScripts;
using Assets.FantasyMonsters.Common.Scripts;
using UnityEngine;

/// <summary>
/// Manages HP, damage, death, and related visual feedback.
/// Fires events so other systems can react without coupling.
/// </summary>
public class Health : MonoBehaviour
{
    [Header("Health")]
    public float maxHealth = 100f;
    public float currentHealth;
    public Vector3 healthBarOffset = new Vector3(0, 3.0f, 1);

    public ResourceBar healthBar;

    [Header("Shield")]
    [Tooltip("An absorb pool that takes hits before health. Granted by effects; expires or breaks.")]
    public float shield;
    private float _shieldExpiresAt = float.PositiveInfinity;

    /// <summary>Put shield on. Zero or negative duration lasts until broken or the fight ends.</summary>
    public void AddShield(float amount, float duration = 0f)
    {
        if (IsDead || amount <= 0f) return;
        bool had = shield > 0f;
        shield += amount;
        float until = duration > 0f ? Time.time + duration : float.PositiveInfinity;
        _shieldExpiresAt = had ? Mathf.Max(_shieldExpiresAt, until) : until;
        var word = StatusLibrary.Shielded;
        if (word != null && _entity != null && _entity.Statuses != null) _entity.Statuses.Apply(word, 0f);
    }

    /// <summary>Take the shield off. <paramref name="broken"/> says a hit did it rather than time or the bell.</summary>
    public void ClearShield(bool broken)
    {
        if (shield <= 0f) return;
        shield = 0f;
        _shieldExpiresAt = float.PositiveInfinity;
        var word = StatusLibrary.Shielded;
        if (word != null && _entity != null && _entity.Statuses != null) _entity.Statuses.Remove(word);
        CombatEvents.RaiseShieldEnded(_entity, broken);
    }

    /// <summary>Called each fight frame: a timed shield lapses.</summary>
    public void TickShield()
    {
        if (shield > 0f && Time.time >= _shieldExpiresAt) ClearShield(broken: false);
    }

    /// <summary>Restore health, never above the maximum. Heals do nothing to the dead.</summary>
    public void Heal(float amount, Entity source = null)
    {
        if (IsDead || amount <= 0f) return;
        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        RefreshBar();
    }

    /// <summary>Fired when this entity takes damage. See <see cref="DamageInfo"/> for the payload.</summary>
    public event Action<DamageInfo> OnDamaged;

    /// <summary>Fired when this entity dies.</summary>
    public event Action OnDied;

    public bool IsDead { get; private set; }

    private Entity _entity;

    public void Initialize(Entity entity)
    {
        _entity = entity;
        currentHealth = maxHealth;

        // Equipment grants max health through Stats, and this used to never hear about it: a hero
        // whose gear was worth +23 max health still had maxHealth at the base 1000 while the stat
        // panel read 1023, so they walked into every fight apparently already wounded — and the 23
        // was inert, since nothing ever turned it into health that could absorb a hit.
        if (_entity.Stats != null) _entity.Stats.OnStatsChanged += SyncMaxFromStats;
    }

    private void OnDestroy()
    {
        if (_entity != null && _entity.Stats != null) _entity.Stats.OnStatsChanged -= SyncMaxFromStats;
    }

    /// <summary>
    /// Bring max health in line with <see cref="EntityStats.MaxHealth"/>, which is where equipment
    /// and engravings land.
    ///
    /// Gaining max health grants that much health with it, so putting on a stout helm is an
    /// immediate gain rather than an instant wound — the alternative reads as being hurt by your own
    /// armour. Losing it only clamps, so taking the helm off cannot kill anyone outright.
    /// </summary>
    public void SyncMaxFromStats()
    {
        if (_entity == null || _entity.Stats == null || _entity.Stats.MaxHealth == null) return;

        float updated = _entity.Stats.MaxHealth.Value;
        if (Mathf.Approximately(updated, maxHealth)) return;

        float gained = Mathf.Max(0f, updated - maxHealth);
        maxHealth = updated;
        currentHealth = Mathf.Clamp(currentHealth + gained, 0f, maxHealth);

        RefreshBar();
    }

    /// <summary>
    /// Push current health onto the bar.
    ///
    /// Every path that changes health has to end here, and two did not: <see cref="Revive"/> and
    /// <see cref="HealToFull"/> — the two the between-fight patch-up uses. A company walked into its
    /// second fight at full health behind bars still showing how the last one ended. The revived
    /// were worst: <see cref="Die"/> empties the bar, and reviving refilled the health without ever
    /// refilling the bar, so a hero back on their feet looked dead.
    /// </summary>
    public void RefreshBar()
    {
        if (healthBar != null && maxHealth > 0f) healthBar.SetSize(currentHealth / maxHealth);
    }

    /// <summary>
    /// Clear the death state and restore full health — between encounters the company is patched up
    /// and the fallen are back on their feet, because a run ends only on a full wipe
    /// (Docs/RunLoop.md). Fires <see cref="OnRevived"/> so feedback can undo whatever the death
    /// sequence did to the body.
    /// </summary>
    public void Revive()
    {
        IsDead = false;
        currentHealth = maxHealth;
        shield = 0f; _shieldExpiresAt = float.PositiveInfinity;
        RefreshBar();
        OnRevived?.Invoke();
    }

    /// <summary>Full-heal without touching the death state — the between-encounter patch-up.</summary>
    public void HealToFull()
    {
        if (IsDead) return;
        currentHealth = maxHealth;
        RefreshBar();
    }

    /// <summary>Fired when a dead entity is brought back, so visuals can be reset.</summary>
    public event Action OnRevived;

    /// <summary>
    /// Apply damage. <paramref name="source"/> and <paramref name="isCrit"/> are optional and only
    /// drive feedback — which way the body falls, who gets the kill freeze-frame, and whether the
    /// damage number reads as a crit. Damage itself never depends on them, so callers with no real
    /// attacker (burn, decay) can leave them defaulted.
    /// </summary>
    /// <param name="quiet">No flash, squash or shake: for damage that ticks (a pool, a blade's pass), which would otherwise hold the body white.</param>
    public void TakeDamage(float amount, Entity source = null, bool isCrit = false, bool quiet = false, DamageKind kind = DamageKind.Hit)
    {
        if (IsDead) return;

        // Nothing is hurt outside a fight. A round ends the instant the last enemy dies, but its
        // arrow is still in the air; letting it land killed a hero AFTER the revive pass for the
        // next fight had already run, so the hero lay dead through the map screen and began the
        // next fight in its death pose with nothing left to bring it back.
        if (_entity != null && !_entity.IsFighting) return;

        float incoming = amount;
        amount = ApplyBlocking(amount);

        // Statuses are the one other thing that scales a hit — a Mark on the victim, a curse on the
        // attacker — and they apply here so every spell, old or new, respects them.
        if (_entity.Statuses != null) amount *= _entity.Statuses.DamageTakenMultiplier;
        if (source != null && source.Statuses != null) amount *= source.Statuses.DamageDealtMultiplier;

        // A shield takes the hit first. What it absorbs counts as blocked for attunement, so a piece
        // that attunes by blocking advances when its shield does the work.
        if (shield > 0f)
        {
            float absorbed = Mathf.Min(shield, amount);
            shield -= absorbed;
            amount -= absorbed;
            if (shield <= 0f) ClearShield(broken: true);
        }

        // Resonance counters tick on the blow itself, not at the end of the fight, so a shield that
        // attunes by blocking advances exactly when it blocks.
        if (_entity.Resonance != null)
            _entity.Resonance.Accrue(ResonanceRequirement.DamageBlocked, incoming - amount);
        if (source != null && source.Resonance != null)
            source.Resonance.Accrue(ResonanceRequirement.DamageDealt, amount);

        currentHealth -= amount;
        RefreshBar();

        // Visual hit feedback — flash / shake / squash, all configurable on the HitFeedback component.
        // Hitstop and flinch are spell-driven (a spell calls ApplyHitstop / HitReact), not per-hit.
        if (_entity.HitFeedback != null)
            if (!quiet) _entity.HitFeedback.Play(maxHealth > 0f ? amount / maxHealth : 0f);

        // Mana charges from participation — taking hits is the secondary source.
        if (_entity.Mana != null) _entity.Mana.OnDamageTaken(amount);

        OnDamaged?.Invoke(new DamageInfo(amount, currentHealth, source, isCrit, incoming - amount, kind));
        CombatEvents.RaiseHit(new HitInfo(source, _entity, amount, isCrit, currentHealth <= 0f));

        if (!IsDead && currentHealth <= 0)
        {
            Die(source);
        }
    }

    /// <summary>
    /// Subtract the target's Blocking from an incoming hit. Blocking has existed as a stat — items
    /// grant it, seeds add to it — but nothing ever read it, so armour was decorative and damage was
    /// applied raw.
    ///
    /// A hit always lands for at least <see cref="MinimumDamage"/>, so stacking enough Blocking can
    /// blunt an attacker but never make a unit immune to one.
    /// </summary>
    private float ApplyBlocking(float amount)
    {
        if (_entity == null || _entity.Stats == null || _entity.Stats.Blocking == null) return amount;

        float blocking = _entity.Stats.Blocking.Value;
        if (blocking <= 0f) return amount;

        // Blocking takes at most half of any hit. Flat blocking against weapons that swing for ten
        // to forty turned a fifteen-blocking knight into a wall that took one damage a hit, and a
        // 3v3 into a minute of tapping; half through keeps armour worth wearing and fights ending.
        float blocked = Mathf.Min(blocking, amount * MaxBlockedFraction);
        return Mathf.Max(MinimumDamage, amount - blocked);
    }

    /// <summary>Floor on a blocked hit, so damage reduction can never fully negate an attack.</summary>
    private const float MinimumDamage = 1f;

    /// <summary>The most of a hit blocking may take: half.</summary>
    private const float MaxBlockedFraction = 0.5f;

    private void Die(Entity killer)
    {
        if (killer != null && killer.Resonance != null)
            killer.Resonance.Accrue(ResonanceRequirement.EnemiesKilled, 1f);

        // Die is the one place that knows who struck last, so it is where a kill can be credited.
        CombatTelemetry.RecordKill(killer);
        CombatEvents.RaiseKill(killer, _entity);

        IsDead = true;
        currentHealth = 0f;
        RefreshBar();

        // Fire BEFORE the death sequence: GameManager decides win/lose from IsDead, and it should
        // not have to wait out a second of corpse animation to call the round.
        OnDied?.Invoke();

        // The sequence owns the destroy — it plays the death animation, holds the corpse, fades it
        // out, and only then despawns. Destroying here (as this used to) meant the death animation
        // was set and thrown away on the same frame, so it was never drawn.
        var death = GetComponent<DeathFeedback>();
        if (death != null)
        {
            death.PlayAndDespawn(killer);
        }
        else
        {
            if (_entity.character != null) _entity.character.SetState(CharacterState.DeathB);
            else if (_entity.monster != null) _entity.monster.Die();
            Destroy(gameObject);
        }
    }
}
