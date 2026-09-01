using System.Collections;
using System.Linq;
using UnityEngine;

/// <summary>
/// Lob a bomb at where the enemy is standing.
///
/// The first ability that hits a PLACE rather than a unit. Shockwave is an area attack too, but it
/// erupts from the caster, so it rewards being surrounded; a thrown bomb rewards the enemy being
/// bunched somewhere else, which is a different question to ask of a formation and the reason this
/// is worth having alongside it.
///
/// The rig throws but cannot show what is being thrown: HeroEditor states plainly that supplies
/// "are present as icons only and are not displayed on a character". So the hand is empty during
/// the wind-up and the bomb appears as it leaves — which is what the eye reads anyway, since a
/// thrown object is only interesting once it is in the air.
/// </summary>
[CreateAssetMenu(menuName = "Spells/BombThrowSpell")]
public class BombThrowSpell : Spell
{
    [Header("Blast")]
    public float damage = 30f;
    public float radius = 2.5f;

    [Tooltip("Applied outward from the point of impact, so a bomb in a crowd scatters it.")]
    public float knockbackForce = 4f;
    public float hitstopDuration = 0.12f;

    [Header("Flight")]
    [Tooltip("Seconds in the air. Long enough to read as a lob; long enough to be dodged by a " +
             "unit that happens to move, which is the price of throwing at a place.")]
    public float flightTime = 0.55f;
    [Tooltip("Height of the arc at its peak, in world units.")]
    public float arcHeight = 2.2f;
    public float spinDegreesPerSecond = 540f;

    [Header("Appearance")]
    [Tooltip("Name of a sprite in the character's SpriteCollection.Supplies — HandBomb, MagicBomb, " +
             "SpikeBomb and HolyHandGrenade all exist.")]
    public string bombSpriteName = "HandBomb";
    public float bombScale = 1f;

    [Header("Timing")]
    [Tooltip("Used only if the throw animation has no release event. The clip does carry one " +
             "(CustomEvent 'ThrowSupply' at a third of the way in), so this is a safety net.")]
    public float releaseFallback = 0.33f;
    public float maxReleaseWait = 1f;

    private const string ThrowAnimation = "ThrowSupply";
    private const string CharacterReleaseEvent = "ThrowSupply";
    private const string MonsterReleaseEvent = "Attack";

    public override float BaseDamage => damage;

    private void Reset()
    {
        range = 7f;        // thrown further than a sword reaches, shorter than an arrow flies
        cooldown = 6f;
        manaCost = 100f;   // an ability, not a basic attack: it fires when the bar fills
    }

    public override bool CanCast(Entity caster, Entity target) => target != null && !target.isDead;

    public override IEnumerator Cast(Entity caster, Entity target)
    {
        var animator = GetAnimator(caster);
        if (caster.isCharacter && animator != null) animator.Play(ThrowAnimation, 0);
        else if (caster.monster != null) caster.monster.Attack();

        yield return WaitForAnimationEvent(caster, CharacterReleaseEvent, MonsterReleaseEvent,
            releaseFallback, maxReleaseWait);

        // Aimed where the target stands AT RELEASE, and committed to from there. If they die or
        // walk out of it while the bomb is in the air, the bomb still lands where it was thrown.
        if (target == null) yield break;
        Vector3 landing = target.transform.position;

        var bomb = BuildBomb(caster);
        if (bomb == null) yield break;

        bomb.transform.position = caster.fireTransform != null
            ? caster.fireTransform.position
            : caster.transform.position + Vector3.up;

        bomb.Launch(caster, landing, flightTime, arcHeight, damage, radius,
                    knockbackForce, hitstopDuration, spinDegreesPerSecond);
    }

    /// <summary>
    /// Assemble the bomb from a supply icon. Built in code rather than from a prefab because the
    /// only thing that varies is which icon it wears, and a prefab per bomb would be four prefabs
    /// that differ by a sprite reference.
    /// </summary>
    private ThrownBomb BuildBomb(Entity caster)
    {
        Sprite sprite = FindSupplySprite(caster, bombSpriteName);
        if (sprite == null)
        {
            Debug.LogWarning($"[BombThrowSpell] No supply sprite named '{bombSpriteName}' — " +
                             "nothing to throw, so the throw is skipped.");
            return null;
        }

        var go = new GameObject("ThrownBomb");
        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        go.transform.localScale = Vector3.one * bombScale;

        // Draw over the units it flies past, on whatever layer they are sorted in.
        var casterRenderer = caster.GetComponentInChildren<SpriteRenderer>();
        if (casterRenderer != null)
        {
            renderer.sortingLayerID = casterRenderer.sortingLayerID;
            renderer.sortingOrder = casterRenderer.sortingOrder + 50;
        }

        return go.AddComponent<ThrownBomb>();
    }

    private static Sprite FindSupplySprite(Entity caster, string name)
    {
        var collection = caster.character != null ? caster.character.SpriteCollection : null;
        if (collection == null || collection.Supplies == null) return null;

        var entry = collection.Supplies.FirstOrDefault(i => i != null && i.Name == name);
        return entry != null ? entry.Sprite : null;
    }
}
