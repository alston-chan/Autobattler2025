using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Cycles the attached <see cref="SpriteRenderer"/> through a list of background sprites with the
/// Left / Right arrow keys — a quick way to preview arenas while iterating on the game.
///
/// Fill <see cref="backgrounds"/> from the Inspector, or use the context-menu
/// "Load From Backgrounds Folder" to auto-populate every sprite under Assets/Backgrounds.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class BackgroundCycler : MonoBehaviour
{
    [Tooltip("All selectable backgrounds. Right-click the component header → 'Load From Backgrounds " +
             "Folder' to auto-fill from Assets/Backgrounds.")]
    public List<Sprite> backgrounds = new List<Sprite>();

    [Tooltip("Which background is shown on start.")]
    public int index = 0;

    /// <summary>Assigns a reusable bounds preset to a background.</summary>
    [System.Serializable]
    public class MapPreset
    {
        [Tooltip("The background this preset applies to. Must match an entry in the list above.")]
        public Sprite background;
        [Tooltip("The reusable bounds preset to use while this background is shown.")]
        public ArenaBoundsPreset preset;
    }

    [Header("Arena bounds per map")]
    [Tooltip("Assign a reusable bounds preset per map. Maps not listed here fall back to Default " +
             "Preset. Several maps can share one preset — edit the preset asset to update them all.")]
    public List<MapPreset> mapPresets = new List<MapPreset>();

    [Tooltip("Preset used for any map without an entry above.")]
    public ArenaBoundsPreset defaultPreset;

    [Header("Keys")]
    public KeyCode previousKey = KeyCode.LeftArrow;
    public KeyCode nextKey = KeyCode.RightArrow;

    [Tooltip("Sorting order forced onto the background so it stays behind ALL gameplay. The default " +
             "arrow/projectile sprites live at order 0, so a background at 0 would hide them — keep " +
             "this well negative.")]
    [SerializeField] private int sortingOrder = -1000;

    private SpriteRenderer _sr;

    private void Awake()
    {
        _sr = GetComponent<SpriteRenderer>();
        // The backdrop must never occlude projectiles/effects that render at order 0.
        _sr.sortingOrder = sortingOrder;
    }

    private void Start()
    {
        // Show whatever index is set, so the serialized starting choice is honoured. If the list is
        // empty we leave the SpriteRenderer's existing sprite alone.
        if (backgrounds.Count > 0) Show(index);
    }

    private void Update()
    {
        if (backgrounds.Count == 0) return;

        if (Input.GetKeyDown(previousKey)) Show(index - 1);
        else if (Input.GetKeyDown(nextKey)) Show(index + 1);
    }

    /// <summary>Show background <paramref name="i"/>, wrapping around in both directions.</summary>
    public void Show(int i)
    {
        if (backgrounds.Count == 0) return;

        // True modulo: keeps the index in range even when i goes negative (Left past the first).
        index = ((i % backgrounds.Count) + backgrounds.Count) % backgrounds.Count;

        if (backgrounds[index] != null)
        {
            _sr.sprite = backgrounds[index];
            ApplyBounds(backgrounds[index]);
            Debug.Log($"[BackgroundCycler] {index + 1}/{backgrounds.Count}: {backgrounds[index].name}");
        }
    }

    /// <summary>The preset assigned to <paramref name="sprite"/>, or the default preset if it has none.</summary>
    private ArenaBoundsPreset PresetFor(Sprite sprite)
    {
        var m = mapPresets.Find(p => p != null && p.background == sprite);
        return m != null && m.preset != null ? m.preset : defaultPreset;
    }

    /// <summary>Push this map's preset into the global <see cref="ArenaBounds"/>.</summary>
    private void ApplyBounds(Sprite sprite)
    {
        var preset = PresetFor(sprite);
        if (preset != null) preset.Apply();
    }

    /// <summary>
    /// Always-on Scene-view gizmo of the current map's play area, so bounds are visible without
    /// selecting the object. Set <see cref="index"/> to preview a different map's shape.
    /// </summary>
    private void OnDrawGizmos()
    {
        if (backgrounds.Count == 0) return;
        int i = ((index % backgrounds.Count) + backgrounds.Count) % backgrounds.Count;
        var preset = PresetFor(backgrounds[i]);
        if (preset == null) return;
        ArenaBounds.DrawGizmo(preset.minX, preset.maxX, preset.minY, preset.maxY, preset.shape,
            new Color(1f, 0.7f, 0.1f, 0.9f));
    }

#if UNITY_EDITOR
    /// <summary>
    /// Editor helper: scan Assets/Backgrounds for every sprite and fill the list, sorted by path so
    /// the order is stable (Arena 1, Arena 2, Castle, …). Re-run after adding new art.
    /// </summary>
    [ContextMenu("Load From Backgrounds Folder")]
    private void LoadFromFolder()
    {
        const string folder = "Assets/Backgrounds";
        var found = new List<Sprite>();
        foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:Sprite", new[] { folder }))
        {
            var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            var sprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null) found.Add(sprite);
        }
        found.Sort((a, b) => string.Compare(
            UnityEditor.AssetDatabase.GetAssetPath(a),
            UnityEditor.AssetDatabase.GetAssetPath(b),
            System.StringComparison.Ordinal));

        backgrounds = found;
        UnityEditor.EditorUtility.SetDirty(this);
        Debug.Log($"[BackgroundCycler] Loaded {backgrounds.Count} backgrounds from {folder}.");
    }
#endif
}
