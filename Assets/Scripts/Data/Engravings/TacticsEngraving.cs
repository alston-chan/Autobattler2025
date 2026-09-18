using UnityEngine;

/// <summary>
/// An item that tells its wearer how to fight: the stance it takes, whom it goes for, how long it
/// stays on a target it cannot reach (Docs/Combat.md, "Tactics come from items"). There is no
/// control for these on the card any more — a hero fights the way its gear says, the way it casts
/// the verb its weapon teaches — so a berserker's helm is what makes a berserker, and taking it off
/// makes the hero ordinary again. Each part is optional: a helm can set the stance and leave the
/// target rule alone. Tier does nothing; a tactic has no magnitude.
/// </summary>
[CreateAssetMenu(menuName = "Data/Engravings/Tactics", fileName = "Tactics")]
public class TacticsEngraving : Engraving
{
    [Tooltip("How the wearer uses the space between it and the enemy. Auto leaves the wearer's own.")]
    public Stance stance = Stance.Auto;

    [Tooltip("Whether this item decides whom the wearer goes for.")]
    public bool setsTarget;
    [Sirenix.OdinInspector.ShowIf("setsTarget")]
    public TargetMode targetMode = TargetMode.Nearest;

    [Tooltip("Whether this item decides how long the wearer stays on a target it cannot reach.")]
    public bool setsCommitment;
    [Sirenix.OdinInspector.ShowIf("setsCommitment")]
    public Commitment commitment = Commitment.Balanced;

    public override void OnGranted(Entity owner, int tier)
    {
        if (owner == null) return;
        owner.SetTactics(this, stance, setsTarget ? targetMode : (TargetMode?)null, setsCommitment ? commitment : (Commitment?)null);
    }

    public override void OnRevoked(Entity owner, int tier)
    {
        if (owner == null) return;
        owner.ClearTactics(this);
    }

    /// <summary>What it changes, as words: "Holds · goes for the weakest · never lets go".</summary>
    public override string DescribeTier(int tier)
    {
        var parts = new System.Collections.Generic.List<string>();
        if (stance != Stance.Auto) parts.Add(Tactics.Word(stance));
        if (setsTarget) parts.Add("goes for " + Tactics.Word(targetMode));
        if (setsCommitment) parts.Add(Tactics.Word(commitment));
        return parts.Count > 0 ? string.Join(" · ", parts) : "Changes nothing";
    }
}
