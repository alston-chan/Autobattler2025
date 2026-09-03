using System.Collections.Generic;
using System.Linq;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One window for designing equipment: every resonant item as a page that shows the item, its
/// resonance numbers and its engraving together, with the engravings, spells, spellbooks, reward
/// pools and runs alongside. Designing an item touched three or four assets cross-referenced by
/// string id; this is the one screen where that reads as one thing.
///
/// Tools > Equipment > Designer. Odin attributes only, never Odin serialization — the assets stay
/// plain YAML that git and text tools can read.
/// </summary>
public class EquipmentWindow : OdinMenuEditorWindow
{
    private const string ResonancePath = "Assets/Resources/ResonanceDatabase.asset";
    private const string SpellbooksPath = "Assets/Resources/SpellbookDatabase.asset";

    [MenuItem("Tools/Equipment/Designer %#e")]
    private static void Open()
    {
        var window = GetWindow<EquipmentWindow>("Equipment");
        window.minSize = new Vector2(960f, 600f);
    }

    /// <summary>One body for the whole window: pages dress it and draw it, the window owns it.</summary>
    public MannequinPreview Mannequin => _mannequin ?? (_mannequin = new MannequinPreview());
    private MannequinPreview _mannequin;

    protected override void OnEnable()
    {
        base.OnEnable();
        // A domain reload drops the field without disposing the preview: its scene — and the doll's
        // sprite mask in it — lived on and masked the next doll's helmet down to the horns. Clean up
        // before the reload takes the reference away.
        AssemblyReloadEvents.beforeAssemblyReload += DisposeMannequin;
    }

    protected override void OnDisable()
    {
        AssemblyReloadEvents.beforeAssemblyReload -= DisposeMannequin;
        base.OnDisable();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        DisposeMannequin();
    }

    private void DisposeMannequin()
    {
        _mannequin?.Dispose();
        _mannequin = null;
    }

    protected override OdinMenuTree BuildMenuTree()
    {
        var tree = new OdinMenuTree(supportsMultiSelect: false);
        tree.Config.DrawSearchToolbar = true;
        tree.DefaultMenuStyle.IconSize = 20f;

        var resonance = AssetDatabase.LoadAssetAtPath<ResonanceDatabase>(ResonancePath);
        var spellbooks = AssetDatabase.LoadAssetAtPath<SpellbookDatabase>(SpellbooksPath);

        // Art first: the whole collection as pictures, and where a new designed item starts.
        tree.Add("Art", new ArtPage(this));

        // Items: the designed ones — anything with a resonance entry. Named by the item, so the
        // menu reads as the company's wardrobe rather than as ids. A piece that belongs to a set
        // (or goes with one) lives under the set's page instead, so a set is edited as one thing.
        if (resonance != null)
        {
            var sets = new Dictionary<string, SetPage>();
            foreach (var entry in resonance.entries.OrderBy(e => Catalog.DisplayName(e.itemId)))
            {
                var page = new ItemPage(this, resonance, entry);
                string setKey = Catalog.SetKeyFor(entry.itemId);
                if (setKey == null)
                {
                    tree.Add("Items/" + Catalog.DisplayName(entry.itemId), page, page.Icon);
                    continue;
                }
                if (!sets.TryGetValue(setKey, out var setPage))
                {
                    setPage = sets[setKey] = new SetPage(this, resonance, setKey);
                    tree.Add("Sets/" + Catalog.SetName(setKey), setPage, setPage.Icon);
                }
                tree.Add("Sets/" + Catalog.SetName(setKey) + "/" + Catalog.DisplayName(entry.itemId), page, page.Icon);
            }
        }

        if (spellbooks != null)
        {
            foreach (var entry in spellbooks.entries.OrderBy(e => Catalog.DisplayName(e.itemId)))
                if (entry.spell != null)
                    tree.Add("Spellbooks/" + Catalog.DisplayName(entry.itemId), entry.spell, Catalog.Icon(entry.itemId));
        }

        tree.AddAllAssetsAtPath("Engravings", "Assets/Data/Engravings", typeof(Engraving), true);
        tree.AddAllAssetsAtPath("Spells", "Assets/Data/Spells", typeof(Spell), true);
        tree.AddAllAssetsAtPath("Reward pools", "Assets/Data/Run", typeof(RewardPool), true, true);
        tree.AddAllAssetsAtPath("Runs", "Assets/Data/Run", typeof(RunData), true, true);

        // Drafts: sets still being thought about. Nothing here is in the game until promoted.
        var drafts = Drafts;
        tree.Add("Drafts", drafts);
        foreach (var draft in drafts.drafts)
        {
            var page = new DraftPage(this, drafts, draft);
            tree.Add("Drafts/" + draft.status + " · " + (string.IsNullOrEmpty(draft.title) ? "(untitled)" : draft.title), page, page.Icon);
        }

        if (resonance != null) tree.Add("Databases/Resonance", resonance);
        if (spellbooks != null) tree.Add("Databases/Spellbooks", spellbooks);

        return tree;
    }

