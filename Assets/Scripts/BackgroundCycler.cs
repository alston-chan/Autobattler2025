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
            Debug.Log($"[BackgroundCycler] {index + 1}/{backgrounds.Count}: {backgrounds[index].name}");
        }
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
