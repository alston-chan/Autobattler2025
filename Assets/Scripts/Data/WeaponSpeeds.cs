using Assets.HeroEditor.InventorySystem.Scripts.Enums;

/// <summary>
/// How quickly each kind of weapon swings, as a percentage change to Attack Speed.
///
/// The vendor catalogue gives every weapon the same single property — Damage 7 — so without this a
/// dagger and a greataxe would be identical apart from their sprite, and equipping one over the
/// other would move no number the player can see. Speed is what separates them: a dagger is a flurry
/// and an axe is a commitment, and that difference should be legible before the fight starts.
///
/// The scale is anchored so that NO weapon is slower than carrying nothing. An earlier version
/// centred it on the sword, which read fine on paper and wrong in the hand: a hero with bare fists
/// showed 1 attack/sec, and picking up a bow dropped them to 0.9. The bow is the better weapon by a
/// wide margin — 17.1 damage per second against 12 — but the stat line the player was watching went
/// down, and a number going down on equip reads as a mistake no matter what the arithmetic says.
///
/// So the heaviest weapons sit at bare-handed pace and everything else is faster. The ordering
/// between classes is unchanged; only the anchor moved.
///
/// This is a fallback, not a law: an item carrying an explicit <see cref="PropertyId.ChargeSpeed"/>
/// property uses that instead, so a specific weapon can break its class's rule without the table
/// needing to know about it.
/// </summary>
public static class WeaponSpeeds
{
    /// <summary>
    /// Attack-speed delta for a weapon class, as a fraction (+0.55 = 55% faster than bare hands).
    /// Zero for anything that isn't a weapon class — armour and trinkets don't change how fast you
    /// swing — and never negative, so equipping a weapon can't cost attack speed.
    /// </summary>
    public static float HandlingFor(ItemClass weaponClass) => weaponClass switch
    {
        ItemClass.Dagger => 0.55f,
        ItemClass.Claw => 0.5f,
        ItemClass.Fang => 0.45f,
        ItemClass.Wand => 0.35f,
        ItemClass.Sword => 0.2f,
        ItemClass.Bow => 0.1f,
        ItemClass.Lance => 0.1f,
        ItemClass.Axe => 0.05f,
        ItemClass.Firearm => 0f,
        ItemClass.Blunt => 0f,
        _ => 0f
    };
}
