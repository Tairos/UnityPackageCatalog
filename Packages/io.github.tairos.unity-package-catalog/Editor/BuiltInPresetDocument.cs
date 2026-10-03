using System;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityPackageCatalog
{
    [Serializable]
    public sealed class BuiltInPresetDocument
    {
        public int schemaVersion = 1;
        public string displayName;
        public string unityVersion;
        public BuiltInModuleRule[] builtInModules;

        public string VersionNotice => !string.IsNullOrEmpty(unityVersion) && unityVersion != Application.unityVersion
            ? "Created for Unity " + unityVersion + "; checking availability in Unity " + Application.unityVersion + "." : "";

        public static BuiltInPresetDocument Load(string path) => Parse(path, File.ReadAllText(path));
        internal static BuiltInPresetDocument Parse(string path, string json)
        {
            if (!string.Equals(Path.GetExtension(path), ".upcbuiltinjson", StringComparison.OrdinalIgnoreCase))
                throw new FormatException("Built-in presets must use the .upcbuiltinjson extension.");
            BuiltInPresetDocument preset;
            try { preset = JsonUtility.FromJson<BuiltInPresetDocument>(json); }
            catch (ArgumentException) { throw new FormatException("Built-in preset is not valid JSON."); }
            if (preset == null || preset.schemaVersion != 1) throw new FormatException("Expected Built-in preset schemaVersion 1.");
            if (string.IsNullOrWhiteSpace(preset.displayName) || preset.builtInModules == null)
                throw new FormatException("A Built-in preset needs displayName and builtInModules.");
            if (JsonUtility.FromJson<CatalogDocument>(json).packages != null)
                throw new FormatException("Packages belong in a .upcjson catalogue, not a Built-in preset.");
            BuiltInModuleRule.Validate(preset.builtInModules);
            return preset;
        }

        internal static void Save(string path, BuiltInPresetDocument preset)
        {
            var json = JsonUtility.ToJson(preset, true);
            Parse(path, json);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temporary, json); File.Replace(temporary, path, null); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        internal static string[] AssetPaths() => AssetDatabase.FindAssets("t:UpcBuiltInPresetAsset")
            .Select(AssetDatabase.GUIDToAssetPath).OrderBy(path => path, StringComparer.Ordinal).ToArray();
    }
    [Serializable]
    public sealed class BuiltInModuleRule
    {
        public string name;
        public string requirement;

        public static void Validate(BuiltInModuleRule[] rules)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var rule in rules ?? Array.Empty<BuiltInModuleRule>())
            {
                if (rule == null || !Regex.IsMatch(rule.name ?? "", @"^com\.unity\.modules\.[a-z0-9]+$"))
                    throw new FormatException("Built-in module rules need a com.unity.modules.* package ID.");
                if (!names.Add(rule.name)) throw new FormatException("Duplicate or conflicting built-in module rule: " + rule.name);
                if (rule.requirement != "required" && rule.requirement != "excluded")
                    throw new FormatException(rule.name + ": requirement must be required or excluded.");
            }
        }
    }

}
