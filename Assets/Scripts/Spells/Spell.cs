using System.Collections;
using Assets.HeroEditor.Common.Scripts.CharacterScripts;
using UnityEngine;

public abstract class Spell : ScriptableObject
{
    public string spellName;
    public float cooldown;
    [Tooltip("The effective range of this spell (used for AI and targeting)")]
    public float range = 1.5f;
    public bool alwaysOn = false;
    public abstract bool CanCast(Entity caster, Entity target);
    public abstract IEnumerator Cast(Entity caster, Entity target);

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
