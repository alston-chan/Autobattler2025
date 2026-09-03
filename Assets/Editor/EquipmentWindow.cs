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
        // menu reads as the company's wardrobe rather than as ids.
        if (resonance != null)
        {
            foreach (var entry in resonance.entries.OrderBy(e => Catalog.DisplayName(e.itemId)))
            {
                var page = new ItemPage(this, resonance, entry);
                tree.Add("Items/" + Catalog.DisplayName(entry.itemId), page, page.Icon);
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

        if (resonance != null) tree.Add("Databases/Resonance", resonance);
        if (spellbooks != null) tree.Add("Databases/Spellbooks", spellbooks);

        return tree;
    }

    /// <summary>Rebuild the menu and open the page of the item with this id.</summary>
    public void ShowItem(string itemId)
    {
        ForceMenuTreeRebuild();
        string path = "Items/" + Catalog.DisplayName(itemId);
        var item = MenuTree.EnumerateTree().FirstOrDefault(i => i.GetFullPath() == path);
        if (item != null) item.Select();
        else Debug.LogWarning($"[Equipment] No page for {itemId} after rebuild.");
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

    [BoxGroup("Set"), ShowIf("InSet"), ShowInInspector, ReadOnly, LabelText("Set"), PropertyOrder(-0.5f)]
    private string SetName => _setKey != null ? Catalog.SetName(_setKey) : "";

    [BoxGroup("Set"), ShowIf("InSet"), OnInspectorGUI, PropertyOrder(-0.4f)]
    private void DrawSiblings()
    {
        if (_setKey == null) return;
        EditorGUILayout.BeginHorizontal();
        foreach (var part in Catalog.ArmorParts)
            DrawSibling(part, Catalog.PartId(_setKey, part));
        EditorGUILayout.EndHorizontal();

        // The helmets and capes on the theme — there can be several, each a fit.
        var extras = Catalog.MatchingPieces(_setKey);
        if (extras.Count == 0) return;
        EditorGUILayout.BeginHorizontal();
        foreach (var extra in extras)
            DrawSibling(Catalog.DisplayName(extra.Id), extra.Id);
        EditorGUILayout.EndHorizontal();
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
