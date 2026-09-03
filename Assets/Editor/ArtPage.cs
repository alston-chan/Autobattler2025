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
///
/// Armour comes in sets — vest, gloves and boots on one sprite family, 297 of them — with helmets
/// and capes on the same theme in the same pack (<see cref="Catalog.Theme"/>), so the page also
/// shows the collection as sets: one tile each, expanding to every piece on the theme, and a button
/// that designs the pieces together.
/// </summary>
public class ArtPage
{
    private const int Tile = 56;
    private const int Gap = 6;
    private const int MaxShown = 400;
    private const string ResonancePath = "Assets/Resources/ResonanceDatabase.asset";

    private class Entry
    {
        public ItemParams item;
        public Sprite icon;
        public string name;
        public string search;   // name + id, lower case
    }

    private class SetEntry
    {
        public string key;
        public string name;
        public Entry vest, gloves, boots;
        public readonly List<Entry> extras = new List<Entry>();   // helmets and capes on the theme
        public string search;
        public IEnumerable<Entry> Pieces => new[] { vest, gloves, boots }.Where(p => p != null).Concat(extras);
    }

    /// <summary>A helmet or cape on the set's theme, as a row: what it is, and the engraving to give it.</summary>
    private class ExtraPiece
    {
        [HideInInspector] public readonly Entry entry;
        public ExtraPiece(Entry entry) { this.entry = entry; }

        [TableColumnWidth(58, Resizable = false), PreviewField(48, ObjectFieldAlignment.Center), ShowInInspector, ReadOnly, HideLabel]
        public Sprite Icon => entry.icon;

        [TableColumnWidth(260), ShowInInspector, ReadOnly, DisplayAsString, HideLabel]
        public string Piece => $"{entry.item.Type}  {entry.name}";

        [TableColumnWidth(180), ShowInInspector, ReadOnly, DisplayAsString, HideLabel]
        public string Stats => entry.item.Properties != null ? string.Join(", ", entry.item.Properties.Select(p => p.Id + " " + p.Value)) : "";

        [TableColumnWidth(200), ShowInInspector, AssetsOnly, ValueDropdown("@ArtPage.EngravingOptions()"), EnableIf("CanDesign"), HideLabel]
        public Engraving engraving;

        public bool CanDesign => ResonanceEntryFor(entry.item.Id) == null;

        [TableColumnWidth(120), ShowInInspector, ReadOnly, DisplayAsString, HideLabel]
        public string Designed
        {
            get
            {
                var existing = ResonanceEntryFor(entry.item.Id);
                return existing == null ? "" : existing.engraving != null ? existing.engraving.DisplayName : "yes";
            }
        }
    }

    public enum View { Items, Sets }

