using Assets.HeroEditor.InventorySystem.Scripts.Enums;

/// <summary>
/// How quickly each kind of weapon swings, as a percentage change to Attack Speed.
///
/// The vendor catalogue gives every weapon the same single property — Damage 7 — so without this a
/// dagger and a greataxe would be identical apart from their sprite, and equipping one over the
/// other would move no number the player can see. Speed is what separates them: a dagger is a flurry
/// and an axe is a commitment, and that difference should be legible before the fight starts.
///
/// Sword is deliberately the 1.0 baseline, so these read as "faster/slower than a sword".
///
/// This is a fallback, not a law: an item carrying an explicit <see cref="PropertyId.ChargeSpeed"/>
/// property uses that instead, so a specific weapon can break its class's rule without the table
/// needing to know about it.
/// </summary>
public static class WeaponSpeeds
{
    /// <summary>
    /// Attack-speed delta for a weapon class, as a fraction (+0.35 = 35% faster). Zero for anything
    /// that isn't a weapon class — armour and trinkets don't change how fast you swing.
    /// </summary>
    public static float HandlingFor(ItemClass weaponClass) => weaponClass switch
    {
        ItemClass.Dagger => 0.35f,
        ItemClass.Claw => 0.3f,
        ItemClass.Fang => 0.25f,
        ItemClass.Wand => 0.15f,
        ItemClass.Sword => 0f,
        ItemClass.Bow => -0.1f,
        ItemClass.Lance => -0.1f,
        ItemClass.Axe => -0.15f,
        ItemClass.Firearm => -0.2f,
        ItemClass.Blunt => -0.2f,
        _ => 0f
    };
}
