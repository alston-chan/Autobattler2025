using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Imports equipment-design.json (exported from Tools/EquipmentDesigner.html) and
/// generates one ItemDefinition ScriptableObject per designed item.
/// Menu: Tools > Equipment > Import Design JSON
/// </summary>
public static class EquipmentDesignImporter
{
    private const string OutputFolder = "Assets/Data/ItemDefinitions";

    [System.Serializable]
    private class DesignEntry
    {
        public string id;
        public string slot;
        public string sprite;
        public string icon;
        public string theme;
        public string rarity;
        public string title;
        public string affix;
        public string min, max, unit;
        public string engName;
        public string engraving;
        public string pool;
        public string req;
        public string t1, t2, t3;
        public string notes;
    }

    [System.Serializable]
    private class DesignList { public List<DesignEntry> items = new List<DesignEntry>(); }

    [System.Serializable]
    private class AffixJson
    {
        public string name, stat, unit, min, max, slots, themes, weight;
    }

    [System.Serializable]
    private class EngJson
    {
        public string name, text, verb, pools, slots, weight;
    }

    [System.Serializable]
    private class Bundle
    {
        public List<DesignEntry> items = new List<DesignEntry>();
        public List<AffixJson> affixPool = new List<AffixJson>();
        public List<EngJson> engravingPool = new List<EngJson>();
    }

    private static float F(string s) { float.TryParse(s, out float v); return v; }

    [MenuItem("Tools/Equipment/Import Design JSON")]
    public static void Import()
    {
        string path = EditorUtility.OpenFilePanel("Select equipment-design.json", Application.dataPath, "json");
        if (string.IsNullOrEmpty(path)) return;

        string raw = File.ReadAllText(path).TrimStart();

        // Current format is an object { items, affixPool, engravingPool }.
        // Older exports were a bare array — JsonUtility can't parse those, so wrap them.
        Bundle bundle = raw.StartsWith("[")
            ? new Bundle { items = JsonUtility.FromJson<DesignList>("{\"items\":" + raw + "}").items }
            : JsonUtility.FromJson<Bundle>(raw);

        var list = new DesignList { items = bundle?.items ?? new List<DesignEntry>() };

        if (list.items.Count == 0 && (bundle?.affixPool?.Count ?? 0) == 0 && (bundle?.engravingPool?.Count ?? 0) == 0)
        {
            Debug.LogWarning("[EquipmentDesignImporter] No entries found in " + path);
            return;
        }

        if (!AssetDatabase.IsValidFolder(OutputFolder))
        {
            Directory.CreateDirectory(OutputFolder);
            AssetDatabase.Refresh();
        }

        int created = 0, updated = 0;

        foreach (var e in list.items)
        {
            if (string.IsNullOrEmpty(e.id)) continue;

            string assetPath = $"{OutputFolder}/{Sanitize(e.id)}.asset";
            var def = AssetDatabase.LoadAssetAtPath<ItemDefinition>(assetPath);
            bool isNew = def == null;

            if (isNew)
            {
                def = ScriptableObject.CreateInstance<ItemDefinition>();
                AssetDatabase.CreateAsset(def, assetPath);
                created++;
            }
            else
            {
                updated++;
            }

            def.id = e.id;
            def.displayName = string.IsNullOrEmpty(e.title) ? e.sprite : e.title;
            def.slot = e.slot;
            def.spriteId = e.sprite;
            def.theme = e.theme;
            def.rarity = e.rarity;
            def.affix = e.affix;
            float.TryParse(e.min, out float mn); def.affixMin = mn;
            float.TryParse(e.max, out float mx); def.affixMax = mx;
            def.affixUnit = string.IsNullOrEmpty(e.unit) ? "%" : e.unit;
            def.engravingName = e.engName;
            def.engravingText = e.engraving;
            def.rolledEngravingPool = e.pool;
            def.resonanceRequirement = e.req;
            def.tierI = e.t1;
            def.tierII = e.t2;
            def.tierIII = e.t3;
            def.designerNotes = e.notes;

            EditorUtility.SetDirty(def);
        }

        // ── Pools ────────────────────────────────────────────────────────────
        int affixN = 0, engN = 0;
        if ((bundle?.affixPool?.Count ?? 0) > 0 || (bundle?.engravingPool?.Count ?? 0) > 0)
        {
            const string poolPath = OutputFolder + "/EffectPools.asset";
            var pools = AssetDatabase.LoadAssetAtPath<EffectPoolDatabase>(poolPath);
            if (pools == null)
            {
                pools = ScriptableObject.CreateInstance<EffectPoolDatabase>();
                AssetDatabase.CreateAsset(pools, poolPath);
            }

            pools.affixPool.Clear();
            foreach (var a in bundle.affixPool)
                pools.affixPool.Add(new EffectPoolDatabase.AffixEntry
                {
                    name = a.name, stat = a.stat, unit = string.IsNullOrEmpty(a.unit) ? "%" : a.unit,
                    min = F(a.min), max = F(a.max),
                    slots = string.IsNullOrEmpty(a.slots) ? "any" : a.slots,
                    themes = string.IsNullOrEmpty(a.themes) ? "any" : a.themes,
                    weight = Mathf.Max(0.01f, F(a.weight))
                });

            pools.engravingPool.Clear();
            foreach (var e in bundle.engravingPool)
                pools.engravingPool.Add(new EffectPoolDatabase.EngravingEntry
                {
                    name = e.name, text = e.text,
                    verb = string.IsNullOrEmpty(e.verb) ? "HOOK" : e.verb,
                    pools = e.pools,
                    slots = string.IsNullOrEmpty(e.slots) ? "any" : e.slots,
                    weight = Mathf.Max(0.01f, F(e.weight))
                });

            affixN = pools.affixPool.Count;
            engN = pools.engravingPool.Count;
            EditorUtility.SetDirty(pools);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[EquipmentDesignImporter] Items: {list.items.Count} ({created} created, {updated} updated). " +
                  $"Pools: {affixN} affixes, {engN} engravings. → {OutputFolder}");
    }

    private static string Sanitize(string id)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) id = id.Replace(c, '_');
        return id;
    }
}
