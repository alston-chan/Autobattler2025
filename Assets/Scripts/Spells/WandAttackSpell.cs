using System.Collections;
using UnityEngine;

/// <summary>
/// The wand's basic attack: a bolt, thrown without a wind-up.
///
/// Meant to be a third weapon identity rather than a faster bow, so the difference is not only in
/// the timing:
///
/// <list type="bullet">
/// <item>No draw. A bow spends half a second pulling before anything happens; a wand's damage
/// leaves the moment the cast begins.</item>
/// <item>Shorter reach. A bow outranges most of the board, while a wand has to be brought close
/// enough that where it stands in the formation actually matters.</item>
/// <item>A slower bolt, so damage arrives visibly after the cast rather than effectively on it.</item>
/// <item>Less damage per hit, on a shorter cooldown.</item>
/// </list>
///
/// The last point is the one that decides its role, and not for the obvious reason. Cadence is set
/// by cooldown, not by wind-up — a bow's charge happens INSIDE its cooldown window, so dropping the
/// charge alone would change when damage lands and not how often. Giving the wand a shorter cooldown
/// is what makes it swing more, and since mana is earned per basic attack, more swings mean the
/// caster's ultimate comes round sooner. The bow is the damage platform; the wand is the ability
/// platform.
/// </summary>
[CreateAssetMenu(menuName = "Spells/WandAttackSpell")]
public class WandAttackSpell : Spell
{
    [Header("Wand Attack Properties")]
    [Tooltip("The bolt. Needs a Projectile component, like the arrow prefab.")]
    public GameObject boltPrefab;

    public float damage = 6f;
    [Tooltip("Zero by default: a bolt of light should not shove people. Knockback also carries a " +
             "stun, so leaving it on a fast weapon quietly locks the target down.")]
    public float knockbackForce = 0f;

    [Tooltip("How fast the bolt travels. Deliberately slower than an arrow (18.75) so a wand's " +
             "damage is seen to cross the gap rather than arriving the instant it is cast.")]
    public float boltSpeed = 11f;

    [Tooltip("Fallback delay before the bolt leaves, used only if the cast animation has no release " +
             "event. Cast1H does have one, so this is a safety net rather than the usual path.")]
    public float releaseDelay = 0.25f;

    [Tooltip("Safety timeout: max seconds to wait for the animation's release event before firing " +
             "anyway, so a missing event can never leave a caster stuck mid-cast.")]
    public float maxReleaseWait = 1f;

    /// <summary>Animator trigger for the rig's one-handed cast (state <c>Cast1H</c>).</summary>
    private const string CastTrigger = "Cast";

    /// <summary>The cast animation's release frame, fired by Cast1H as CustomEvent("Hit").</summary>
    private const string ReleaseEvent = "Hit";

    // Basic weapon attack — its rate scales with the caster's AttackSpeed, like the bow and melee.
    public override bool ScalesWithAttackSpeed => true;
    public override float BaseDamage => damage;

    private void Reset()
    {
        // Sensible defaults for a NEW asset; override per-asset in the Inspector.
        range = 6f;
        cooldown = 0.75f;
        weaponRequirement = WeaponClass.Wand;
    }

    public override bool CanCast(Entity caster, Entity target) => target != null && !target.isDead;

    public override IEnumerator Cast(Entity caster, Entity target)
    {
        if (caster.character == null) yield break;   // wand attacks are character-only

        float attackSpeed = GetAttackSpeed(caster);
        Animator animator = caster.character.Animator;

        // The rig has a real cast: Human.controller carries a Cast trigger wired to a Cast1H state.
        // HeroEditor's CharacterAnimation helper never exposes it — it offers only Slash, Jab and the
        // bow's Charge sequence — so the trigger is set directly, the same way the bow spells drive
        // "Charge". A wand is Melee1H, which is precisely what Cast1H animates.
        if (animator != null)
        {
            animator.SetTrigger(CastTrigger);
            animator.speed = attackSpeed;
        }

        // Release on the animation's own frame rather than a guessed delay. Cast1H fires
        // CustomEvent("Hit") at 0.25 of its half-second, which is the moment the hand comes forward
        // — the bolt used to leave before that, so it appeared to jump out ahead of the gesture.
        // The event also travels with the clip, so speeding the animation up moves the release with
        // it instead of drifting out of step.
        yield return WaitForAnimationEvent(caster, ReleaseEvent, null,
            releaseDelay / Mathf.Max(0.01f, attackSpeed),
            maxReleaseWait / Mathf.Max(0.01f, attackSpeed));

        // The bolt only carries the damage — it lands when the projectile arrives, not now.
        Fire(caster, target);

        if (animator != null) animator.speed = 1f;
    }

    private void Fire(Entity caster, Entity target)
    {
        if (boltPrefab == null || caster.fireTransform == null || target == null) return;

        var bolt = Instantiate(boltPrefab, caster.fireTransform.position, Quaternion.identity);

        var projectile = bolt.GetComponent<Assets.HeroEditor.Common.Scripts.ExampleScripts.Projectile>();
        if (projectile == null) return;

        projectile.damage = caster.Stats != null ? caster.Stats.Damage.Value : damage;
        projectile.knockbackForce = knockbackForce;
        projectile.shooter = caster;
        projectile.target = target;

        // Projectile steers toward its target every frame at this speed, so this is the whole of the
        // bolt's flight behaviour — no launch velocity needed.
        projectile.homingSpeed = boltSpeed;
    }
}
