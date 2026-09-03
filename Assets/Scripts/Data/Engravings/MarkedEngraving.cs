using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// "The first enemy in the bearer's lane starts the fight already wounded."
///
/// The third positional engraving, and the first to read the <i>enemy's</i> half of the grid.
/// Bulwark and Vanguard make where a hero stands matter relative to the company; this makes it
/// matter relative to the opposition — the scouting step turned into a formation decision. The map
/// says a sniper waits in the back rank; a player who wants it softened has to put this bearer in
/// the back rank too, and give up whatever else that cell was for.
///
/// Applied as a starting condition, not a hit. The target simply begins below full health — no
/// flash, no hit-stop, no kill credit, and no mana for the victim. A "hit" landed before the fight
/// begins would charge the enemy's ultimate for free.
///
/// Read at the start of the fight, when enemies still stand on their spawn cells; a second later
/// everyone has moved and "across" means nothing. Two grants on the same target (worn and banked)
/// set the same floor rather than cutting twice, so the deeper cut wins and nothing stacks.
/// </summary>
[CreateAssetMenu(menuName = "Data/Engraving/Marked", fileName = "Engraving_Marked")]
public class MarkedEngraving : Engraving
{
    [Tooltip("Fraction of max health the target is missing at Tier I. 0.2 means it starts at 80%.")]
    [Range(0f, 0.9f)] public float startingCut = 0.2f;
    [Tooltip("Extra fraction per tier above I. 0.1 makes Tier II start at 70% and Tier III at 60%.")]
    [Range(0f, 0.5f)] public float extraCutPerTier = 0.1f;

    private void Reset()
    {
        engravingName = "Marked";
        description = "The enemy deployed directly across from the bearer starts the fight wounded.";
    }

    /// <summary>How much of its health the target is missing at <paramref name="tier"/>, 0..1.</summary>
    public float CutFor(int tier) =>
        Mathf.Clamp01(startingCut + extraCutPerTier * (Mathf.Max(1, tier) - 1));

    public override string DescribeTier(int tier) =>
        $"The enemy across from the bearer starts the fight at {(1f - CutFor(tier)) * 100f:0}% health.";

    /// <summary>The badge shown over the enemy this will mark: "MARKED · 80%".</summary>
    public override string PreviewLabel(int tier) => $"MARKED · {(1f - CutFor(tier)) * 100f:0}%";

    // Two bearers across from one enemy do not cut twice: the floor is set by the deeper cut, so
    // the merged badge is the base one — strongest wins, with the count.

    public override void Preview(Entity owner, int tier, List<Badge> into)
    {
        var target = TargetFor(owner, planned: true);
        if (target != null) into.Add(new Badge(target, this, tier));
    }

    public override void OnCombatStart(Entity owner, int tier)
    {
        var target = TargetFor(owner, planned: false);
        if (target == null || target.Health == null) return;

        float floor = target.Health.maxHealth * (1f - CutFor(tier));
        if (target.Health.currentHealth <= floor) return;

        target.Health.currentHealth = floor;
        target.Health.RefreshBar();
    }

    /// <summary>
    /// Whom this would mark from where the bearer stands — or, for a preview, from where the player
    /// is about to put it down. One lookup for both the badge and the bell, so the preview can never
    /// promise a different enemy than the effect delivers.
    /// </summary>
    private static Entity TargetFor(Entity owner, bool planned)
    {
        var runManager = GameManager.Instance != null ? GameManager.Instance.runManager : null;
        if (runManager == null || owner == null || !owner.isTeam) return null;

        // Across is the first enemy in the bearer's lane, not the mirror cell: an enemy standing
        // behind another is covered by it, on this side as on ours.
        return BoardSnapshot.Capture(runManager.Formation, planned).Across(owner);
    }
}
