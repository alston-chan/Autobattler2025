using Assets.HeroEditor.Common.Scripts.CharacterScripts;
using Assets.HeroEditor.InventorySystem.Scripts.Data;
using Assets.HeroEditor.InventorySystem.Scripts.Enums;
using HeroEditor.Common.Enums;

/// <summary>
/// Puts a blade in the off hand.
///
/// The rig has always been able to show this — <c>WeaponType.MeleePaired</c> exists, the animator
/// carries SlashMeleePaired and JabMeleePaired, and Character.Initialize enables a second weapon
/// renderer for exactly that weapon type. Nothing ever reached it: HeroEditor's own equip path only
/// ever chooses between Melee1H and Melee2H by the two-handed tag, and never assigns a secondary
/// sprite, so the off hand stayed empty and those two clips never played.
///
/// Daggers are the class that gets it. There is no "paired" tag to read, and daggers are the one
/// melee class with no identity of its own — they behaved exactly like swords — so they are the
/// natural home for it rather than inventing data to describe something the rig already models.
/// The off-hand blade mirrors the main one, since a pair is a pair.
/// </summary>
public static class DualWield
{
    /// <summary>Whether this weapon is wielded in both hands as a pair.</summary>
    public static bool IsPaired(Item weapon) =>
        weapon != null && weapon.Params != null && weapon.Params.Class == ItemClass.Dagger;

    /// <summary>
    /// Whether this weapon leaves no hand free for a shield — a two-hander, or a pair.
    ///
    /// The rig hides a shield in paired mode anyway, which is worse than refusing it: the shield
    /// would go on being worn, and go on granting its Blocking, while showing nothing on screen to
    /// say why the hero was harder to hurt.
    /// </summary>
    public static bool OccupiesBothHands(Item weapon) =>
        weapon != null && weapon.IsWeapon && (weapon.IsTwoHanded || IsPaired(weapon));

    /// <summary>
    /// Bring the rig in line with what is equipped. Runs AFTER the normal equip pass, which has
    /// already chosen Melee1H and put the blade in the main hand — this promotes that to a pair, or
    /// clears the off hand again when something else is picked up.
    /// </summary>
    public static void Apply(Entity entity, Item weapon)
    {
        var character = entity != null ? entity.character : null;
        if (character == null) return;

        if (IsPaired(weapon))
        {
            character.WeaponType = WeaponType.MeleePaired;
            character.SecondaryMeleeWeapon = character.PrimaryMeleeWeapon;
        }
        else if (character.WeaponType == WeaponType.MeleePaired)
        {
            // Coming off a pair: hand the rig back to the ordinary one-handed grip, or the off-hand
            // blade would hang around under whatever was picked up next.
            character.WeaponType = WeaponType.Melee1H;
            character.SecondaryMeleeWeapon = null;
        }
        else
        {
            return;   // nothing to do — never was a pair, still isn't
        }

        // Initialize re-applies the sprites and the per-weapon renderer switches; UpdateAnimation
        // pushes the new WeaponType into the animator, which is what selects the paired clips.
        character.Initialize();
        character.UpdateAnimation();
    }
}
