using System;
using NUnit.Framework;

namespace UnityPackageCatalog.Tests
{
    public class MultipleCatalogTests
    {
        static CatalogSource Source(string path) => new CatalogSource { path = path, document = new CatalogDocument { packages = Array.Empty<CatalogEntry>() } };
        [Test] public void ConflictingPackageSourcesAreRejected()
        {
            var a = Source("required"); var b = Source("required");
            a.document.packages = new[] { new CatalogEntry { name = "com.example.test", version = "1.0.0", resolvedSource = "file:a" } };
            b.document.packages = new[] { new CatalogEntry { name = "com.example.test", version = "1.0.0", resolvedSource = "file:b" } };
            Assert.Throws<FormatException>(() => CatalogRegistry.ValidateSources(new[] { a, b }));
        }
        [Test] public void RegistrySourceResolvesToPinnedVersion()
        {
            var entry = new CatalogEntry { source = "registry", version = "1.2.3" };
            entry.ResolveSource("."); Assert.That(entry.resolvedSource, Is.EqualTo("1.2.3"));
        }
        [Test] public void PickerSelectionSurvivesFilteringAndDoesNotDuplicateItems()
        {
            var selection = new CatalogPickerSelection();
            var first = new CatalogChoice { key = "84", title = "First" };
            var second = new CatalogChoice { key = "14808", title = "Second" };
            selection.Set(first, true); selection.Set(second, true); selection.Set(first, true);
            Assert.That(selection.Items, Has.Length.EqualTo(2));
            selection.Set(first, false); Assert.That(selection.Items[0], Is.SameAs(second));
        }

        [Test] public void DiscoveryIgnoresPlainJsonAndUsesStableAssetGuid()
        {
            var name = "Assets/UPCDiscovery_" + Guid.NewGuid().ToString("N");
            var json = UnityEngine.JsonUtility.ToJson(new CatalogDocument { displayName = "Discovery", packages = Array.Empty<CatalogEntry>() });
            try
            {
                System.IO.File.WriteAllText(name + ".json", json); UnityEditor.AssetDatabase.ImportAsset(name + ".json");
                Assert.That(CatalogRegistry.AssetPaths(), Does.Not.Contain(name + ".json"));
                System.IO.File.WriteAllText(name + ".upcjson", json); UnityEditor.AssetDatabase.ImportAsset(name + ".upcjson");
                var id = UnityEditor.AssetDatabase.AssetPathToGUID(name + ".upcjson");
                Assert.That(CatalogRegistry.LoadSources(), Has.Some.Matches<CatalogSource>(source => source.id == "UnityPackageCatalog." + id));
                Assert.That(UnityEditor.AssetDatabase.MoveAsset(name + ".upcjson", name + "_Moved.upcjson"), Is.Empty);
                Assert.That(UnityEditor.AssetDatabase.AssetPathToGUID(name + "_Moved.upcjson"), Is.EqualTo(id));
                UnityEditor.AssetDatabase.DeleteAsset(name + "_Moved.upcjson");
                Assert.That(CatalogRegistry.AssetPaths(), Does.Not.Contain(name + "_Moved.upcjson"));
            }
            finally
            {
                UnityEditor.AssetDatabase.DeleteAsset(name + ".json"); UnityEditor.AssetDatabase.DeleteAsset(name + ".upcjson"); UnityEditor.AssetDatabase.DeleteAsset(name + "_Moved.upcjson");
            }
        }
    }
}
