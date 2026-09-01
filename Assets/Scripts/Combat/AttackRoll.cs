using UnityEngine;

/// <summary>
/// The two questions every attack asks when it lands: how much, and was it a good one.
///
/// Both answers were being written out by hand at each damage site, which is how they drifted
/// apart. Four melee attacks rolled for a critical hit; the bow, the wand, the firearm and every
/// thrown weapon never did — not by decision, but because each new attack copied a neighbour that
/// happened not to. Half the game's damage could not crit and nothing said so.
///
/// This is deliberately two one-line rules rather than a damage pipeline. The pipeline is worth
/// building when a mechanic needs every point of damage to pass through one place — armour,
/// resistances, lifesteal, on-hit triggers — and writing it now would be guessing at that shape.
/// What it does buy is that the next attack cannot quietly disagree about these two.
/// </summary>
public static class AttackRoll
{
    /// <summary>
    /// What this attacker actually hits for.
    ///
    /// The caster's Damage stat, not the number on the spell asset: gear, engravings and seeds all
    /// land on the stat, and an attack that read its own asset would ignore every one of them. The
    /// asset's value is the fallback for a caster with no stats at all.
    /// </summary>
    public static float DamageOf(Entity caster, float fallback) =>
        caster != null && caster.Stats != null && caster.Stats.Damage != null
            ? caster.Stats.Damage.Value
            : fallback;

    /// <summary>
    /// Whether this particular hit is a critical one.
    ///
    /// Rolled per hit rather than per attack, so a bomb landing among four enemies gives each of
    /// them their own chance rather than critting all of them or none.
    /// </summary>
    public static bool IsCrit(float chance) => chance > 0f && Random.value < chance;
}
