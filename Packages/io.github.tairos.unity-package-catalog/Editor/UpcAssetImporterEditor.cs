using System;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine.UIElements;

namespace UnityPackageCatalog
{
    public abstract class UpcAssetImporterEditor : ScriptedImporterEditor
    {
        Editor assetEditor;
        protected override bool needsApplyRevert => false;
        public override bool showImportedObject => false;

        public override VisualElement CreateInspectorGUI()
        {
            if (assetEditor != null) DestroyImmediate(assetEditor);
            var asset = AssetDatabase.LoadMainAssetAtPath(((AssetImporter)target).assetPath);
            if (asset == null) return new HelpBox("Reimport this asset to restore its catalogue or preset.", HelpBoxMessageType.Error);
            assetEditor = Editor.CreateEditor(asset);
            saveChangesMessage = "Save changes to " + asset.name + "?";
            if (assetEditor is UpcCatalogEditor catalogue) catalogue.StateChanged = dirty => hasUnsavedChanges = dirty;
            else if (assetEditor is UpcBuiltInPresetEditor preset) preset.StateChanged = dirty => hasUnsavedChanges = dirty;
            return assetEditor.CreateInspectorGUI();
        }

        public override void SaveChanges()
        {
            var saved = assetEditor is UpcCatalogEditor catalogue ? catalogue.TrySave()
                : assetEditor is UpcBuiltInPresetEditor preset && preset.TrySave();
            if (!saved) throw new InvalidOperationException("The asset could not be saved. Fix the reported error or discard changes.");
            hasUnsavedChanges = false;
        }

        public override void DiscardChanges()
        {
            hasUnsavedChanges = false;
            base.DiscardChanges();
            if (assetEditor is UpcCatalogEditor catalogue) catalogue.ReloadDocument();
            else if (assetEditor is UpcBuiltInPresetEditor preset) preset.ReloadDocument();
        }

        public override void OnDisable()
        {
            if (assetEditor != null) DestroyImmediate(assetEditor);
            base.OnDisable();
        }
    }
}
