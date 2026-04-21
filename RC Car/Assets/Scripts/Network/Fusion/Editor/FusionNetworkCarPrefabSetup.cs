using Fusion;
using Fusion.Editor;
using UnityEditor;
using UnityEngine;

namespace RC.Network.Fusion.Editor
{
    public static class FusionNetworkCarPrefabSetup
    {
        private const string CarPrefabPath = "Assets/Resources/Prefabs/Car.prefab";

        [MenuItem("Tools/RC/Fusion/Setup Network Car Prefab", priority = 50)]
        public static void EnsureCarPrefabNetworkReadyMenu()
        {
            EnsureCarPrefabNetworkReady();
        }

        public static void EnsureCarPrefabNetworkReadyBatch()
        {
            EnsureCarPrefabNetworkReady();
        }

        private static void EnsureCarPrefabNetworkReady()
        {
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(CarPrefabPath);
            if (prefabRoot == null)
                throw new UnityException($"Network car prefab could not be loaded. path={CarPrefabPath}");

            bool dirty = false;

            if (!prefabRoot.TryGetComponent<NetworkObject>(out _))
            {
                prefabRoot.AddComponent<NetworkObject>();
                dirty = true;
            }

            if (!prefabRoot.TryGetComponent<NetworkRCCar>(out _))
            {
                prefabRoot.AddComponent<NetworkRCCar>();
                dirty = true;
            }

            if (dirty)
            {
                EditorUtility.SetDirty(prefabRoot);
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, CarPrefabPath);
            }

            PrefabUtility.UnloadPrefabContents(prefabRoot);

            NetworkProjectConfigUtilities.RebuildPrefabTable();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[FusionNetworkCarPrefabSetup] Car prefab is network-ready. path={CarPrefabPath}, updated={dirty}");
        }
    }
}
