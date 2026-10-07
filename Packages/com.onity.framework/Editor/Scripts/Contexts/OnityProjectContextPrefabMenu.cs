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
        private const string k_createMenuPath = "Onity/Contexts/Create ProjectContext Prefab";
        private const string k_prefabAssetPath = "Assets/Resources/Onity/ProjectContext.prefab";

        [MenuItem(k_createMenuPath, false, 2050)]
        private static void CreateProjectContextPrefab()
        {
            GameObject prefab = EnsureProjectContextPrefab();

            if (prefab != null)
            {
                EditorGUIUtility.PingObject(prefab);
            }
        }

        internal static GameObject EnsureProjectContextPrefab()
        {
            EnsureParentFolder();

            GameObject existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_prefabAssetPath);

            if (existingPrefab != null && existingPrefab.GetComponent<ProjectContext>() != null)
            {
                Debug.Log($"Onity ProjectContext prefab already exists at '{k_prefabAssetPath}'.");
                return existingPrefab;
            }

            if (existingPrefab != null && EditorUtility.DisplayDialog(
                "Onity ProjectContext",
                $"A prefab already exists at '{k_prefabAssetPath}' but it does not contain ProjectContext. Overwrite it?",
                "Overwrite",
                "Cancel") == false)
            {
                return null;
            }

            GameObject root = new GameObject("ProjectContext");
            try
            {
                root.AddComponent<ProjectContext>();
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, k_prefabAssetPath);
                Debug.Log($"Saved Onity ProjectContext prefab at '{k_prefabAssetPath}'.");
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
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
