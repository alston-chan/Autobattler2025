using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// "Allies standing beside this hero take less damage."
///
/// The seed that makes the deployment grid matter: its value depends entirely on where the player
/// puts the unit. Parked in a corner it protects one ally; placed in the middle of the formation it
/// protects four. That is positioning as a decision rather than decoration.
///
/// Adjacency is read once as the fight begins, from the formation the player arranged — not tracked
/// live. Units scatter the moment combat starts, so a live version would hand out and revoke the
/// bonus as the AI happened to shuffle people around, which the player can neither see nor plan.
/// </summary>
[CreateAssetMenu(menuName = "Data/Seed/Bulwark", fileName = "Seed_Bulwark")]
public class BulwarkSeed : HeroSeed
{
    [Tooltip("Damage subtracted from each hit an adjacent ally takes.")]
    public float blockingGranted = 6f;

    // Who was buffed this fight, so exactly those grants can be taken back afterwards.
    private readonly List<Entity> _buffed = new List<Entity>();

    private void Reset()
    {
        seedName = "Bulwark";
        description = "Allies adjacent to this hero take less damage from every hit.";
    }

    public override void OnCombatStart(Entity owner)
    {
        _buffed.Clear();

        var runManager = GameManager.Instance != null ? GameManager.Instance.runManager : null;
        if (runManager == null || owner == null) return;

        foreach (var ally in runManager.Formation.AdjacentTo(owner))
        {
            if (ally == null || ally.Stats == null || ally.Stats.Blocking == null) continue;

            // Sourced by this seed instance so the grant can be removed without disturbing the
            // Blocking an ally gets from its own armour.
            ally.Stats.Blocking.AddModifier(new Kryz.CharacterStats.StatModifier(
                blockingGranted, Kryz.CharacterStats.StatModType.Flat, this));
            _buffed.Add(ally);
        }
    }

    public override void OnCombatEnd(Entity owner)
    {
        foreach (var ally in _buffed)
        {
            if (ally == null || ally.Stats == null || ally.Stats.Blocking == null) continue;
            ally.Stats.Blocking.RemoveAllModifiersFromSource(this);
        }
        _buffed.Clear();
    }
}
