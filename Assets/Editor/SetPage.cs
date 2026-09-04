using System.Collections.Generic;
using System.Linq;
using Assets.HeroEditor.InventorySystem.Scripts.Enums;
using Sirenix.OdinInspector;
using UnityEditor;
using UnityEngine;

/// <summary>
/// A designed set on one page: every designed piece on the theme as a row — engraving, what it
/// counts, tier costs — edited in place and written back to the database, with the set on the
/// mannequin above and the pieces not yet designed listed below. The pieces still have their own
/// pages under this one; this is where they are edited together, which is how a set is designed.
/// </summary>
public class SetPage
{
    private readonly EquipmentWindow _window;
    private readonly ResonanceDatabase _database;
    private readonly string _setKey;

    public SetPage(EquipmentWindow window, ResonanceDatabase database, string setKey)
    {
        _window = window;
        _database = database;
        _setKey = setKey;
        Rebuild();
    }

    public string SetKey => _setKey;
    public Sprite Icon => Catalog.Icon(Catalog.PartId(_setKey, "vest"));

    // ---- what is on the theme

    private List<string> _themeIds = new List<string>();      // pieces and companions, designed or not
    private List<SetRow> _rows = new List<SetRow>();
    private readonly HashSet<string> _wearing = new HashSet<string>();

    private void Rebuild()
    {
        _themeIds = Catalog.ArmorParts.Select(part => Catalog.PartId(_setKey, part))
            .Concat(Catalog.MatchingPieces(_setKey).Select(i => i.Id))
            .Concat(Catalog.Companions(_setKey).Select(i => i.Id))
            .Where(Catalog.IsKnown)
            .ToList();

        _rows = _themeIds
            .Select(id => _database.entries.FirstOrDefault(e => e.itemId == id))
            .Where(e => e != null)
            .Select(e => new SetRow(_window, _database, e))
            .ToList();

        _wearing.Clear();
        foreach (var row in _rows) _wearing.Add(row.Id);
        // One weapon and one shield in hand, whether designed or not — the look is the set's.
        var weapon = Catalog.Companions(_setKey).FirstOrDefault(i => i.Type == ItemType.Weapon);
        var shield = Catalog.Companions(_setKey).FirstOrDefault(i => i.Type == ItemType.Shield);
        if (weapon != null) _wearing.Add(weapon.Id);
        if (shield != null) _wearing.Add(shield.Id);
    }

    [ShowInInspector, ReadOnly, LabelText("Set"), PropertyOrder(0)]
    private string Name => Catalog.SetName(_setKey);

    [ShowInInspector, ReadOnly, LabelText("Family · theme"), PropertyOrder(0)]
    private string Family => $"{_setKey}   ·   {Catalog.Theme(Catalog.SetName(_setKey))}";

    // ---- the set on a body, with toggles