    // ---- drafts

    private const string DraftsPath = "Assets/Data/SetDrafts.asset";

    /// <summary>The one drafts asset, created on first use.</summary>
    public SetDrafts Drafts
    {
        get
        {
            var asset = AssetDatabase.LoadAssetAtPath<SetDrafts>(DraftsPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<SetDrafts>();
                AssetDatabase.CreateAsset(asset, DraftsPath);
            }
            return asset;
        }
    }

    /// <summary>Start a draft — from the Sets view with its choices, or empty — and open it.</summary>
    public void NewDraft(string title, string setKey, IEnumerable<SetDrafts.Piece> pieces)
    {
        var asset = Drafts;
        var draft = new SetDrafts.Draft { title = title, setKey = setKey, status = SetDrafts.Status.Idea };
        if (pieces != null) draft.pieces.AddRange(pieces);
        if (draft.pieces.Any(p => p.engraving != null)) draft.status = SetDrafts.Status.Drafting;
        asset.drafts.Add(draft);
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssetIfDirty(asset);
        ShowDraft(draft);
    }

    public void ShowDraft(SetDrafts.Draft draft)
    {
        ForceMenuTreeRebuild();
        var item = MenuTree.EnumerateTree().FirstOrDefault(i => i.Value is DraftPage page && page.Draft == draft);
        if (item != null) item.Select();
    }

    /// <summary>Rebuild the menu and open the page of the item with this id, wherever it sits.</summary>
    public void ShowItem(string itemId)
    {
        ForceMenuTreeRebuild();
        var item = MenuTree.EnumerateTree().FirstOrDefault(i => i.Value is ItemPage page && page.ItemId == itemId);
        if (item != null) item.Select();
        else Debug.LogWarning($"[Equipment] No page for {itemId} after rebuild.");
    }

    /// <summary>Rebuild the menu and open a designed set's page.</summary>
    public void ShowDesignedSet(string setKey)
    {
        ForceMenuTreeRebuild();
        var item = MenuTree.EnumerateTree().FirstOrDefault(i => i.Value is SetPage page && page.SetKey == setKey);
        if (item != null) item.Select();
        else ShowSet(setKey);
    }

    /// <summary>Open the Art page on one set, in its Sets view — to design a sibling piece.</summary>
    public void ShowSet(string setKey)
    {
        var art = MenuTree.EnumerateTree().FirstOrDefault(i => i.GetFullPath() == "Art");
        if (art == null) return;
        art.Select();
        (art.Value as ArtPage)?.PickSet(setKey);
    }

    protected override void OnBeginDrawEditors()
    {
        var selected = MenuTree?.Selection?.FirstOrDefault();
        Sirenix.Utilities.Editor.SirenixEditorGUI.BeginHorizontalToolbar(MenuTree?.Config.SearchToolbarHeight ?? 22);
        {
            GUILayout.Label(selected != null ? selected.Name : "Equipment", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (Sirenix.Utilities.Editor.SirenixEditorGUI.ToolbarButton("New draft"))
                NewDraft("New set", null, null);
            if (Sirenix.Utilities.Editor.SirenixEditorGUI.ToolbarButton("Reload"))
                ForceMenuTreeRebuild();
        }
        Sirenix.Utilities.Editor.SirenixEditorGUI.EndHorizontalToolbar();
    }
}

/// <summary>
/// A resonant item on one page: what the item is (read from the collection), what wearing it
/// counts and costs (the resonance entry, written back to the database), and the engraving it
/// carries, edited in place. Plus the two things a designer does next: try it, and find it.
/// </summary>
public class ItemPage
{
    private readonly EquipmentWindow _window;
    private readonly ResonanceDatabase _database;
    private readonly ResonanceDatabase.Entry _entry;
    private readonly string _setKey;   // null unless the item is an armour part, or a helmet or cape on a set's theme

