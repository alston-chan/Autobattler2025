using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Assets.HeroEditor.InventorySystem.Scripts.Data;
using Assets.HeroEditor.InventorySystem.Scripts.Enums;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The whole wardrobe as one page you can open on a phone: every armour set on the mannequin
/// wearing its entire theme — upper, lower, helmet, cape, weapon, shield — with the icons, names,
/// stats and what is designed or drafted, grouped by pack and searchable. One self-contained HTML
/// file with the images embedded, so it needs nothing but a browser, and a notes file with a
/// heading per set to write against when the editor is far away.
///
/// Written under Docs/SetCatalog, which git ignores: regenerate rather than commit.
/// </summary>
public static class SetCatalogExporter
{
    private const string OutDir = "Docs/SetCatalog";
    private const int BodyWidth = 220, BodyHeight = 320;

    [MenuItem("Tools/Equipment/Export set catalogue")]
    public static void Export()
    {
        var collection = Catalog.Items();
        if (collection == null) { Debug.LogError("[SetCatalog] No item collection."); return; }

        Directory.CreateDirectory(OutDir);
        Directory.CreateDirectory(Path.Combine(OutDir, "img"));

        var resonance = AssetDatabase.LoadAssetAtPath<ResonanceDatabase>("Assets/Resources/ResonanceDatabase.asset");
        var drafts = AssetDatabase.LoadAssetAtPath<SetDrafts>("Assets/Data/SetDrafts.asset");

        // Every set: the vest rows say which exist.
        var setKeys = collection.Items
            .Where(i => i != null && Catalog.TryParseArmorPart(i.Id, out _, out var part) && part == "vest")
            .Select(i => { Catalog.TryParseArmorPart(i.Id, out var key, out _); return key; })
            .Distinct()
            .OrderBy(k => Catalog.Family(k)).ThenBy(Catalog.SetName)
            .ToList();

        var html = new StringBuilder();
        var notes = new StringBuilder();
        var iconCache = new Dictionary<string, string>();
        var mannequin = new MannequinPreview();
        int done = 0;
        try
        {
            html.Append(Head());
            notes.Append("# Set notes\n\nWrite under a set's heading. Lines like `upper: Bulwark`, `lower: idea: slows what it passes`,\n`helmet: Swift` are understood by the importer; anything else is kept as notes.\n\n");

            string currentPack = null;
            foreach (var key in setKeys)
            {
                string pack = Catalog.Family(key);
                if (pack != currentPack)
                {
                    if (currentPack != null) html.Append("</div></section>\n");
                    currentPack = pack;
                    html.Append($"<section class=\"pack\" data-pack=\"{Esc(pack)}\"><h2>{Esc(pack)}</h2><div class=\"grid\">\n");
                    notes.Append($"\n## {pack}\n");
                }

                EditorUtility.DisplayProgressBar("Set catalogue", Catalog.SetName(key), (float)done / setKeys.Count);
                html.Append(Card(key, collection, resonance, drafts, mannequin, iconCache));
                notes.Append($"\n### {Catalog.SetName(key)}\n<!-- {key} -->\n\n");
                done++;
            }
            if (currentPack != null) html.Append("</div></section>\n");
            html.Append(Tail(setKeys.Count));
        }
        finally
        {
            mannequin.Dispose();
            EditorUtility.ClearProgressBar();
        }

        File.WriteAllText(Path.Combine(OutDir, "index.html"), html.ToString(), Encoding.UTF8);
        File.WriteAllText(Path.Combine(OutDir, "notes.md"), notes.ToString(), Encoding.UTF8);
        long bytes = new FileInfo(Path.Combine(OutDir, "index.html")).Length;
        Debug.Log($"[SetCatalog] {done} sets → {OutDir}/index.html ({bytes / 1024 / 1024} MB), notes.md, img/ ({iconCache.Count} icons).");
        EditorUtility.RevealInFinder(Path.Combine(OutDir, "index.html"));
    }

    // ---- one set

