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
    public float knockbackForce = 1.5f;

    [Tooltip("How fast the bolt travels. Deliberately slower than an arrow (18.75) so a wand's " +
             "damage is seen to cross the gap rather than arriving the instant it is cast.")]
    public float boltSpeed = 11f;

    [Tooltip("Pause between the cast gesture and the bolt leaving, in seconds at 1x attack speed. " +
             "Small — this is a flick of the wrist, not a draw.")]
    public float releaseDelay = 0.12f;

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

        // Reuses the melee swing as the cast gesture. HeroEditor has no dedicated cast animation and
        // holds a wand exactly as it holds a sword, so this reads as a flourish rather than a swing
        // only because nothing connects at the end of it.
        caster.character.Slash();
        if (animator != null) animator.speed = attackSpeed;

        yield return new WaitForSeconds(releaseDelay / Mathf.Max(0.01f, attackSpeed));

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
