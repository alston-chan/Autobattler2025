using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// The items a fight can drop. A pool rather than a fixed list per encounter, so the same content can
/// back many fights and a run stays different each time.
///
/// Drops are what make resonance a decision: a freed slot is only worth what can be put in it, and a
/// fresh item only interesting because it competes with what a hero has already sunk attunement into
/// (Docs/Resonance.md).
/// </summary>
[CreateAssetMenu(menuName = "Data/Reward Pool", fileName = "RewardPool")]
public class RewardPool : ScriptableObject
{
    [Tooltip("HeroEditor item ids this pool can offer. Ids that carry an Engraving (see " +
             "ResonanceDatabase) are the interesting ones; plain gear is the filler that makes them " +
             "feel like finds.")]
    [ValueDropdown("ItemIds")]
    public List<string> itemIds = new List<string>();

    private static IEnumerable<ValueDropdownItem<string>> ItemIds() => Catalog.ItemIds();

    /// <summary>
    /// Draw <paramref name="count"/> distinct items. Distinct because an offer of the same item three
    /// times isn't a choice.
    /// </summary>
    public List<string> Draw(int count)
    {
        var drawn = new List<string>();
        if (itemIds == null || itemIds.Count == 0) return drawn;

        var remaining = new List<string>(itemIds);
        while (drawn.Count < count && remaining.Count > 0)
        {
            int index = Random.Range(0, remaining.Count);
            drawn.Add(remaining[index]);
            remaining.RemoveAt(index);
        }
        return drawn;
    }
}
