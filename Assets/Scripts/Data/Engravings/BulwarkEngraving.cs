using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// "Allies standing beside the bearer take less damage."
///
/// The engraving that makes the deployment grid matter: its value depends entirely on where the
/// player puts the hero. Parked in a corner it protects one ally; in the middle of the formation it
/// protects four.
///
/// Adjacency is read once as the fight begins, from the formation the player arranged — not tracked
/// live. Units scatter the moment combat starts, so a live version would hand out and revoke the
/// bonus as the AI shuffled people around, which the player can neither see nor plan around.
/// </summary>
[CreateAssetMenu(menuName = "Data/Engraving/Bulwark", fileName = "Engraving_Bulwark")]
public class BulwarkEngraving : Engraving
{
    [Tooltip("Damage subtracted from each hit an adjacent ally takes, per tier.")]
    public float blockingPerTier = 6f;

    private void Reset()
    {
        engravingName = "Bulwark";
        description = "Allies adjacent to the bearer take less damage from every hit.";
    }

    public override void OnCombatStart(Entity owner, int tier)
    {
        var runManager = GameManager.Instance != null ? GameManager.Instance.runManager : null;
        if (runManager == null || owner == null) return;

        float amount = blockingPerTier * Mathf.Max(1, tier);

        foreach (var ally in runManager.Formation.AdjacentTo(owner))
        {
            if (ally == null || ally.Stats == null || ally.Stats.Blocking == null) continue;

            // Sourced by this engraving so the grant can be removed without disturbing the Blocking
            // an ally gets from its own armour.
            ally.Stats.Blocking.AddModifier(new Kryz.CharacterStats.StatModifier(
                amount, Kryz.CharacterStats.StatModType.Flat, this));
        }
    }

    /// <summary>
    /// Strip every grant this engraving made, from the whole company rather than a remembered list.
    ///
    /// An engraving is a ScriptableObject — one shared asset, however many heroes carry it — so it
    /// cannot hold "who I buffed this fight": a second bearer's OnCombatStart would overwrite the
    /// first's record and those grants would never be taken back, compounding every fight. Removing
    /// by source across the company needs no memory and is idempotent, so a second bearer's call
    /// simply finds nothing left to do.
    /// </summary>
    public override void OnCombatEnd(Entity owner, int tier)
    {
        var gameManager = GameManager.Instance;
        if (gameManager == null) return;

        // The roster, not the registry — a hero who fell mid-fight is deactivated and unregistered,
        // and would otherwise keep the grant through their revival into the next fight.
        foreach (var hero in gameManager.allyCharacters)
        {
            if (hero == null || hero.Stats == null || hero.Stats.Blocking == null) continue;
            hero.Stats.Blocking.RemoveAllModifiersFromSource(this);
        }
    }
}
