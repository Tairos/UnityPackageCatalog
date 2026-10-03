using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace UnityPackageCatalog
{
    internal sealed class CatalogSource
    {
        public string id, path;
        public CatalogDocument document;
    }

    internal static class CatalogRegistry
    {
        internal static string[] AssetPaths() => AssetDatabase.FindAssets("t:UpcCatalogAsset")
            .Select(AssetDatabase.GUIDToAssetPath).Where(path => path.EndsWith(".upcjson", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal).ToArray();

        internal static CatalogSource[] LoadSources()
        {
            var sources = AssetPaths().Select(path => new CatalogSource
            {
                id = "UnityPackageCatalog." + AssetDatabase.AssetPathToGUID(path), path = path,
                document = CatalogDocument.Load(path)
            }).ToArray();
            ValidateSources(sources);
            return sources;
        }

        internal static void ValidateSources(CatalogSource[] sources)
        {
            var entries = new Dictionary<string, CatalogEntry>(StringComparer.Ordinal);
            foreach (var source in sources)
                foreach (var entry in source.document.packages.Where(e => e.assetStoreProductId == 0))
                {
                    if (entries.TryGetValue(entry.name, out var other) &&
                        (other.resolvedSource != entry.resolvedSource || other.version != entry.version))
                        throw new FormatException(entry.name + ": catalogues specify different sources or versions. Unity can install one version per project; align these entries.");
                    entries[entry.name] = entry;
                }
        }

    }
}
