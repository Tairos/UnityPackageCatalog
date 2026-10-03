using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace UnityPackageCatalog.Tests
{
    public class BuiltInModuleTests
    {
        const string Audio = "com.unity.modules.audio";
        const string Physics = "com.unity.modules.physics";
        const string Vehicles = "com.unity.modules.vehicles";
        const string Json = "com.unity.modules.jsonserialize";
        BuiltInModuleRule Rule(string name, string requirement) => new BuiltInModuleRule { name = name, requirement = requirement };
        ModulePackageState Module(string name, bool direct = true, params string[] dependencies) =>
            new ModulePackageState { name = name, version = "1.0.0", builtIn = true, direct = direct, dependencies = dependencies };

        [Test] public void RequiredMissingModuleIsEnabledWithEditorVersion()
        {
            var plan = BuiltInModulePlan.Create(new[] { Rule(Audio, "required") }, new[] { Module(Audio) }, Array.Empty<ModulePackageState>());
            Assert.That(plan.errors, Is.Empty);
            Assert.That(plan.add, Is.EqualTo(new[] { Audio + "@1.0.0" }));
        }
        [Test] public void ExcludedDirectModuleCanBeRemoved()
        {
            var plan = BuiltInModulePlan.Create(new[] { Rule(Audio, "excluded") }, new[] { Module(Audio) }, new[] { Module(Audio) });
            Assert.That(plan.errors, Is.Empty);
            Assert.That(plan.remove, Is.EqualTo(new[] { Audio }));
        }
        [Test] public void DependencyBlocksExcludedModule()
        {
            var modules = new[] { Module(Physics), Module(Vehicles, true, Physics) };
            var plan = BuiltInModulePlan.Create(new[] { Rule(Physics, "excluded") }, modules, modules);
            Assert.That(plan.errors.Single(), Does.Contain(Vehicles + " → " + Physics));
        }
        [Test] public void ExcludingDependentModulesTogetherIsAllowed()
        {
            var modules = new[] { Module(Physics), Module(Vehicles, true, Physics) };
            var plan = BuiltInModulePlan.Create(new[] { Rule(Physics, "excluded"), Rule(Vehicles, "excluded") }, modules, modules);
            Assert.That(plan.errors, Is.Empty);
            Assert.That(plan.remove, Is.EquivalentTo(new[] { Physics, Vehicles }));
        }
        [Test] public void IndirectModuleDisappearsWhenItsExcludedParentIsRemoved()
        {
            var modules = new[] { Module(Physics, false), Module(Vehicles, true, Physics) };
            var plan = BuiltInModulePlan.Create(new[] { Rule(Physics, "excluded"), Rule(Vehicles, "excluded") }, modules, modules);
            Assert.That(plan.errors, Is.Empty);
            Assert.That(plan.remove, Is.EqualTo(new[] { Vehicles }));
        }
        [Test] public void MissingRequiredModuleCannotDependOnExcludedModule()
        {
            var modules = new[] { Module(Physics), Module(Vehicles, true, Physics) };
            var plan = BuiltInModulePlan.Create(new[] { Rule(Physics, "excluded"), Rule(Vehicles, "required") }, modules, Array.Empty<ModulePackageState>());
            Assert.That(plan.errors.Single(), Does.Contain(Vehicles));
        }
        [Test] public void ThirdPartyTransitiveDependencyBlocksExclusion()
        {
            var installed = new[] { Module(Physics, false), Module(Vehicles, false, Physics),
                new ModulePackageState { name = "com.example.game", dependencies = new[] { Vehicles } } };
            var plan = BuiltInModulePlan.Create(new[] { Rule(Physics, "excluded") }, installed.Where(p => p.builtIn).ToArray(), installed);
            Assert.That(plan.errors, Has.Some.Contains("com.example.game → " + Vehicles + " → " + Physics));
        }
        [Test] public void ToolDependencyPreventsJsonReaderExclusion()
        {
            var installed = new[] { Module(Json), new ModulePackageState { name = "io.github.tairos.unity-package-catalog", dependencies = new[] { Json } } };
            var plan = BuiltInModulePlan.Create(new[] { Rule(Json, "excluded") }, new[] { Module(Json) }, installed);
            Assert.That(plan.errors, Has.Some.Contains("io.github.tairos.unity-package-catalog"));
        }
        [Test] public void UnavailableModuleIsReported()
        {
            var plan = BuiltInModulePlan.Create(new[] { Rule(Audio, "required") }, Array.Empty<ModulePackageState>(), Array.Empty<ModulePackageState>());
            Assert.That(plan.errors.Single(), Does.Contain("unavailable"));
        }
        [Test] public void AlreadySatisfiedRulesNeedNoChanges()
        {
            var modules = new[] { Module(Audio), Module(Physics) };
            var plan = BuiltInModulePlan.Create(new[] { Rule(Audio, "required"), Rule(Physics, "excluded") }, modules, new[] { Module(Audio, false) });
            Assert.That(plan.errors, Is.Empty);
            Assert.That(plan.HasChanges, Is.False);
        }
        [TestCase("com.unity.inputsystem", "required")]
        [TestCase("com.unity.modules.audio", "optional")]
        [TestCase("com.unity.modules.audio", "")]
        public void InvalidModuleRulesAreRejected(string name, string requirement) =>
            Assert.Throws<FormatException>(() => BuiltInModuleRule.Validate(new[] { Rule(name, requirement) }));
        [Test] public void ContradictoryRulesAreRejected() => Assert.Throws<FormatException>(() =>
            BuiltInModuleRule.Validate(new[] { Rule(Audio, "required"), Rule(Audio, "excluded") }));
        [Test] public void VerificationReportsMissingAndExcludedModules()
        {
            Assert.That(BuiltInModulePlan.Violations(new[] { Rule(Audio, "required"), Rule(Physics, "excluded") }, new[] { Module(Physics) }), Has.Length.EqualTo(2));
        }
        [Test] public void OpposingPresetsAreIndependent()
        {
            var enable = BuiltInPresetDocument.Parse("enable.upcbuiltinjson", JsonUtility.ToJson(new BuiltInPresetDocument { displayName = "Enable", builtInModules = new[] { Rule(Audio, "required") } }));
            var disable = BuiltInPresetDocument.Parse("disable.upcbuiltinjson", JsonUtility.ToJson(new BuiltInPresetDocument { displayName = "Disable", builtInModules = new[] { Rule(Audio, "excluded") } }));
            Assert.That(BuiltInModulePlan.Create(enable.builtInModules, new[] { Module(Audio) }, Array.Empty<ModulePackageState>()).add, Has.Length.EqualTo(1));
            Assert.That(BuiltInModulePlan.Create(disable.builtInModules, new[] { Module(Audio) }, Array.Empty<ModulePackageState>()).HasChanges, Is.False);
        }
        [Test] public void InvalidPresetSaveDoesNotModifyFile()
        {
            var path = Path.Combine(Path.GetTempPath(), "upc-preset-" + Guid.NewGuid() + ".upcbuiltinjson");
            try
            {
                var preset = new BuiltInPresetDocument { displayName = "Test", builtInModules = new[] { Rule(Audio, "required") } };
                File.WriteAllText(path, JsonUtility.ToJson(preset));
                var before = File.ReadAllText(path); preset.builtInModules[0].requirement = "invalid";
                Assert.Throws<FormatException>(() => BuiltInPresetDocument.Save(path, preset));
                Assert.That(File.ReadAllText(path), Is.EqualTo(before));
            }
            finally { File.Delete(path); }
        }
        [Test] public void MixedCatalogueIsRejectedBeforeRulesCanBeLost() =>
            Assert.Throws<FormatException>(() => CatalogDocument.Parse("mixed.upcjson", "{\"schemaVersion\":1,\"displayName\":\"Mixed\",\"packages\":[],\"builtInModules\":[{\"name\":\"com.unity.modules.audio\",\"requirement\":\"required\"}]}"));
        [Test] public void PresetRejectsPackagesAndWrongExtension()
        {
            Assert.Throws<FormatException>(() => BuiltInPresetDocument.Parse("mixed.upcbuiltinjson", "{\"schemaVersion\":1,\"displayName\":\"Mixed\",\"packages\":[],\"builtInModules\":[]}"));
            Assert.Throws<FormatException>(() => BuiltInPresetDocument.Parse("preset.upcjson", JsonUtility.ToJson(new BuiltInPresetDocument { displayName = "Test", builtInModules = Array.Empty<BuiltInModuleRule>() })));
        }
        [Test] public void VersionDifferenceExplainsCurrentEditorValidation()
        {
            var preset = new BuiltInPresetDocument { unityVersion = "2019.4.0f1" };
            Assert.That(preset.VersionNotice, Does.Contain("2019.4.0f1").And.Contain(Application.unityVersion));
        }
        [Test] public void UnlistedModulesAreNotChanged()
        {
            var modules = new[] { Module(Audio), Module(Physics) };
            var plan = BuiltInModulePlan.Create(new[] { Rule(Audio, "excluded") }, modules, modules);
            Assert.That(plan.remove, Is.EqualTo(new[] { Audio }));
            Assert.That(plan.add, Is.Empty);
        }
        [Test] public void NonBuiltInPackageCannotSatisfyModuleRule()
        {
            var installed = new[] { new ModulePackageState { name = Audio } };
            Assert.That(BuiltInModulePlan.Violations(new[] { Rule(Audio, "required") }, installed), Has.Length.EqualTo(1));
            Assert.That(BuiltInModulePlan.Violations(new[] { Rule(Audio, "excluded") }, installed), Has.Length.EqualTo(1));
            Assert.That(BuiltInModulePlan.Create(new[] { Rule(Audio, "required") }, new[] { Module(Audio) }, installed).errors, Is.Not.Empty);
        }
        [UnityTest] public IEnumerator NativeDiscoveryAndPreviewFindBuiltInModuleWithoutChangingProject()
        {
            var path = "Assets/UPCModulePreview_" + Guid.NewGuid().ToString("N") + ".upcbuiltinjson";
            var previousEnabled = CatalogSettings.Enabled;
            try
            {
                File.WriteAllText(path, JsonUtility.ToJson(new BuiltInPresetDocument { displayName = "Test", builtInModules = new[] { Rule(Audio, "required") } }));
                UnityEditor.AssetDatabase.ImportAsset(path);
                CatalogSettings.SetEnabled(false);
                Assert.That(CatalogRegistry.AssetPaths(), Does.Not.Contain(path));
                Assert.That(BuiltInPresetDocument.AssetPaths(), Does.Contain(path));
                BuiltInModuleRequirements.Check(path);
                var deadline = UnityEditor.EditorApplication.timeSinceStartup + 90;
                while (BuiltInModuleRequirements.Busy && UnityEditor.EditorApplication.timeSinceStartup < deadline) yield return null;
                Assert.That(BuiltInModuleRequirements.Busy, Is.False, "Preview timed out");
                Assert.That(BuiltInModuleRequirements.Status, Does.Not.Contain("failed").And.Not.Contain("unavailable"));
                Assert.That(BuiltInModuleRequirements.Status, Does.Contain("satisfied").Or.Contain("Enable:"));
            }
            finally { UnityEditor.AssetDatabase.DeleteAsset(path); CatalogSettings.SetEnabled(previousEnabled); }
        }
    }
}
