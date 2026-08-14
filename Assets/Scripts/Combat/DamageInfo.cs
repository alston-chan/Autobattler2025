/// <summary>
/// The payload of <see cref="Health.OnDamaged"/> — everything a feedback system needs to react to a
/// hit without reaching back into combat logic. This is the seed of the damage pipeline sketched in
/// Docs/Architecture.md: today it carries the fields damage numbers need (amount, crit), and a
/// damage-type enum slots in here later without touching a single call site.
///
/// A readonly struct: passed by value, allocation-free, safe to hand to any number of subscribers.
/// </summary>
public readonly struct DamageInfo
{
    /// <summary>Damage actually applied (a negative value would be a heal — reserved for later).</summary>
    public readonly float amount;

    /// <summary>The victim's health after this hit landed. Handy for low-HP tints and execute logic.</summary>
    public readonly float remainingHealth;

    /// <summary>Who dealt it. May be null for sourceless damage (burn, decay). Feedback-only.</summary>
    public readonly Entity source;

    /// <summary>True for a critical hit — drives the louder number, per the readability rule.</summary>
    public readonly bool isCrit;

    public DamageInfo(float amount, float remainingHealth, Entity source, bool isCrit)
    {
        this.amount = amount;
        this.remainingHealth = remainingHealth;
        this.source = source;
        this.isCrit = isCrit;
    }
}
