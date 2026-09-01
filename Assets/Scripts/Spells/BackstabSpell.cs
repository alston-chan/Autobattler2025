using System.Collections;
using UnityEngine;

/// <summary>
/// Vanish, reappear behind whoever is closest to dying, and put a knife in them.
///
/// The first ability that chooses its own target rather than hitting whoever the unit happened to
/// be fighting. That is the whole point of it: a finisher is only a finisher if it can cross the
/// battlefield to reach the one enemy worth finishing, and until targeting had modes there was no
/// way to ask for that enemy.
///
/// It ends with the assassin standing alone behind the enemy line, which would be suicide, so the
/// dive also buys a moment out of sight. Dropping aggro is not an escape — every enemy re-picks a
/// target the instant it ends, and if the assassin is all that is left they come straight back — it
/// is time to strike again or to walk home.
/// </summary>
[CreateAssetMenu(menuName = "Spells/BackstabSpell")]
public class BackstabSpell : Spell
{
    [Header("Strike")]
    [Tooltip("Heavy, because it is spent crossing the field to reach one chosen enemy and leaves " +
             "the assassin somewhere dangerous.")]
    public float damage = 45f;
    public float critChance = 0.35f;

    [Header("The dive")]
    [Tooltip("How far behind the victim the assassin lands.")]
    public float behindOffset = 0.9f;

    [Tooltip("Seconds spent unpickable as a target afterwards. Enemies re-choose the moment it " +
             "lapses, so this is a head start, not a disappearance.")]
    public float aggroDropSeconds = 2.5f;

    [Tooltip("Beat between arriving and striking, so the eye can follow what happened.")]
    public float strikeDelay = 0.12f;

    public float hitstopDuration = 0.1f;

    // Contact-frame event names: characters fire "Hit", FantasyMonsters fire "Attack".
    private const string CharacterHitEvent = "Hit";
    private const string MonsterHitEvent = "Attack";

    public override float BaseDamage => damage;

    private void Reset()
    {
        // Reaches the whole field: the victim is chosen by how close to death they are, not by how
        // close they are standing, so a range that could refuse the cast would defeat the ability.
        range = 100f;
        cooldown = 9f;
        manaCost = 100f;
    }

    /// <summary>There must be someone worth finishing — not merely someone in front of us.</summary>
    public override bool CanCast(Entity caster, Entity target) =>
        Targeting.Pick(caster, TargetMode.LowestHealth) != null;

    public override IEnumerator Cast(Entity caster, Entity ignoredTarget)
    {
        // Chosen here rather than accepted from the AI: whoever is nearest death, wherever they are.
        Entity victim = Targeting.Pick(caster, TargetMode.LowestHealth);
        if (victim == null) yield break;

        caster.transform.position = ArenaBounds.ClampToArena(BehindOf(victim));
        caster.SetFacing(victim.transform.position.x > caster.transform.position.x);

        if (strikeDelay > 0f) yield return new WaitForSeconds(strikeDelay);
        if (victim == null || victim.isDead) { caster.DropAggro(aggroDropSeconds); yield break; }

        if (caster.isCharacter && caster.character != null) caster.character.Jab();
        else if (caster.monster != null) caster.monster.Attack();

        yield return WaitForAnimationEvent(caster, CharacterHitEvent, MonsterHitEvent, 0.2f, 1f);

        if (victim != null && !victim.isDead)
        {
            bool isCrit = Random.value < critChance;
            victim.TakeDamage(damage, caster, isCrit);
            if (hitstopDuration > 0f) victim.ApplyHitstop(hitstopDuration);
        }

        // Last, so the head start begins when the knife lands rather than when the dive started.
        caster.DropAggro(aggroDropSeconds);
    }

    /// <summary>
    /// The far side of the victim from where it is looking.
    ///
    /// Facing lives in localScale.x, and monsters are authored mirrored — the same encoding the
    /// death sequence reads to decide which way a body falls.
    /// </summary>
    private Vector3 BehindOf(Entity victim)
    {
        float facing = Mathf.Sign(victim.transform.localScale.x) * (victim.isCharacter ? 1f : -1f);
        return victim.transform.position - new Vector3(facing * behindOffset, 0f, 0f);
    }
}
