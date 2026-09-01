using System.Collections.Generic;
using Assets.HeroEditor.InventorySystem.Scripts.Data;
using Assets.HeroEditor.InventorySystem.Scripts.Enums;

/// <summary>
/// What a unit can wear at once, and what wearing it means.
///
/// This used to be decided in three places that could not see each other — the equipment window, the
/// random loadout roll, and signature items — and each had to be taught every rule separately. The
/// cost was not theoretical: "a shield cannot share hands with a two-handed weapon" was fixed in the
/// roll, came back through signatures, and came back again when paired blades arrived, because each
/// new way of filling both hands had to be taught to all three. The rules live here now, so a fourth
/// weapon shape is learned once.
///
/// Two questions, kept apart on purpose:
///
/// <list type="bullet">
/// <item><b>What can coexist</b> — <see cref="Normalise"/>, which works on a plain list of items and
/// so can run before a unit exists, which is what the startup path needs.</item>
/// <item><b>What it means to wear it</b> — <see cref="ApplyTo"/>, which needs a live unit because it
/// touches stats, spells and the rig.</item>
/// </list>
/// </summary>
public static class Loadout
{
    /// <summary>
    /// Whether this weapon leaves no hand free for a shield — a two-hander, or a pair of blades.
    ///
    /// Paired blades are the case worth stating: the rig hides a shield in paired mode, so a shield
    /// worn alongside them stays equipped and goes on granting Blocking while showing nothing on
    /// screen to explain why the unit is harder to hurt.
    /// </summary>
    public static bool OccupiesBothHands(Item weapon) =>
        weapon != null && weapon.IsWeapon && (weapon.IsTwoHanded || DualWield.IsPaired(weapon));

    /// <summary>The weapon in a set of equipped items, or null if the unit is unarmed.</summary>
    public static Item WeaponIn(IEnumerable<Item> equipped)
    {
        if (equipped == null) return null;

        foreach (var item in equipped)
            if (item != null && item.Params != null && item.Params.Type == ItemType.Weapon) return item;

        return null;
    }

    /// <summary>
    /// Whether <paramref name="worn"/> cannot share a body with <paramref name="keep"/>.
    ///
    /// The single statement of the rule. Both callers need it in a different shape — the equipment
    /// window walks worn items one at a time so it can hand each displaced piece back to the bag,
    /// while <see cref="Normalise"/> rewrites a whole list at once — but they must never disagree
    /// about what conflicts, which is what having written it twice used to guarantee.
    /// </summary>
    public static bool Conflicts(Item keep, Item worn)
    {
        if (keep == null || worn == null || ReferenceEquals(keep, worn)) return false;

        bool bothHands = OccupiesBothHands(keep);

        return (bothHands && worn.IsShield)
            || (keep.IsShield && OccupiesBothHands(worn))
            // Firearms are their own exclusion: HeroEditor cannot equip them on these rigs at all,
            // but the rule is kept so the day that changes it is already correct.
            || (keep.IsFirearm && (worn.IsShield || OccupiesBothHands(worn)))
            || ((keep.IsShield || bothHands) && worn.IsWeapon && worn.IsFirearm);
    }

    /// <summary>
    /// Make a set of equipped items legal, treating <paramref name="keep"/> as the piece that wins
    /// any argument — the item just equipped, or the signature that defines a hero.
    ///
    /// Returns what was removed, so a caller that must replace it (a shield signature displacing the
    /// only weapon, say) can see that it needs to.
    /// </summary>
    public static List<Item> Normalise(List<Item> equipped, Item keep)
    {
        var removed = new List<Item>();
        if (equipped == null || keep == null) return removed;

        for (int i = equipped.Count - 1; i >= 0; i--)
        {
            if (!Conflicts(keep, equipped[i])) continue;

            removed.Add(equipped[i]);
            equipped.RemoveAt(i);
        }

        return removed;
    }

    /// <summary>
    /// Tell a unit what it is holding: the weapon's class, the basic attack that comes with it, and
    /// whether it fills both hands.
    ///
    /// Deliberately unconditional. This once lived inside the spell-slot rebuild, which at startup
    /// only ran for characters that had authored spellbooks — so a hero without one was left
    /// swinging whatever attack they started with, and every hero who did carry a book worked by
    /// coincidence.
    /// </summary>
    public static void ApplyTo(Entity entity, Item weapon)
    {
        if (entity == null) return;

        // The rig cannot tell a wand from a sword — both are Melee1H — so the item's own class is
        // the only record of the difference.
        entity.SetWeaponClass(weapon != null && weapon.Params != null
            ? weapon.Params.Class : ItemClass.Unknown);

        if (weapon != null) WeaponAttacks.Apply(entity, weapon);

        // Last, and in this order: a gun and a pair of blades are competing answers to what the
        // hands are doing, so the gun is asked first and the pair only when there is no gun.
        FirearmRig.Apply(entity, weapon);
        if (!FirearmRig.IsFirearm(weapon)) DualWield.Apply(entity, weapon);
    }
}
