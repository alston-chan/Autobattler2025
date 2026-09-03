using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Where a designed set is thought about before it exists. A draft names a set's theme, the
/// pieces it might have, the engraving each might carry, and the notes — and none of it touches
/// the game: nothing here is a resonance entry until the draft is promoted. Plain YAML, one asset
/// for all drafts, so a half-formed idea survives a session and can be read in the diff.
/// </summary>
[CreateAssetMenu(menuName = "Data/Set Drafts", fileName = "SetDrafts")]
public class SetDrafts : ScriptableObject
{
    public enum Status
    {
        /// <summary>A theme and a thought; pieces not yet chosen.</summary>
        Idea,
        /// <summary>Pieces and engravings being worked out.</summary>
        Drafting,
        /// <summary>Every piece has its engraving; waiting to be promoted, or promoted and kept for the notes.</summary>
        Ready,
    }

    [System.Serializable]
    public class Piece
    {
        [ValueDropdown("ItemIds"), TableColumnWidth(240, Resizable = true)]
        public string itemId;

        [AssetsOnly, TableColumnWidth(170)]
        [Tooltip("What wearing it would grant. Empty while the idea is still words.")]
        public Engraving engraving;

        [TextArea(1, 3), TableColumnWidth(260)]
        [Tooltip("The idea, in words, before it is an engraving — or why it is this one.")]
        public string idea;

        private static IEnumerable<ValueDropdownItem<string>> ItemIds() => Catalog.ItemIds();
    }

    [System.Serializable]
    public class Draft
    {
        public string title = "New set";
        public Status status = Status.Idea;

        [Tooltip("The armour set's key (Pack.Tier.Armor.Name) the draft is about, if any — from the Sets view.")]
        public string setKey;

        [TextArea(3, 10)]
        public string notes;

        public List<Piece> pieces = new List<Piece>();
    }

    [TableList(AlwaysExpanded = true, DrawScrollView = false)]
    public List<Draft> drafts = new List<Draft>();
}
