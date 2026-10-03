using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityPackageCatalog.Tests
{
    public class CatalogInspectorTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void ImportedAssetsHaveValidScriptsAndInlineAuthoring(bool preset)
        {
            var path = "Assets/UPCInspector_" + Guid.NewGuid().ToString("N") + (preset ? ".upcbuiltinjson" : ".upcjson");
            Editor editor = null;
            EditorWindow host = null;
            try
            {
                var json = preset
                    ? JsonUtility.ToJson(new BuiltInPresetDocument { displayName = "Inspector", builtInModules = Array.Empty<BuiltInModuleRule>() })
                    : JsonUtility.ToJson(new CatalogDocument { displayName = "Inspector", packages = Array.Empty<CatalogEntry>() });
                File.WriteAllText(path, json);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                var importer = AssetImporter.GetAtPath(path);
                var importerScript = new SerializedObject(importer).FindProperty("m_Script")?.objectReferenceValue as MonoScript;
                Assert.That(importerScript, Is.Not.Null);
                Assert.That(importerScript.GetClass(), Is.EqualTo(importer.GetType()));
                var asset = AssetDatabase.LoadMainAssetAtPath(path) as ScriptableObject;
                Assert.That(MonoScript.FromScriptableObject(asset).GetClass(), Is.EqualTo(asset.GetType()));
                editor = Editor.CreateEditor(importer);
                Assert.That(editor, Is.InstanceOf<UpcAssetImporterEditor>());
                var root = editor.CreateInspectorGUI();
                Assert.That(root.Q<Button>(preset ? "save-preset" : "save-catalogue"), Is.Not.Null);
                Assert.That(root.Q<Button>(preset ? "apply-preset" : "add-asset-store"), Is.Not.Null);
                Assert.That(((UnityEditor.AssetImporters.ScriptedImporterEditor)editor).showImportedObject, Is.False);
                host = ScriptableObject.CreateInstance<EditorWindow>();
                host.Show();
                host.rootVisualElement.Add(root);
                var name = root.Q<TextField>();
                name.value = "Edited in Inspector";
                Assert.That(editor.hasUnsavedChanges, Is.True);
                editor.SaveChanges();
                var saved = preset ? BuiltInPresetDocument.Load(path).displayName : CatalogDocument.Load(path).displayName;
                Assert.That(saved, Is.EqualTo("Edited in Inspector"));
                Assert.That(editor.hasUnsavedChanges, Is.False);
            }
            finally
            {
                if (host != null) host.Close();
                if (editor != null) UnityEngine.Object.DestroyImmediate(editor);
                AssetDatabase.DeleteAsset(path);
            }
        }
    }
}