    private readonly EquipmentWindow _window;
    private readonly List<Entry> _all = new List<Entry>();
    private readonly Dictionary<string, Entry> _byId = new Dictionary<string, Entry>();
    private readonly List<SetEntry> _sets = new List<SetEntry>();
    private readonly List<string> _types = new List<string> { "All" };
    private List<Entry> _shown = new List<Entry>();
    private List<SetEntry> _shownSets = new List<SetEntry>();
    private string _lastSearch = null, _lastType = null;
    private View _lastView = View.Items;
    private Vector2 _scroll;
    private Entry _selected;
    private SetEntry _selectedSet;

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
            var entry = new Entry { item = item, icon = icon, name = name, search = (name + " " + item.Id).ToLowerInvariant() };
            _all.Add(entry);
            _byId[item.Id] = entry;
        }
        _types.AddRange(_all.Select(e => e.item.Type.ToString()).Distinct().OrderBy(t => t));

        // Sets: group the armour parts by what they share, then the helmets and capes on the theme.
        var sets = new Dictionary<string, SetEntry>();
        foreach (var entry in _all)
        {
            if (!Catalog.TryParseArmorPart(entry.item.Id, out var key, out var part)) continue;
            if (!sets.TryGetValue(key, out var set))
            {
                set = sets[key] = new SetEntry { key = key, name = Catalog.SetName(key) };
                foreach (var piece in Catalog.MatchingPieces(key))
                    if (_byId.TryGetValue(piece.Id, out var extra)) set.extras.Add(extra);
            }
            if (part == "vest") set.vest = entry;
            else if (part == "gloves") set.gloves = entry;
            else if (part == "boots") set.boots = entry;
        }
        foreach (var set in sets.Values)
        {
            set.search = (set.name + " " + set.key + " " + string.Join(" ", set.extras.Select(e => e.name))).ToLowerInvariant();
            _sets.Add(set);
        }
        _sets.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
    }

    // ---- filters

    [HorizontalGroup("filters", 0.5f), ShowInInspector, LabelWidth(60), LabelText("Search")]
    [Tooltip("Matches the item's name or id — or the set's, and its helmets' and capes', in the Sets view.")]
    private string Search { get; set; } = "";

    [HorizontalGroup("filters"), ShowInInspector, LabelWidth(40), LabelText("Type"), ValueDropdown("Types"), HideIf("IsSetsView")]
    private string Type { get; set; } = "All";

    [HorizontalGroup("filters", 180), ShowInInspector, HideLabel, EnumToggleButtons]
    private View Show { get; set; } = View.Items;

    private bool IsSetsView => Show == View.Sets;
    private IEnumerable<string> Types => _types;

    [ShowInInspector, ReadOnly, HideLabel, DisplayAsString, PropertyOrder(1)]
    private string Count
    {
        get
        {
            Refilter();
            int n = IsSetsView ? _shownSets.Count : _shown.Count;
            string what = IsSetsView ? "sets" : "items";
            return n > MaxShown
                ? $"{n} {what} match — showing the first {MaxShown}, narrow the search to see the rest"
                : $"{n} {what}";
        }
    }

    private void Refilter()
    {
        if (_lastSearch == Search && _lastType == Type && _lastView == Show) return;
        _lastSearch = Search; _lastType = Type; _lastView = Show;
        string needle = (Search ?? "").Trim().ToLowerInvariant();
        if (IsSetsView)
            _shownSets = _sets.Where(s => needle.Length == 0 || s.search.Contains(needle)).ToList();
        else
            _shown = _all.Where(e => (Type == "All" || e.item.Type.ToString() == Type) &&
                                     (needle.Length == 0 || e.search.Contains(needle))).ToList();
    }

    /// <summary>Open the Sets view on one set — from an item page's "design" button.</summary>
    public void PickSet(string setKey)
    {
        Show = View.Sets;
        Search = "";
        Refilter();
        SelectSet(_sets.FirstOrDefault(s => s.key == setKey));
    }

    private void SelectSet(SetEntry set)
    {
        _selectedSet = set;
        _selected = null;
        _vestEngraving = _glovesEngraving = _bootsEngraving = null;
        _extras = set != null ? set.extras.Select(e => new ExtraPiece(e)).ToList() : new List<ExtraPiece>();
    }

    // ---- the grid

    [OnInspectorGUI, PropertyOrder(2)]
    private void DrawGrid()
    {
        Refilter();
        var designed = Designed();

        float width = EditorGUIUtility.currentViewWidth - 40f;
        int columns = Mathf.Max(1, (int)(width / (Tile + Gap)));
        int total = IsSetsView ? _shownSets.Count : _shown.Count;
        int shown = Mathf.Min(total, MaxShown);
        int rows = (shown + columns - 1) / columns;

        _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(Mathf.Min(rows, 6) * (Tile + Gap) + 10f));
        var area = GUILayoutUtility.GetRect(columns * (Tile + Gap), rows * (Tile + Gap));
        for (int i = 0; i < shown; i++)
        {
            var rect = new Rect(area.x + (i % columns) * (Tile + Gap), area.y + (i / columns) * (Tile + Gap), Tile, Tile);

            Sprite icon; string tooltip; bool isSelected; int designedPieces;
            Entry entry = null; SetEntry set = null;
            if (IsSetsView)
            {
                set = _shownSets[i];
                icon = set.vest?.icon ?? set.gloves?.icon ?? set.boots?.icon;
                tooltip = set.name + (set.extras.Count > 0 ? "  +" + set.extras.Count : "");
                isSelected = set == _selectedSet;
                designedPieces = set.Pieces.Count(p => designed.Contains(p.item.Id));
            }
            else
            {
                entry = _shown[i];
                icon = entry.icon;
                tooltip = entry.name;
                isSelected = entry == _selected;
                designedPieces = designed.Contains(entry.item.Id) ? 1 : 0;
            }

            EditorGUI.DrawRect(rect, isSelected ? new Color(1f, 0.85f, 0.3f, 0.35f) : new Color(1f, 1f, 1f, 0.06f));
            if (icon != null) DrawSprite(rect.Padding(4f), icon);
            else GUI.Label(rect, "?", EditorStyles.centeredGreyMiniLabel);

            // A designed item wears a dot: the point of the page is to find the ones that are not.
            // A set wears one per designed piece.
            for (int d = 0; d < Mathf.Min(designedPieces, 6); d++)
                EditorGUI.DrawRect(new Rect(rect.xMax - 10f - d * 8f, rect.y + 4f, 6f, 6f), new Color(1f, 0.85f, 0.3f, 1f));

            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                if (IsSetsView) SelectSet(set);
                else { _selected = entry; _selectedSet = null; _look = Catalog.Look(entry.item.Id); }
                Event.current.Use();
                GUI.changed = true;
            }
            if (rect.Contains(Event.current.mousePosition))
                GUI.Label(rect, new GUIContent("", tooltip));    // the tooltip
        }
        EditorGUILayout.EndScrollView();
    }

    private static HashSet<string> Designed()
    {
        var resonance = AssetDatabase.LoadAssetAtPath<ResonanceDatabase>(ResonancePath);
        return resonance != null ? new HashSet<string>(resonance.entries.Select(e => e.itemId)) : new HashSet<string>();
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

    private bool HasSelection => _selected != null && !IsSetsView;
    private bool IsDesigned => HasSelection && ResonanceEntryFor(_selected.item.Id) != null;
    private bool CanMake => HasSelection && !IsDesigned;

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

    private IEnumerable<ValueDropdownItem<Engraving>> Engravings => EngravingOptions();

    public static IEnumerable<ValueDropdownItem<Engraving>> EngravingOptions() =>
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
        var resonance = AssetDatabase.LoadAssetAtPath<ResonanceDatabase>(ResonancePath);
        if (resonance == null) { Debug.LogError("[Equipment] No ResonanceDatabase at Resources/ResonanceDatabase."); return; }

        Design(resonance, _selected, _engraving, _requirement, _pool);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Equipment] {_selected.name} is now a designed item: {_engraving.DisplayName}, counts {ResonanceRequirements.Describe(_requirement)}" +
                  (_pool != null ? $", offered in {_pool.name}." : "."));
        _window.ShowItem(_selected.item.Id);
    }

    /// <summary>One designed item: the resonance entry, and the pool if one was picked. Not saved here.</summary>
    private static void Design(ResonanceDatabase resonance, Entry entry, Engraving engraving, ResonanceRequirement requirement, RewardPool pool)
    {
        resonance.entries.Add(new ResonanceDatabase.Entry { itemId = entry.item.Id, engraving = engraving, requirement = requirement });
        EditorUtility.SetDirty(resonance);
        if (pool != null && !pool.itemIds.Contains(entry.item.Id))
        {
            pool.itemIds.Add(entry.item.Id);
            EditorUtility.SetDirty(pool);
        }
    }

    private static ResonanceDatabase.Entry ResonanceEntryFor(string id)
    {
        var resonance = AssetDatabase.LoadAssetAtPath<ResonanceDatabase>(ResonancePath);
        return resonance?.entries.FirstOrDefault(e => e.itemId == id);
    }

    // ---- the picked set

    private bool HasSet => _selectedSet != null && IsSetsView;
    private bool HasExtras => HasSet && _extras.Count > 0;

    [BoxGroup("Set"), ShowIf("HasSet"), PropertyOrder(3), ShowInInspector, ReadOnly, LabelText("Set")]
    private string SetName => _selectedSet?.name;

    [BoxGroup("Set"), ShowIf("HasSet"), PropertyOrder(3), ShowInInspector, ReadOnly, LabelText("Family · theme")]
    private string SetKey => _selectedSet == null ? "" : $"{_selectedSet.key}   ·   {Catalog.Theme(_selectedSet.name)}";

    [BoxGroup("Set"), ShowIf("HasSet"), PropertyOrder(4)]
    [HorizontalGroup("Set/art", 110), PreviewField(80, ObjectFieldAlignment.Left), ShowInInspector, ReadOnly, HideLabel, Tooltip("Vest")]
    private Sprite VestIcon => _selectedSet?.vest?.icon;
    [HorizontalGroup("Set/art", 110), PreviewField(80, ObjectFieldAlignment.Left), ShowInInspector, ReadOnly, HideLabel, Tooltip("Gloves"), ShowIf("HasSet"), PropertyOrder(4)]
    private Sprite GlovesIcon => _selectedSet?.gloves?.icon;
    [HorizontalGroup("Set/art", 110), PreviewField(80, ObjectFieldAlignment.Left), ShowInInspector, ReadOnly, HideLabel, Tooltip("Boots"), ShowIf("HasSet"), PropertyOrder(4)]
    private Sprite BootsIcon => _selectedSet?.boots?.icon;
    [HorizontalGroup("Set/art"), PreviewField(80, ObjectFieldAlignment.Left), ShowInInspector, ReadOnly, HideLabel, ShowIf("HasSet"), PropertyOrder(4)]
    [Tooltip("The vest as worn; the three parts share the sprite family.")]
    private Sprite SetLook => _selectedSet?.vest != null ? Catalog.Look(_selectedSet.vest.item.Id) : null;

    [BoxGroup("Set"), ShowIf("HasSet"), PropertyOrder(5), ShowInInspector, ReadOnly, ListDrawerSettings(IsReadOnly = true, ShowFoldout = false), LabelText("Pieces")]
    private List<string> SetPieces
    {
        get
        {
            var list = new List<string>();
            if (_selectedSet == null) return list;
            foreach (var piece in new[] { _selectedSet.vest, _selectedSet.gloves, _selectedSet.boots })
            {
                if (piece == null) continue;
                var entry = ResonanceEntryFor(piece.item.Id);
                string stats = piece.item.Properties != null ? string.Join(", ", piece.item.Properties.Select(p => p.Id + " " + p.Value)) : "";
                list.Add($"{piece.item.Type}  {piece.name}  ·  {stats}" +
                         (entry != null ? $"  ·  designed: {(entry.engraving != null ? entry.engraving.DisplayName : "no engraving")}" : ""));
            }
            return list;
        }
    }

    // One engraving per piece. The doc's model: a set is pieces designed together, each with a
    // different engraving that combos with the others — not one item with a bonus.
    [BoxGroup("Set/Make this a set"), ShowIf("HasSet"), PropertyOrder(6), ShowInInspector, AssetsOnly, ValueDropdown("Engravings"), LabelText("Vest"), EnableIf("@this.CanDesign(\"vest\")")]
    private Engraving _vestEngraving;
    [BoxGroup("Set/Make this a set"), ShowIf("HasSet"), PropertyOrder(6), ShowInInspector, AssetsOnly, ValueDropdown("Engravings"), LabelText("Gloves"), EnableIf("@this.CanDesign(\"gloves\")")]
    private Engraving _glovesEngraving;
    [BoxGroup("Set/Make this a set"), ShowIf("HasSet"), PropertyOrder(6), ShowInInspector, AssetsOnly, ValueDropdown("Engravings"), LabelText("Boots"), EnableIf("@this.CanDesign(\"boots\")")]
    private Engraving _bootsEngraving;

    [BoxGroup("Set/Make this a set"), ShowIf("HasExtras"), PropertyOrder(6), ShowInInspector, LabelText("Helmets and capes on the theme")]
    [TableList(AlwaysExpanded = true, DrawScrollView = false, IsReadOnly = true, ShowIndexLabels = false)]
    private List<ExtraPiece> _extras = new List<ExtraPiece>();

    [BoxGroup("Set/Make this a set"), ShowIf("HasSet"), PropertyOrder(6), ShowInInspector, LabelText("Counts")]
    [Tooltip("The same counting rule for every piece; change any one on its page afterwards.")]
    private ResonanceRequirement _setRequirement = ResonanceRequirement.CombatsWorn;

    [BoxGroup("Set/Make this a set"), ShowIf("HasSet"), PropertyOrder(6), ShowInInspector, AssetsOnly, ValueDropdown("Pools"), LabelText("Offer in")]
    private RewardPool _setPool;

    private Entry Piece(string part) => _selectedSet == null ? null
        : part == "vest" ? _selectedSet.vest : part == "gloves" ? _selectedSet.gloves : _selectedSet.boots;

    private bool CanDesign(string part)
    {
        var piece = Piece(part);
        return piece != null && ResonanceEntryFor(piece.item.Id) == null;
    }

    private IEnumerable<(Entry piece, Engraving engraving)> ToDesign()
    {
        foreach (var (part, engraving) in new[] { ("vest", _vestEngraving), ("gloves", _glovesEngraving), ("boots", _bootsEngraving) })
            if (engraving != null && CanDesign(part)) yield return (Piece(part), engraving);
        foreach (var extra in _extras)
            if (extra.engraving != null && extra.CanDesign) yield return (extra.entry, extra.engraving);
    }

    private int PiecesToDesign => ToDesign().Count();

    [BoxGroup("Set/Make this a set"), ShowIf("HasSet"), PropertyOrder(7)]
    [Button("@\"Make this a set  (\" + this.PiecesToDesign + \" piece\" + (this.PiecesToDesign == 1 ? \"\" : \"s\") + \")\"", ButtonSizes.Large), EnableIf("@this.PiecesToDesign > 0")]
    [InfoBox("Designs every piece with an engraving chosen above — one resonance entry each, the ids added to the " +
             "chosen pool — saves, and opens the first piece's page. Pieces already designed are left as they are.")]
    private void MakeThisASet()
    {
        if (_selectedSet == null) return;
        var resonance = AssetDatabase.LoadAssetAtPath<ResonanceDatabase>(ResonancePath);
        if (resonance == null) { Debug.LogError("[Equipment] No ResonanceDatabase at Resources/ResonanceDatabase."); return; }

        var made = new List<string>();
        string first = null;
        foreach (var (piece, engraving) in ToDesign().ToList())
        {
            Design(resonance, piece, engraving, _setRequirement, _setPool);
            made.Add($"{piece.name} → {engraving.DisplayName}");
            if (first == null) first = piece.item.Id;
        }
        if (made.Count == 0) return;

        AssetDatabase.SaveAssets();
        Debug.Log($"[Equipment] {_selectedSet.name} designed as a set: {string.Join(", ", made)}" +
                  (_setPool != null ? $"; offered in {_setPool.name}." : "."));
        SelectSet(_selectedSet);
        _window.ShowItem(first);
    }
}
