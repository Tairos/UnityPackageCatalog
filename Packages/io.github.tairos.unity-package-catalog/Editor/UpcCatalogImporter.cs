using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace UnityPackageCatalog
{
    [ScriptedImporter(2, "upcjson")]
    public sealed class UpcCatalogImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext context)
        {
            var asset = ScriptableObject.CreateInstance<UpcCatalogAsset>();
            asset.json = File.ReadAllText(context.assetPath);
            // Keep malformed assets editable and discoverable; validation is reported by the editor/bridge.
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(CatalogSettings.PackageRoot + "/Editor/Icons/UpcCatalogIcon.png");
            context.AddObjectToAsset("catalog", asset, icon);
            context.SetMainObject(asset);
        }
    }
}
