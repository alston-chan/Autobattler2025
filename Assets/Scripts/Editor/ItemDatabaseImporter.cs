using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Assets.HeroEditor.InventorySystem.Scripts;
using Assets.HeroEditor.InventorySystem.Scripts.Data;
using Assets.HeroEditor.InventorySystem.Scripts.Enums;
using HeroEditor.Common;
using HeroEditor.Common.Data;
using UnityEditor;
using UnityEngine;

namespace Assets.Scripts.Editor
{
    public static class ItemDatabaseImporter
    {
        private const string ItemsCsvPath = "Assets/Data/Items.csv";
        private const string PropertiesCsvPath = "Assets/Data/Properties.csv";
        private const string ItemCollectionPath = "Assets/Data/ItemCollection.asset";

        // ─────────────────────────────────────────────────────────────
        //  EXPORT: Generate Items.csv from SpriteCollection
        // ─────────────────────────────────────────────────────────────

        [MenuItem("Tools/Item Database/Export CSV from SpriteCollection")]
        public static void ExportFromSpriteCollection()
        {
            var collection = LoadSpriteCollection();
            if (collection == null) return;

            // Preserve existing rows so user edits (Enabled, Price, etc.) are not lost
            var existing = LoadExistingRows();

            var sb = new StringBuilder();
            sb.AppendLine("Enabled,Id,Type,Class,Rarity,Level,Price,Weight,SpriteId,IconId,Tags,Meta,Name_EN");

            int newCount = 0;
            int preservedCount = 0;

            // Armor → 3 rows per sprite (VestBeltPauldron, Gloves, Boots)
            foreach (var sprite in collection.Armor)
            {
                WriteArmorSubPart(sb, sprite, "vest", "VestBeltPauldron", existing, ref newCount, ref preservedCount);
                WriteArmorSubPart(sb, sprite, "gloves", "Gloves", existing, ref newCount, ref preservedCount);
                WriteArmorSubPart(sb, sprite, "boots", "Boots", existing, ref newCount, ref preservedCount);
            }

            // Helmet
            foreach (var sprite in collection.Helmet)
                WriteSpriteRow(sb, sprite, "Helmet", "Light", existing, ref newCount, ref preservedCount);

            // Shield
            foreach (var sprite in collection.Shield)
                WriteSpriteRow(sb, sprite, "Shield", "Light", existing, ref newCount, ref preservedCount);

            // MeleeWeapon1H
            foreach (var sprite in collection.MeleeWeapon1H)
                WriteSpriteRow(sb, sprite, "Weapon", GuessWeaponClass(sprite.Id), existing, ref newCount, ref preservedCount);

            // MeleeWeapon2H
            foreach (var sprite in collection.MeleeWeapon2H)
                WriteSpriteRow(sb, sprite, "Weapon", GuessWeaponClass(sprite.Id), existing, ref newCount, ref preservedCount, tags: "TwoHanded");

            // Bow
            foreach (var sprite in collection.Bow)
                WriteSpriteRow(sb, sprite, "Weapon", "Bow", existing, ref newCount, ref preservedCount, tags: "TwoHanded");

            // Firearm1H
            foreach (var sprite in collection.Firearm1H)
                WriteSpriteRow(sb, sprite, "Weapon", "Firearm", existing, ref newCount, ref preservedCount);

            // Firearm2H
            foreach (var sprite in collection.Firearm2H)
                WriteSpriteRow(sb, sprite, "Weapon", "Firearm", existing, ref newCount, ref preservedCount, tags: "TwoHanded");

            // Cape
            foreach (var sprite in collection.Cape)
                WriteSpriteRow(sb, sprite, "Armor", "Light", existing, ref newCount, ref preservedCount);

            // Back
            foreach (var sprite in collection.Back)
                WriteSpriteRow(sb, sprite, "Armor", "Light", existing, ref newCount, ref preservedCount);

            Directory.CreateDirectory(Path.GetDirectoryName(ItemsCsvPath));
            File.WriteAllText(ItemsCsvPath, sb.ToString());
            AssetDatabase.Refresh();

            Debug.Log($"[ItemDatabase] Exported Items.csv — {newCount} new rows, {preservedCount} preserved from existing CSV. Total: {newCount + preservedCount}");
        }

