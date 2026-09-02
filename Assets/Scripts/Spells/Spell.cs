using System.Collections;
using Assets.HeroEditor.Common.Scripts.CharacterScripts;
using HeroEditor.Common.Enums;
using UnityEngine;

/// <summary>
/// The kind of weapon a character must have equipped for an ability to work. Abilities are learned
/// from spellbooks (Docs), but stay thematically coherent — arrows need a bow, slashes need a melee
/// weapon. Groups HeroEditor's granular WeaponType (Melee1H/2H/Paired, Bow, …) into game classes.
/// </summary>
/// <summary>
/// What a spell needs in the caster's hands. Explicit values because spell assets store them.
/// </summary>
public enum WeaponClass
{
    Any = 0,
    Melee = 1,
    Bow = 2,

    /// <summary>
    /// A wand. Cannot be answered by the rig's WeaponType, which calls a wand Melee1H exactly like a
    /// sword — so this one is checked against the equipped item's class instead.
    /// </summary>
    Wand = 3
}

public abstract class Spell : ScriptableObject
{
    public string spellName;
    [TextArea, Tooltip("What it does, with the real numbers. Shown when the spellbook is clicked. " +
                       "Weapon attacks can leave this empty.")]
    public string description;
    public float cooldown;
    [Tooltip("The effective range of this spell (used for AI and targeting)")]
    public float range = 1.5f;
    public bool alwaysOn = false;

    [Tooltip("Mana required to cast. 0 = a free basic attack (the CHARGER). >0 = a cost ability " +
             "(an ult): the AI casts it the moment the caster can afford it, and spends the mana. " +
             "Set equal to Mana.maxMana for the classic 'fires when the bar is full' feel.")]
    public float manaCost = 0f;

    /// <summary>A cost ability (ultimate) rather than a free basic attack.</summary>
    public bool IsUltimate => manaCost > 0f;

    /// <summary>
    /// An ability rather than a weapon swing. The weapon basic attack is the one spell that neither
    /// costs mana nor runs on its own, which makes this the line between "how this unit hits" and
    /// "what this unit can do" — the second is what a player wants named on a card.
    /// </summary>
    public bool IsAbility => IsUltimate || alwaysOn;

    /// <summary>
    /// What to call this spell out loud. Authored names are for the player; the asset name is the
    /// fallback so an unnamed spell still reads as something. Three places were spelling this rule
    /// out for themselves and would have drifted apart the first time one of them was edited.
    /// </summary>
    public string DisplayName => string.IsNullOrEmpty(spellName) ? name : spellName;

    [Tooltip("Which weapon the caster must have equipped. A spellbook can teach Multi Shot, but it " +
             "only works while a bow is held. 'Any' skips the check (e.g. a self-buff).")]
    public WeaponClass weaponRequirement = WeaponClass.Any;

    /// <summary>
    /// True when the caster's equipped weapon satisfies <see cref="weaponRequirement"/>. Monsters have
    /// no weapon rig, so they count as melee brawlers.
    /// </summary>
    public bool MeetsWeaponRequirement(Entity caster)
    {
        if (weaponRequirement == WeaponClass.Any) return true;
        if (caster == null || caster.character == null)
            return weaponRequirement == WeaponClass.Melee;

        // A wand reads as Melee1H on the rig, so asking the rig would let any sword pass. The item's
        // class is the only record of the difference — see Entity.weaponClass.
        if (weaponRequirement == WeaponClass.Wand)
            return caster.weaponClass == Assets.HeroEditor.InventorySystem.Scripts.Enums.ItemClass.Wand;

        WeaponType wt = caster.character.WeaponType;
        switch (weaponRequirement)
        {
            case WeaponClass.Bow:
                return wt == WeaponType.Bow;
            case WeaponClass.Melee:
                // A wand is swung like a sword but is not one, so a melee spell does not accept it.
                return (wt == WeaponType.Melee1H || wt == WeaponType.Melee2H ||
                        wt == WeaponType.MeleePaired) &&
                       caster.weaponClass != Assets.HeroEditor.InventorySystem.Scripts.Enums.ItemClass.Wand;
            default:
                return true;
        }
    }

