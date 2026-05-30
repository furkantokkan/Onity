using System;
using System.Collections.Generic;
using Onity.Samples.TankArena2D.SceneFlow;
using Onity.Unity.Contexts;
using Onity.Unity.SceneFlow;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Onity.Editor.Samples
{
    /// <summary>
    /// Adds minimal Tank Arena scene-flow scaffolding when Apply Scene Flow is executed.
    /// </summary>
    public static class TankArenaSceneFlowApplyHooks
    {
        private const string k_scenesRoot = "Assets/Onity-Packages/Onity/Samples/TankArena2D/Scenes";

        public static bool TryApplyProfile(OnitySceneFlowProfile profile)
        {
            if (profile == null)
            {
                return false;
            }

            List<SceneAsset> tankArenaScenes = CollectTankArenaSceneAssets(profile);

            if (tankArenaScenes.Count <= 0)
            {
                return false;
            }

            string returnScenePath = SceneManager.GetActiveScene().path;

            try
            {
                for (int i = 0; i < tankArenaScenes.Count; i++)
                {
                    string scenePath = AssetDatabase.GetAssetPath(tankArenaScenes[i]);
                    Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                    bool modified = EnsureTankArenaSceneScaffolding(scene, profile);

                    if (modified)
                    {
                        EditorSceneManager.MarkSceneDirty(scene);
                        EditorSceneManager.SaveScene(scene);
                    }
                }
            }
            finally
            {
                if (string.IsNullOrWhiteSpace(returnScenePath) == false
                    && AssetDatabase.LoadAssetAtPath<SceneAsset>(returnScenePath) != null)
                {
                    EditorSceneManager.OpenScene(returnScenePath, OpenSceneMode.Single);
                }
            }

            return true;
        }

        private static List<SceneAsset> CollectTankArenaSceneAssets(OnitySceneFlowProfile profile)
        {
            List<SceneAsset> scenes = new List<SceneAsset>(8);
            HashSet<SceneAsset> seenScenes = new HashSet<SceneAsset>();
            AddSceneByName(profile.GetSceneName(OnitySceneFlowStateId.Bootstrap), scenes, seenScenes);
            AddSceneByName(profile.GetSceneName(OnitySceneFlowStateId.Loading), scenes, seenScenes);

            List<string> groupedNames = new List<string>(8);
            profile.CopySceneNames(OnitySceneFlowStateId.MainMenuHub, groupedNames);

            for (int i = 0; i < groupedNames.Count; i++)
            {
                AddSceneByName(groupedNames[i], scenes, seenScenes);
            }

            groupedNames.Clear();
            profile.CopySceneNames(OnitySceneFlowStateId.Gameplay, groupedNames);

            for (int i = 0; i < groupedNames.Count; i++)
            {
                AddSceneByName(groupedNames[i], scenes, seenScenes);
            }

            return scenes;
        }

        private static void AddSceneByName(
            string sceneName,
            List<SceneAsset> scenes,
            HashSet<SceneAsset> seenScenes)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                return;
            }

            string[] sceneGuids = AssetDatabase.FindAssets("t:Scene", new[] { k_scenesRoot });

            for (int i = 0; i < sceneGuids.Length; i++)
            {
                string scenePath = AssetDatabase.GUIDToAssetPath(sceneGuids[i]);

                if (string.Equals(
                        System.IO.Path.GetFileNameWithoutExtension(scenePath),
                        sceneName,
                        StringComparison.Ordinal) == false)
                {
                    continue;
                }

                SceneAsset sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);

                if (sceneAsset != null && seenScenes.Add(sceneAsset))
                {
                    scenes.Add(sceneAsset);
                }

                return;
            }
        }

        private static bool EnsureTankArenaSceneScaffolding(Scene scene, OnitySceneFlowProfile profile)
        {
            bool modified = false;
            EnsureCameraAndLight(scene, ref modified);

            if (profile.TryGetStateId(scene.name, out OnitySceneFlowStateId stateId) == false)
            {
                return modified;
            }

            switch (stateId)
            {
                case OnitySceneFlowStateId.Bootstrap:
                    TankArenaBootstrapSceneController bootstrapController =
                        EnsureComponentOnMainCamera<TankArenaBootstrapSceneController>(scene, ref modified);
                    AssignSceneFlowProfile(bootstrapController, profile, ref modified);
                    break;

                case OnitySceneFlowStateId.Loading:
                    EnsureSceneContext(scene, ref modified);
                    TankArenaLoadingSceneController loadingController =
                        EnsureComponentOnRoot<TankArenaLoadingSceneController>(scene, "LoadingFlow", ref modified);
                    AssignSceneFlowProfile(loadingController, profile, ref modified);
                    break;

                case OnitySceneFlowStateId.MainMenuHub:
                    EnsureSceneContext(scene, ref modified);
                    TankArenaMainMenuSceneController mainMenuController =
                        EnsureComponentOnRoot<TankArenaMainMenuSceneController>(scene, "MainMenuFlow", ref modified);
                    AssignSceneFlowProfile(mainMenuController, profile, ref modified);
                    break;

                case OnitySceneFlowStateId.Gameplay:
                    EnsureSceneContext(scene, ref modified);
                    TankArenaGameSceneController gameSceneController =
                        EnsureComponentOnRoot<TankArenaGameSceneController>(scene, "GameFlow", ref modified);
                    AssignSceneFlowProfile(gameSceneController, profile, ref modified);
                    break;
            }

            return modified;
        }

        private static void EnsureSceneContext(Scene scene, ref bool modified)
        {
            if (FindFirstComponentInScene<SceneContext>(scene) != null)
            {
                return;
            }

            GameObject contextRoot = EnsureRootGameObject(scene, "OnitySceneContext");
            contextRoot.AddComponent<SceneContext>();
            modified = true;
        }

        private static void EnsureCameraAndLight(Scene scene, ref bool modified)
        {
            Camera camera = FindFirstComponentInScene<Camera>(scene);

            if (camera == null)
            {
                GameObject cameraRoot = EnsureRootGameObject(scene, "Main Camera");
                camera = cameraRoot.GetComponent<Camera>();

                if (camera == null)
                {
                    camera = cameraRoot.AddComponent<Camera>();
                }

                cameraRoot.tag = "MainCamera";
                cameraRoot.transform.position = new Vector3(0f, 24f, 0f);
                cameraRoot.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.06f, 0.1f, 0.16f, 1f);
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 200f;
                camera.orthographic = true;
                camera.orthographicSize = 11f;
                modified = true;
            }

            if (FindFirstComponentInScene<Light>(scene) != null)
            {
                return;
            }

            GameObject lightRoot = EnsureRootGameObject(scene, "Directional Light");
            Light light = lightRoot.GetComponent<Light>();

            if (light == null)
            {
                light = lightRoot.AddComponent<Light>();
            }

            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.color = new Color(1f, 0.97f, 0.92f, 1f);
            lightRoot.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            modified = true;
        }

        private static TComponent EnsureComponentOnMainCamera<TComponent>(Scene scene, ref bool modified)
            where TComponent : Component
        {
            TComponent component = FindFirstComponentInScene<TComponent>(scene);

            if (component != null)
            {
                return component;
            }

            Camera camera = FindFirstComponentInScene<Camera>(scene);

            if (camera == null)
            {
                GameObject cameraRoot = EnsureRootGameObject(scene, "Main Camera");
                camera = cameraRoot.AddComponent<Camera>();
                modified = true;
            }

            component = camera.gameObject.AddComponent<TComponent>();
            modified = true;
            return component;
        }

        private static TComponent EnsureComponentOnRoot<TComponent>(Scene scene, string rootName, ref bool modified)
            where TComponent : Component
        {
            TComponent component = FindFirstComponentInScene<TComponent>(scene);

            if (component != null)
            {
                return component;
            }

            GameObject root = EnsureRootGameObject(scene, rootName);
            component = root.AddComponent<TComponent>();
            modified = true;
            return component;
        }

        private static void AssignSceneFlowProfile(Component component, OnitySceneFlowProfile profile, ref bool modified)
        {
            if (component == null)
            {
                return;
            }

            SerializedObject serializedObject = new SerializedObject(component);
            SerializedProperty property = serializedObject.FindProperty("m_sceneFlowProfile");

            if (property == null || property.objectReferenceValue == profile)
            {
                return;
            }

            property.objectReferenceValue = profile;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            modified = true;
        }

        private static TComponent FindFirstComponentInScene<TComponent>(Scene scene)
            where TComponent : Component
        {
            GameObject[] rootGameObjects = scene.GetRootGameObjects();

            for (int i = 0; i < rootGameObjects.Length; i++)
            {
                TComponent component = rootGameObjects[i].GetComponentInChildren<TComponent>(true);

                if (component != null)
                {
                    return component;
                }
            }

            return null;
        }

        private static GameObject EnsureRootGameObject(Scene scene, string objectName)
        {
            GameObject[] rootGameObjects = scene.GetRootGameObjects();

            for (int i = 0; i < rootGameObjects.Length; i++)
            {
                if (string.Equals(rootGameObjects[i].name, objectName, StringComparison.Ordinal))
                {
                    return rootGameObjects[i];
                }
            }

            GameObject root = new GameObject(objectName);
            SceneManager.MoveGameObjectToScene(root, scene);
            return root;
        }
    }
}
