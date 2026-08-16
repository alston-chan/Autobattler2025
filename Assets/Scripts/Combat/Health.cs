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
        OnRevived?.Invoke();
    }

    /// <summary>Full-heal without touching the death state — the between-encounter patch-up.</summary>
    public void HealToFull()
    {
        if (IsDead) return;
        currentHealth = maxHealth;
    }

    /// <summary>Fired when a dead entity is brought back, so visuals can be reset.</summary>
    public event Action OnRevived;

    /// <summary>
    /// Apply damage. <paramref name="source"/> and <paramref name="isCrit"/> are optional and only
    /// drive feedback — which way the body falls, who gets the kill freeze-frame, and whether the
    /// damage number reads as a crit. Damage itself never depends on them, so callers with no real
    /// attacker (burn, decay) can leave them defaulted.
    /// </summary>
    public void TakeDamage(float amount, Entity source = null, bool isCrit = false)
    {
        if (IsDead) return;

        currentHealth -= amount;
        if (healthBar != null) healthBar.SetSize(currentHealth / maxHealth);

        // Visual hit feedback — flash / shake / squash, all configurable on the HitFeedback component.
        // Hitstop and flinch are spell-driven (a spell calls ApplyHitstop / HitReact), not per-hit.
        if (_entity.HitFeedback != null)
            _entity.HitFeedback.Play(maxHealth > 0f ? amount / maxHealth : 0f);

        // Mana charges from participation — taking hits is the secondary source.
        if (_entity.Mana != null) _entity.Mana.OnDamageTaken(amount);

        OnDamaged?.Invoke(new DamageInfo(amount, currentHealth, source, isCrit));

        if (!IsDead && currentHealth <= 0)
        {
            Die(source);
        }
    }

    private void Die(Entity killer)
    {
        IsDead = true;
        currentHealth = 0f;
        if (healthBar != null) healthBar.SetSize(0f);

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
