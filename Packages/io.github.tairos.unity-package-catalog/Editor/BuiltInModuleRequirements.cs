using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using UnityEngine;

namespace UnityPackageCatalog
{
    [InitializeOnLoad]
    public static class BuiltInModuleRequirements
    {
        const string PendingKey = "UnityPackageCatalog.PendingModuleRules";
        const string StatusKey = "UnityPackageCatalog.ModuleStatus";
        static SearchRequest search;
        static ListRequest list;
        static AddAndRemoveRequest change;
        static ModulePackageState[] available;
        static BuiltInModuleRule[] rules;
        static bool apply, verifying;
        static double deadline;
        public static bool Busy => search != null || list != null || change != null;
        public static string Status => SessionState.GetString(StatusKey, "No module operation requested.");

        static BuiltInModuleRequirements()
        {
            EditorApplication.update += Poll;
            // Enabling/disabling modules may reload scripts before the Request can finish.
            // Verify the saved rule snapshot after reload; never repeat the mutation.
            if (!string.IsNullOrEmpty(SessionState.GetString(PendingKey, "")))
                EditorApplication.delayCall += ResumeVerification;
        }

        public static void Check(string path) => Begin(path, false);
        public static void Apply(string path) => Begin(path, true);

        static void Begin(string path, bool shouldApply)
        {
            if (Busy || EditorApplication.isCompiling || NativeCatalogBridge.IsPackageOperationInProgress())
                throw new InvalidOperationException("Wait for the current Package Manager operation or compilation to finish.");
            if (!string.IsNullOrEmpty(SessionState.GetString(PendingKey, "")))
            {
                if (shouldApply) throw new InvalidOperationException("Check requirements to verify the previous operation before applying again.");
                ResumeVerification();
                return;
            }
            rules = CatalogDocument.Load(path).builtInModules ?? Array.Empty<BuiltInModuleRule>();
            apply = shouldApply;
            verifying = false;
            if (rules.Length == 0) { SetStatus("This catalogue has no built-in module requirements."); return; }
            SetStatus("Checking available built-in modules and project dependencies…");
            deadline = EditorApplication.timeSinceStartup + 240;
            search = Client.SearchAll(true);
        }

        static ModulePackageState[] Snapshot(PackageInfo[] packages) => packages.Select(p => new ModulePackageState
        {
            name = p.name, version = p.version, builtIn = p.source == PackageSource.BuiltIn,
            direct = p.isDirectDependency,
            dependencies = (p.dependencies ?? Array.Empty<DependencyInfo>()).Select(d => d.name).ToArray()
        }).ToArray();

        static void ResumeVerification()
        {
            var json = SessionState.GetString(PendingKey, "");
            if (string.IsNullOrEmpty(json) || Busy) return;
            rules = JsonUtility.FromJson<CatalogDocument>(json).builtInModules;
            verifying = true;
            SetStatus("Verifying module requirements after package resolution…");
            deadline = EditorApplication.timeSinceStartup + 240;
            list = Client.List(true, true);
        }

        static void Poll()
        {
            if (!Busy || EditorApplication.isCompiling) return;
            try
            {
                if (EditorApplication.timeSinceStartup > deadline)
                    throw new TimeoutException("Module operation timed out. Check requirements again before retrying; Unity may still be resolving packages.");
                if (search != null)
                {
                    if (!search.IsCompleted) return;
                    if (search.Status != StatusCode.Success) throw new InvalidOperationException(search.Error?.message ?? "Could not discover built-in modules.");
                    available = Snapshot(search.Result);
                    search = null;
                    list = Client.List(true, true);
                    return;
                }
                if (change != null)
                {
                    if (!change.IsCompleted) return;
                    if (change.Status != StatusCode.Success)
                    {
                        SessionState.EraseString(PendingKey);
                        throw new InvalidOperationException(change.Error?.message ?? "Unity could not apply module requirements.");
                    }
                    change = null;
                    ResumeVerification();
                    return;
                }
                if (list == null || !list.IsCompleted) return;
                if (list.Status != StatusCode.Success) throw new InvalidOperationException(list.Error?.message ?? "Could not inspect installed packages.");
                var installed = Snapshot(list.Result.ToArray());
                list = null;
                if (verifying)
                {
                    var violations = BuiltInModulePlan.Violations(rules, installed);
                    SetStatus(violations.Length == 0 ? "Module requirements applied and verified." : "Module requirements remain unsatisfied:\n" + string.Join("\n", violations));
                    SessionState.EraseString(PendingKey);
                    return;
                }
                var plan = BuiltInModulePlan.Create(rules, available, installed);
                SetStatus(plan.Describe());
                if (!apply || plan.errors.Length != 0 || !plan.HasChanges) return;
                SessionState.SetString(PendingKey, JsonUtility.ToJson(new CatalogDocument { builtInModules = rules }));
                SetStatus("Applying module requirements…\n" + plan.Describe());
                change = Client.AddAndRemove(plan.add, plan.remove);
            }
            catch (Exception error)
            {
                search = null; list = null; change = null;
                SetStatus("Module operation failed: " + error.Message);
                // Preserve the snapshot if Unity may still be processing a mutation.
            }
        }

        static void SetStatus(string message) => SessionState.SetString(StatusKey, message);

        public static string ActiveDiagnostics()
        {
            if (!CatalogSettings.Enabled) return "Module requirements inactive (catalogue disabled).";
            try
            {
                var rules = CatalogDocument.Load(CatalogSettings.CatalogPath).builtInModules;
                if (rules == null || rules.Length == 0) return "No built-in module requirements in the active catalogue.";
                var installed = Snapshot(PackageInfo.GetAllRegisteredPackages());
                var issues = BuiltInModulePlan.Violations(rules, installed);
                var state = string.Join("\n", rules.Select(rule => rule.name + " · " + rule.requirement + " · " +
                    (installed.Any(p => p.name == rule.name && p.builtIn) ? "enabled" : "disabled")));
                return (issues.Length == 0 ? "Active module requirements satisfied." : string.Join("\n", issues)) + "\n" + state;
            }
            catch (Exception error) { return "Cannot validate active module requirements: " + error.Message; }
        }

        public static void ValidateRulesForBuild(BuiltInModuleRule[] rules, ModulePackageState[] installed)
        {
            var issues = BuiltInModulePlan.Violations(rules, installed);
            if (issues.Length > 0) throw new BuildFailedException("Package Catalog module requirements:\n" + string.Join("\n", issues) + "\nOpen Package Catalog Settings to check and apply requirements.");
        }

        public static void ValidateActiveForBuild()
        {
            if (!CatalogSettings.Enabled) return;
            try
            {
                if (Busy || !string.IsNullOrEmpty(SessionState.GetString(PendingKey, "")))
                    throw new BuildFailedException("Wait for module requirements to be applied and verified before building.");
                var rules = CatalogDocument.Load(CatalogSettings.CatalogPath).builtInModules;
                ValidateRulesForBuild(rules, Snapshot(PackageInfo.GetAllRegisteredPackages()));
            }
            catch (BuildFailedException) { throw; }
            catch (Exception error) { throw new BuildFailedException("Cannot validate Package Catalog module requirements: " + error.Message); }
        }
    }

    public sealed class BuiltInModuleBuildCheck : IPreprocessBuildWithReport
    {
        public int callbackOrder => -1000;
        public void OnPreprocessBuild(BuildReport report) => BuiltInModuleRequirements.ValidateActiveForBuild();
    }
}