    public abstract bool CanCast(Entity caster, Entity target);
    public abstract IEnumerator Cast(Entity caster, Entity target);

    /// <summary>
    /// True for basic weapon attacks (melee/bow) whose rate scales with the caster's AttackSpeed.
    /// Other spells scale with cooldown reduction instead and should leave this false.
    /// </summary>
    public virtual bool ScalesWithAttackSpeed => false;

    /// <summary>
    /// The raw damage this spell contributes as a unit's base Damage stat, when it's the weapon
    /// basic attack. Spells that deal no flat damage of their own (or scale off the caster's stat
    /// rather than defining it) leave this at 0. Overriding here keeps <see cref="EntityStats"/>
    /// from having to type-check every spell subclass to find a damage field.
    /// </summary>
    public virtual float BaseDamage => 0f;

    /// <summary>Caster's current attack-speed multiplier (clamped above zero), or 1 if unavailable.</summary>
    protected float GetAttackSpeed(Entity caster)
        => (caster != null && caster.Stats != null && caster.Stats.AttackSpeed != null)
            ? Mathf.Max(0.01f, caster.Stats.AttackSpeed.Value)
            : 1f;

    /// <summary>The Animator that plays the caster's attack animation (character or monster).</summary>
    protected Animator GetAnimator(Entity caster)
    {
        if (caster == null) return null;
        if (caster.isCharacter && caster.character != null) return caster.character.Animator;
        if (caster.monster != null) return caster.monster.Animator;
        return null;
    }

    /// <summary>
    /// Waits until the caster's animation reaches a named event, then returns — so effects
    /// (damage, projectile spawn, VFX) land on the real animation keyframe instead of a guess.
    /// Characters fire events on HeroEditor <see cref="AnimationEvents"/>; monsters fire them on
    /// <c>Monster.OnEvent</c>, so the same moment often has a different event name per type —
    /// pass both. Falls back to <paramref name="fallbackDelay"/> when there's no event source,
    /// and a <paramref name="timeout"/> guards against a clip that never fires the event so
    /// combat can never stall.
    /// </summary>
    protected IEnumerator WaitForAnimationEvent(
        Entity caster,
        string characterEvent,
        string monsterEvent,
        float fallbackDelay = 0.2f,
        float timeout = 1f)
    {
        // Character channel: HeroEditor AnimationEvents on the Animator object.
        if (caster != null && caster.isCharacter && caster.character != null && caster.character.Animator != null)
        {
            var events = caster.character.Animator.GetComponent<AnimationEvents>();
            if (events != null && !string.IsNullOrEmpty(characterEvent))
            {
                yield return WaitForNamedEvent(
                    h => events.OnCustomEvent += h,
                    h => events.OnCustomEvent -= h,
                    characterEvent, timeout);
                yield break;
            }
        }
        // Monster channel: FantasyMonsters Monster.OnEvent.
        else if (caster != null && caster.monster != null && !string.IsNullOrEmpty(monsterEvent))
        {
            yield return WaitForNamedEvent(
                h => caster.monster.OnEvent += h,
                h => caster.monster.OnEvent -= h,
                monsterEvent, timeout);
            yield break;
        }

        // No usable event source — preserve a fixed-delay approximation.
        yield return new WaitForSeconds(fallbackDelay);
    }

    /// <summary>
    /// Subscribes to a string animation-event channel and waits until <paramref name="eventName"/>
    /// fires, with a safety timeout. Always unsubscribes.
    /// </summary>
    private IEnumerator WaitForNamedEvent(
        System.Action<System.Action<string>> subscribe,
        System.Action<System.Action<string>> unsubscribe,
        string eventName,
        float timeout)
    {
        bool fired = false;
        System.Action<string> handler = e => { if (e == eventName) fired = true; };
        subscribe(handler);

        float elapsed = 0f;
        while (!fired && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        unsubscribe(handler);
    }
}
