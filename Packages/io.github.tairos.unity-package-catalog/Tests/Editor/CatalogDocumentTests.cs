using System;
using System.IO;
using System.Reflection;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace UnityPackageCatalog.Tests
{
    public class CatalogDocumentTests
    {
        string directory;
        [SetUp] public void Setup() { directory = Path.Combine(Path.GetTempPath(), "upc-test-" + Guid.NewGuid()); Directory.CreateDirectory(directory); }
        [TearDown] public void Cleanup() { Directory.Delete(directory, true); }

        CatalogEntry Entry(string source = "ssh://git@github.com/example-owner/example-package.git#v1.0.0") => new CatalogEntry
        { name = "com.example.tool", displayName = "Example Tool", description = "Example", version = "1.0.0", source = source };
        CatalogDocument Load(params CatalogEntry[] entries)
        {
            var path = Path.Combine(directory, "catalog.json");
            File.WriteAllText(path, JsonUtility.ToJson(new CatalogDocument { displayName = "Example Packages", packages = entries }));
            return CatalogDocument.Load(path);
        }

        [Test] public void PrivateSshUrlKeepsTagAndNeedsNoStoredCredential()
        {
            var entry = Entry();
            Assert.That(Load(entry).packages[0].resolvedSource, Is.EqualTo(entry.source));
        }
        [Test] public void GitSubfolderAndTagArePreserved()
        {
            var entry = Entry("https://github.com/example-owner/repository.git?path=/Packages/tool#v1.0.0");
            Assert.That(Load(entry).packages[0].resolvedSource, Is.EqualTo(entry.source));
        }
        [Test] public void DuplicatePackageIdsAreRejected() => Assert.Throws<FormatException>(() => Load(Entry(), Entry()));
        [TestCase("https://user:secret@github.com/example/repo.git")]
        [TestCase("ssh://git:secret@github.com/example/repo.git")]
        [TestCase("http://github.com/example/repo.git")]
        [TestCase("https://github.com/example/repo.git\nmalformed")]
        public void UnsafeOrMalformedSourcesAreRejected(string source) => Assert.Throws<FormatException>(() => Load(Entry(source)));
        [Test] public void LocalSourceResolvesRelativeToCatalogue()
        {
            var package = Path.Combine(directory, "tool"); Directory.CreateDirectory(package);
            File.WriteAllText(Path.Combine(package, "package.json"), JsonUtility.ToJson(Entry()));
            Assert.That(Load(Entry("file:./tool")).packages[0].resolvedSource, Is.EqualTo("file:" + package.Replace('\\', '/')));
        }
        [Test] public void WrongLocalPackageIdentityIsRejected()
        {
            var package = Path.Combine(directory, "tool"); Directory.CreateDirectory(package);
            File.WriteAllText(Path.Combine(package, "package.json"), "{\"name\":\"com.example.other\",\"version\":\"1.0.0\"}");
            Assert.Throws<FormatException>(() => Load(Entry("file:./tool")));
        }
        [Test] public void UnsupportedSchemaIsRejected()
        {
            var path = Path.Combine(directory, "catalog.json");
            File.WriteAllText(path, "{\"schemaVersion\":2,\"displayName\":\"Example\",\"packages\":[]}");
            Assert.Throws<FormatException>(() => CatalogDocument.Load(path));
        }
        [Test] public void EmptyCatalogueIsValid() => Assert.That(Load().packages, Is.Empty);

        [TestCase("null")]
        [TestCase("{}")]
        [TestCase("invalid json")]
        public void MalformedLocalManifestHasPackageContext(string json)
        {
            var package = Path.Combine(directory, "tool"); Directory.CreateDirectory(package);
            File.WriteAllText(Path.Combine(package, "package.json"), json);
            var error = Assert.Throws<FormatException>(() => Load(Entry("file:./tool")));
            Assert.That(error.Message, Does.Contain("entry 1 (com.example.tool)"));
        }

        [Test] public void LocalMismatchReportsExpectedAndActualIdentity()
        {
            var package = Path.Combine(directory, "tool"); Directory.CreateDirectory(package);
            File.WriteAllText(Path.Combine(package, "package.json"), "{\"name\":\"com.example.other\",\"version\":\"2.0.0\"}");
            var error = Assert.Throws<FormatException>(() => Load(Entry("file:./tool")));
            Assert.That(error.Message, Does.Contain("com.example.other@2.0.0"));
            Assert.That(error.Message, Does.Contain("com.example.tool@1.0.0"));
        }

        [Test] public void MalformedCatalogueHasClearError()
        {
            var path = Path.Combine(directory, "catalog.json");
            File.WriteAllText(path, "invalid json");
            Assert.That(Assert.Throws<FormatException>(() => CatalogDocument.Load(path)).Message,
                Is.EqualTo("Catalogue is not valid JSON."));
        }

        [Test] public void RefreshDetectsEditsRetainsValidDocumentAndRecoversAfterMissingFile()
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            var type = typeof(NativeCatalogBridge);
            if (type.GetField("manager", flags).GetValue(null) != null)
                Assert.Ignore("Run refresh state test with Package Manager closed.");
            var fields = new[] { "document", "observedJson", "refreshError", "observedReadFailure", "openRequested" }
                .Select(name => type.GetField(name, flags)).ToArray();
            var saved = fields.Select(field => field.GetValue(null)).ToArray();
            var refresh = type.GetMethod("RefreshDocument", flags);
            var path = Path.Combine(directory, "catalog.json");
            try
            {
                fields[0].SetValue(null, null);
                fields[1].SetValue(null, null);
                fields[2].SetValue(null, null);
                fields[3].SetValue(null, false);
                Load(Entry());
                refresh.Invoke(null, new object[] { path });
                var first = (CatalogDocument)fields[0].GetValue(null);
                Assert.That(first.packages.Length, Is.EqualTo(1));
                Load();
                refresh.Invoke(null, new object[] { path });
                var updated = (CatalogDocument)fields[0].GetValue(null);
                Assert.That(updated.packages, Is.Empty);
                File.WriteAllText(path, "invalid json");
                refresh.Invoke(null, new object[] { path });
                Assert.That(fields[0].GetValue(null), Is.SameAs(updated));
                Assert.That(fields[2].GetValue(null), Is.Not.Null);
                Load();
                refresh.Invoke(null, new object[] { path });
                Assert.That(fields[2].GetValue(null), Is.Null);
                File.Delete(path);
                refresh.Invoke(null, new object[] { path });
                Assert.That(fields[2].GetValue(null), Is.Not.Null);
                Load();
                refresh.Invoke(null, new object[] { path });
                Assert.That(fields[2].GetValue(null), Is.Null);
            }
            finally
            {
                for (var i = 0; i < fields.Length; i++) fields[i].SetValue(null, saved[i]);
            }
        }

        [Test] public void AssetStoreEntryUsesProductIdWithoutUpmMetadata()
        {
            var entry = new CatalogEntry { assetStoreProductId = 12345 };
            Assert.That(Load(entry).packages[0].assetStoreProductId, Is.EqualTo(12345));
        }
        [Test] public void GitAndAssetStoreEntriesCanShareCatalogue()
        {
            Assert.That(Load(Entry(), new CatalogEntry { assetStoreProductId = 12345 }).packages.Length, Is.EqualTo(2));
        }
        [Test] public void DuplicateAssetStoreIdsAreRejected()
        {
            Assert.Throws<FormatException>(() => Load(new CatalogEntry { assetStoreProductId = 12345 }, new CatalogEntry { assetStoreProductId = 12345 }));
        }
        [Test] public void NegativeAssetStoreIdIsRejected()
        {
            Assert.Throws<FormatException>(() => Load(new CatalogEntry { assetStoreProductId = -1 }));
        }
        [Test] public void AssetStoreEntryCannotMasqueradeAsGitPackage()
        {
            var entry = Entry(); entry.assetStoreProductId = 12345;
            Assert.Throws<FormatException>(() => Load(entry));
        }
        [Test] public void AddingAssetsPreservesGitEntriesAndDeduplicatesSelection()
        {
            Load(Entry());
            var path = Path.Combine(directory, "catalog.json");
            var additions = new[] { new CatalogEntry { assetStoreProductId = 12345 }, new CatalogEntry { assetStoreProductId = 12345 } };
            Assert.That(CatalogDocument.AddAssetStoreEntries(path, additions), Is.EqualTo(1));
            Assert.That(CatalogDocument.AddAssetStoreEntries(path, additions), Is.Zero);
            var result = CatalogDocument.Load(path);
            Assert.That(result.packages[0].source, Is.EqualTo(Entry().source));
            Assert.That(result.packages[1].assetStoreProductId, Is.EqualTo(12345));
        }
        [Test] public void InvalidAdditionDoesNotOverwriteCatalogue()
        {
            Load(Entry());
            var path = Path.Combine(directory, "catalog.json");
            var before = File.ReadAllText(path);
            var invalid = Entry(); invalid.assetStoreProductId = 12345;
            Assert.Throws<FormatException>(() => CatalogDocument.AddAssetStoreEntries(path, new[] { invalid }));
            Assert.That(File.ReadAllText(path), Is.EqualTo(before));
        }
    }
}