    public string ItemId => _entry.itemId;

    public ItemPage(EquipmentWindow window, ResonanceDatabase database, ResonanceDatabase.Entry entry)
    {
        _window = window;
        _database = database;
        _entry = entry;
        _setKey = Catalog.SetKeyFor(entry.itemId);
        // Resolved once: the collection logs a warning for a missing icon, and a getter would
        // repeat it on every repaint.
        Icon = Catalog.Icon(entry.itemId);
        Look = Catalog.Look(entry.itemId);
    }

    // ---- the item, as the collection has it

    [BoxGroup("Item"), HorizontalGroup("Item/art", 220), PreviewField(96, ObjectFieldAlignment.Left)]
    [ShowInInspector, ReadOnly, HideLabel, PropertyOrder(-2)]
    public Sprite Icon { get; }

    [HorizontalGroup("Item/art"), PreviewField(96, ObjectFieldAlignment.Left)]
    [ShowInInspector, ReadOnly, HideLabel, PropertyOrder(-1)]
    [Tooltip("What the item looks like worn.")]
    public Sprite Look { get; }

    // The item on a body, alone — the mannequin wears only this, so the piece reads on its own.
    [HorizontalGroup("Item/art", 150), OnInspectorGUI, PropertyOrder(-0.9f)]
    private void DrawOnBody()
    {
        var rect = GUILayoutUtility.GetRect(140f, 200f, GUILayout.ExpandWidth(false));
        _window.Mannequin.Dress(new[] { _entry.itemId });
        _window.Mannequin.Draw(rect);
    }

    [BoxGroup("Item"), ShowInInspector, ReadOnly, LabelText("Name")]
    private string Name => Catalog.DisplayName(_entry.itemId);

    [BoxGroup("Item"), ShowInInspector, ReadOnly, LabelText("Id")]
    private string Id => _entry.itemId;

    [BoxGroup("Item"), ShowInInspector, ReadOnly]
    private string Kind
    {
        get
        {
            var item = Catalog.Find(_entry.itemId);
            return item == null ? "not in ItemCollection" : $"{item.Type} · {item.Class} · {item.Rarity} · {item.Price}g";
        }
    }

    [BoxGroup("Item"), ShowInInspector, ReadOnly, ListDrawerSettings(IsReadOnly = true, ShowFoldout = false)]
    private List<string> Properties
    {
        get
        {
            var item = Catalog.Find(_entry.itemId);
            if (item == null || item.Properties == null) return new List<string>();
            return item.Properties.Select(p => p.Id + "  " + p.Value).ToList();
        }
    }

    // ---- the set it belongs to, with the siblings one click away

    private bool InSet => _setKey != null;

    // A weapon or shield is not a piece of the set; it goes with it. Same box, said differently.
    private bool IsCompanion
    {
        get
        {
            var item = Catalog.Find(_entry.itemId);
            return item != null && (item.Type == Assets.HeroEditor.InventorySystem.Scripts.Enums.ItemType.Weapon ||
                                    item.Type == Assets.HeroEditor.InventorySystem.Scripts.Enums.ItemType.Shield);
        }
    }

    [BoxGroup("Set"), ShowIf("InSet"), ShowInInspector, ReadOnly, LabelText("@this.IsCompanion ? \"Goes with\" : \"Set\""), PropertyOrder(-0.5f)]
    private string SetName => _setKey != null ? Catalog.SetName(_setKey) : "";

    [BoxGroup("Set"), ShowIf("InSet"), Button("Edit the set together"), PropertyOrder(-0.45f)]
    private void OpenSetPage() => _window.ShowDesignedSet(_setKey);

    [BoxGroup("Set"), ShowIf("InSet"), OnInspectorGUI, PropertyOrder(-0.4f)]
    private void DrawSiblings()
    {
        if (_setKey == null) return;
        EditorGUILayout.BeginHorizontal();
        foreach (var part in Catalog.ArmorParts)
            DrawSibling(Catalog.PartLabel(part), Catalog.PartId(_setKey, part));
        EditorGUILayout.EndHorizontal();

        // The helmets and capes on the theme — there can be several, each a fit.
        var extras = Catalog.MatchingPieces(_setKey);
        if (extras.Count > 0)
        {
            EditorGUILayout.BeginHorizontal();
            foreach (var extra in extras)
                DrawSibling(Catalog.DisplayName(extra.Id), extra.Id);
            EditorGUILayout.EndHorizontal();
        }

        // And what goes with it: the weapons and shields on the theme.
        var companions = Catalog.Companions(_setKey);
        if (companions.Count > 0)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("goes with", EditorStyles.miniLabel, GUILayout.Width(60f));
            foreach (var companion in companions)
                DrawSibling(Catalog.DisplayName(companion.Id), companion.Id);
            EditorGUILayout.EndHorizontal();
        }
    }

