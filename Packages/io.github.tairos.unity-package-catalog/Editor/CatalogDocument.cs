using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace UnityPackageCatalog
{
    [Serializable]
    public sealed class CatalogDocument
    {
        public int schemaVersion = 1;
        public string displayName;
        public CatalogEntry[] packages;

        public static CatalogDocument Load(string path)
        {
            return Parse(path, File.ReadAllText(path));
        }

        public static int AddAssetStoreEntries(string path, CatalogEntry[] additions)
        {
            var document = Load(path);
            var entries = new List<CatalogEntry>(document.packages);
            var ids = new HashSet<long>();
            foreach (var entry in entries) if (entry.assetStoreProductId > 0) ids.Add(entry.assetStoreProductId);
            var added = 0;
            foreach (var entry in additions)
            {
                if (entry == null || entry.assetStoreProductId <= 0)
                    throw new FormatException("Only Asset Store entries can be added by this action.");
                if (ids.Add(entry.assetStoreProductId)) { entries.Add(entry); added++; }
            }
            if (added == 0) return 0;
            document.packages = entries.ToArray();
            var json = JsonUtility.ToJson(document, true);
            Parse(path, json); // Validate the complete result before modifying the private file.
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, json);
                File.Replace(temporary, path, null);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return added;
        }

        internal static CatalogDocument Parse(string path, string json)
        {
            CatalogDocument document;
            try { document = JsonUtility.FromJson<CatalogDocument>(json); }
            catch (ArgumentException) { throw new FormatException("Catalogue is not valid JSON."); }
            if (document == null || document.schemaVersion != 1)
                throw new FormatException("Expected catalogue schemaVersion 1.");
            if (string.IsNullOrWhiteSpace(document.displayName) || document.packages == null)
                throw new FormatException("A catalogue needs displayName and packages.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            var productIds = new HashSet<long>();
            for (var index = 0; index < document.packages.Length; index++)
            {
                var entry = document.packages[index];
                try
                {
                    if (entry != null && entry.assetStoreProductId != 0)
                    {
                        if (entry.assetStoreProductId < 0 || !productIds.Add(entry.assetStoreProductId))
                            throw new FormatException("Asset Store product IDs must be positive and unique.");
                        if (!string.IsNullOrEmpty(entry.source) || !string.IsNullOrEmpty(entry.name) || !string.IsNullOrEmpty(entry.version))
                            throw new FormatException("Asset Store entries use assetStoreProductId; omit UPM name, source and version. Unity supplies the metadata.");
                        continue;
                    }
                    if (entry == null || string.IsNullOrEmpty(entry.name) ||
                        !Regex.IsMatch(entry.name, @"^[a-z0-9]+(?:[.-][a-z0-9]+)+$") || !names.Add(entry.name))
                        throw new FormatException("Package names must be valid, unique UPM IDs.");
                    if (string.IsNullOrWhiteSpace(entry.displayName) ||
                        !Regex.IsMatch(entry.version ?? "", @"^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$"))
                        throw new FormatException("Each package needs displayName and a semantic version.");
                    entry.ResolveSource(Path.GetDirectoryName(Path.GetFullPath(path)));
                }
                catch (Exception error) when (error is FormatException || error is ArgumentException || error is IOException || error is UnauthorizedAccessException)
                {
                    throw new FormatException($"Catalogue entry {index + 1} ({(entry != null && entry.assetStoreProductId != 0 ? "Asset Store " + entry.assetStoreProductId : entry?.name ?? "missing ID")}): {error.Message}", error);
                }
            }
            return document;
        }
    }

    [Serializable]
    public sealed class CatalogEntry
    {
        public long assetStoreProductId;
        public string name;
        public string displayName;
        public string description;
        public string version;
        public string source;
        [NonSerialized] public string resolvedSource;

        internal void ResolveSource(string directory)
        {
            if (string.IsNullOrWhiteSpace(source) || source.IndexOfAny(new[] {'\r', '\n'}) >= 0)
                throw new FormatException("Each package needs a Git URL or file: source.");
            if (source.StartsWith("file:", StringComparison.Ordinal))
            {
                var path = Path.GetFullPath(Path.Combine(directory, source.Substring(5)));
                if (!File.Exists(Path.Combine(path, "package.json")))
                    throw new FormatException("A local package source must contain package.json.");
                CatalogEntry manifest;
                try { manifest = JsonUtility.FromJson<CatalogEntry>(File.ReadAllText(Path.Combine(path, "package.json"))); }
                catch (ArgumentException) { throw new FormatException("Local package.json is not valid JSON."); }
                if (manifest == null || string.IsNullOrWhiteSpace(manifest.name) || string.IsNullOrWhiteSpace(manifest.version))
                    throw new FormatException("Local package.json needs name and version.");
                if (manifest.name != name || manifest.version != version)
                    throw new FormatException($"Local package.json declares {manifest.name}@{manifest.version}; expected {name}@{version}.");
                resolvedSource = "file:" + path.Replace('\\', '/');
                return;
            }
            if (!Uri.TryCreate(source.StartsWith("git+") ? source.Substring(4) : source, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "https" && uri.Scheme != "ssh"))
                throw new FormatException("Use an https:// or ssh:// Git URL, optionally prefixed with git+.");
            if ((uri.Scheme == "https" && !string.IsNullOrEmpty(uri.UserInfo)) ||
                (uri.Scheme == "ssh" && uri.UserInfo.Contains(":")))
                throw new FormatException("Do not put credentials in catalogue URLs. Use Git authentication.");
            resolvedSource = source;
        }
    }
}
