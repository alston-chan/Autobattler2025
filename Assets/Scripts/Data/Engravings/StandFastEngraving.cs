using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Elite Knight Helm — Stand Fast: at the bell, every enemy in the wearer's row is Taunted to it for
/// a few seconds. A front-liner's opening: the row facing the knight spends its first swings on
/// plate instead of on whoever stands behind it.
///
/// It was a tactic — the Hold stance and "the nearest" — until Hold was removed (a unit standing
/// idle at the bell read as a bug). "The nearest" alone is what every unit does, so the helm needed
/// an effect of its own, and taunting the row is the one a player can see: every enemy in it turns
/// to the knight with a mark over its head.
///
/// The row is read from the board frozen at the bell (<see cref="BoardSnapshot"/>), so an enemy's
/// Stand Fast works as a hero's does; the preview reads the formation as the player is arranging it.
/// </summary>
[CreateAssetMenu(menuName = "Data/Engravings/Stand Fast", fileName = "StandFast")]
public class StandFastEngraving : Engraving
{
    [Tooltip("Forced onto its source. The Taunted status.")]
    public Status taunted;
    [Tooltip("How long the row is taunted at Tier I.")]
    public float seconds = 3f;
    [Tooltip("Added per tier above I.")]
    public float secondsPerTier = 1f;

    public float SecondsAt(int tier) => seconds + secondsPerTier * (Mathf.Max(1, tier) - 1);

    public override void OnCombatStart(Entity owner, int tier)
    {
        if (owner == null || taunted == null) return;
        foreach (var enemy in BoardSnapshot.Facing(owner))
            if (enemy != null && !enemy.isDead && enemy.Statuses != null)
                enemy.Statuses.Apply(taunted, SecondsAt(tier), owner);
    }

    public override string DescribeTier(int tier) => $"At the bell, every enemy in your row is Taunted to you for {SecondsAt(tier):0.#} s.";

    public override string PreviewLabel(int tier) => $"TAUNTED · {SecondsAt(tier):0.#}s";

    public override void Preview(Entity owner, int tier, List<Badge> into)
    {
        var runManager = GameManager.Instance != null ? GameManager.Instance.runManager : null;
        if (runManager == null || owner == null || !owner.isTeam) return;
        foreach (var enemy in BoardSnapshot.Capture(runManager.Formation, planned: true).Facing(owner))
            into.Add(new Badge(enemy, this, tier));
    }
}