        private static Dictionary<string, string> LoadExistingRows()
        {
            var existing = new Dictionary<string, string>();

            if (!File.Exists(ItemsCsvPath)) return existing;

            var lines = File.ReadAllLines(ItemsCsvPath);
            if (lines.Length < 2) return existing;

            var headers = ParseCsvLine(lines[0]);
            var idIdx = Array.IndexOf(headers, "Id");

            if (idIdx < 0) return existing;

            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                var vals = ParseCsvLine(line);

                if (vals.Length > idIdx && !string.IsNullOrEmpty(vals[idIdx]))
                {
                    existing[vals[idIdx]] = line;
                }
            }

            return existing;
        }

        private static void WriteArmorSubPart(StringBuilder sb, ItemSprite sprite, string subPart, string itemType,
            Dictionary<string, string> existing, ref int newCount, ref int preservedCount)
        {
            var id = $"{sprite.Id}.{subPart}";

            if (existing.ContainsKey(id))
            {
                sb.AppendLine(existing[id]);
                preservedCount++;
            }
            else
            {
                var subPartCapitalized = char.ToUpper(subPart[0]) + subPart.Substring(1);
                var iconId = sprite.Id.Replace(".Armor.", $".{subPartCapitalized}.");
                var name = $"{sprite.Name} ({subPartCapitalized})";
                sb.AppendLine($"FALSE,{CsvEscape(id)},{itemType},Light,Common,1,0,0,{CsvEscape(sprite.Id)},{CsvEscape(iconId)},,," + CsvEscape(name));
                newCount++;
            }
        }

        private static void WriteSpriteRow(StringBuilder sb, ItemSprite sprite, string itemType, string itemClass,
            Dictionary<string, string> existing, ref int newCount, ref int preservedCount, string tags = "")
        {
            var id = sprite.Id;

            if (existing.ContainsKey(id))
            {
                sb.AppendLine(existing[id]);
                preservedCount++;
            }
            else
            {
                sb.AppendLine($"FALSE,{CsvEscape(id)},{itemType},{itemClass},Common,1,0,0,{CsvEscape(id)},{CsvEscape(id)},{tags},,{CsvEscape(sprite.Name)}");
                newCount++;
            }
        }

        private static string GuessWeaponClass(string spriteId)
        {
            var lower = spriteId.ToLower();
            if (lower.Contains("sword")) return "Sword";
            if (lower.Contains("axe")) return "Axe";
            if (lower.Contains("dagger")) return "Dagger";
            if (lower.Contains("blunt") || lower.Contains("mace") || lower.Contains("hammer")) return "Blunt";
            if (lower.Contains("lance") || lower.Contains("spear")) return "Lance";
            if (lower.Contains("wand") || lower.Contains("staff")) return "Wand";
            if (lower.Contains("claw")) return "Claw";
            if (lower.Contains("pickaxe")) return "Pickaxe";
            return "Sword"; // default fallback
        }

        private static string CsvEscape(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }

        private static SpriteCollection LoadSpriteCollection()
        {
            // Try known path first
            var collection = AssetDatabase.LoadAssetAtPath<SpriteCollection>(
                "Assets/HeroEditor/Megapack/Resources/SpriteCollection.asset");

            if (collection != null) return collection;

            // Search for any SpriteCollection
            var guids = AssetDatabase.FindAssets("t:SpriteCollection");
            if (guids.Length == 0)
            {
                Debug.LogError("[ItemDatabase] No SpriteCollection found in the project.");
                return null;
            }

            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            collection = AssetDatabase.LoadAssetAtPath<SpriteCollection>(path);

            if (collection == null)
                Debug.LogError($"[ItemDatabase] Failed to load SpriteCollection at: {path}");

            return collection;
        }

        // ─────────────────────────────────────────────────────────────
        //  IMPORT: Load enabled Items.csv rows into ItemCollection
        // ─────────────────────────────────────────────────────────────

