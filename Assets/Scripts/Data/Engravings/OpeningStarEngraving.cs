using UnityEngine;

/// <summary>
/// Ninja helmet — Opening Star: at the bell, throw a shuriken at your opener; it is Marked.
/// The first Mark of the fight, so the dagger's Backstab and the lower's Shadowstep have someone
/// to find before anyone has swung. Tier lengthens the Mark.
/// </summary>
[CreateAssetMenu(menuName = "Data/Engravings/Opening Star", fileName = "OpeningStar")]
public class OpeningStarEngraving : Engraving
{
    [Tooltip("The Mark status this puts on the opener.")]
    public Status mark;
    [Tooltip("Seconds the Mark lasts at tier I; each tier adds the same again.")]
    public float markSecondsPerTier = 4f;
    [Tooltip("Star damage as a share of the hero's Damage stat.")]
    public float damageScale = 0.6f;
    public float speed = 16f;
    public float hitRadius = 0.6f;
    [Range(0f, 1f)] public float critChance = 0.1f;

    public override void OnCombatStart(Entity owner, int tier)
    {
        if (owner == null || owner.isDead) return;
        // The same pick CombatAI will make a frame later, so the star flies at the promised opener.
        var opener = Targeting.Choose(owner, owner.EffectiveTarget, null, 0f);
        if (opener == null) return;
        float duration = markSecondsPerTier * Mathf.Max(1, tier);
        Supplies.ThrowStar(owner, opener,
            AttackRoll.DamageOf(owner, 10f) * damageScale, speed, hitRadius, 540f, critChance, mark, duration);
    }

    public override string DescribeTier(int tier)
        => $"At the bell, a shuriken at your opener: Marked for {markSecondsPerTier * Mathf.Max(1, tier):0} s.";
}