    [OnInspectorGUI, PropertyOrder(1)]
    private void DrawOnBody()
    {
        EditorGUILayout.BeginHorizontal();
        var rect = GUILayoutUtility.GetRect(180f, 260f, GUILayout.ExpandWidth(false));
        _window.Mannequin.Dress(_wearing);
        _window.Mannequin.Draw(rect);

        EditorGUILayout.BeginVertical();
        GUILayout.Label("Wearing", EditorStyles.miniBoldLabel);
        foreach (var id in _themeIds)
        {
            bool on = _wearing.Contains(id);
            bool designed = _database.entries.Any(e => e.itemId == id);
            bool now = GUILayout.Toggle(on, " " + Catalog.DisplayName(id) + (designed ? "  ●" : ""), GUILayout.Height(20f));
            if (now != on) { if (now) _wearing.Add(id); else _wearing.Remove(id); }
        }
        GUILayout.Space(6f);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("All", GUILayout.Width(50f))) foreach (var id in _themeIds) _wearing.Add(id);
        if (GUILayout.Button("None", GUILayout.Width(50f))) _wearing.Clear();
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
    }

    // ---- the designed pieces, edited together

    [ShowInInspector, PropertyOrder(2), LabelText("Designed pieces")]
    [TableList(AlwaysExpanded = true, DrawScrollView = false, IsReadOnly = true, ShowIndexLabels = false)]
    private List<SetRow> Rows => _rows;

    [BoxGroup("For every piece"), PropertyOrder(3), ShowInInspector, LabelText("Counts")]
    private ResonanceRequirement _allRequirement = ResonanceRequirement.CombatsWorn;

    [BoxGroup("For every piece"), PropertyOrder(3), Button, HorizontalGroup("For every piece/counts")]
    private void ApplyCountsToAll()
    {
        foreach (var row in _rows) row.Counts = _allRequirement;
    }

    [BoxGroup("For every piece"), PropertyOrder(4), ShowInInspector, HorizontalGroup("For every piece/costs"), LabelText("Tier II at"), LabelWidth(70), MinValue(0)]
    private int _allTierII = 3;
    [BoxGroup("For every piece"), PropertyOrder(4), ShowInInspector, HorizontalGroup("For every piece/costs"), LabelText("Tier III at"), LabelWidth(70), MinValue(0)]
    private int _allTierIII = 6;
    [BoxGroup("For every piece"), PropertyOrder(4), ShowInInspector, HorizontalGroup("For every piece/costs"), LabelText("Bankable at"), LabelWidth(80), MinValue(0)]
    private int _allEngrave = 3;

    [BoxGroup("For every piece"), PropertyOrder(4), Button, HorizontalGroup("For every piece/costs")]
    private void ApplyCostsToAll()
    {
        foreach (var row in _rows) { row.TierII = _allTierII; row.TierIII = _allTierIII; row.Bankable = _allEngrave; }
    }

    // ---- what is not designed yet

    private List<string> Undesigned => _themeIds.Where(id => !_database.entries.Any(e => e.itemId == id)).ToList();
    private bool HasUndesigned => Undesigned.Count > 0;

    [PropertyOrder(5), ShowInInspector, ReadOnly, ShowIf("HasUndesigned"), ListDrawerSettings(IsReadOnly = true, ShowFoldout = false), LabelText("Not designed yet")]
    private List<string> UndesignedNames => Undesigned.Select(id => $"{Catalog.TypeLabel(Catalog.Find(id)?.Type ?? ItemType.Undefined)}  {Catalog.DisplayName(id)}").ToList();

    [PropertyOrder(6), Button(ButtonSizes.Medium), ShowIf("HasUndesigned"), LabelText("Design the rest in the Sets view")]
    private void DesignTheRest() => _window.ShowSet(_setKey);

    // ---- try it

    [BoxGroup("Try it"), PropertyOrder(7), Button(ButtonSizes.Large), EnableIf("@UnityEngine.Application.isPlaying")]
    [InfoBox("In play, in Setup: banks every designed piece's engraving on the selected hero (or the first one), tier I, and reconciles.")]
    private void BankAllOnSelectedHero()
    {
        var game = GameManager.Instance;
        if (game == null) return;
        var inspector = Object.FindObjectOfType<UnitInspector>();
        var hero = inspector != null && inspector.Selected != null && inspector.Selected.isTeam
            ? inspector.Selected
            : game.allyCharacters.FirstOrDefault(h => h != null && h.gameObject.activeInHierarchy);
        if (hero == null || hero.Resonance == null) { Debug.Log("[Equipment] No hero to bank on."); return; }

        int banked = 0;
        foreach (var row in _rows)
        {
            if (row.Engraving == null) continue;
            hero.Resonance.banked.Add(new Resonance.Banked { engraving = row.Engraving, tier = 1 });
            banked++;
        }
        hero.Resonance.Refresh();
        Debug.Log($"[Equipment] Banked {banked} engraving(s) of {Catalog.SetName(_setKey)} on {UnitInspector.DisplayName(hero)}.");
    }

    [BoxGroup("Try it"), PropertyOrder(8), Button, HorizontalGroup("Try it/find")]
    private void PingDatabase() => EditorGUIUtility.PingObject(_database);

    /// <summary>One designed piece as a table row; every setter writes to the database entry.</summary>
    public class SetRow
    {
        private readonly EquipmentWindow _window;
        private readonly ResonanceDatabase _database;
        private readonly ResonanceDatabase.Entry _entry;

        public SetRow(EquipmentWindow window, ResonanceDatabase database, ResonanceDatabase.Entry entry)
        {
            _window = window;
            _database = database;
            _entry = entry;
            Icon = Catalog.Icon(entry.itemId);
        }

        public string Id => _entry.itemId;

        [TableColumnWidth(58, Resizable = false), PreviewField(48, ObjectFieldAlignment.Center), ShowInInspector, ReadOnly, HideLabel]
        public Sprite Icon { get; }

        [TableColumnWidth(200), ShowInInspector, ReadOnly, DisplayAsString, HideLabel]
        public string Piece => $"{Catalog.TypeLabel(Catalog.Find(_entry.itemId)?.Type ?? ItemType.Undefined)}  {Catalog.DisplayName(_entry.itemId)}";

        [TableColumnWidth(190), ShowInInspector, AssetsOnly, ValueDropdown("@ArtPage.EngravingOptions()"), HideLabel]
        public Engraving Engraving
        {
            get => _entry.engraving;
            set { _entry.engraving = value; Dirty(); }
        }

        [TableColumnWidth(130), ShowInInspector, HideLabel]
        public ResonanceRequirement Counts
        {
            get => _entry.requirement;
            set { _entry.requirement = value; Dirty(); }
        }

        [TableColumnWidth(60), ShowInInspector, LabelText("II"), LabelWidth(16), MinValue(0)]
        public int TierII
        {
            get => _entry.tierIICost;
            set { _entry.tierIICost = value; Dirty(); }
        }

        [TableColumnWidth(60), ShowInInspector, LabelText("III"), LabelWidth(20), MinValue(0)]
        public int TierIII
        {
            get => _entry.tierIIICost;
            set { _entry.tierIIICost = value; Dirty(); }
        }

        [TableColumnWidth(70), ShowInInspector, LabelText("Bank"), LabelWidth(32), MinValue(0)]
        public int Bankable
        {
            get => _entry.engraveCost;
            set { _entry.engraveCost = value; Dirty(); }
        }

        [TableColumnWidth(60, Resizable = false), Button("Open")]
        private void Open() => _window.ShowItem(_entry.itemId);

        private void Dirty() => EditorUtility.SetDirty(_database);
    }
}
