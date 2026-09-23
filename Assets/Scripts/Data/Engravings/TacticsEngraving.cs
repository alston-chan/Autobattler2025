using UnityEngine;

/// <summary>
/// An item that tells its wearer whom to go for (Docs/Combat.md, "Tactics come from items"). There
/// is no control for this on the card — a hero fights the way its gear says — so a berserker's grip
/// is what makes a berserker, and taking it off makes the hero ordinary again. Tier does nothing; a
/// tactic has no magnitude.
///
/// It also set a stance and a commitment until 2026-09-22, when both were removed: whom a unit
/// picks is the one part of how it fights that a player can see.
/// </summary>
[CreateAssetMenu(menuName = "Data/Engravings/Tactics", fileName = "Tactics")]
public class TacticsEngraving : Engraving
{
    [Tooltip("Whom the wearer goes for.")]
    public TargetMode targetMode = TargetMode.Nearest;

    public override void OnGranted(Entity owner, int tier)
    {
        if (owner != null) owner.SetTactics(this, targetMode);
    }

    public override void OnRevoked(Entity owner, int tier)
    {
        if (owner != null) owner.ClearTactics(this);
    }

    /// <summary>What it changes, as words: "Goes for the weakest".</summary>
    public override string DescribeTier(int tier) => "Goes for " + Tactics.Word(targetMode);
}
