using System.Collections.Generic;
using System.Linq;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Which theme each armour set belongs to — Shadow, Fury, Aegis… the ladder its pieces are authored
/// on, and the tag a Common inherits from its sprite family so it counts toward a hero's theme
/// without an engraving of its own (Docs/Resonance.md, the theme count). Keyed by the set key the
/// armour parts share; a helmet, weapon or shield on the set's look resolves to it through
/// <see cref="Catalog.SetKeyFor"/>. Lives in Resources so play can read it. Edited from the
/// designer's Sets view and from the catalogue notes' <c>theme:</c> lines.
/// </summary>
[CreateAssetMenu(menuName = "Data/Set Themes", fileName = "SetThemes")]
public class SetThemes : ScriptableObject
{
    public const string AssetPath = "Assets/Resources/SetThemes.asset";

    [System.Serializable]
    public class Entry
    {
        [ReadOnly, TableColumnWidth(340)] public string setKey;
        [ValueDropdown("@SetThemes.ThemeOptions()"), TableColumnWidth(150)] public string theme;
    }

    [Tooltip("The theme names, in the order the designer lists them. Adding one here is how a theme is born.")]
    public List<string> themes = new List<string>();

    [TableList(AlwaysExpanded = true, DrawScrollView = false)]
    public List<Entry> entries = new List<Entry>();

    private static SetThemes _active;

    /// <summary>The asset, from Resources. Null when it has not been created yet.</summary>
    public static SetThemes Active
    {
        get
        {
            if (_active == null) _active = Resources.Load<SetThemes>("SetThemes");
            return _active;
        }
    }

    /// <summary>The set's theme, or "" when it has none.</summary>
    public string ThemeOf(string setKey)
    {
        if (string.IsNullOrEmpty(setKey)) return "";
        var entry = entries.FirstOrDefault(e => e.setKey == setKey);
        return entry != null && entry.theme != null ? entry.theme : "";
    }

    /// <summary>The theme of any item, through the set it belongs to or goes with. "" when none.</summary>
    public string ThemeOfItem(string itemId)
    {
        var key = Catalog.SetKeyFor(itemId);
        return key != null ? ThemeOf(key) : "";
    }

    /// <summary>Give a set a theme; "" takes it away. A name not in the list is added to it.</summary>
    public void Set(string setKey, string theme)
    {
        if (string.IsNullOrEmpty(setKey)) return;
        theme = theme?.Trim() ?? "";
        var entry = entries.FirstOrDefault(e => e.setKey == setKey);
        if (theme == "")
        {
            if (entry != null) entries.Remove(entry);
            return;
        }
        if (!themes.Contains(theme)) themes.Add(theme);
        if (entry == null) entries.Add(new Entry { setKey = setKey, theme = theme });
        else entry.theme = theme;
    }

    /// <summary>Every set on a theme.</summary>
    public IEnumerable<string> SetsOn(string theme) =>
        entries.Where(e => e.theme == theme).Select(e => e.setKey);

    /// <summary>For the designer's dropdowns: none, then the themes in their listed order.</summary>
    public static IEnumerable<ValueDropdownItem<string>> ThemeOptions()
    {
        yield return new ValueDropdownItem<string>("(none)", "");
        var active = Active;
        if (active == null) yield break;
        foreach (var theme in active.themes)
            if (!string.IsNullOrEmpty(theme)) yield return new ValueDropdownItem<string>(theme, theme);
    }
}
