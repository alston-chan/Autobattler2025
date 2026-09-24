using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A soft oval shadow on the floor under every unit, so units stand on the arena instead of floating
/// over it. Provisioned from <see cref="EntityRegistry"/> like <see cref="UnitBarsManager"/>, so
/// summons, decoys and revives get one with no setup.
///
/// The shadow is NOT a child of the unit: <see cref="HitFeedback"/> flashes every sprite under a
/// unit (a shadow would flash white), facing flips the unit's x scale, and the rig draws children at
/// its 0.5 scale. It lives under a neutral parent and follows the feet in LateUpdate instead. It is
/// sized from <see cref="Entity.BodyRadius"/>, so an Ogre casts a bigger one than a hero, and sits
/// on the floor's layer: above the background (-1000) and the grid (-500), under the rings and
/// ground effects (-400) so a selection ring or a tar pool is never dimmed by it.
/// </summary>
public class ContactShadows : MonoBehaviour
{
    [Tooltip("Shadow width as a multiple of the unit's body diameter.")]
    [SerializeField] private float widthScale = 1.3f;
    [Tooltip("Height as a fraction of width: the floor is seen at an angle, so the circle is squashed.")]
    [SerializeField] private float flatness = 0.32f;
    [Tooltip("How dark the centre is.")]
    [SerializeField, Range(0f, 1f)] private float opacity = 0.4f;
    [Tooltip("Nudge from the feet. Slightly down reads as the shadow falling behind the toes.")]
    [SerializeField] private Vector2 offset = new Vector2(0f, -0.04f);

    public const int SortingOrder = -450;

    private readonly Dictionary<Entity, SpriteRenderer> _shadows = new Dictionary<Entity, SpriteRenderer>();
    private Transform _parent;
    private static Sprite _sprite;

    private void OnEnable()
    {
        EntityRegistry.OnRegistered += Provision;
        EntityRegistry.OnUnregistered += Despawn;
        foreach (var e in new List<Entity>(EntityRegistry.All)) Provision(e);
    }

    private void OnDisable()
    {
        EntityRegistry.OnRegistered -= Provision;
        EntityRegistry.OnUnregistered -= Despawn;
    }

    private void Provision(Entity entity)
    {
        if (entity == null || _shadows.ContainsKey(entity)) return;
        if (_parent == null) _parent = new GameObject("ContactShadows").transform;

        var go = new GameObject("Shadow " + entity.name);
        go.transform.SetParent(_parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = SoftDisc();
        sr.sortingOrder = SortingOrder;
        sr.color = new Color(0f, 0f, 0f, opacity);
        _shadows[entity] = sr;
        Follow(entity, sr);
    }

    private void Despawn(Entity entity)
    {
        if (entity == null || !_shadows.TryGetValue(entity, out var sr)) return;
        if (sr != null) Destroy(sr.gameObject);
        _shadows.Remove(entity);
    }

    private void LateUpdate()
    {
        List<Entity> gone = null;
        foreach (var kv in _shadows)
        {
            if (kv.Key == null || kv.Value == null) { (gone ??= new List<Entity>()).Add(kv.Key); continue; }
            Follow(kv.Key, kv.Value);
        }
        if (gone != null)
            foreach (var e in gone)
            {
                if (_shadows.TryGetValue(e, out var sr) && sr != null) Destroy(sr.gameObject);
                _shadows.Remove(e);
            }
    }

    private void Follow(Entity entity, SpriteRenderer sr)
    {
        float width = entity.BodyRadius * 2f * widthScale;
        var t = sr.transform;
        t.position = entity.transform.position + (Vector3)offset;
        // The disc sprite is one unit across, so its scale is its size in the world.
        t.localScale = new Vector3(width, width * flatness, 1f);
        sr.enabled = entity.gameObject.activeInHierarchy;
    }

    /// <summary>
    /// A disc one world unit across that fades from its centre to nothing at its rim — squashed by
    /// the transform into the oval on the floor. Built once, in code, so there's no asset to lose.
    /// </summary>
    public static Sprite SoftDisc()
    {
        if (_sprite != null) return _sprite;
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                // Solid through the middle, then a smooth falloff to the rim: a contact shadow is
                // darkest right under the feet.
                // Not Mathf.SmoothStep: that blends between its first two arguments, which left the
                // centre at half strength and the whole shadow a faint smudge.
                float t = Mathf.InverseLerp(0.5f, 1f, d);
                float a = 1f - t * t * (3f - 2f * t);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255));
            }
        tex.SetPixels32(px);
        tex.Apply();
        _sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        _sprite.name = "ContactShadow";
        return _sprite;
    }
}
