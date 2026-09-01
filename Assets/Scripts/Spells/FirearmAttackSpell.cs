using System.Collections;
using Assets.HeroEditor.Common.Scripts.CharacterScripts;
using Assets.HeroEditor.Common.Scripts.CharacterScripts.Firearms;
using UnityEngine;

/// <summary>
/// The firearm basic attack: pull the trigger and let the gun do the rest.
///
/// Almost nothing here is the shot itself. HeroEditor's <see cref="FirearmFire"/> already owns the
/// recoil, the muzzle flash, the report, the ejected shell and the reload — including a reload whose
/// length depends on how the weapon loads — and its documented interface is a trigger flag "set
/// outside (by input manager or AI)". This is the AI.
///
/// What the gun will not do is deliver the bullet: its own <c>CreateBullet</c> drives 3D physics
/// left over from HeroEditor's demo, so <see cref="FirearmRig"/> switches it off and the shot is
/// fired here as an ordinary 2D projectile, the same kind the bow and wand use.
///
/// The identity is the magazine. A crossbow or a musket carries one round, so it fires, reloads, and
/// only then fires again — a rhythm of heavy single shots separated by a long, visible, punishable
/// pause, which is quite unlike the bow's steady draw. Revolvers carry six and behave completely
/// differently for the same reason. None of that is written here; it comes off the weapon.
/// </summary>
[CreateAssetMenu(menuName = "Spells/FirearmAttackSpell")]
public class FirearmAttackSpell : Spell
{
    [Header("Firearm Attack Properties")]
    public float damage = 18f;

    [Tooltip("Zero by default. A bullet's stopping power is damage, not shove, and knocking the " +
             "target away only lengthens the next reload's walk.")]
    public float knockbackForce = 0f;

    [Header("Projectile")]
    [Tooltip("The 2D projectile actually fired — the arrow/bolt prefab. HeroEditor's own bullet is " +
             "disabled because it drives 3D physics that nothing in this game has.")]
    public GameObject projectilePrefab;
    public float projectileSpeed = 24f;

    [Header("Timing")]
    [Tooltip("How long to wait for the gun to discharge before giving up on the shot.")]
    public float maxShotWait = 1.5f;

    [Tooltip("Seconds spent reloading once the magazine is spent. This is the whole cost of a big " +
             "magazine-fed weapon: a one-round musket pays it after every shot, a revolver after six.")]
    public float reloadSeconds = 1.2f;

    public override bool ScalesWithAttackSpeed => true;
    public override float BaseDamage => damage;

    private void Reset()
    {
        range = 10f;      // further than a thrown spell, shorter than a bow's 15
        cooldown = 1.6f;  // the gun's own fire rate still gates it from underneath
    }

    public override bool CanCast(Entity caster, Entity target) => target != null && !target.isDead;

    public override IEnumerator Cast(Entity caster, Entity target)
    {
        var character = caster != null ? caster.character : null;
        var firearm = character != null ? character.Firearm : null;
        if (firearm == null || firearm.Params == null || firearm.Fire == null) yield break;

        // Reload first if the magazine is spent, so the pause is seen before the shot rather than
        // stranded after it.
        if (firearm.AmmoShooted >= firearm.Params.MagazineCapacity)
            yield return Reload(character, firearm);

        int ammoBefore = firearm.AmmoShooted;

        firearm.Fire.FireButtonDown = true;
        yield return null;                           // FirearmFire samples the trigger in its Update
        firearm.Fire.FireButtonDown = false;

        for (float waited = 0f; waited < maxShotWait; waited += Time.deltaTime)
        {
            if (firearm.AmmoShooted > ammoBefore) { FireProjectile(caster, target, firearm); yield break; }
            yield return null;
        }
    }

    /// <summary>
    /// Stand and reload, on our own clock.
    ///
    /// HeroEditor's own <c>FirearmReload</c> cannot be used: it takes the reload's length from
    /// <c>GetNextAnimatorStateInfo</c>, which only describes a real state while a transition is
    /// running. This project's animator has no reload transition wired for firearms, so that speed
    /// comes back as zero, the duration divides to infinity, and the weapon reloads for ever — one
    /// shot per fight, with no error to say why. Keeping the clock here also keeps the pause a
    /// tuning value rather than a property of an animation nobody authored.
    /// </summary>
    private IEnumerator Reload(Character character, Firearm firearm)
    {
        if (character.Animator != null) character.Animator.SetBool("Reloading", true);

        yield return new WaitForSeconds(reloadSeconds);

        firearm.AmmoShooted = 0;
        if (character.Animator != null)
        {
            character.Animator.SetBool("Reloading", false);
            character.Animator.SetInteger("HoldType", (int)firearm.Params.HoldType);
        }
    }

    private void FireProjectile(Entity caster, Entity target, Firearm firearm)
    {
        if (projectilePrefab == null || firearm.FireTransform == null || target == null) return;

        var shot = Instantiate(projectilePrefab, firearm.FireTransform.position, Quaternion.identity);

        var projectile = shot.GetComponent<Assets.HeroEditor.Common.Scripts.ExampleScripts.Projectile>();
        if (projectile == null) { Destroy(shot); return; }

        projectile.shooter = caster;
        projectile.target = target;
        projectile.damage = caster.Stats != null ? caster.Stats.Damage.Value : damage;
        projectile.knockbackForce = knockbackForce;
        projectile.homingSpeed = projectileSpeed;

        // Launched aimed, so it still travels if the target dies before it arrives.
        var body = shot.GetComponent<Rigidbody2D>();
        Vector2 heading = (Vector2)target.transform.position - (Vector2)shot.transform.position;
        if (body != null && heading.sqrMagnitude > 0.0001f)
        {
            body.velocity = projectileSpeed * heading.normalized;
            shot.transform.right = heading.normalized;
        }
    }
}
