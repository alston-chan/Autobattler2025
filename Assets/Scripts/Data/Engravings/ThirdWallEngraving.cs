using UnityEngine;

/// <summary>
/// Wall Keeper upper — Third Wall: every third hit on you rings off a wall of light that flares out
/// and knocks the attacker back a unit. The hit has already landed when the hook fires, so the wall
/// gives the damage back and answers the attacker; on screen it reads as the hit bouncing off.
/// Tier brings the wall sooner: every third hit, then every other.
/// </summary>
[CreateAssetMenu(menuName = "Data/Engravings/Third Wall", fileName = "ThirdWall")]
public class ThirdWallEngraving : Engraving
{
    [Tooltip("Hits between walls at tier I. Tier III makes it every second hit.")]
    public int hitsPerWall = 3;
    [Tooltip("How hard the attacker is knocked back.")]
    public float knockback = 6f;

    [System.NonSerialized] private int _hits;

    public override void OnCombatStart(Entity owner, int tier) => _hits = 0;

    public override void OnDamaged(Entity owner, HitInfo hit, int tier)
    {
        if (owner == null || owner.isDead || hit.amount <= 0f) return;
        int every = Mathf.Max(2, hitsPerWall - (Mathf.Max(1, tier) - 1) / 2);
        _hits++;
        if (_hits % every != 0) return;

        owner.Health.Heal(hit.amount, owner);
        AbilityFeedback.Announce(owner, "Third Wall");
        if (hit.source != null && !hit.source.isDead)
        {
            Vector3 dir = hit.source.transform.position - owner.transform.position;
            hit.source.ApplyKnockback(dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.right, knockback);
        }
    }

    public override string DescribeTier(int tier)
    {
        int every = Mathf.Max(2, hitsPerWall - (Mathf.Max(1, tier) - 1) / 2);
        return $"Every {(every == 2 ? "second" : every == 3 ? "third" : every + "th")} hit on you rings off a wall of light and knocks the attacker back.";
    }
}