    // Each sibling is a button: a designed one opens its page, an undesigned one opens the Art
    // page on the set so it can be designed next to the others.
    private void DrawSibling(string label, string id)
    {
        bool self = id == _entry.itemId;
        bool designed = _database.entries.Any(e => e.itemId == id);
        label = self ? $"{label} (this)" : designed ? $"{label} ●" : $"{label} — design";
        using (new EditorGUI.DisabledScope(self))
        {
            if (GUILayout.Button(label, GUILayout.Height(24f)))
            {
                if (designed) _window.ShowItem(id);
                else _window.ShowSet(_setKey);
            }
        }
    }

    // ---- resonance: what wearing it counts, and what it costs to deepen

    [BoxGroup("Resonance"), ShowInInspector, LabelText("Counts")]
    private ResonanceRequirement Requirement
    {
        get => _entry.requirement;
        set { _entry.requirement = value; Dirty(); }
    }

    [BoxGroup("Resonance"), ShowInInspector, MinValue(0), LabelText("Tier II at")]
    private int TierIICost
    {
        get => _entry.tierIICost;
        set { _entry.tierIICost = value; Dirty(); }
    }

    [BoxGroup("Resonance"), ShowInInspector, MinValue(0), LabelText("Tier III at")]
    private int TierIIICost
    {
        get => _entry.tierIIICost;
        set { _entry.tierIIICost = value; Dirty(); }
    }

    [BoxGroup("Resonance"), ShowInInspector, MinValue(0), LabelText("Bankable at")]
    private int EngraveCost
    {
        get => _entry.engraveCost;
        set { _entry.engraveCost = value; Dirty(); }
    }

    // ---- the engraving, edited here rather than found in the project

    [BoxGroup("Engraving"), ShowInInspector, Required, AssetsOnly, HideLabel]
    [InlineEditor(InlineEditorObjectFieldModes.Boxed, Expanded = true)]
    private Engraving Engraving
    {
        get => _entry.engraving;
        set { _entry.engraving = value; Dirty(); }
    }

    // ---- try it

    // Ordered last by hand: Odin draws fields before properties, which put this box above the
    // numbers it exists to try.
    [BoxGroup("Try it"), ShowInInspector, Range(1, 3), LabelText("Tier"), PropertyOrder(100)]
    private int _testTier = 1;

    [BoxGroup("Try it"), Button(ButtonSizes.Large), EnableIf("@UnityEngine.Application.isPlaying"), PropertyOrder(101)]
    [InfoBox("In play, in Setup: banks this engraving on the selected hero (or the first one) and " +
             "reconciles, so the badges and the fight show it without finding the item first.")]
    private void BankOnSelectedHero()
    {
        var game = GameManager.Instance;
        if (game == null || _entry.engraving == null) return;

        var inspector = Object.FindObjectOfType<UnitInspector>();
        var hero = inspector != null && inspector.Selected != null && inspector.Selected.isTeam
            ? inspector.Selected
            : game.allyCharacters.FirstOrDefault(h => h != null && h.gameObject.activeInHierarchy);
        if (hero == null || hero.Resonance == null) { Debug.Log("[Equipment] No hero to bank on."); return; }

        hero.Resonance.banked.Add(new Resonance.Banked { engraving = _entry.engraving, tier = _testTier });
        hero.Resonance.Refresh();
        Debug.Log($"[Equipment] Banked {_entry.engraving.DisplayName} {_testTier} on {UnitInspector.DisplayName(hero)}.");
    }

    [BoxGroup("Try it"), Button, HorizontalGroup("Try it/find"), PropertyOrder(102)]
    private void PingDatabase() => EditorGUIUtility.PingObject(_database);

    [BoxGroup("Try it"), Button, HorizontalGroup("Try it/find"), EnableIf("@this.Engraving != null"), PropertyOrder(103)]
    private void PingEngraving() => EditorGUIUtility.PingObject(_entry.engraving);

    private void Dirty()
    {
        EditorUtility.SetDirty(_database);
    }
}
