using System.Collections.Generic;
using System.Linq;
using Assets.HeroEditor.InventorySystem.Scripts.Data;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The whole collection as pictures. Every sprite in the HeroEditor packs is already an item —
/// 1,871 of them — but until now the only way to see one was to know its id. This page is a search
/// box, a type filter and a grid of icons; click one to see what it is and, if it carries no
/// engraving yet, make it a designed item in one step: a resonance entry with an engraving, and a
/// place to find it.
/// </summary>
public class ArtPage
{
    private const int Tile = 56;
    private const int Gap = 6;
    private const int MaxShown = 400;

    private class Entry
    {
        public ItemParams item;
        public Sprite icon;
        public string name;
        public string search;   // name + id, lower case
    }

    private readonly EquipmentWindow _window;
    private readonly List<Entry> _all = new List<Entry>();
    private readonly List<string> _types = new List<string> { "All" };
    private List<Entry> _shown = new List<Entry>();
    private string _lastSearch = null, _lastType = null;
    private Vector2 _scroll;
    private Entry _selected;

    public ArtPage(EquipmentWindow window)
    {
        _window = window;

        var collection = Catalog.Items();
        if (collection == null || collection.Items == null) return;

        // Icons keyed by id, from the collections directly: the collection's own lookup logs a
        // warning per missing icon, and this page asks for every icon there is.
        var icons = new Dictionary<string, Sprite>();
        if (collection.IconCollections != null)
            foreach (var set in collection.IconCollections)
                if (set != null && set.Icons != null)
                    foreach (var icon in set.Icons)
                        if (icon != null && !string.IsNullOrEmpty(icon.Id) && !icons.ContainsKey(icon.Id))
                            icons[icon.Id] = icon.Sprite;

        foreach (var item in collection.Items)
        {
            if (item == null || string.IsNullOrEmpty(item.Id)) continue;
            string name = Catalog.DisplayName(item.Id);
            icons.TryGetValue(item.IconId ?? "", out var icon);
            _all.Add(new Entry { item = item, icon = icon, name = name, search = (name + " " + item.Id).ToLowerInvariant() });
        }
        _types.AddRange(_all.Select(e => e.item.Type.ToString()).Distinct().OrderBy(t => t));
    }

    // ---- filters

    [HorizontalGroup("filters", 0.6f), ShowInInspector, LabelWidth(60), LabelText("Search")]
    [Tooltip("Matches the item's name or id.")]
    private string Search { get; set; } = "";

    [HorizontalGroup("filters"), ShowInInspector, LabelWidth(40), LabelText("Type"), ValueDropdown("Types")]
    private string Type { get; set; } = "All";

    private IEnumerable<string> Types => _types;

    [ShowInInspector, ReadOnly, HideLabel, DisplayAsString, PropertyOrder(1)]
    private string Count
    {
        get
        {
            Refilter();
            return _shown.Count > MaxShown
                ? $"{_shown.Count} items match — showing the first {MaxShown}, narrow the search to see the rest"
                : $"{_shown.Count} items";
        }
    }

    private void Refilter()
    {
        if (_lastSearch == Search && _lastType == Type) return;
        _lastSearch = Search; _lastType = Type;
        string needle = (Search ?? "").Trim().ToLowerInvariant();
        _shown = _all.Where(e => (Type == "All" || e.item.Type.ToString() == Type) &&
                                 (needle.Length == 0 || e.search.Contains(needle))).ToList();
    }

    // ---- the grid

