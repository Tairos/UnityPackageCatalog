using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityPackageCatalog
{
    internal enum CatalogPickerKind { BuiltIn, AssetStore, Registry }

    internal sealed class CatalogChoice
    {
        public string key, title, detail, module;
        public CatalogEntry entry;
    }

    internal sealed class CatalogPickerSelection
    {
        readonly Dictionary<string, CatalogChoice> chosen = new Dictionary<string, CatalogChoice>(StringComparer.Ordinal);
        public CatalogChoice[] Items => chosen.Values.OrderBy(item => item.key, StringComparer.Ordinal).ToArray();
        public bool Contains(string key) => chosen.ContainsKey(key);
        public void Set(CatalogChoice item, bool selected) { if (selected) chosen[item.key] = item; else chosen.Remove(item.key); }
    }

    public sealed class CatalogItemPicker : EditorWindow
    {
        const int PageSize = 100;
        CatalogPickerKind kind;
        Action<CatalogChoice[], string> accept;
        HashSet<string> existing;
        readonly CatalogPickerSelection selection = new CatalogPickerSelection();
        CatalogChoice[] all = Array.Empty<CatalogChoice>(), visible = Array.Empty<CatalogChoice>();
        ListView list;
        ToolbarSearchField search;
        Label status, selectionCount;
        Button add, previous, next;
        PopupField<string> requirement;
        SearchRequest request;
        IVisualElementScheduledItem searchDelay;
        int offset, generation;
        long total;
        bool loading;
        double deadline;

        internal static void Show(CatalogPickerKind kind, IEnumerable<string> existing, Action<CatalogChoice[], string> accept)
        {
            var window = CreateInstance<CatalogItemPicker>();
            window.kind = kind; window.accept = accept;
            window.existing = new HashSet<string>(existing, StringComparer.Ordinal);
            window.titleContent = new GUIContent(kind == CatalogPickerKind.BuiltIn ? "Add Built-in" : kind == CatalogPickerKind.AssetStore ? "Add Asset Store item" : "Add UPM package");
            window.minSize = new Vector2(520, 420);
            window.position = new Rect(200, 200, 660, 560);
            window.ShowUtility();
        }

        public void CreateGUI()
        {
            var root = rootVisualElement; root.Clear();
            root.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(CatalogSettings.PackageRoot + "/Editor/CatalogEditor.uss"));
            root.AddToClassList("upc-picker");
            if (accept == null) { root.Add(new Label("Reopen this picker from its asset editor to continue.")); return; }
            var heading = new Label(titleContent.text); heading.AddToClassList("upc-heading"); root.Add(heading);
            var hint = new Label(kind == CatalogPickerKind.BuiltIn ? "Choose modules available in this Unity version. Apply the preset separately."
                : kind == CatalogPickerKind.AssetStore ? "Choose items owned by your Unity account. Download and import them later in Package Manager."
                : "Browse Unity Registry packages compatible with this Editor. Installation happens in Package Manager.");
            hint.AddToClassList("upc-subtitle"); root.Add(hint);
            var tools = new VisualElement(); tools.AddToClassList("upc-row"); root.Add(tools);
            search = new ToolbarSearchField { name = "picker-search" }; search.AddToClassList("upc-grow"); tools.Add(search);
            search.RegisterValueChangedCallback(_ =>
            {
                if (kind != CatalogPickerKind.AssetStore) { Filter(); return; }
                generation++; searchDelay?.Pause(); offset = 0;
                searchDelay = root.schedule.Execute(Load).StartingIn(350);
            });
            tools.Add(new Button(() => { offset = 0; Load(); }) { text = "Refresh" });
            if (kind == CatalogPickerKind.BuiltIn)
            {
                requirement = new PopupField<string>("Action", new List<string> { "Enable", "Disable" }, 0);
                root.Add(requirement);
            }
            status = new Label(); status.AddToClassList("upc-wrap"); root.Add(status);
            list = new ListView { name = "picker-items", fixedItemHeight = 54, selectionType = SelectionType.None, makeItem = MakeRow, bindItem = BindRow };
            list.AddToClassList("upc-picker-list"); root.Add(list);
            if (kind == CatalogPickerKind.AssetStore)
            {
                var paging = new VisualElement(); paging.AddToClassList("upc-actions"); root.Add(paging);
                previous = new Button(() => { offset = Math.Max(0, offset - PageSize); Load(); }) { text = "Previous page" };
                next = new Button(() => { offset += PageSize; Load(); }) { text = "Next page" };
                paging.Add(previous); paging.Add(next);
            }
            var footer = new VisualElement(); footer.AddToClassList("upc-actions"); root.Add(footer);
            selectionCount = new Label(); selectionCount.AddToClassList("upc-grow"); footer.Add(selectionCount);
            footer.Add(new Button(Close) { text = "Cancel" });
            add = new Button(() =>
            {
                try { accept(selection.Items, requirement?.value == "Disable" ? "excluded" : "required"); Close(); }
                catch (Exception error) { status.text = error.Message; }
            }) { text = "Add selected", name = "add-picked-items" }; footer.Add(add);
            Load();
        }

        VisualElement MakeRow()
        {
            var row = new VisualElement(); row.AddToClassList("upc-picker-row");
            var toggle = new Toggle { name = "choose" }; row.Add(toggle);
            var text = new VisualElement(); text.AddToClassList("upc-grow"); row.Add(text);
            var title = new Label { name = "item-title" }; title.AddToClassList("upc-title"); text.Add(title);
            var detail = new Label { name = "item-detail" }; detail.AddToClassList("upc-subtitle"); text.Add(detail);
            toggle.RegisterValueChangedCallback(e =>
            {
                if (row.userData is CatalogChoice item) { selection.Set(item, e.newValue); UpdateControls(); }
            });
            row.RegisterCallback<ClickEvent>(e =>
            {
                if (e.target == toggle || toggle.Contains(e.target as VisualElement) || !toggle.enabledSelf) return;
                toggle.value = !toggle.value;
            });
            return row;
        }

        void BindRow(VisualElement row, int index)
        {
            var item = visible[index]; row.userData = item;
            var already = existing.Contains(item.key);
            var toggle = row.Q<Toggle>("choose"); toggle.SetEnabled(!already);
            toggle.SetValueWithoutNotify(already || selection.Contains(item.key));
            row.Q<Label>("item-title").text = item.title;
            row.Q<Label>("item-detail").text = item.detail + (already ? kind == CatalogPickerKind.BuiltIn ? " · Already in preset" : " · Already in catalogue" : "");
        }

        void Load()
        {
            var current = ++generation; loading = true; deadline = EditorApplication.timeSinceStartup + 90;
            status.text = "Loading…"; UpdateControls();
            try
            {
                if (kind == CatalogPickerKind.AssetStore)
                {
                    NativeCatalogBridge.FetchOwnedAssets(search.value, offset, PageSize, (entries, count) =>
                    {
                        if (this == null || current != generation) return;
                        total = count; all = entries.Select(entry => new CatalogChoice { key = entry.assetStoreProductId.ToString(), title = entry.displayName, detail = "Asset Store · " + entry.assetStoreProductId, entry = entry }).ToArray();
                        loading = false; Filter();
                    }, message => { if (this != null && current == generation) { loading = false; all = Array.Empty<CatalogChoice>(); Filter(); status.text = message; } });
                }
                else
                {
                    if (BuiltInModuleRequirements.Busy || NativeCatalogBridge.IsPackageOperationInProgress()) throw new InvalidOperationException("Wait for the current package operation to finish, then Refresh.");
                    request = Client.SearchAll(kind == CatalogPickerKind.BuiltIn);
                }
            }
            catch (Exception error) { loading = false; status.text = error.GetBaseException().Message; UpdateControls(); }
        }

        void OnEnable() => EditorApplication.update += Poll;
        void OnDisable() { generation++; searchDelay?.Pause(); EditorApplication.update -= Poll; }

        void Poll()
        {
            if (!loading || status == null) return;
            if (EditorApplication.timeSinceStartup > deadline)
            {
                generation++; request = null; loading = false; status.text = "Loading timed out. Choose Refresh to retry."; UpdateControls(); return;
            }
            if (request == null || !request.IsCompleted) return;
            var completed = request; request = null; loading = false;
            if (completed.Status != StatusCode.Success) { status.text = completed.Error?.message ?? "Packages could not be loaded."; UpdateControls(); return; }
            all = completed.Result.Where(info => kind == CatalogPickerKind.BuiltIn
                ? info.source == PackageSource.BuiltIn && info.name.StartsWith("com.unity.modules.", StringComparison.Ordinal)
                : info.source == PackageSource.Registry).Select(info => new CatalogChoice
                {
                    key = info.name, title = string.IsNullOrEmpty(info.displayName) ? info.name : info.displayName,
                    detail = info.name + (kind == CatalogPickerKind.Registry ? " · " + info.version : ""),
                    module = kind == CatalogPickerKind.BuiltIn ? info.name : null,
                    entry = kind == CatalogPickerKind.Registry ? new CatalogEntry { name = info.name, displayName = string.IsNullOrEmpty(info.displayName) ? info.name : info.displayName, description = info.description, version = info.version, source = "registry" } : null
                }).GroupBy(item => item.key).Select(group => group.First()).OrderBy(item => item.title, StringComparer.OrdinalIgnoreCase).ToArray();
            Filter();
        }

        void Filter()
        {
            var query = search.value?.Trim() ?? "";
            visible = kind == CatalogPickerKind.AssetStore ? all : all.Where(item => (item.title ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || item.detail.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
            list.itemsSource = visible; list.Rebuild();
            status.text = visible.Length == 0 ? "No matching items." : kind == CatalogPickerKind.AssetStore ? $"{offset + 1}–{offset + visible.Length} of {total} owned items" : visible.Length + " available items";
            UpdateControls();
        }

        void UpdateControls()
        {
            if (add == null) return;
            var count = selection.Items.Length;
            selectionCount.text = count + " selected";
            add.text = count == 0 ? "Add selected" : "Add selected (" + count + ")";
            add.SetEnabled(count > 0 && !loading);
            list.SetEnabled(!loading);
            previous?.SetEnabled(!loading && offset > 0); next?.SetEnabled(!loading && offset + visible.Length < total && visible.Length > 0);
        }
    }
}
