using UnityEngine;

/// <summary>
/// Ninja upper — Substitute: once per combat at half health, leave a scarecrow enemies target for
/// a moment and blink to the rear. The scarecrow is a decoy sprite where the ninja stood; the ninja
/// is out of sight for the same span, so every enemy that was on them picks again and finds the
/// scarecrow's spot empty of a target and the ninja gone. Tier lengthens the vanish.
/// </summary>
[CreateAssetMenu(menuName = "Data/Engravings/Substitute", fileName = "Substitute")]
public class SubstituteEngraving : Engraving
{
    [Range(0.05f, 0.95f)] public float healthThreshold = 0.5f;
    [Tooltip("Seconds out of sight at tier I; each tier adds half again.")]
    public float hideSeconds = 3f;
    [Tooltip("How far toward the rear the ninja lands when there is no board to name a back line.")]
    public float blinkDistance = 3f;
    [Tooltip("The supply sprite that stands in. Blank: a plain sign.")]
    public string scarecrowSprite = "";

    [System.NonSerialized] private bool _used;

    public override void OnCombatStart(Entity owner, int tier) => _used = false;

    public override void OnDamaged(Entity owner, HitInfo hit, int tier)
    {
        if (_used || owner == null || owner.isDead || owner.Health == null) return;
        if (owner.Health.currentHealth > owner.Health.maxHealth * healthThreshold) return;
        _used = true;

        float seconds = hideSeconds * (1f + 0.5f * (Mathf.Max(1, tier) - 1));
        Vector3 from = owner.transform.position;

        // The scarecrow: a decoy with a little health where the ninja stood. Everyone who was on the
        // ninja is taunted onto it, so the lock turns, and it is swept away when the vanish ends.
        var sprite = Supplies.FindSprite(owner, string.IsNullOrEmpty(scarecrowSprite) ? "ThrowingStar" : scarecrowSprite);
        Decoy.Spawn(owner, from, Mathf.Max(1f, owner.Health.maxHealth * 0.15f), seconds, sprite, "Scarecrow");
        AbilityFeedback.Announce(owner, "Substitute");

        owner.DropAggro(seconds);
        // To the back line — the rear cell of the ninja's own lane — and it counts as movement, so a
        // Shadowstep lower throws on the way. With no board, simply away from the nearest enemy.
        var nearest = Nearest(owner);
        Vector3 away = nearest != null ? (from - nearest.transform.position).normalized : (owner.isTeam ? Vector3.left : Vector3.right);
        owner.Relocate(BoardSnapshot.RearOf(owner, from + away * blinkDistance));
    }

    private static Entity Nearest(Entity owner)
    {
        Entity best = null; float bestD = float.MaxValue;
        foreach (var e in EntityRegistry.All)
        {
            if (e == null || e.isDead || e.isTeam == owner.isTeam) continue;
            float d = (e.transform.position - owner.transform.position).sqrMagnitude;
            if (d < bestD) { bestD = d; best = e; }
        }
        return best;
    }

    public override string DescribeTier(int tier)
        => $"Once per combat at {healthThreshold:P0} health: leave a scarecrow, vanish for {hideSeconds * (1f + 0.5f * (Mathf.Max(1, tier) - 1)):0.#} s and blink to the rear.";
}
