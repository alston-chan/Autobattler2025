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

        // Visual hit feedback
        if (_entity.character != null)
        {
            _entity.character.HitAsScale();
            StartCoroutine(_entity.character.HitAsRed(0.1f));
        }
        else if (_entity.monster != null)
        {
            _entity.monster.Spring();
            StartCoroutine(_entity.monster.HitAsRed(0.1f));
        }

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
