using UnityEngine;

/// <summary>
/// The one curve both resistances use: a hit is multiplied by 100 / (100 + rating).
///
/// It is the League of Legends curve, chosen for the property that makes it scale without a table:
/// every point of rating is one percent more <i>effective health</i> against that type, forever.
/// Fifty armour means a unit takes a third less; a hundred, half; there is no cap and no breakpoint,
/// so a reward that adds armour is worth the same to a knight who has plenty as to a mage who has
/// none. Flat reduction, which this replaced, could not say that: its worth depended on what was
/// swinging.
///
/// Presented as the reduction, never the rating — see <see cref="Fraction"/>. "Armour 18" is a
/// number the player would have to do this arithmetic on; "15% less" is the thing they want to know.
/// </summary>
public static class Mitigation
{
    /// <summary>The curve's constant: the rating at which a hit is halved.</summary>
    public const float Constant = 100f;

    /// <summary>What lands, after a rating. A rating of zero or less changes nothing.</summary>
    public static float Reduce(float amount, float rating) =>
        amount * (Constant / (Constant + Mathf.Max(0f, rating)));

    /// <summary>The fraction of a hit a rating takes off — the number to show a player.</summary>
    public static float Fraction(float rating) =>
        rating <= 0f ? 0f : rating / (Constant + rating);

    /// <summary>
    /// What the old flat Blocking became. Six armour per point matches the reduction on a median
    /// weapon hit (three blocking on twenty damage was 15%; eighteen armour is 15%). Capped, because
    /// a shield's twelve was half of any hit only by the old cap — seventy-two armour would have
    /// been more than that against every hit, and a shield is meant to be a large armour item, not
    /// a wall.
    /// </summary>
    public const float ArmorPerBlocking = 6f;
    public const float ArmorFromBlockingCap = 60f;
    public static float ArmorFromBlocking(float blocking) => Mathf.Min(Mathf.Max(0f, blocking) * ArmorPerBlocking, ArmorFromBlockingCap);
}
