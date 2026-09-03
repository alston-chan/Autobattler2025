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

        // Items: the designed ones — anything with a resonance entry. Named by the item, so the
        // menu reads as the company's wardrobe rather than as ids.
        if (resonance != null)
        {
            foreach (var entry in resonance.entries.OrderBy(e => Catalog.DisplayName(e.itemId)))
            {
                var page = new ItemPage(resonance, entry);
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
    private readonly ResonanceDatabase _database;
    private readonly ResonanceDatabase.Entry _entry;

    public ItemPage(ResonanceDatabase database, ResonanceDatabase.Entry entry)
    {
        _database = database;
        _entry = entry;
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
