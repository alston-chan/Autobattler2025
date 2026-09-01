using System.Collections;
using System.Linq;
using UnityEngine;

/// <summary>
/// Shared machinery for anything thrown by hand.
///
/// The throw itself is the same every time — play <c>ThrowSupply</c>, wait for the clip's release
/// event, then let go — and so is dressing the thrown object, since everything a unit throws wears
/// an icon out of <c>SpriteCollection.Supplies</c>. What differs is only what happens after it
/// leaves the hand, which is the one thing subclasses supply.
///
/// The rig throws but cannot show what is held: HeroEditor states that supplies "are present as
/// icons only and are not displayed on a character". The hand is empty through the wind-up and the
/// object appears as it leaves.
/// </summary>
public abstract class ThrownSupplySpell : Spell
{
    [Header("Appearance")]
    [Tooltip("Name of a sprite in SpriteCollection.Supplies — HandBomb, SpikeBomb, Boomerang, " +
             "TribalBoomerang, ThrowingStar and others all exist.")]
    public string supplySpriteName = "HandBomb";
    public float supplyScale = 1f;
    public float spinDegreesPerSecond = 540f;

    [Header("Timing")]
    [Tooltip("Used only if the throw clip has no release event. It does carry one (CustomEvent " +
             "'ThrowSupply', a third of the way in), so this is a safety net.")]
    public float releaseFallback = 0.33f;
    public float maxReleaseWait = 1f;

    private const string ThrowAnimation = "ThrowSupply";
    private const string CharacterReleaseEvent = "ThrowSupply";
    private const string MonsterReleaseEvent = "Attack";

    public override bool CanCast(Entity caster, Entity target) => target != null && !target.isDead;

    public override IEnumerator Cast(Entity caster, Entity target)
    {
        var animator = GetAnimator(caster);
        if (caster.isCharacter && animator != null) animator.Play(ThrowAnimation, 0);
        else if (caster.monster != null) caster.monster.Attack();

        yield return WaitForAnimationEvent(caster, CharacterReleaseEvent, MonsterReleaseEvent,
            releaseFallback, maxReleaseWait);

        if (target == null) yield break;
        Release(caster, target);
    }

    /// <summary>What leaves the hand, called on the release frame.</summary>
    protected abstract void Release(Entity caster, Entity target);

    /// <summary>Where a thrown thing starts: the hand if the rig marks one, the chest otherwise.</summary>
    protected static Vector3 ThrowOrigin(Entity caster) =>
        caster.fireTransform != null ? caster.fireTransform.position
                                     : caster.transform.position + Vector3.up;

    /// <summary>
    /// Build the thrown object from a supply icon, already positioned in the hand.
    ///
    /// Made in code rather than from a prefab because the only thing that varies between a bomb, a
    /// boomerang and a throwing star is which icon it wears and which component drives it — a
    /// prefab each would be three prefabs differing by one sprite reference.
    /// </summary>
    protected GameObject BuildSupply(Entity caster, string objectName)
    {
        Sprite sprite = FindSupplySprite(caster, supplySpriteName);
        if (sprite == null)
        {
            Debug.LogWarning($"[{GetType().Name}] No supply sprite named '{supplySpriteName}' — " +
                             "there is nothing to throw, so the throw is skipped.");
            return null;
        }

        var go = new GameObject(objectName);
        go.transform.position = ThrowOrigin(caster);
        go.transform.localScale = Vector3.one * supplyScale;

        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;

        // Drawn over the units it passes, on whatever layer they sort in.
        var casterRenderer = caster.GetComponentInChildren<SpriteRenderer>();
        if (casterRenderer != null)
        {
            renderer.sortingLayerID = casterRenderer.sortingLayerID;
            renderer.sortingOrder = casterRenderer.sortingOrder + 50;
        }

        return go;
    }

    private static Sprite FindSupplySprite(Entity caster, string name)
    {
        var collection = caster.character != null ? caster.character.SpriteCollection : null;
        if (collection == null || collection.Supplies == null) return null;

        var entry = collection.Supplies.FirstOrDefault(i => i != null && i.Name == name);
        return entry != null ? entry.Sprite : null;
    }
}
