using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The catalogue's notes file, Docs/SetCatalog/notes.md: a heading per set to write against when
/// the editor is far away, with a <c>theme:</c> line per set that round-trips to the SetThemes
/// asset. Writing keeps whatever text already sits under a set's heading; importing reads the
/// theme lines back and leaves the rest alone. Under Docs, which git ignores.
/// </summary>
public static class SetNotes
{
    public const string Path = "Docs/SetCatalog/notes.md";

    private const string Preamble =
        "# Set notes\n\n" +
        "Write under a set's heading. `theme: Shadow` names the set's theme (Tools > Equipment > Import catalogue notes\n" +
        "reads these back). Lines like `upper: Bulwark`, `lower: idea: slows what it passes`, `helmet: Swift` are\n" +
        "kept as notes for the pieces; anything else is kept as it is.\n";

    /// <summary>Every armour set the collection has, in the catalogue's order.</summary>
    public static List<string> AllSetKeys()
    {
        var collection = Catalog.Items();
        if (collection == null) return new List<string>();
        return collection.Items
            .Where(i => i != null && Catalog.TryParseArmorPart(i.Id, out _, out var part) && part == "vest")
            .Select(i => { Catalog.TryParseArmorPart(i.Id, out var key, out _); return key; })
            .Distinct()
            .OrderBy(k => Catalog.Family(k)).ThenBy(Catalog.SetName)
            .ToList();
    }

    [MenuItem("Tools/Equipment/Write catalogue notes")]
    public static void WriteAll() => Write(AllSetKeys());

    /// <summary>
    /// (Re)write the notes for these sets. Text already under a set's heading is kept; its theme
    /// line is replaced with the asset's current theme.
    /// </summary>
    public static void Write(IEnumerable<string> setKeys)
    {
        var kept = File.Exists(Path) ? Parse(File.ReadAllText(Path)) : new Dictionary<string, Section>();
        var themes = SetThemes.Active;

        var sb = new StringBuilder();
        sb.Append(Preamble);
        string currentPack = null;
        int count = 0;
        foreach (var key in setKeys)
        {
            string pack = Catalog.Family(key);
            if (pack != currentPack)
            {
                currentPack = pack;
                sb.Append($"\n## {pack}\n");
            }
            sb.Append($"\n### {Catalog.SetName(key)}\n<!-- {key} -->\n");
            sb.Append($"theme: {(themes != null ? themes.ThemeOf(key) : "")}\n");
            if (kept.TryGetValue(key, out var section) && section.body.Count > 0)
            {
                sb.Append('\n');
                foreach (var line in section.body) sb.Append(line).Append('\n');
            }
            sb.Append('\n');
            count++;
        }

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
        File.WriteAllText(Path, sb.ToString(), new UTF8Encoding(false));
        Debug.Log($"[SetNotes] {count} sets → {Path} ({kept.Count(k => k.Value.body.Count > 0)} with notes kept).");
    }

    /// <summary>Read the theme lines back into the SetThemes asset. Piece lines are counted, not imported.</summary>
    [MenuItem("Tools/Equipment/Import catalogue notes")]
    public static void Import()
    {
        if (!File.Exists(Path)) { Debug.LogWarning($"[SetNotes] Nothing at {Path} — export the catalogue first."); return; }
        var themes = SetThemes.Active;
        if (themes == null) { Debug.LogWarning($"[SetNotes] No SetThemes asset at {SetThemes.AssetPath}."); return; }

        var sections = Parse(File.ReadAllText(Path));
        int set = 0, cleared = 0, unchanged = 0, pieceLines = 0;
        var newThemes = new List<string>();
        foreach (var pair in sections)
        {
            var section = pair.Value;
            if (section.theme != null)
            {
                string before = themes.ThemeOf(pair.Key);
                if (before == section.theme) unchanged++;
                else
                {
                    if (section.theme != "" && !themes.themes.Contains(section.theme)) newThemes.Add(section.theme);
                    themes.Set(pair.Key, section.theme);
                    if (section.theme == "") cleared++; else set++;
                }
            }
            pieceLines += section.body.Count(l => System.Text.RegularExpressions.Regex.IsMatch(l, @"^(upper|lower|helmet|weapon|shield)\s*:", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        }

        if (set + cleared > 0)
        {
            EditorUtility.SetDirty(themes);
            AssetDatabase.SaveAssetIfDirty(themes);
        }
        Debug.Log($"[SetNotes] Themes: {set} set, {cleared} cleared, {unchanged} unchanged" +
                  (newThemes.Count > 0 ? $"; new theme name(s): {string.Join(", ", newThemes.Distinct())}" : "") +
                  (pieceLines > 0 ? $". {pieceLines} piece line(s) seen — those stay notes." : "."));
    }

    private class Section
    {
        public string theme;                       // null when the section has no theme line
        public readonly List<string> body = new List<string>();
    }

    /// <summary>Sections by set key: the theme line, and every other line under the heading, trimmed of blank ends.</summary>
    private static Dictionary<string, Section> Parse(string text)
    {
        var result = new Dictionary<string, Section>();
        Section current = null;
        string pendingKey = null;
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            string line = raw.TrimEnd();
            if (line.StartsWith("## ") || line.StartsWith("# ")) { current = null; pendingKey = null; continue; }
            if (line.StartsWith("### ")) { current = null; pendingKey = "?"; continue; }
            var comment = System.Text.RegularExpressions.Regex.Match(line, @"^<!--\s*(.+?)\s*-->$");
            if (pendingKey != null && comment.Success)
            {
                current = new Section();
                result[comment.Groups[1].Value] = current;
                pendingKey = null;
                continue;
            }
            if (current == null) continue;
            var theme = System.Text.RegularExpressions.Regex.Match(line, @"^theme\s*:(.*)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (theme.Success && current.theme == null) { current.theme = theme.Groups[1].Value.Trim(); continue; }
            current.body.Add(line);
        }
        foreach (var section in result.Values)
        {
            while (section.body.Count > 0 && section.body[0] == "") section.body.RemoveAt(0);
            while (section.body.Count > 0 && section.body[section.body.Count - 1] == "") section.body.RemoveAt(section.body.Count - 1);
        }
        return result;
    }
}
