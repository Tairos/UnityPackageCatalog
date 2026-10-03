using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityPackageCatalog
{
    [CustomEditor(typeof(UpcCatalogAsset))]
    public sealed class UpcCatalogEditor : Editor
    {
        string original, baseline, message;
        CatalogDocument draft;
        VisualElement root;
        Label status;
        internal Action<bool> StateChanged;
        internal bool IsDirty => draft != null && JsonUtility.ToJson(draft) != baseline;
        string Path => AssetDatabase.GetAssetPath(target);

        [MenuItem("Assets/Create/Unity Package Catalog", priority = 210)]
        public static void CreateCatalog()
        {
            var selected = AssetDatabase.GetAssetPath(Selection.activeObject);
            var folder = AssetDatabase.IsValidFolder(selected) ? selected : System.IO.Path.GetDirectoryName(selected);
            if (string.IsNullOrEmpty(folder) || (folder != "Assets" && !folder.StartsWith("Assets/", StringComparison.Ordinal))) folder = "Assets";
            var path = AssetDatabase.GenerateUniqueAssetPath(folder + "/New Catalog.upcjson");
            File.WriteAllText(path, JsonUtility.ToJson(new CatalogDocument { displayName = "New Catalog", packages = Array.Empty<CatalogEntry>() }, true));
            AssetDatabase.ImportAsset(path);
            var asset = AssetDatabase.LoadAssetAtPath<UpcCatalogAsset>(path);
            Selection.activeObject = asset; EditorGUIUtility.PingObject(asset);
            UpcAssetSelection.Select(asset);
        }

        public override VisualElement CreateInspectorGUI()
        {
            root = new VisualElement(); root.AddToClassList("upc-editor");
            root.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(CatalogSettings.PackageRoot + "/Editor/CatalogEditor.uss"));
            ReloadDocument();
            return root;
        }

        internal void ReloadDocument()
        {
            original = File.ReadAllText(Path); draft = null; message = null;
            try { draft = CatalogDocument.Parse(Path, original); baseline = JsonUtility.ToJson(draft); }
            catch (Exception error) { message = error.Message; }
            StateChanged?.Invoke(false); Render();
        }

        void Render()
        {
            root.Clear();
            var location = new Label(Path); location.AddToClassList("upc-subtitle"); root.Add(location);
            status = new Label(message ?? ""); status.AddToClassList("upc-wrap");
            if (draft == null)
            {
                root.Add(new HelpBox(message, HelpBoxMessageType.Error));
                var repair = new TextField("Repair catalogue") { multiline = true, value = original }; repair.AddToClassList("upc-json"); root.Add(repair);
                root.Add(new Button(() =>
                {
                    try { var repaired = CatalogDocument.Parse(Path, repair.value); Write(repaired); }
                    catch (Exception error) { SetMessage(error.Message); }
                }) { text = "Validate and save" });
                root.Add(status); return;
            }
            AddText(root, "Catalogue name", draft.displayName, value => draft.displayName = value);
            var toolbar = new VisualElement(); toolbar.AddToClassList("upc-actions"); root.Add(toolbar);
            toolbar.Add(new Button(() => OpenPicker(CatalogPickerKind.AssetStore)) { text = "Add Asset Store item", name = "add-asset-store" });
            toolbar.Add(new Button(() => OpenPicker(CatalogPickerKind.Registry)) { text = "Add UPM package", name = "add-upm" });
            toolbar.Add(new Button(AddLocalPackage) { text = "Add local package" });
            toolbar.Add(new Button(() =>
            {
                draft.packages = draft.packages.Concat(new[] { new CatalogEntry { displayName = "New Git package", name = "com.example.package", version = "1.0.0", source = "https://github.com/OWNER/REPOSITORY.git" } }).ToArray();
                Render(); Changed();
            }) { text = "Add Git package" });
            var packageHeading = new Label("Packages (" + draft.packages.Length + ")"); packageHeading.AddToClassList("upc-section-title"); root.Add(packageHeading);
            if (draft.packages.Length == 0) AddHint(root, "Add packages using the buttons above. Nothing is installed when you add a catalogue entry.");
            foreach (var entry in draft.packages)
            {
                var type = entry.assetStoreProductId > 0 ? "Asset Store" : entry.source == "registry" ? "UPM" : entry.source?.StartsWith("file:", StringComparison.Ordinal) == true ? "Local" : "Git";
                string EntryLabel() => string.IsNullOrWhiteSpace(entry.displayName) ? entry.assetStoreProductId > 0 ? "Asset Store " + entry.assetStoreProductId : entry.name : entry.displayName;
                var foldout = new Foldout { text = EntryLabel() + " · " + type, value = false };
                foldout.AddToClassList("upc-card"); root.Add(foldout);
                AddText(foldout, "Display name", entry.displayName, value => { entry.displayName = value; foldout.text = EntryLabel() + " · " + type; });
                if (entry.assetStoreProductId > 0)
                    AddHint(foldout, "Asset Store product " + entry.assetStoreProductId + ". Unity supplies download and import details.");
                else
                {
                    AddText(foldout, "Package ID", entry.name, value => entry.name = value);
                    AddText(foldout, "Version", entry.version, value => entry.version = value);
                    AddText(foldout, "Source", entry.source, value => entry.source = value);
                    AddText(foldout, "Description", entry.description, value => entry.description = value);
                }
                AddText(foldout, "Group override", entry.group, value => entry.group = value);
                foldout.Add(new Button(() => { draft.packages = Array.FindAll(draft.packages, item => item != entry); Render(); Changed(); }) { text = "Remove entry" });
            }
            var footer = new VisualElement(); footer.AddToClassList("upc-actions"); root.Add(footer);
            footer.Add(new Button(() => TrySave()) { text = "Save catalogue", name = "save-catalogue" });
            footer.Add(new Button(ReloadDocument) { text = "Revert changes" });
            footer.Add(new Button(() => { CatalogSettings.OpenCatalog(); NativeCatalogBridge.RequestOpen(Path); }) { text = "Open in Package Manager" });
            root.Add(status);
        }

        void OpenPicker(CatalogPickerKind kind)
        {
            var owner = draft;
            CatalogItemPicker.Show(kind, draft.packages.Where(entry => kind == CatalogPickerKind.AssetStore ? entry.assetStoreProductId > 0 : entry.assetStoreProductId == 0).Select(entry => kind == CatalogPickerKind.AssetStore ? entry.assetStoreProductId.ToString() : entry.name), (choices, requirement) =>
            {
                if (this == null || !ReferenceEquals(owner, draft)) throw new InvalidOperationException("The catalogue editor changed. Reopen the picker from the current catalogue.");
                var entries = new List<CatalogEntry>(draft.packages);
                foreach (var choice in choices) if (!entries.Any(entry => choice.entry.assetStoreProductId > 0 ? entry.assetStoreProductId == choice.entry.assetStoreProductId : entry.name == choice.entry.name)) entries.Add(choice.entry);
                draft.packages = entries.ToArray();
                Render(); Changed();
            });
        }

        void AddLocalPackage()
        {
            try
            {
                var folder = EditorUtility.OpenFolderPanel("Choose local package folder", "", "");
                if (string.IsNullOrEmpty(folder)) return;
                var entry = JsonUtility.FromJson<CatalogEntry>(File.ReadAllText(System.IO.Path.Combine(folder, "package.json")));
                if (entry == null || string.IsNullOrWhiteSpace(entry.name)) throw new FormatException("The folder needs a valid package.json.");
                entry.assetStoreProductId = 0; entry.source = "file:" + folder.Replace('\\', '/');
                if (string.IsNullOrWhiteSpace(entry.displayName)) entry.displayName = entry.name;
                entry.ResolveSource(folder);
                if (draft.packages.Any(existing => existing.name == entry.name)) throw new InvalidOperationException("This package is already in the catalogue.");
                draft.packages = draft.packages.Concat(new[] { entry }).ToArray(); Render(); Changed();
            }
            catch (Exception error) { SetMessage(error.Message); }
        }

        void AddText(VisualElement parent, string label, string value, Action<string> change)
        {
            var field = new TextField(label) { value = value ?? "" };
            field.RegisterValueChangedCallback(e => { change(e.newValue); Changed(); }); parent.Add(field);
        }

        static void AddHint(VisualElement parent, string text) { var hint = new Label(text); hint.AddToClassList("upc-subtitle"); parent.Add(hint); }
        void Changed() { StateChanged?.Invoke(IsDirty); SetMessage(IsDirty ? "Unsaved changes" : ""); }
        void SetMessage(string value) { message = value; status.text = value; }

        internal bool TrySave()
        {
            try { Write(draft); return true; }
            catch (Exception error) { SetMessage(error.Message); return false; }
        }

        void Write(CatalogDocument document)
        {
            if (File.ReadAllText(Path) != original) throw new InvalidOperationException("The asset changed externally. Revert changes to load the current file before saving.");
            CatalogDocument.Save(Path, document);
            original = File.ReadAllText(Path); draft = CatalogDocument.Parse(Path, original); baseline = JsonUtility.ToJson(draft);
            AssetDatabase.ImportAsset(Path); message = "Catalogue saved."; StateChanged?.Invoke(false); Render();
        }
    }
}