    private static string Card(string key, Assets.HeroEditor.InventorySystem.Scripts.ItemCollection collection,
                               ResonanceDatabase resonance, SetDrafts drafts, MannequinPreview mannequin,
                               Dictionary<string, string> iconCache)
    {
        string name = Catalog.SetName(key);
        var pieces = new List<ItemParams>();
        foreach (var part in Catalog.ArmorParts) { var p = Catalog.Find(Catalog.PartId(key, part)); if (p != null) pieces.Add(p); }
        var extras = Catalog.MatchingPieces(key);
        var companions = Catalog.Companions(key);

        // The whole theme on the body: one weapon and one shield at most.
        var wear = pieces.Select(p => p.Id).Concat(extras.Select(p => p.Id)).ToList();
        var weapon = companions.FirstOrDefault(c => c.Type == ItemType.Weapon);
        var shield = companions.FirstOrDefault(c => c.Type == ItemType.Shield);
        if (weapon != null) wear.Add(weapon.Id);
        if (shield != null) wear.Add(shield.Id);
        mannequin.Dress(wear);
        string bodyPng = mannequin.RenderPng(BodyWidth, BodyHeight);
        File.WriteAllBytes(Path.Combine(OutDir, "img", Safe(key) + ".png"), Convert.FromBase64String(bodyPng));

        var sb = new StringBuilder();
        var search = new StringBuilder(name + " " + key + " ");
        sb.Append($"<article class=\"set\" id=\"{Esc(key)}\">");
        sb.Append($"<img class=\"body\" src=\"data:image/png;base64,{bodyPng}\" alt=\"{Esc(name)}\">");
        sb.Append("<div class=\"info\">");
        sb.Append($"<h3>{Esc(name)}</h3><div class=\"key\">{Esc(key)}</div>");

        sb.Append("<div class=\"pieces\">");
        foreach (var p in pieces.Concat(extras)) sb.Append(Piece(p, resonance, iconCache, search, false));
        foreach (var c in companions) sb.Append(Piece(c, resonance, iconCache, search, true));
        sb.Append("</div>");

        if (drafts != null)
        {
            foreach (var d in drafts.drafts.Where(d => d.setKey == key))
            {
                sb.Append($"<div class=\"draft\"><b>Draft · {d.status}</b> {Esc(d.title)}");
                if (!string.IsNullOrEmpty(d.notes)) sb.Append($"<div class=\"notes\">{Esc(d.notes)}</div>");
                foreach (var piece in d.pieces)
                    if (piece.engraving != null || !string.IsNullOrEmpty(piece.idea))
                        sb.Append($"<div class=\"dp\">{Esc(Catalog.DisplayName(piece.itemId))}: {(piece.engraving != null ? Esc(piece.engraving.DisplayName) : "")} {Esc(piece.idea ?? "")}</div>");
                sb.Append("</div>");
                search.Append(d.title).Append(' ').Append(d.notes).Append(' ');
            }
        }
        sb.Append("</div>");
        sb.Append($"<div class=\"search\" hidden>{Esc(search.ToString().ToLowerInvariant())}</div>");
        sb.Append("</article>\n");
        return sb.ToString();
    }

    private static string Piece(ItemParams item, ResonanceDatabase resonance, Dictionary<string, string> iconCache, StringBuilder search, bool goesWith)
    {
        string label = Catalog.TypeLabel(item.Type);
        string display = Catalog.DisplayName(item.Id);
        string stats = item.Properties != null ? string.Join(", ", item.Properties.Select(p => p.Id + " " + p.Value)) : "";
        var entry = resonance != null ? resonance.entries.FirstOrDefault(e => e.itemId == item.Id) : null;
        string designed = entry != null ? $"<span class=\"designed\">● {(entry.engraving != null ? Esc(entry.engraving.DisplayName) : "designed")}</span>" : "";
        search.Append(display).Append(' ').Append(label).Append(' ');
        if (entry != null && entry.engraving != null) search.Append(entry.engraving.DisplayName).Append(' ');

        string icon = IconData(item, iconCache);
        return $"<div class=\"piece{(goesWith ? " goes" : "")}\">" +
               (icon != null ? $"<img src=\"data:image/png;base64,{icon}\" alt=\"\">" : "<span class=\"noicon\"></span>") +
               $"<div><div class=\"pname\">{Esc(display)} {designed}</div><div class=\"ptype\">{(goesWith ? "goes with · " : "")}{Esc(label)} · {Esc(stats)}</div></div></div>";
    }

    // ---- icons: read a sprite's pixels through a render texture, since atlases are not readable

    private static string IconData(ItemParams item, Dictionary<string, string> cache)
    {
        if (string.IsNullOrEmpty(item.IconId)) return null;
        if (cache.TryGetValue(item.IconId, out var cached)) return cached;
        var sprite = Catalog.Icon(item.Id);
        string data = sprite != null ? SpritePng(sprite, 56) : null;
        cache[item.IconId] = data;
        return data;
    }

