using UnityEngine;

/// <summary>
/// A hunt helmet, generically: prefer enemies carrying a status whenever this hero picks a target
/// (Docs/SetDesigns.md — Cold Snap hunts the Chilled, Thirst the Bleeding, Pyre Sight the Burning).
/// When nobody carries it the ordinary pick stands, so it is never nonsense at the bell. Optionally
/// only for the bell pick, which makes it an opener instead.
/// </summary>
[CreateAssetMenu(menuName = "Data/Engravings/Hunt a Status", fileName = "Hunt")]
public class HuntStatusEngraving : Engraving
{
    [Tooltip("The word to hunt for.")]
    public Status status;
    [Tooltip("Consult only for the bell pick (an opener) rather than every pick (a hunt).")]
    public bool openerOnly = false;

    public override void OnCombatStart(Entity owner, int tier)
    {
        if (owner == null || status == null) return;
        System.Func<Entity, bool> filter = e => e != null && e.Statuses != null && e.Statuses.Has(status);
        if (openerOnly) owner.Opener = filter; else owner.Hunt = filter;
    }

    public override void OnCombatEnd(Entity owner, int tier)
    {
        if (owner == null) return;
        if (openerOnly) owner.Opener = null; else owner.Hunt = null;
    }

    public override string DescribeTier(int tier)
        => status != null ? $"You {(openerOnly ? "open on" : "hunt")} the {status.DisplayName} enemy." : "Hunts nothing yet.";
}
