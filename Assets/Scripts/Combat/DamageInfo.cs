/// <summary>
/// What mitigates a hit. Physical is reduced by Armor, Magical by MagicResist, True by nothing.
/// Both resistances use the same curve (<see cref="Mitigation"/>): every point is one percent
/// more effective health against that type, with no cap and no breakpoints.
/// </summary>
public enum DamageType { Physical, Magical, True }

/// <summary>What kind of hurt this was — the one distinction feedback needs to make today.</summary>
public enum DamageKind
{
    /// <summary>A weapon, a spell, a status: the ordinary number.</summary>
    Hit,
    /// <summary>A thrown body hitting a wall or another body (CombatPhysics). Shown as SLAM, so the player can see when physics hurt someone.</summary>
    Slam,
}

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

    /// <summary>What mitigation — armour or magic resist, then any shield — took off this hit before it landed. Feedback and telemetry only.</summary>
    public readonly float blocked;

    /// <summary>A hit, or a slam.</summary>
    public readonly DamageKind kind;

    /// <summary>Physical, magical or true — what it was resisted by.</summary>
    public readonly DamageType type;

    public DamageInfo(float amount, float remainingHealth, Entity source, bool isCrit, float blocked = 0f, DamageKind kind = DamageKind.Hit, DamageType type = DamageType.Physical)
    {
        this.type = type;
        this.amount = amount;
        this.remainingHealth = remainingHealth;
        this.source = source;
        this.isCrit = isCrit;
        this.blocked = blocked;
        this.kind = kind;
    }
}
