using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace UnityPackageCatalog
{
    internal static class UpcAssetSelection
    {
        internal static void Select(Object asset)
        {
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
            EditorApplication.ExecuteMenuItem("Window/General/Inspector");
        }

        [OnOpenAsset]
        static bool Open(EntityId instanceID, int line)
        {
            var asset = EditorUtility.EntityIdToObject(instanceID);
            if (!(asset is UpcCatalogAsset) && !(asset is UpcBuiltInPresetAsset)) return false;
            Select(asset);
            return true;
        }
    }
}
