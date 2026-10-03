using System;
using System.Linq;
using UnityEditor;
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
        static string versionNotice;
        public static string PresetPath => SessionState.GetString("UnityPackageCatalog.ModulePresetPath", "");
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

        public static void Check(string presetPath) => Begin(presetPath, false);
        public static void Apply(string presetPath) => Begin(presetPath, true);

        static void Begin(string presetPath, bool shouldApply)
        {
            if (Busy || EditorApplication.isCompiling || NativeCatalogBridge.IsPackageOperationInProgress())
                throw new InvalidOperationException("Wait for the current Package Manager operation or compilation to finish.");
            if (!string.IsNullOrEmpty(SessionState.GetString(PendingKey, "")))
            {
                if (shouldApply) throw new InvalidOperationException("Preview changes to verify the previous operation before applying again.");
                ResumeVerification();
                return;
            }
            var preset = BuiltInPresetDocument.Load(presetPath);
            rules = preset.builtInModules;
            SessionState.SetString("UnityPackageCatalog.ModulePresetPath", presetPath);
            versionNotice = preset.VersionNotice;
            apply = shouldApply;
            verifying = false;
            if (rules.Length == 0) { SetStatus("This preset has no built-in module actions."); return; }
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
            rules = JsonUtility.FromJson<BuiltInPresetDocument>(json).builtInModules;
            verifying = true;
            SetStatus("Verifying preset actions after package resolution…");
            deadline = EditorApplication.timeSinceStartup + 240;
            list = Client.List(true, true);
        }

        static void Poll()
        {
            if (!Busy || EditorApplication.isCompiling) return;
            try
            {
                if (EditorApplication.timeSinceStartup > deadline)
                    throw new TimeoutException("Module operation timed out. Preview changes again before retrying; Unity may still be resolving packages.");
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
                        throw new InvalidOperationException(change.Error?.message ?? "Unity could not apply the Built-in preset.");
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
                    SetStatus(violations.Length == 0 ? "Built-in preset applied and verified." : "Preset actions remain unsatisfied:\n" + string.Join("\n", violations));
                    SessionState.EraseString(PendingKey);
                    return;
                }
                var plan = BuiltInModulePlan.Create(rules, available, installed);
                SetStatus((string.IsNullOrEmpty(versionNotice) ? "" : versionNotice + "\n") + plan.Describe());
                if (!apply || plan.errors.Length != 0 || !plan.HasChanges) return;
                SessionState.SetString(PendingKey, JsonUtility.ToJson(new BuiltInPresetDocument { builtInModules = rules }));
                SetStatus("Applying Built-in preset…\n" + plan.Describe());
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

    }
}
