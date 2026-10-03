using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityPackageCatalog
{
    public static class CatalogSettings
    {
        internal const string PackageRoot = "Packages/io.github.tairos.unity-package-catalog";
        static string Key
        {
            get
            {
                using var hash = SHA256.Create();
                return "UnityPackageCatalog." + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Application.dataPath)));
            }
        }
        public static string CatalogPath => EditorPrefs.GetString(Key + ".path", "");
        public static bool Enabled => EditorPrefs.GetBool(Key + ".enabled", false);

        public static void Configure(string path, bool enabled)
        {
            if (enabled) CatalogDocument.Load(path);
            NativeCatalogBridge.Detach();
            EditorPrefs.SetString(Key + ".path", path);
            EditorPrefs.SetBool(Key + ".enabled", enabled);
            NativeCatalogBridge.Reload();
        }

        [MenuItem("Window/Package Management/Package Catalog Settings")]
        public static void OpenSettings() => SettingsService.OpenUserPreferences("Preferences/Package Catalog");

        [MenuItem("Window/Package Management/Package Catalog")]
        public static void OpenCatalog()
        {
            EditorApplication.ExecuteMenuItem("Window/Package Management/Package Manager");
            NativeCatalogBridge.RequestOpen();
        }

        public static void UseExample()
        {
            Configure(Path.Combine(UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(CatalogSettings).Assembly).resolvedPath, "Samples~/LocalCatalog/catalog.json"), true);
            OpenCatalog();
        }

        public static int AddSelectedAssets(string path)
        {
            var fullPath = RequireExternalCatalogue(path);
            var selected = NativeCatalogBridge.SelectedOwnedAssets();
            var count = CatalogDocument.AddAssetStoreEntries(fullPath, selected);
            Configure(fullPath, true);
            return count;
        }

        static string RequireExternalCatalogue(string path)
        {
            var fullPath = Path.GetFullPath(path);
            var toolPath = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(CatalogSettings).Assembly).resolvedPath;
            if (fullPath.StartsWith(Path.GetFullPath(toolPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidOperationException("Choose a private catalogue outside the tool package. Use Create private catalogue first if you are using the demo.");
            return fullPath;
        }

        public static void SetSelectedModuleRequirements(string path, string requirement)
        {
            var fullPath = RequireExternalCatalogue(path);
            CatalogDocument.SetBuiltInModuleRules(fullPath, NativeCatalogBridge.SelectedBuiltInModules(requirement));
            Configure(fullPath, true);
        }

        public static void ClearSelectedModuleRequirements(string path)
        {
            var fullPath = RequireExternalCatalogue(path);
            CatalogDocument.ClearBuiltInModuleRules(fullPath, NativeCatalogBridge.SelectedBuiltInModules("required").Select(rule => rule.name).ToArray());
            Configure(fullPath, true);
        }

        [SettingsProvider]
        static SettingsProvider CreateProvider()
        {
            return new SettingsProvider("Preferences/Package Catalog", SettingsScope.User)
            {
                label = "Package Catalog",
                activateHandler = (_, root) =>
                {
                    root.Add(new HelpBox("Experimental native Package Manager integration for Unity 6000.6. Catalogue location and enablement are saved per project on this computer, outside version control. Clone your private catalogue separately, then choose its JSON file.", HelpBoxMessageType.Info));
                    var path = new TextField("Catalogue JSON") { value = CatalogPath };
                    root.Add(path);
                    root.Add(new Button(() =>
                    {
                        var selected = EditorUtility.OpenFilePanel("Select catalogue JSON", "", "json");
                        if (!string.IsNullOrEmpty(selected)) path.value = selected;
                    }) { text = "Choose catalogue…" });
                    var enabled = new Toggle("Enable experimental integration") { value = Enabled };
                    root.Add(enabled);
                    var status = new Label();
                    root.Add(new Button(() =>
                    {
                        try { Configure(path.value, enabled.value); status.text = "Settings applied."; }
                        catch (Exception error) { status.text = error.Message; }
                    }) { text = "Apply / Reload" });
                    root.Add(new Button(() => { UseExample(); path.value = CatalogPath; enabled.value = Enabled; }) { text = "Use local demo catalogue" });
                    root.Add(new Button(OpenCatalog) { text = "Open in Package Manager" });
                    root.Add(new Button(() => { Configure(CatalogPath, false); enabled.value = false; }) { text = "Disable integration" });
                    root.Add(new HelpBox("Asset Store: create or choose a private catalogue, browse My Assets, select owned items, then add the selection here. Unity handles downloads, imports and account authentication.", HelpBoxMessageType.Info));
                    root.Add(new Button(() =>
                    {
                        try
                        {
                            var selected = EditorUtility.SaveFilePanel("Create private catalogue outside the public tool repository", "", "catalog", "json");
                            if (string.IsNullOrEmpty(selected)) return;
                            File.WriteAllText(selected, JsonUtility.ToJson(new CatalogDocument { displayName = "My Packages", packages = Array.Empty<CatalogEntry>() }, true));
                            Configure(selected, true);
                            path.value = CatalogPath;
                            enabled.value = true;
                            status.text = "Private catalogue created.";
                        }
                        catch (Exception error) { status.text = error.Message; }
                    }) { text = "Create private catalogue…" });
                    root.Add(new Button(() =>
                    {
                        try { NativeCatalogBridge.OpenMyAssets(); }
                        catch (Exception error) { status.text = error.GetBaseException().Message; }
                    }) { text = "Browse My Assets" });
                    root.Add(new Button(() =>
                    {
                        try
                        {
                            var count = AddSelectedAssets(path.value);
                            enabled.value = true;
                            status.text = count + " Asset Store entries added. Open in Package Manager to view your catalogue.";
                        }
                        catch (Exception error) { status.text = error.Message; }
                    }) { text = "Add selected My Assets items to catalogue" });
                    root.Add(new HelpBox("Built-in modules can be required or excluded. Rules appear in your catalogue and are checked before builds while the catalogue is enabled. Check requirements to preview changes, then apply explicitly. Unity resolves dependencies; conflicts are reported before changes.", HelpBoxMessageType.Info));
                    root.Add(new Button(() =>
                    {
                        try { NativeCatalogBridge.OpenBuiltIn(); }
                        catch (Exception error) { status.text = error.GetBaseException().Message; }
                    }) { text = "Browse Built-in modules" });
                    foreach (var requirement in new[] { "required", "excluded" })
                    {
                        var desired = requirement;
                        root.Add(new Button(() =>
                        {
                            try { SetSelectedModuleRequirements(path.value, desired); enabled.value = true; status.text = "Selected module rules saved as " + desired + ". Check and apply requirements to change the project."; }
                            catch (Exception error) { status.text = error.Message; }
                        }) { text = desired == "required" ? "Require selected Built-in modules" : "Exclude selected Built-in modules" });
                    }
                    root.Add(new Button(() =>
                    {
                        try { ClearSelectedModuleRequirements(path.value); enabled.value = true; status.text = "Selected modules are now unmanaged. Their project state is unchanged."; }
                        catch (Exception error) { status.text = error.Message; }
                    }) { text = "Clear requirements for selected Built-in modules" });
                    var checkModules = new Button(() =>
                    {
                        try { BuiltInModuleRequirements.Check(path.value); }
                        catch (Exception error) { status.text = error.Message; }
                    }) { text = "Check module requirements / preview changes" };
                    var applyModules = new Button(() =>
                    {
                        try { BuiltInModuleRequirements.Apply(path.value); }
                        catch (Exception error) { status.text = error.Message; }
                    }) { text = "Apply module requirements" };
                    root.Add(checkModules);
                    root.Add(applyModules);
                    var moduleStatus = new Label();
                    moduleStatus.style.whiteSpace = WhiteSpace.Normal;
                    root.Add(moduleStatus);
                    moduleStatus.schedule.Execute(() =>
                    {
                        moduleStatus.text = BuiltInModuleRequirements.Status + "\n" + BuiltInModuleRequirements.ActiveDiagnostics();
                        checkModules.SetEnabled(!BuiltInModuleRequirements.Busy);
                        applyModules.SetEnabled(!BuiltInModuleRequirements.Busy);
                    }).Every(1000);
                    root.Add(status);
                    var diagnostics = new Label();
                    root.Add(diagnostics);
                    diagnostics.schedule.Execute(() => diagnostics.text = NativeCatalogBridge.Diagnostics()).Every(1000);
                }
            };
        }
    }
}
