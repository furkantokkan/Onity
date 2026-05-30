using System.Collections.Generic;
using Onity.Samples.TankArena2D.SceneFlow;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Onity.Editor.Samples
{
    /// <summary>
    /// Editor menu for applying Tank Arena scene flow order to Build Settings.
    /// </summary>
    public static class TankArenaSceneFlowBuildSettingsMenu
    {
        private const string k_scenesRoot = "Assets/Onity-Packages/Onity/Samples/TankArena2D/Scenes";

        public static void ApplySceneFlowToBuildSettings()
        {
            if (TryBuildSceneFlow(out List<EditorBuildSettingsScene> scenes, out string bootstrapPath)
                == false)
            {
                return;
            }

            EditorBuildSettings.scenes = scenes.ToArray();
            SceneAsset bootstrapScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(bootstrapPath);
            EditorSceneManager.playModeStartScene = bootstrapScene;
            Debug.Log("Tank Arena scene flow applied. Bootstrap is first and set as Play Mode start scene.");
        }

        public static void ArrangeSceneFlow()
        {
            if (TryBuildSceneFlow(out List<EditorBuildSettingsScene> scenes, out string bootstrapPath)
                == false)
            {
                return;
            }

            EditorBuildSettings.scenes = scenes.ToArray();
            SceneAsset bootstrapScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(bootstrapPath);
            EditorSceneManager.playModeStartScene = bootstrapScene;
            EditorSceneManager.OpenScene(bootstrapPath, OpenSceneMode.Single);

            Debug.Log(
                "Tank Arena scenes arranged. Build Settings, Play Mode start scene, and active editor scene were updated.");
        }

        public static void GenerateAndArrangeSceneFlow()
        {
            OnityTankArenaSceneGenerator.GenerateTankArenaScene();
            ArrangeSceneFlow();
        }

        private static bool TryBuildSceneFlow(
            out List<EditorBuildSettingsScene> scenes,
            out string bootstrapPath)
        {
            bootstrapPath = FindScenePath(TankArenaSceneIds.Bootstrap);
            string loadingPath = FindScenePath(TankArenaSceneIds.Loading);
            string mainMenuPath = FindScenePath(TankArenaSceneIds.MainMenu);
            string gameplayPath = FindScenePath(TankArenaSceneIds.Gameplay);

            scenes = null;

            if (string.IsNullOrEmpty(bootstrapPath)
                || string.IsNullOrEmpty(loadingPath)
                || string.IsNullOrEmpty(mainMenuPath)
                || string.IsNullOrEmpty(gameplayPath))
            {
                Debug.LogError(
                    "Tank Arena scene flow setup failed. Ensure all 4 scenes exist under "
                    + k_scenesRoot + ".");
                return false;
            }

            scenes = new List<EditorBuildSettingsScene>(4)
            {
                new EditorBuildSettingsScene(bootstrapPath, true),
                new EditorBuildSettingsScene(loadingPath, true),
                new EditorBuildSettingsScene(mainMenuPath, true),
                new EditorBuildSettingsScene(gameplayPath, true)
            };

            return true;
        }

        private static string FindScenePath(string sceneName)
        {
            string[] sceneGuids = AssetDatabase.FindAssets("t:Scene", new[] { k_scenesRoot });

            for (int i = 0; i < sceneGuids.Length; i++)
            {
                string scenePath = AssetDatabase.GUIDToAssetPath(sceneGuids[i]);
                string fileName = System.IO.Path.GetFileNameWithoutExtension(scenePath);

                if (string.Equals(fileName, sceneName))
                {
                    return scenePath;
                }
            }

            return null;
        }
    }
}

