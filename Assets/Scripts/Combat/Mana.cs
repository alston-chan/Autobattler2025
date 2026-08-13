using System;
using UnityEngine;

/// <summary>
/// The resource for cost abilities (ultimates). Per Docs/AbilityGrammar.md:
///
///   * charged by <b>participation, not a clock</b> — mainly from attacking, a little from
///     taking damage. Building Attack Speed therefore accelerates your ult too.
///   * <b>no passive regen</b>, or mana would just be a second cooldown.
///
/// Weapon/automatic abilities are cost 0 — they are the chargers, not the spenders.
/// </summary>
public class Mana : MonoBehaviour
{
    [Header("Pool")]
    public float maxMana = 100f;
    public float currentMana;

    [Header("Gain")]
    [Tooltip("Mana gained per basic (weapon) attack — the primary source.")]
    public float gainPerAttack = 12f;
    [Tooltip("Mana gained per point of damage taken — the secondary source, rewards frontliners.")]
    public float gainPerDamageTaken = 0.15f;

    [Header("Visual")]
    public Vector3 manaBarOffset = new Vector3(0f, 2.75f, 1f);
    public ResourceBar manaBar;

    /// <summary>Fired whenever mana changes. Args: current, max.</summary>
    public event Action<float, float> OnManaChanged;

    public bool IsFull => currentMana >= maxMana;
    public float Normalized => maxMana <= 0f ? 0f : currentMana / maxMana;

    public void Initialize(Entity entity, float startingPercent = 0f)
    {
        currentMana = Mathf.Clamp(maxMana * startingPercent, 0f, maxMana);
        Push();
    }

    /// <summary>Add mana (clamped). Negative values are ignored — use <see cref="TrySpend"/>.</summary>
    public void Gain(float amount)
    {
        if (amount <= 0f || currentMana >= maxMana) return;
        currentMana = Mathf.Min(maxMana, currentMana + amount);
        Push();
    }

    /// <summary>Spend if affordable. Returns false and changes nothing otherwise.</summary>
    public bool TrySpend(float cost)
    {
        if (cost <= 0f) return true;
        if (currentMana < cost) return false;
        currentMana -= cost;
        Push();
        return true;
    }

    /// <summary>Called by Health when the owner takes damage.</summary>
    public void OnDamageTaken(float damage) => Gain(damage * gainPerDamageTaken);

    /// <summary>Called when the owner lands a basic (weapon) attack.</summary>
    public void OnBasicAttack() => Gain(gainPerAttack);

    private void Push()
    {
        if (manaBar != null) manaBar.SetSize(Normalized);
        OnManaChanged?.Invoke(currentMana, maxMana);
    }
}
