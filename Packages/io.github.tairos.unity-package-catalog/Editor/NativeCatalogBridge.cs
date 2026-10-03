using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Serialization;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityPackageCatalog
{
    // All unsupported Unity internals are isolated here. No Unity reference-source code is vendored.
    [InitializeOnLoad]
    public static class NativeCatalogBridge
    {
        public const string PageId = "Extension/UnityPackageCatalog";
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;
        const string Namespace = "UnityEditor.PackageManager.UI.Internal.";
        static CatalogDocument document;
        static object manager, database, page;
        static EditorWindow attachedWindow;
        static readonly Dictionary<string, object> synthetic = new Dictionary<string, object>();
        static readonly HashSet<long> visibleOwnedAssets = new HashSet<long>();
        static readonly HashSet<long> requestedAssets = new HashSet<long>();
        static double assetFetchStarted;
        static double nextTick;
        static string observedJson, refreshError;
        static bool observedReadFailure;
        static bool stopped, openRequested;
        public static string Status { get; private set; } = "Not enabled.";

        static NativeCatalogBridge()
        {
            EditorApplication.update += Tick;
            AssemblyReloadEvents.beforeAssemblyReload += Detach;
            EditorApplication.quitting += Detach;
        }

        public static void Reload()
        {
            document = null;
            observedJson = refreshError = null;
            observedReadFailure = false;
            stopped = false;
            nextTick = 0;
            Status = CatalogSettings.Enabled ? "Waiting for Package Manager." : "Disabled.";
        }

        public static void RequestOpen() { openRequested = true; nextTick = 0; }

        static void Tick()
        {
            if (!CatalogSettings.Enabled || stopped || EditorApplication.isCompiling || EditorApplication.timeSinceStartup < nextTick)
                return;
            nextTick = EditorApplication.timeSinceStartup + 0.75;
            try
            {
                if (!Application.unityVersion.StartsWith("6000.6.", StringComparison.Ordinal))
                    throw new NotSupportedException("This experimental adapter targets Unity 6000.6 only. Disable it or port the adapter for this Editor version.");
                var windowType = FindType("UnityEditor.PackageManager.UI.PackageManagerWindow");
                var window = Resources.FindObjectsOfTypeAll(windowType).OfType<EditorWindow>().FirstOrDefault();
                if (window == null)
                {
                    if (manager != null) Detach();
                    return;
                }
                RefreshDocument(CatalogSettings.CatalogPath);
                if (document == null) { Status = refreshError; return; }
                if (manager == null || attachedWindow != window)
                    Attach(window);
                EnsureEntries();
                EnsureAssetStoreEntries();
                MoveRowToSources(window);
                if (openRequested)
                {
                    SetProperty(manager, "activePage", page);
                    openRequested = false;
                }
                Status = "Connected to Unity " + Application.unityVersion + " · " + document.displayName + " · " + document.packages.Length + " catalogue entries. Native UI adapter is experimental.";
                if (refreshError != null) Status += "\n" + refreshError;
            }
            catch (Exception error)
            {
                var message = error.GetBaseException().Message;
                stopped = true;
                try { Detach(); } catch { /* Preserve the first diagnostic. */ }
                Status = "Integration stopped: " + message + " Open Package Catalog Settings to retry or disable.";
                Debug.LogWarning("[Unity Package Catalog] " + Status);
            }
        }

        static void RefreshDocument(string path)
        {
            string json;
            try { json = File.ReadAllText(path); }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException)
            {
                observedReadFailure = true;
                refreshError = "Catalogue refresh failed: " + error.Message + ". Last valid catalogue remains active, if available.";
                return;
            }
            if (!observedReadFailure && json == observedJson) return;
            observedReadFailure = false;
            observedJson = json;
            CatalogDocument updated;
            try { updated = CatalogDocument.Parse(path, json); }
            catch (Exception error) when (error is FormatException || error is ArgumentException || error is IOException || error is UnauthorizedAccessException)
            {
                refreshError = "Catalogue refresh failed: " + error.Message + ". Last valid catalogue remains active, if available.";
                return;
            }
            // Defer teardown while Unity owns an installation or removal in progress.
            if (manager != null && (bool)GetProperty(Service("IPackageOperationDispatcher"), "isInstallOrUninstallInProgress"))
            {
                observedJson = null;
                return;
            }
            var wasActive = manager != null && page != null && ReferenceEquals(GetProperty(manager, "activePage"), page);
            Detach();
            document = updated;
            refreshError = null;
            openRequested |= wasActive;
        }

        static void Attach(EditorWindow window)
        {
            manager = Service("PageManager");
            database = Service("IPackageDatabase");
            var args = Activator.CreateInstance(Type("ExtensionPageArgs"), true);
            SetField(args, "name", "UnityPackageCatalog");
            SetField(args, "displayName", document.displayName);
            SetField(args, "icon", Enum.Parse(Type("Icon"), "MyRegistriesPage"));
            SetField(args, "refreshOptions", Enum.Parse(Type("RefreshOptions"),
                document.packages.Any(p => p.assetStoreProductId > 0) ? "UpmList, LocalInfo, ImportedAssets" : "UpmList"));
            var sorts = Array.CreateInstance(Type("PageSortOption"), 2);
            sorts.SetValue(Enum.Parse(Type("PageSortOption"), "NameAsc"), 0);
            sorts.SetValue(Enum.Parse(Type("PageSortOption"), "NameDesc"), 1);
            SetField(args, "supportedSortOptions", sorts);
            var filterField = args.GetType().GetField("filter", Flags);
            var parameter = Expression.Parameter(Type("IPackage"), "package");
            var callback = typeof(NativeCatalogBridge).GetMethod(nameof(Includes), Flags);
            filterField.SetValue(args, Expression.Lambda(filterField.FieldType,
                Expression.Call(callback, Expression.Convert(parameter, typeof(object))), parameter).Compile());
            var pages = (IDictionary)GetField(manager, "m_Pages");
            if (pages.Contains(PageId))
                Invoke(pages[PageId], "UpdateArgs", args);
            var orderedArgs = (IList)GetField(manager, "m_OrderedExtensionPageArgs");
            if (!orderedArgs.Cast<object>().Any(a => (string)GetField(a, "name") == "UnityPackageCatalog"))
                Invoke(manager, "AddExtensionPage", args);
            page = Invoke(manager, "GetPage", PageId);
            attachedWindow = window;
        }

        static bool Includes(object package)
        {
            if (document == null) return false;
            var product = GetProperty(package, "product");
            if (product != null)
            {
                var id = (long)GetProperty(product, "id");
                if (document.packages.Any(p => p.assetStoreProductId == id) && IsAssetOwned(id)) return true;
            }
            var name = (string)GetProperty(package, "name");
            return document.packages.Any(p => p.assetStoreProductId == 0 && p.name == name);
        }

        static void EnsureEntries()
        {
            // Unity owns real installed records and operation progress. Never overwrite them.
            if ((bool)GetProperty(Service("IPackageOperationDispatcher"), "isInstallOrUninstallInProgress")) return;
            var additions = new List<object>();
            foreach (var entry in document.packages)
            {
                if (entry.assetStoreProductId > 0) continue;
                var existing = Invoke(database, "GetPackage", entry.name);
                if (existing != null) continue;
                var version = Activator.CreateInstance(Type("UpmPackageVersion"), Flags, null,
                    new object[] { entry.name, entry.version, Enum.Parse(Type("RegistryType"), "None") }, null);
                SetField(version, "m_DisplayName", entry.displayName);
                SetField(version, "m_Description", entry.description ?? "");
                SetField(version, "m_IsFullyFetched", true);
                SetField(version, "m_PackageId", entry.name + "@" + entry.resolvedSource);
                SetField(version, "m_Tag", Enum.Parse(Type("PackageTag"), entry.resolvedSource.StartsWith("file:") ? "UpmFormat, Local" : "UpmFormat, Git"));
                // UpmVersionList has no empty constructor. Populate every field needed for a single uninstalled version.
                var versions = FormatterServices.GetUninitializedObject(Type("UpmVersionList"));
                var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(Type("UpmPackageVersion")));
                list.Add(version);
                SetField(versions, "m_Versions", list);
                SetField(versions, "m_InstalledIndex", -1);
                SetField(versions, "m_RecommendedIndex", -1);
                SetField(versions, "m_SuggestedUpdateIndex", -1);
                var package = Activator.CreateInstance(Type("Package"), Flags, null,
                    new object[] { entry.name, versions, null, false, false, null, null }, null);
                synthetic[entry.name] = package;
                additions.Add(package);
            }
            if (additions.Count != 0)
            {
                var array = Array.CreateInstance(Type("IPackage"), additions.Count);
                for (var i = 0; i < additions.Count; i++) array.SetValue(additions[i], i);
                Invoke(database, "UpdatePackages", array, null, Enum.Parse(Type("PackagesChangedSource"), "Other"));
            }
        }

        static bool IsAssetOwned(long id) =>
            (bool)GetProperty(Service("IUnityConnectProxy"), "isUserLoggedIn") &&
            Invoke(Service("IAssetStoreCache"), "GetPurchaseInfo", id) != null;

        static void EnsureAssetStoreEntries()
        {
            var entries = document.packages.Where(p => p.assetStoreProductId > 0).ToArray();
            if (entries.Length == 0) return;
            if (!(bool)GetProperty(Service("IUnityConnectProxy"), "isUserLoggedIn"))
            {
                requestedAssets.Clear();
                if (visibleOwnedAssets.Count > 0)
                {
                    visibleOwnedAssets.Clear();
                    Invoke(page, "Rebuild", false);
                }
                return;
            }
            var owned = new HashSet<long>(entries.Where(e => IsAssetOwned(e.assetStoreProductId)).Select(e => e.assetStoreProductId));
            if (!visibleOwnedAssets.SetEquals(owned))
            {
                visibleOwnedAssets.Clear();
                visibleOwnedAssets.UnionWith(owned);
                Invoke(page, "Rebuild", false);
            }
            var client = Service("IAssetStoreClient");
            foreach (var entry in entries)
                if (requestedAssets.Add(entry.assetStoreProductId))
                {
                    assetFetchStarted = EditorApplication.timeSinceStartup;
                    // Unity fetches purchase and product metadata and creates its own package record.
                    Invoke(client, "ExtraFetch", entry.assetStoreProductId);
                }
        }

        static string AssetStoreDiagnostic(CatalogEntry entry)
        {
            var label = "Asset Store " + entry.assetStoreProductId + ": ";
            if (!(bool)GetProperty(Service("IUnityConnectProxy"), "isUserLoggedIn"))
                return label + "sign in to Unity to access owned assets.";
            var fetch = Invoke(Service("IFetchStatusTracker"), "GetProductInfoFetchStatus", entry.assetStoreProductId);
            var error = GetField(fetch, "error");
            if (error != null) return label + "Unity could not load product metadata: " + GetProperty(error, "message") + ". Apply / Reload to retry.";
            if (!IsAssetOwned(entry.assetStoreProductId))
                return label + (EditorApplication.timeSinceStartup - assetFetchStarted < 30
                    ? "checking ownership…" : "ownership unavailable or not owned by this account. Check My Assets, then Apply / Reload.");
            var package = Invoke(database, "GetPackage", entry.assetStoreProductId);
            if (package == null) return label + "loading native metadata…";
            var cache = Service("IAssetStoreCache");
            return label + GetProperty(package, "displayName") + " · " +
                (Invoke(cache, "GetImportedPackage", entry.assetStoreProductId) != null ? "imported" :
                 Invoke(cache, "GetLocalInfo", entry.assetStoreProductId) != null ? "downloaded; ready to import" : "owned; ready to download");
        }

        public static void OpenMyAssets()
        {
            EditorApplication.ExecuteMenuItem("Window/Package Management/Package Manager");
            var pageManager = Service("PageManager");
            SetProperty(pageManager, "activePage", Invoke(pageManager, "GetPage", "MyAssets"));
        }

        public static CatalogEntry[] SelectedOwnedAssets()
        {
            var pageManager = Service("PageManager");
            var active = GetProperty(pageManager, "activePage");
            if (active == null || (string)GetProperty(active, "id") != "MyAssets")
                throw new InvalidOperationException("Select assets in Package Manager's My Assets list first.");
            var selection = (IEnumerable)Invoke(active, "GetSelection");
            var db = Service("IPackageDatabase");
            var entries = new List<CatalogEntry>();
            foreach (string selected in selection)
            {
                var package = Invoke(db, "GetPackageByIdOrName", selected);
                var product = package == null ? null : GetProperty(package, "product");
                if (product == null) continue;
                var id = (long)GetProperty(product, "id");
                if (!IsAssetOwned(id)) continue;
                entries.Add(new CatalogEntry { assetStoreProductId = id, displayName = (string)GetProperty(package, "displayName") });
            }
            if (entries.Count == 0) throw new InvalidOperationException("Select at least one owned Asset Store item in My Assets first.");
            return entries.ToArray();
        }

        static void MoveRowToSources(EditorWindow window)
        {
            var sidebar = window.rootVisualElement.Query<VisualElement>().ToList().FirstOrDefault(e => e.GetType().Name == "Sidebar");
            if (sidebar == null) return;
            var row = (VisualElement)Invoke(sidebar, "GetRow", PageId);
            if (row == null) return;
            // Find the native Built-in row rather than relying on localized foldout text.
            var builtIn = sidebar.Query<VisualElement>().ToList().FirstOrDefault(e => e.GetType().Name == "SidebarRow" &&
                (string)GetProperty(e, "pageId") == "BuiltIn");
            var sources = builtIn?.GetFirstAncestorOfType<Foldout>();
            if (sources == null) throw new MissingMemberException("Could not find the native Sources group.");
            if (row.GetFirstAncestorOfType<Foldout>() != sources) sources.Add(row);
        }

        public static void Detach()
        {
            if (manager == null) return;
            if (page != null && ReferenceEquals(GetProperty(manager, "activePage"), page))
                SetProperty(manager, "activePage", Invoke(manager, "GetPage", "InProject"));
            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
                foreach (var element in window.rootVisualElement.Query<VisualElement>().ToList())
                    if (element.GetType().Name == "SidebarRow" && (string)GetProperty(element, "pageId") == PageId)
                        element.RemoveFromHierarchy();
            var owned = synthetic.Where(p => ReferenceEquals(Invoke(database, "GetPackage", p.Key), p.Value)).Select(p => p.Key).ToArray();
            if (owned.Length > 0)
                Invoke(database, "UpdatePackages", null, owned, Enum.Parse(Type("PackagesChangedSource"), "Other"));
            if (page != null) Invoke(page, "OnDisable");
            ((IDictionary)GetField(manager, "m_Pages")).Remove(PageId);
            var args = (IList)GetField(manager, "m_OrderedExtensionPageArgs");
            for (var i = args.Count - 1; i >= 0; i--)
                if ((string)GetField(args[i], "name") == "UnityPackageCatalog") args.RemoveAt(i);
            synthetic.Clear();
            requestedAssets.Clear();
            visibleOwnedAssets.Clear();
            manager = database = page = null;
            attachedWindow = null;
        }

        public static string Diagnostics()
        {
            if (database == null) return Status;
            var registered = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages();
            return Status + "\n" + string.Join("\n", document.packages.Select(entry =>
            {
                if (entry.assetStoreProductId > 0) return AssetStoreDiagnostic(entry);
                var package = Invoke(database, "GetPackage", entry.name);
                var versions = package == null ? null : GetProperty(package, "versions");
                var installed = versions == null ? null : GetProperty(versions, "installed");
                if (installed == null)
                {
                    var other = registered.FirstOrDefault(p => p.name != entry.name &&
                        (p.source == UnityEditor.PackageManager.PackageSource.Git && p.packageId == p.name + "@" + GitSource(entry.resolvedSource) ||
                         entry.resolvedSource.StartsWith("file:", StringComparison.Ordinal) && p.source == UnityEditor.PackageManager.PackageSource.Local &&
                         Path.GetFullPath(p.resolvedPath).TrimEnd('/', '\\') == entry.resolvedSource.Substring(5).TrimEnd('/', '\\')));
                    if (other != null) return entry.name + ": package ID mismatch — source installed as " + other.name + "; catalogue expects " + entry.name;
                    return entry.name + ": " + (versions == null ? "missing" : "available");
                }
                var info = registered.FirstOrDefault(p => p.name == entry.name);
                if (info == null) return entry.name + ": installed (metadata unavailable)";
                var issues = new List<string>();
                if (info.version != entry.version) issues.Add("version " + info.version + "; catalogue expects " + entry.version);
                if (entry.resolvedSource.StartsWith("file:", StringComparison.Ordinal))
                {
                    if (info.source != UnityEditor.PackageManager.PackageSource.Local ||
                        Path.GetFullPath(info.resolvedPath).TrimEnd('/', '\\') != entry.resolvedSource.Substring(5).TrimEnd('/', '\\'))
                        issues.Add("source differs from catalogue local package");
                }
                else if (info.source != UnityEditor.PackageManager.PackageSource.Git ||
                    info.packageId != entry.name + "@" + GitSource(entry.resolvedSource))
                    issues.Add("Git source/revision differs from catalogue (or Unity normalized its URL); verify installed package source");
                return entry.name + ": installed" + (issues.Count == 0 ? "" : " — " + string.Join("; ", issues));
            }));
        }

        static string GitSource(string source) => source.StartsWith("git+", StringComparison.Ordinal) ? source.Substring(4) : source;

        static Type FindType(string fullName) => typeof(UnityEditor.PackageManager.UI.PackageManagerExtensions).Assembly.GetType(fullName)
            ?? throw new TypeLoadException("Missing Unity internal type " + fullName);
        static Type Type(string name) => FindType(Namespace + name);
        static object Service(string name)
        {
            var container = Type("ServicesContainer").GetProperty("instance", Flags).GetValue(null);
            return container.GetType().GetMethods(Flags).Single(m => m.Name == "Resolve" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0)
                .MakeGenericMethod(Type(name)).Invoke(container, null);
        }
        static FieldInfo Field(object target, string name)
        {
            for (var type = target.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(name, Flags);
                if (field != null) return field;
            }
            throw new MissingFieldException(target.GetType().FullName, name);
        }
        static object GetField(object target, string name) => Field(target, name).GetValue(target);
        static void SetField(object target, string name, object value) => Field(target, name).SetValue(target, value);
        static object GetProperty(object target, string name) => target.GetType().GetProperty(name, Flags).GetValue(target);
        static void SetProperty(object target, string name, object value) => target.GetType().GetProperty(name, Flags).SetValue(target, value);
        static object Invoke(object target, string name, params object[] args)
        {
            var methods = target.GetType().GetMethods(Flags).Where(m => m.Name == name && !m.IsGenericMethod && m.GetParameters().Length == args.Length);
            var method = methods.FirstOrDefault(m => m.GetParameters().Select((p, i) => args[i] == null || p.ParameterType.IsInstanceOfType(args[i])).All(v => v));
            if (method == null) throw new MissingMethodException(target.GetType().FullName, name);
            return method.Invoke(target, args);
        }
    }
}