    [OnInspectorGUI, PropertyOrder(2)]
    private void DrawGrid()
    {
        Refilter();
        var resonance = AssetDatabase.LoadAssetAtPath<ResonanceDatabase>("Assets/Resources/ResonanceDatabase.asset");
        var designed = resonance != null ? new HashSet<string>(resonance.entries.Select(e => e.itemId)) : new HashSet<string>();

        float width = EditorGUIUtility.currentViewWidth - 40f;
        int columns = Mathf.Max(1, (int)(width / (Tile + Gap)));
        int shown = Mathf.Min(_shown.Count, MaxShown);
        int rows = (shown + columns - 1) / columns;

        _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(Mathf.Min(rows, 6) * (Tile + Gap) + 10f));
        var area = GUILayoutUtility.GetRect(columns * (Tile + Gap), rows * (Tile + Gap));
        for (int i = 0; i < shown; i++)
        {
            var entry = _shown[i];
            var rect = new Rect(area.x + (i % columns) * (Tile + Gap), area.y + (i / columns) * (Tile + Gap), Tile, Tile);

            bool isSelected = entry == _selected;
            EditorGUI.DrawRect(rect, isSelected ? new Color(1f, 0.85f, 0.3f, 0.35f) : new Color(1f, 1f, 1f, 0.06f));
            if (entry.icon != null) DrawSprite(rect.Padding(4f), entry.icon);
            else GUI.Label(rect, "?", EditorStyles.centeredGreyMiniLabel);

            // A designed item wears a dot: the point of the page is to find the ones that are not.
            if (designed.Contains(entry.item.Id))
                EditorGUI.DrawRect(new Rect(rect.xMax - 10f, rect.y + 4f, 6f, 6f), new Color(1f, 0.85f, 0.3f, 1f));

            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                _selected = entry;
                _look = Catalog.Look(entry.item.Id);
                Event.current.Use();
                GUI.changed = true;
            }
            if (rect.Contains(Event.current.mousePosition))
                GUI.Label(rect, new GUIContent("", entry.name));    // the tooltip
        }
        EditorGUILayout.EndScrollView();
    }

    private static void DrawSprite(Rect rect, Sprite sprite)
    {
        var texture = sprite.texture;
        if (texture == null) return;
        Rect uv;
        try
        {
            var tr = sprite.textureRect;
            uv = new Rect(tr.x / texture.width, tr.y / texture.height, tr.width / texture.width, tr.height / texture.height);
            // Keep the sprite's proportions inside the tile.
            float aspect = tr.width / Mathf.Max(1f, tr.height);
            if (aspect > 1f) { float h = rect.width / aspect; rect = new Rect(rect.x, rect.y + (rect.height - h) * 0.5f, rect.width, h); }
            else { float w = rect.height * aspect; rect = new Rect(rect.x + (rect.width - w) * 0.5f, rect.y, w, rect.height); }
            GUI.DrawTextureWithTexCoords(rect, texture, uv, true);
        }
        catch (System.Exception)
        {
            // A tightly packed atlas sprite has no rectangle; the asset preview is close enough.
            var preview = AssetPreview.GetAssetPreview(sprite);
            if (preview != null) GUI.DrawTexture(rect, preview, ScaleMode.ScaleToFit, true);
        }
    }

    // ---- the picked item

    private Sprite _look;

    private bool HasSelection => _selected != null;
    private bool IsDesigned => _selected != null && ResonanceEntryFor(_selected.item.Id) != null;
    private bool CanMake => _selected != null && !IsDesigned;

    [BoxGroup("Picked"), ShowIf("HasSelection"), PropertyOrder(3)]
    [HorizontalGroup("Picked/art", 220), PreviewField(96, ObjectFieldAlignment.Left), ShowInInspector, ReadOnly, HideLabel]
    private Sprite Icon => _selected?.icon;

    [HorizontalGroup("Picked/art"), PreviewField(96, ObjectFieldAlignment.Left), ShowInInspector, ReadOnly, HideLabel, ShowIf("HasSelection"), PropertyOrder(3)]
    private Sprite Look => _look;

    [BoxGroup("Picked"), ShowInInspector, ReadOnly, ShowIf("HasSelection"), PropertyOrder(4)]
    private string Name => _selected?.name;

    [BoxGroup("Picked"), ShowInInspector, ReadOnly, ShowIf("HasSelection"), PropertyOrder(4)]
    private string Id => _selected?.item.Id;

    [BoxGroup("Picked"), ShowInInspector, ReadOnly, ShowIf("HasSelection"), PropertyOrder(4)]
    private string Kind => _selected == null ? "" : $"{_selected.item.Type} · {_selected.item.Class} · {_selected.item.Rarity} · {_selected.item.Price}g";

    [BoxGroup("Picked"), ShowInInspector, ReadOnly, ShowIf("HasSelection"), PropertyOrder(4), ListDrawerSettings(IsReadOnly = true, ShowFoldout = false)]
    private List<string> Properties => _selected?.item.Properties?.Select(p => p.Id + "  " + p.Value).ToList() ?? new List<string>();

    [BoxGroup("Picked"), ShowInInspector, ReadOnly, ShowIf("IsDesigned"), PropertyOrder(4), DisplayAsString]
    private string Already => "Already a designed item — it has a resonance entry.";

    [BoxGroup("Picked"), Button(ButtonSizes.Medium), ShowIf("IsDesigned"), PropertyOrder(5)]
    private void OpenItsPage() => _window.ShowItem(_selected.item.Id);

    // ---- make it an item

    [BoxGroup("Picked/Make this an item"), ShowIf("CanMake"), PropertyOrder(6)]
    [ShowInInspector, Required, AssetsOnly, ValueDropdown("Engravings"), LabelText("Engraving")]
    [Tooltip("What wearing it grants. A new behaviour is a new Engraving class; this list is the ones that exist.")]
    private Engraving _engraving;

    [BoxGroup("Picked/Make this an item"), ShowIf("CanMake"), PropertyOrder(6)]
    [ShowInInspector, LabelText("Counts")]
    private ResonanceRequirement _requirement = ResonanceRequirement.CombatsWorn;

    [BoxGroup("Picked/Make this an item"), ShowIf("CanMake"), PropertyOrder(6)]
    [ShowInInspector, AssetsOnly, ValueDropdown("Pools"), LabelText("Offer in")]
    [Tooltip("Reward pool to add the item to, so a run can find it. None: kits and the workshop only.")]
    private RewardPool _pool;

    private IEnumerable<ValueDropdownItem<Engraving>> Engravings =>
        AssetDatabase.FindAssets("t:Engraving")
            .Select(g => AssetDatabase.LoadAssetAtPath<Engraving>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(e => e != null)
            .OrderBy(e => e.DisplayName)
            .Select(e => new ValueDropdownItem<Engraving>(e.DisplayName, e));

    private IEnumerable<ValueDropdownItem<RewardPool>> Pools
    {
        get
        {
            yield return new ValueDropdownItem<RewardPool>("None", null);
            foreach (var g in AssetDatabase.FindAssets("t:RewardPool"))
            {
                var pool = AssetDatabase.LoadAssetAtPath<RewardPool>(AssetDatabase.GUIDToAssetPath(g));
                if (pool != null) yield return new ValueDropdownItem<RewardPool>(pool.name, pool);
            }
        }
    }

    [BoxGroup("Picked/Make this an item"), ShowIf("CanMake"), PropertyOrder(7)]
    [Button(ButtonSizes.Large), EnableIf("@this._engraving != null")]
    [InfoBox("Adds a resonance entry (Tier II at 3, Tier III at 6, bankable at 3 — change them on the item's page), " +
             "adds the id to the chosen pool, saves, and opens the new page.")]
    private void MakeThisAnItem()
    {
        if (_selected == null || _engraving == null) return;

        var resonance = AssetDatabase.LoadAssetAtPath<ResonanceDatabase>("Assets/Resources/ResonanceDatabase.asset");
        if (resonance == null) { Debug.LogError("[Equipment] No ResonanceDatabase at Resources/ResonanceDatabase."); return; }

        resonance.entries.Add(new ResonanceDatabase.Entry
        {
            itemId = _selected.item.Id,
            engraving = _engraving,
            requirement = _requirement,
        });
        EditorUtility.SetDirty(resonance);

        if (_pool != null && !_pool.itemIds.Contains(_selected.item.Id))
        {
            _pool.itemIds.Add(_selected.item.Id);
            EditorUtility.SetDirty(_pool);
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[Equipment] {_selected.name} is now a designed item: {_engraving.DisplayName}, counts {ResonanceRequirements.Describe(_requirement)}" +
                  (_pool != null ? $", offered in {_pool.name}." : "."));

        _window.ShowItem(_selected.item.Id);
    }

    private static ResonanceDatabase.Entry ResonanceEntryFor(string id)
    {
        var resonance = AssetDatabase.LoadAssetAtPath<ResonanceDatabase>("Assets/Resources/ResonanceDatabase.asset");
        return resonance?.entries.FirstOrDefault(e => e.itemId == id);
    }
}