    public static string SpritePng(Sprite sprite, int size)
    {
        var texture = sprite.texture;
        Rect tr;
        try { tr = sprite.textureRect; } catch (Exception) { return null; }

        var full = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        Graphics.Blit(texture, full);
        RenderTexture.active = full;
        var crop = new Texture2D((int)tr.width, (int)tr.height, TextureFormat.RGBA32, false);
        crop.ReadPixels(new Rect(tr.x, tr.y, tr.width, tr.height), 0, 0);
        crop.Apply();
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(full);

        // Fit into a square of `size`, keeping proportions.
        float scale = Mathf.Min((float)size / crop.width, (float)size / crop.height);
        int w = Mathf.Max(1, Mathf.RoundToInt(crop.width * scale)), h = Mathf.Max(1, Mathf.RoundToInt(crop.height * scale));
        var small = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(crop, small);
        RenderTexture.active = small;
        var result = new Texture2D(w, h, TextureFormat.RGBA32, false);
        result.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        result.Apply();
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(small);

        string png = Convert.ToBase64String(result.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(crop);
        UnityEngine.Object.DestroyImmediate(result);
        return png;
    }

    // ---- the page

    private static string Head() =>
        "<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">" +
        "<title>Set catalogue</title><style>" +
        "body{margin:0;font:14px system-ui,sans-serif;background:#1b1d22;color:#e6e6e6}" +
        "header{position:sticky;top:0;background:#23262d;padding:10px 14px;display:flex;gap:10px;align-items:center;z-index:2;border-bottom:1px solid #333}" +
        "header input,header select{font:inherit;padding:8px;border-radius:6px;border:1px solid #444;background:#15171b;color:#eee}" +
        "header input{flex:1}h2{margin:18px 14px 8px;font-size:16px;color:#aab}" +
        ".grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(360px,1fr));gap:10px;padding:0 10px}" +
        ".set{display:flex;gap:10px;background:#262930;border:1px solid #363a44;border-radius:10px;padding:10px}" +
        ".set img.body{width:130px;height:190px;object-fit:contain;background:#111;border-radius:8px;flex:none}" +
        ".info{min-width:0;flex:1}h3{margin:0 0 2px;font-size:15px}.key{font-size:11px;color:#889;word-break:break-all;margin-bottom:6px}" +
        ".piece{display:flex;gap:6px;align-items:center;margin:3px 0}.piece img{width:28px;height:28px;object-fit:contain;flex:none}.noicon{width:28px;height:28px;flex:none;background:#333;border-radius:4px}" +
        ".pname{font-size:13px}.ptype{font-size:11px;color:#99a}.goes .pname{color:#cbd}.designed{color:#f2c94c;font-size:11px;margin-left:4px}" +
        ".draft{margin-top:6px;padding:6px;background:#1f2a1f;border-radius:6px;font-size:12px}.notes{white-space:pre-wrap;color:#cdc}.dp{color:#bcb}" +
        ".hidden{display:none}#count{color:#889;font-size:12px}" +
        "</style></head><body><header><input id=\"q\" placeholder=\"Search sets, pieces, engravings…\"><select id=\"pack\"><option value=\"\">All packs</option></select><span id=\"count\"></span></header>\n";

    private static string Tail(int sets) =>
        "<script>const q=document.getElementById('q'),pack=document.getElementById('pack'),count=document.getElementById('count');" +
        "const packs=[...document.querySelectorAll('section.pack')];packs.forEach(p=>{const o=document.createElement('option');o.value=p.dataset.pack;o.textContent=p.dataset.pack;pack.appendChild(o)});" +
        "function apply(){const t=q.value.trim().toLowerCase(),pk=pack.value;let n=0;packs.forEach(p=>{let any=false;p.querySelectorAll('article.set').forEach(a=>{const ok=(!pk||p.dataset.pack===pk)&&(!t||a.querySelector('.search').textContent.includes(t));a.classList.toggle('hidden',!ok);if(ok){any=true;n++}});p.classList.toggle('hidden',!any)});count.textContent=n+' of " + sets + " sets'}" +
        "q.addEventListener('input',apply);pack.addEventListener('change',apply);apply();</script></body></html>";

    private static string Esc(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    private static string Safe(string s) { foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_'); return s.Replace(' ', '_'); }
}
