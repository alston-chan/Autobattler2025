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
    private const int BodyWidth = 180, BodyHeight = 260;   // small enough that the whole page stays under a hosted-page limit

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
        var mannequin = new MannequinPreview();
        int done = 0;
        try
        {
            html.Append(Head());

            string currentPack = null;
            foreach (var key in setKeys)
            {
                string pack = Catalog.Family(key);
                if (pack != currentPack)
                {
                    if (currentPack != null) html.Append("</div></section>\n");
                    currentPack = pack;
                    html.Append($"<section class=\"pack\" data-pack=\"{Esc(pack)}\"><h2>{Esc(pack)}</h2><div class=\"grid\">\n");
                }

                EditorUtility.DisplayProgressBar("Set catalogue", Catalog.SetName(key), (float)done / setKeys.Count);
                html.Append(Card(key, collection, resonance, drafts, mannequin));
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
        SetNotes.Write(setKeys);   // the notes file: a heading and a theme line per set, your text kept
        long bytes = new FileInfo(Path.Combine(OutDir, "index.html")).Length;
        Debug.Log($"[SetCatalog] {done} sets → {OutDir}/index.html ({bytes / 1024 / 1024} MB), notes.md, img/.");
        EditorUtility.RevealInFinder(Path.Combine(OutDir, "index.html"));
    }

    // ---- one set

    private static string Card(string key, Assets.HeroEditor.InventorySystem.Scripts.ItemCollection collection,
                               ResonanceDatabase resonance, SetDrafts drafts, MannequinPreview mannequin)
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
        foreach (var p in pieces.Concat(extras)) sb.Append(Piece(p, resonance, search, false));
        foreach (var c in companions) sb.Append(Piece(c, resonance, search, true));
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

    private static string Piece(ItemParams item, ResonanceDatabase resonance, StringBuilder search, bool goesWith)
    {
        string label = Catalog.TypeLabel(item.Type);
        string display = Catalog.DisplayName(item.Id);
        var entry = resonance != null ? resonance.entries.FirstOrDefault(e => e.itemId == item.Id) : null;
        string designed = entry != null ? $"<span class=\"designed\">● {(entry.engraving != null ? Esc(entry.engraving.DisplayName) : "designed")}</span>" : "";
        search.Append(display).Append(' ').Append(label).Append(' ');
        if (entry != null && entry.engraving != null) search.Append(entry.engraving.DisplayName).Append(' ');

        return $"<div class=\"piece{(goesWith ? " goes" : "")}\">" +
               $"<div><div class=\"pname\">{Esc(display)} {designed}</div><div class=\"ptype\">{(goesWith ? "goes with · " : "")}{Esc(label)}</div></div></div>";
    }

    // ---- the page

    private static string Head() =>
        "<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">" +
        "<title>Autobattler Wardrobe</title>" +
        "<link rel=\"stylesheet\" href=\"https://fonts.googleapis.com/css2?family=Fraunces:opsz,wght@9..144,500;9..144,600&family=IBM+Plex+Sans:wght@400;500;600&display=swap\">" +
        "<style>" +
        // Tokens: the light palette on :root, dark under the system preference and the explicit stamp.
        ":root{--bg:#EEF0F3;--surface:#FFFFFF;--surface-2:#E4E7EC;--ink:#1F2430;--muted:#667085;--line:#D5D9E0;--accent:#B8860B;--draft:#E8F0E6;--draft-ink:#2F4F2F;--body-bg:#0F1114}" +
        "@media (prefers-color-scheme: dark){:root:not([data-theme=\"light\"]){--bg:#1B1D22;--surface:#262930;--surface-2:#2E323A;--ink:#E8E8EA;--muted:#9AA0AC;--line:#363A44;--accent:#F2C94C;--draft:#1F2A1F;--draft-ink:#CFE3CF;--body-bg:#0B0C0E}}" +
        ":root[data-theme=\"dark\"]{--bg:#1B1D22;--surface:#262930;--surface-2:#2E323A;--ink:#E8E8EA;--muted:#9AA0AC;--line:#363A44;--accent:#F2C94C;--draft:#1F2A1F;--draft-ink:#CFE3CF;--body-bg:#0B0C0E}" +
        "body{margin:0;background:var(--bg);color:var(--ink);font:14px/1.45 'IBM Plex Sans',system-ui,sans-serif}" +
        "header{position:sticky;top:0;z-index:2;display:flex;gap:10px;align-items:center;padding:10px 14px;background:var(--surface);border-bottom:1px solid var(--line)}" +
        "header input,header select{font:inherit;padding:8px 10px;border-radius:6px;border:1px solid var(--line);background:var(--bg);color:var(--ink)}" +
        "header input{flex:1;min-width:0}header input:focus,header select:focus{outline:2px solid var(--accent);outline-offset:1px}" +
        "#count{color:var(--muted);font-size:12px;font-variant-numeric:tabular-nums;white-space:nowrap}" +
        "h2{margin:22px 14px 8px;font:600 15px 'Fraunces',Georgia,serif;letter-spacing:.02em;text-transform:uppercase;color:var(--muted)}" +
        ".grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(340px,1fr));gap:10px;padding:0 10px}" +
        ".set{display:flex;gap:12px;background:var(--surface);border:1px solid var(--line);border-radius:10px;padding:10px}" +
        ".set img.body{width:120px;height:174px;object-fit:contain;background:var(--body-bg);border-radius:8px;flex:none}" +
        ".info{min-width:0;flex:1}h3{margin:0 0 2px;font:600 17px/1.2 'Fraunces',Georgia,serif;text-wrap:balance}" +
        ".key{font-size:11px;color:var(--muted);word-break:break-all;margin-bottom:8px}" +
        ".piece{display:flex;gap:8px;align-items:center;margin:4px 0}.piece img{width:26px;height:26px;object-fit:contain;flex:none}" +
        ".noicon{width:26px;height:26px;flex:none;background:var(--surface-2);border-radius:4px}" +
        ".pname{font-size:13px;font-weight:500}.ptype{font-size:11px;color:var(--muted);letter-spacing:.02em}" +
        ".goes .pname{font-weight:400}.designed{color:var(--accent);font-size:11px;font-weight:600;margin-left:6px}" +
        ".draft{margin-top:8px;padding:8px;background:var(--draft);color:var(--draft-ink);border-radius:6px;font-size:12px}.notes{white-space:pre-wrap}.dp{opacity:.9}" +
        ".hidden{display:none}" +
        "</style></head><body><header><input id=\"q\" placeholder=\"Search sets, pieces, engravings\" aria-label=\"Search\"><select id=\"pack\" aria-label=\"Pack\"><option value=\"\">All packs</option></select><span id=\"count\"></span></header>\n";

    private static string Tail(int sets) =>
        "<script>const q=document.getElementById('q'),pack=document.getElementById('pack'),count=document.getElementById('count');" +
        "const packs=[...document.querySelectorAll('section.pack')];packs.forEach(p=>{const o=document.createElement('option');o.value=p.dataset.pack;o.textContent=p.dataset.pack;pack.appendChild(o)});" +
        "function apply(){const t=q.value.trim().toLowerCase(),pk=pack.value;let n=0;packs.forEach(p=>{let any=false;p.querySelectorAll('article.set').forEach(a=>{const ok=(!pk||p.dataset.pack===pk)&&(!t||a.querySelector('.search').textContent.includes(t));a.classList.toggle('hidden',!ok);if(ok){any=true;n++}});p.classList.toggle('hidden',!any)});count.textContent=n+' of " + sets + " sets'}" +
        "q.addEventListener('input',apply);pack.addEventListener('change',apply);apply();</script></body></html>";

    private static string Esc(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    private static string Safe(string s) { foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_'); return s.Replace(' ', '_'); }
}
