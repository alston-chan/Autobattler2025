using System;
using System.Collections.Generic;

/// <summary>
/// Which variation to play next — a shuffled bag, not a random draw.
///
/// A fight here is ten units swinging for two minutes, so a hit sound fires hundreds of times. Two
/// things make that read as cheap, and both are avoidable:
///
/// <list type="bullet">
/// <item>The same clip twice in a row. The ear catches an immediate repeat instantly, and from that
/// moment it hears a <i>sample</i> rather than a sword. A uniform random pick over four clips does
/// this a quarter of the time.</item>
/// <item>One clip that happens not to come up for twenty swings. Random draws clump; the player's
/// impression of the library is the handful of clips they heard most.</item>
/// </list>
///
/// So: deal all the variations in a shuffled order, use each one once, then reshuffle — and if the
/// new deal opens with the clip the old one closed on, swap it away. Every clip gets heard equally
/// often, and none is ever heard back to back.
///
/// The roll is injectable so the shuffle can be pinned down in a test. Nothing here touches Unity,
/// which is the point: this is the part that is easy to get subtly wrong.
/// </summary>
public class SfxBag
{
    private readonly Func<int, int> _roll;
    private readonly List<int> _order = new List<int>();
    private int _next;
    private int _last = -1;

    /// <param name="roll">Returns a value in [0, max). Defaults to Unity's RNG.</param>
    public SfxBag(Func<int, int> roll = null)
    {
        _roll = roll ?? (max => UnityEngine.Random.Range(0, max));
    }

    /// <summary>
    /// The next index into a bank of <paramref name="count"/> clips, or -1 when there are none.
    /// A bank that grows or shrinks (clips dropped in while the game runs) just gets a fresh deal.
    /// </summary>
    public int Next(int count)
    {
        if (count <= 0) return -1;
        if (count == 1) return _last = 0;

        if (_next >= _order.Count || _order.Count != count) Refill(count);

        _last = _order[_next++];
        return _last;
    }

    private void Refill(int count)
    {
        _order.Clear();
        for (int i = 0; i < count; i++) _order.Add(i);

        // Fisher-Yates.
        for (int i = count - 1; i > 0; i--)
        {
            int j = _roll(i + 1);
            (_order[i], _order[j]) = (_order[j], _order[i]);
        }

        // The seam between two deals is the only place a repeat can happen. Push it away rather
        // than reshuffling until it goes: reshuffling could in principle never terminate.
        if (_order[0] == _last) (_order[0], _order[count - 1]) = (_order[count - 1], _order[0]);

        _next = 0;
    }
}
