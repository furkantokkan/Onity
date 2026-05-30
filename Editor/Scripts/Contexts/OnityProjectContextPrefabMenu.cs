using System.IO;
using Onity.Unity.Contexts;
using UnityEditor;
using UnityEngine;

namespace Onity.Editor.Contexts
{
    /// <summary>
    /// Editor helpers for creating the runtime-loaded ProjectContext prefab.
    /// </summary>
    public static class OnityProjectContextPrefabMenu
    {
        private const string k_createMenuPath = "Tools/Onity/Contexts/Create ProjectContext Prefab";
        private const string k_prefabAssetPath = "Assets/Resources/Onity/ProjectContext.prefab";

        [MenuItem(k_createMenuPath, false, 2050)]
        private static void CreateProjectContextPrefab()
        {
            EnsureParentFolder();

            GameObject existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_prefabAssetPath);

            if (existingPrefab != null && existingPrefab.GetComponent<ProjectContext>() != null)
            {
                EditorGUIUtility.PingObject(existingPrefab);
                Debug.Log($"Onity ProjectContext prefab already exists at '{k_prefabAssetPath}'.");
                return;
            }

            GameObject root = new GameObject("ProjectContext");
            root.AddComponent<ProjectContext>();

            if (existingPrefab == null)
            {
                PrefabUtility.SaveAsPrefabAsset(root, k_prefabAssetPath);
                Object.DestroyImmediate(root);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"Created Onity ProjectContext prefab at '{k_prefabAssetPath}'.");
                return;
            }

            bool overwrite = EditorUtility.DisplayDialog(
                "Onity ProjectContext",
                $"A prefab already exists at '{k_prefabAssetPath}' but it does not contain ProjectContext. Overwrite it?",
                "Overwrite",
                "Cancel");

            if (overwrite == false)
            {
                Object.DestroyImmediate(root);
                return;
            }

            PrefabUtility.SaveAsPrefabAsset(root, k_prefabAssetPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Updated Onity ProjectContext prefab at '{k_prefabAssetPath}'.");
        }

        private static void EnsureParentFolder()
        {
            const string resourcesFolder = "Assets/Resources";
            const string onityFolder = "Assets/Resources/Onity";

            if (AssetDatabase.IsValidFolder(resourcesFolder) == false)
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }

            if (AssetDatabase.IsValidFolder(onityFolder) == false)
            {
                AssetDatabase.CreateFolder(resourcesFolder, "Onity");
            }

            string filesystemPath = Path.GetDirectoryName(k_prefabAssetPath);

            if (string.IsNullOrEmpty(filesystemPath) == false && Directory.Exists(filesystemPath) == false)
            {
                Directory.CreateDirectory(filesystemPath);
            }
        }
    }
}
