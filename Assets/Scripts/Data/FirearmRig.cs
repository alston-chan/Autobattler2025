using System.Linq;
using Assets.HeroEditor.Common.Scripts.CharacterScripts;
using Assets.HeroEditor.Common.Scripts.Collections;
using Assets.HeroEditor.Common.Scripts.Data;
using Assets.HeroEditor.InventorySystem.Scripts.Data;
using Assets.HeroEditor.InventorySystem.Scripts.Enums;
using HeroEditor.Common.Enums;
using UnityEngine;

/// <summary>
/// Puts a gun in a unit's hands.
///
/// Almost all of this already existed and had simply never been reached. The rig models firearms
/// properly — magazine size, fire rate, recoil, muzzle flash, shell ejection, and a reload whose
/// animation depends on how the weapon loads — and the catalogue carries 27 of them. Nothing in the
/// game ever set <c>WeaponType</c> to a firearm, so none of it ran.
///
/// The one thing HeroEditor cannot do here is spawn the bullet: its <c>FirearmFire.CreateBullet</c>
/// reads <c>GetComponent&lt;Rigidbody&gt;()</c>, 3D physics left over from its own demo, which does
/// not exist on anything in this 2D game. Bullets are ours to fire; the rest is theirs.
/// </summary>
/// <remarks>
/// Named FirearmRig rather than Firearms because HeroEditor already has a namespace by that name
/// (CharacterScripts.Firearms) and Character has a Firearms sprite list — a type called Firearms is
/// ambiguous the moment either is in scope.
/// </remarks>
public static class FirearmRig
{
    /// <summary>Whether this weapon is a firearm — which here includes crossbows.</summary>
    public static bool IsFirearm(Item weapon) =>
        weapon != null && weapon.Params != null && weapon.Params.Class == ItemClass.Firearm;

    /// <summary>
    /// How a gun behaves is stored apart from the item, in a <see cref="FirearmCollection"/>, and the
    /// only thing joining the two is the name. Item ids read
    /// <c>&lt;pack&gt;.&lt;set&gt;.Firearm1H|Firearm2H.&lt;Name&gt;</c>, and that last segment is
    /// exactly the <see cref="FirearmParams.Name"/> — so the id carries both the params to look up
    /// and the grip to hold it in.
    /// </summary>
    public static string ParamsNameOf(Item weapon)
    {
        if (weapon == null || string.IsNullOrEmpty(weapon.Id)) return null;

        int dot = weapon.Id.LastIndexOf('.');
        return dot >= 0 && dot < weapon.Id.Length - 1 ? weapon.Id.Substring(dot + 1) : weapon.Id;
    }

    /// <summary>Whether the gun is shouldered rather than held in one hand.</summary>
    public static bool IsTwoHanded(Item weapon) =>
        weapon != null && (weapon.IsTwoHanded ||
                           (weapon.Id != null && weapon.Id.Contains(".Firearm2H.")));

    /// <summary>The behaviour block for a gun, or null if the catalogue has none under that name.</summary>
    public static FirearmParams ParamsFor(Item weapon)
    {
        string name = ParamsNameOf(weapon);
        if (string.IsNullOrEmpty(name)) return null;

        // Instances is filled by a RuntimeInitializeOnLoadMethod, so it is empty in edit mode.
        if (FirearmCollection.Instances == null || FirearmCollection.Instances.Count == 0)
        {
            foreach (var collection in Resources.LoadAll<FirearmCollection>(""))
            {
                var match = collection.Firearms.FirstOrDefault(i => i.Name == name);
                if (match != null) return match;
            }
            return null;
        }

        foreach (var collection in FirearmCollection.Instances.Values)
        {
            var match = collection.Firearms.FirstOrDefault(i => i.Name == name);
            if (match != null) return match;
        }
        return null;
    }

    /// <summary>Whether the rig is currently holding a gun rather than a blade.</summary>
    public static bool IsHoldingFirearm(Character character) =>
        character != null && (character.WeaponType == WeaponType.Firearm1H ||
                              character.WeaponType == WeaponType.Firearm2H ||
                              character.WeaponType == WeaponType.FirearmsPaired);

    /// <summary>
    /// Bring the rig in line with what is equipped, in both directions: raise a gun, or put one away
    /// and go back to a blade.
    ///
    /// Runs BEFORE <see cref="DualWield"/> and instead of it, because a gun and a pair of daggers are
    /// competing answers to the same question and only one of them can be holding the hands.
    /// </summary>
    public static void Apply(Entity entity, Item weapon)
    {
        var character = entity != null ? entity.character : null;
        if (character == null || character.Firearm == null) return;

        if (IsFirearm(weapon))
        {
            var firearmParams = ParamsFor(weapon);
            if (firearmParams == null)
            {
                // Posing with a firearm we have no behaviour for would leave a unit aiming an empty
                // hand, which is worse than leaving them holding the blade they already had.
                Debug.LogWarning($"[Firearms] No FirearmParams named '{ParamsNameOf(weapon)}' for " +
                                 $"item '{weapon.Id}' — the rig keeps its previous weapon.");
                return;
            }

            // Initialize reads Params to build the gun, so it has to be set first.
            character.Firearm.Params = firearmParams;
            character.SecondaryMeleeWeapon = null;          // no off-hand blade behind a gun
            character.WeaponType = IsTwoHanded(weapon) ? WeaponType.Firearm2H : WeaponType.Firearm1H;
            character.Initialize();
            character.UpdateAnimation();

            // Their bullet is 3D; ours is not. We fire our own from Firearm.FireTransform.
            if (character.Firearm.Fire != null) character.Firearm.Fire.CreateBullets = false;
            return;
        }

        if (!IsHoldingFirearm(character)) return;           // never was a gun, nothing to put away

        character.WeaponType = WeaponType.Melee1H;
        character.Initialize();
        character.UpdateAnimation();
    }
}