        [MenuItem("Tools/Item Database/Import CSV into ItemCollection")]
        public static void Import()
        {
            if (!File.Exists(ItemsCsvPath))
            {
                Debug.LogError($"[ItemDatabase] Items CSV not found at: {ItemsCsvPath}. Run 'Tools > Item Database > Export CSV from SpriteCollection' first.");
                return;
            }

            var collection = AssetDatabase.LoadAssetAtPath<ItemCollection>(ItemCollectionPath);

            if (collection == null)
            {
                // Create if it doesn't exist
                collection = ScriptableObject.CreateInstance<ItemCollection>();
                Directory.CreateDirectory(Path.GetDirectoryName(ItemCollectionPath));
                AssetDatabase.CreateAsset(collection, ItemCollectionPath);
                Debug.Log($"[ItemDatabase] Created new ItemCollection at: {ItemCollectionPath}");
            }

            Debug.Log($"[ItemDatabase] Importing items into: {ItemCollectionPath}");

            var propertiesMap = ParseProperties();
            var items = ParseItems(propertiesMap);

            collection.Items = items;
            EditorUtility.SetDirty(collection);
            AssetDatabase.SaveAssets();

            Debug.Log($"[ItemDatabase] Successfully imported {items.Count} enabled items into ItemCollection.");
        }

        private static List<ItemParams> ParseItems(Dictionary<string, List<Property>> propertiesMap)
        {
            var items = new List<ItemParams>();
            var lines = File.ReadAllLines(ItemsCsvPath);

            if (lines.Length < 2)
            {
                Debug.LogWarning("[ItemDatabase] Items CSV is empty (no data rows).");
                return items;
            }

            var headers = ParseCsvLine(lines[0]);
            var headerIndex = new Dictionary<string, int>();

            for (int i = 0; i < headers.Length; i++)
                headerIndex[headers[i].Trim()] = i;

            var requiredColumns = new[] { "Id", "Type" };
            foreach (var col in requiredColumns)
            {
                if (!headerIndex.ContainsKey(col))
                {
                    Debug.LogError($"[ItemDatabase] Items CSV is missing required column: {col}");
                    return items;
                }
            }

            int skippedDisabled = 0;

            for (int row = 1; row < lines.Length; row++)
            {
                var line = lines[row].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                var values = ParseCsvLine(line);

                // Check Enabled flag — skip items marked FALSE
                var enabledStr = GetValue(values, headerIndex, "Enabled");
                if (!string.IsNullOrEmpty(enabledStr) &&
                    (enabledStr.Equals("FALSE", StringComparison.OrdinalIgnoreCase) ||
                     enabledStr == "0" ||
                     enabledStr.Equals("no", StringComparison.OrdinalIgnoreCase)))
                {
                    skippedDisabled++;
                    continue;
                }

                var itemParams = new ItemParams();
                itemParams.Id = GetValue(values, headerIndex, "Id");

                if (string.IsNullOrEmpty(itemParams.Id))
                {
                    Debug.LogWarning($"[ItemDatabase] Skipping row {row + 1}: missing Id.");
                    continue;
                }

                // Type (required)
                var typeStr = GetValue(values, headerIndex, "Type");
                if (!TryParseEnum<ItemType>(typeStr, out var type))
                {
                    Debug.LogWarning($"[ItemDatabase] Row {row + 1}: Unknown ItemType '{typeStr}', skipping.");
                    continue;
                }
                itemParams.Type = type;

                // Class
                var classStr = GetValue(values, headerIndex, "Class");
                if (!string.IsNullOrEmpty(classStr) && TryParseEnum<ItemClass>(classStr, out var itemClass))
                    itemParams.Class = itemClass;

                // Rarity
                var rarityStr = GetValue(values, headerIndex, "Rarity");
                if (!string.IsNullOrEmpty(rarityStr) && TryParseEnum<ItemRarity>(rarityStr, out var rarity))
                    itemParams.Rarity = rarity;

                // Numeric fields
                var levelStr = GetValue(values, headerIndex, "Level");
                if (!string.IsNullOrEmpty(levelStr) && int.TryParse(levelStr, out var level))
                    itemParams.Level = level;

                var priceStr = GetValue(values, headerIndex, "Price");
                if (!string.IsNullOrEmpty(priceStr) && int.TryParse(priceStr, out var price))
                    itemParams.Price = price;

                var weightStr = GetValue(values, headerIndex, "Weight");
                if (!string.IsNullOrEmpty(weightStr) && int.TryParse(weightStr, out var weight))
                    itemParams.Weight = weight;

                // Sprite/Icon references
                itemParams.SpriteId = GetValue(values, headerIndex, "SpriteId");
                itemParams.IconId = GetValue(values, headerIndex, "IconId");

                // Tags (semicolon-separated enum values)
                var tagsStr = GetValue(values, headerIndex, "Tags");
                if (!string.IsNullOrEmpty(tagsStr))
                {
                    itemParams.Tags = tagsStr.Split(';')
                        .Select(t => t.Trim())
                        .Where(t => !string.IsNullOrEmpty(t))
                        .Select(t =>
                        {
                            if (TryParseEnum<ItemTag>(t, out var tag)) return tag;
                            Debug.LogWarning($"[ItemDatabase] Row {row + 1}: Unknown ItemTag '{t}'");
                            return ItemTag.Undefined;
                        })
                        .Where(t => t != ItemTag.Undefined)
                        .ToList();
                }

                // Meta
                var metaStr = GetValue(values, headerIndex, "Meta");
                if (!string.IsNullOrEmpty(metaStr))
                    itemParams.Meta = metaStr;

                // Localization
                var nameEn = GetValue(values, headerIndex, "Name_EN");
                if (!string.IsNullOrEmpty(nameEn))
                {
                    itemParams.Localization = new List<LocalizedValue>
                    {
                        new LocalizedValue("English", nameEn)
                    };
                }

                // Properties from Properties.csv
                if (propertiesMap.ContainsKey(itemParams.Id))
                    itemParams.Properties = propertiesMap[itemParams.Id];

                items.Add(itemParams);
            }

            if (skippedDisabled > 0)
                Debug.Log($"[ItemDatabase] Skipped {skippedDisabled} disabled items (Enabled=FALSE).");

            return items;
        }

