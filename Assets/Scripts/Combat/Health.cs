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

    /// <summary>Fired when this entity takes damage. Args: damage amount, current health.</summary>
    public event Action<float, float> OnDamaged;

    /// <summary>Fired when this entity dies.</summary>
    public event Action OnDied;

    public bool IsDead { get; private set; }

    private Entity _entity;

    public void Initialize(Entity entity)
    {
        _entity = entity;
        currentHealth = maxHealth;
    }

    public void TakeDamage(float amount)
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

        OnDamaged?.Invoke(amount, currentHealth);

        if (!IsDead && currentHealth <= 0)
        {
            Die();
        }
    }

    private void Die()
    {
        IsDead = true;

        if (_entity.character != null)
        {
            _entity.character.SetState(CharacterState.DeathB);
        }
        else if (_entity.monster != null)
        {
            _entity.monster.Die();
        }

        OnDied?.Invoke();

        // Health bar cleanup is handled by Entity's OnDied subscriber
        Destroy(gameObject);
    }
}
