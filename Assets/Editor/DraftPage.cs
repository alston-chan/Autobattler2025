using System.Collections.Generic;
using System.Linq;
using Assets.HeroEditor.InventorySystem.Scripts.Enums;
using Sirenix.OdinInspector;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One draft on a page: the title, status and notes; the pieces as a table with the engraving each
/// might get and the idea in words; the pieces on the mannequin. Nothing here reaches the game
/// until <see cref="Promote"/>, which makes resonance entries of every piece that has an engraving
/// — through the same path the Sets view uses — and opens the designed set's page.
/// </summary>
public class DraftPage
{
    private const string ResonancePath = "Assets/Resources/ResonanceDatabase.asset";

    private readonly EquipmentWindow _window;
    private readonly SetDrafts _asset;
    private readonly SetDrafts.Draft _draft;
    private readonly HashSet<string> _wearing = new HashSet<string>();
    private string _wornKey;

    public DraftPage(EquipmentWindow window, SetDrafts asset, SetDrafts.Draft draft)
    {
        _window = window;
        _asset = asset;
        _draft = draft;
    }

    public SetDrafts.Draft Draft => _draft;
    public Sprite Icon => _draft.pieces.Select(p => Catalog.Icon(p.itemId)).FirstOrDefault(s => s != null)
                          ?? (_draft.setKey != null ? Catalog.Icon(Catalog.PartId(_draft.setKey, "vest")) : null);

    // ---- what it is

    [ShowInInspector, PropertyOrder(0), LabelText("Title")]
    private string Title { get => _draft.title; set { _draft.title = value; Dirty(); } }

    [ShowInInspector, PropertyOrder(0), EnumToggleButtons, LabelText("Status")]
    private SetDrafts.Status Status { get => _draft.status; set { _draft.status = value; Dirty(); } }

    [ShowInInspector, PropertyOrder(0), ReadOnly, LabelText("Set"), ShowIf("HasSet")]
    private string SetName => string.IsNullOrEmpty(_draft.setKey) ? "" : $"{Catalog.SetName(_draft.setKey)}   ·   {_draft.setKey}";

    private bool HasSet => !string.IsNullOrEmpty(_draft.setKey);

    [ShowInInspector, PropertyOrder(1), MultiLineProperty(6), HideLabel, Title("Notes", Bold = false)]
    private string Notes { get => _draft.notes; set { _draft.notes = value; Dirty(); } }

    // ---- the pieces on a body

    [OnInspectorGUI, PropertyOrder(2)]
    private void DrawOnBody()
    {
        var ids = _draft.pieces.Select(p => p.itemId).Where(Catalog.IsKnown).Distinct().ToList();
        string key = string.Join("|", ids);
        if (_wornKey != key)
        {
            _wornKey = key;
            _wearing.Clear();
            // Everything in the draft, but one weapon and one shield at most: two swords cannot both be in hand.
            bool weapon = false, shield = false;
            foreach (var id in ids)
            {
                var item = Catalog.Find(id);
                if (item == null) continue;
                if (item.Type == ItemType.Weapon) { if (weapon) continue; weapon = true; }
                if (item.Type == ItemType.Shield) { if (shield) continue; shield = true; }
                _wearing.Add(id);
            }
        }
        if (ids.Count == 0) return;

        EditorGUILayout.BeginHorizontal();
        var rect = GUILayoutUtility.GetRect(180f, 260f, GUILayout.ExpandWidth(false));
        _window.Mannequin.Dress(_wearing);
        _window.Mannequin.Draw(rect);

        EditorGUILayout.BeginVertical();
        GUILayout.Label("Wearing", EditorStyles.miniBoldLabel);
        foreach (var id in ids)
        {
            bool on = _wearing.Contains(id);
            bool now = GUILayout.Toggle(on, " " + Catalog.DisplayName(id), GUILayout.Height(20f));
            if (now != on) { if (now) _wearing.Add(id); else _wearing.Remove(id); }
        }
        EditorGUILayout.EndVertical();
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
    }

    // ---- the pieces

    [ShowInInspector, PropertyOrder(3), LabelText("Pieces")]
    [TableList(AlwaysExpanded = true, DrawScrollView = false, ShowIndexLabels = false)]
    [OnValueChanged("Dirty", IncludeChildren = true)]
    private List<SetDrafts.Piece> Pieces
    {
        get => _draft.pieces;
        set { _draft.pieces = value; Dirty(); }   // a setter, so Odin treats the rows as editable
    }