        private static Dictionary<string, List<Property>> ParseProperties()
        {
            var map = new Dictionary<string, List<Property>>();

            if (!File.Exists(PropertiesCsvPath))
            {
                Debug.Log($"[ItemDatabase] Properties CSV not found at: {PropertiesCsvPath} (optional, skipping).");
                return map;
            }

            var lines = File.ReadAllLines(PropertiesCsvPath);
            if (lines.Length < 2) return map;

            var headers = ParseCsvLine(lines[0]);
            var headerIndex = new Dictionary<string, int>();

            for (int i = 0; i < headers.Length; i++)
                headerIndex[headers[i].Trim()] = i;

            for (int row = 1; row < lines.Length; row++)
            {
                var line = lines[row].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                var values = ParseCsvLine(line);
                var itemId = GetValue(values, headerIndex, "ItemId");
                var propIdStr = GetValue(values, headerIndex, "PropertyId");
                var propValue = GetValue(values, headerIndex, "Value");

                if (string.IsNullOrEmpty(itemId) || string.IsNullOrEmpty(propIdStr)) continue;

                if (!TryParseEnum<PropertyId>(propIdStr, out var propId))
                {
                    Debug.LogWarning($"[ItemDatabase] Properties row {row + 1}: Unknown PropertyId '{propIdStr}', skipping.");
                    continue;
                }

                if (!map.ContainsKey(itemId))
                    map[itemId] = new List<Property>();

                map[itemId].Add(new Property { Id = propId, Value = propValue });
            }

            return map;
        }

        // ─────────────────────────────────────────────────────────────
        //  CSV Helpers
        // ─────────────────────────────────────────────────────────────

        private static string[] ParseCsvLine(string line)
        {
            var fields = new List<string>();
            bool inQuotes = false;
            var current = "";

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                    inQuotes = !inQuotes;
                else if (c == ',' && !inQuotes)
                {
                    fields.Add(current.Trim());
                    current = "";
                }
                else
                    current += c;
            }

            fields.Add(current.Trim());
            return fields.ToArray();
        }

        private static string GetValue(string[] values, Dictionary<string, int> headerIndex, string column)
        {
            if (!headerIndex.ContainsKey(column)) return null;

            var idx = headerIndex[column];
            if (idx >= values.Length) return null;

            var value = values[idx].Trim();
            return string.IsNullOrEmpty(value) ? null : value;
        }

        private static bool TryParseEnum<T>(string value, out T result) where T : struct
        {
            if (string.IsNullOrEmpty(value))
            {
                result = default;
                return false;
            }

            return Enum.TryParse(value.Trim(), true, out result);
        }
    }
}