    [PropertyOrder(4), Button, HorizontalGroup("pieces"), ShowIf("HasSet"), LabelText("Add the theme's pieces")]
    [Tooltip("Adds the armour parts, the helmets and capes, and the weapons and shields on the set's theme that are not in the draft yet.")]
    private void AddThemePieces()
    {
        var have = new HashSet<string>(_draft.pieces.Select(p => p.itemId));
        var ids = Catalog.ArmorParts.Select(part => Catalog.PartId(_draft.setKey, part))
            .Concat(Catalog.MatchingPieces(_draft.setKey).Select(i => i.Id))
            .Concat(Catalog.Companions(_draft.setKey).Select(i => i.Id))
            .Where(Catalog.IsKnown);
        int added = 0;
        foreach (var id in ids)
            if (have.Add(id)) { _draft.pieces.Add(new SetDrafts.Piece { itemId = id }); added++; }
        if (added > 0) Dirty();
    }

    [PropertyOrder(4), Button, HorizontalGroup("pieces"), LabelText("Open the set in Art"), ShowIf("HasSet")]
    private void OpenInArt() => _window.ShowSet(_draft.setKey);

    // ---- promote or drop

    private int Promotable => _draft.pieces.Count(p => p.engraving != null && Catalog.IsKnown(p.itemId) && !IsDesigned(p.itemId));
    private static bool IsDesigned(string id)
    {
        var resonance = AssetDatabase.LoadAssetAtPath<ResonanceDatabase>(ResonancePath);
        return resonance != null && resonance.entries.Any(e => e.itemId == id);
    }

    [BoxGroup("Promote"), PropertyOrder(5), ShowInInspector, LabelText("Counts")]
    private ResonanceRequirement _requirement = ResonanceRequirement.CombatsWorn;

    [BoxGroup("Promote"), PropertyOrder(5), ShowInInspector, AssetsOnly, ValueDropdown("Pools"), LabelText("Offer in")]
    private RewardPool _pool;

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

    [BoxGroup("Promote"), PropertyOrder(6)]
    [Button("@\"Promote to a designed set  (\" + this.Promotable + \" piece\" + (this.Promotable == 1 ? \"\" : \"s\") + \")\"", ButtonSizes.Large)]
    [EnableIf("@this.Promotable > 0")]
    [InfoBox("Makes a resonance entry of every piece that has an engraving and is not designed yet, adds the ids to the " +
             "chosen pool, saves, marks the draft Ready, and opens the designed set's page. Pieces still in words are left in the draft.")]
    private void Promote()
    {
        var resonance = AssetDatabase.LoadAssetAtPath<ResonanceDatabase>(ResonancePath);
        if (resonance == null) { Debug.LogError("[Equipment] No ResonanceDatabase at Resources/ResonanceDatabase."); return; }

        var made = new List<string>();
        string first = null;
        foreach (var piece in _draft.pieces)
        {
            if (piece.engraving == null || !Catalog.IsKnown(piece.itemId) || IsDesigned(piece.itemId)) continue;
            resonance.entries.Add(new ResonanceDatabase.Entry { itemId = piece.itemId, engraving = piece.engraving, requirement = _requirement });
            if (_pool != null && !_pool.itemIds.Contains(piece.itemId)) { _pool.itemIds.Add(piece.itemId); EditorUtility.SetDirty(_pool); }
            made.Add($"{Catalog.DisplayName(piece.itemId)} → {piece.engraving.DisplayName}");
            if (first == null) first = piece.itemId;
        }
        if (made.Count == 0) return;

        EditorUtility.SetDirty(resonance);
        _draft.status = SetDrafts.Status.Ready;
        Dirty();
        AssetDatabase.SaveAssetIfDirty(resonance);
        if (_pool != null) AssetDatabase.SaveAssetIfDirty(_pool);
        AssetDatabase.SaveAssetIfDirty(_asset);
        Debug.Log($"[Equipment] Draft '{_draft.title}' promoted: {string.Join(", ", made)}" + (_pool != null ? $"; offered in {_pool.name}." : "."));

        string setKey = _draft.setKey ?? Catalog.SetKeyFor(first);
        if (setKey != null) _window.ShowDesignedSet(setKey); else _window.ShowItem(first);
    }

    [BoxGroup("Promote"), PropertyOrder(7), Button, GUIColor(1f, 0.6f, 0.6f)]
    private void DeleteDraft()
    {
        if (!EditorUtility.DisplayDialog("Delete draft", $"Delete the draft '{_draft.title}'? Its notes go with it; nothing in the game changes.", "Delete", "Keep")) return;
        _asset.drafts.Remove(_draft);
        Dirty();
        AssetDatabase.SaveAssetIfDirty(_asset);
        _window.ForceMenuTreeRebuild();
    }

    private void Dirty() => EditorUtility.SetDirty(_asset);
}
