using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Onity.Unity.Contexts;
using Onity.Unity.SceneFlow;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Onity.Editor.SceneFlow
{
    /// <summary>
    /// Editor window for managing grouped scene-flow profiles and build-scene ordering.
    /// </summary>
    public sealed class OnitySceneFlowManagerWindow : EditorWindow
    {
        private const string k_windowTitle = "Onity Scene Flow";
        private const string k_lastProfilePathEditorPrefKey = "Onity.SceneFlow.LastProfilePath";

        private readonly List<SceneAsset> m_mainMenuScenes = new List<SceneAsset>();
        private readonly List<SceneAsset> m_gameplayScenes = new List<SceneAsset>();

        private OnitySceneFlowProfile m_profile;
        private bool m_routeViaLoadingScene = true;
        private SceneAsset m_bootstrapScene;
        private SceneAsset m_loadingScene;
        private int m_defaultMainMenuSceneIndex = -1;
        private int m_defaultGameplaySceneIndex = -1;

        [MenuItem("Window/Onity/Scene Flow Manager")]
        [MenuItem("Onity/Tools/Scene Flow Manager", false, 122)]
        private static void OpenWindow()
        {
            OnitySceneFlowManagerWindow window = GetWindow<OnitySceneFlowManagerWindow>();
            window.titleContent = new GUIContent(k_windowTitle);
            window.minSize = new Vector2(640f, 420f);
            window.Show();
        }

        private void OnEnable()
        {
            LoadLastProfile();
            LoadProfileIntoWindow();
        }

        private void OnGUI()
        {
            DrawHeader();
            EditorGUILayout.Space(4f);
            DrawProfileSection();
            EditorGUILayout.Space(8f);
            DrawFlowSection();
            EditorGUILayout.Space(10f);
            DrawActionSection();
        }

        private static void DrawHeader()
        {
            EditorGUILayout.LabelField("Onity Scene Flow Manager", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Configure optional Bootstrap/Loading scenes plus grouped Menu and Level scenes. "
                + "Bootstrap and Loading are singletons; Menu and Level groups can contain as many scenes as you want.",
                MessageType.Info);
        }

        private void DrawProfileSection()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Profile", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            OnitySceneFlowProfile nextProfile =
                (OnitySceneFlowProfile)EditorGUILayout.ObjectField(
                    "Scene Flow Profile",
                    m_profile,
                    typeof(OnitySceneFlowProfile),
                    false);

            if (EditorGUI.EndChangeCheck())
            {
                m_profile = nextProfile;
                SaveLastProfilePath();
                LoadProfileIntoWindow();
            }

            EditorGUILayout.HelpBox(
                "Select an existing Scene Flow Profile asset here. Load and Save below read/write this asset.",
                MessageType.None);
            EditorGUILayout.EndVertical();
        }

        private void DrawFlowSection()
        {
            if (m_profile == null)
            {
                EditorGUILayout.HelpBox(
                    "Select an OnitySceneFlowProfile asset to configure grouped scenes.",
                    MessageType.Warning);
                return;
            }

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Flow Configuration", EditorStyles.boldLabel);

            m_routeViaLoadingScene =
                EditorGUILayout.Toggle("Route Via Loading Scene", m_routeViaLoadingScene);

            EditorGUILayout.Space(4f);
            m_bootstrapScene = DrawSceneField("Bootstrap Scene (Optional)", m_bootstrapScene);
            m_loadingScene = DrawSceneField("Loading Scene (Optional)", m_loadingScene);

            EditorGUILayout.Space(8f);
            DrawGroupedSceneList(
                "Menu Scenes",
                "Add Menu Scene",
                m_mainMenuScenes,
                ref m_defaultMainMenuSceneIndex);

            EditorGUILayout.Space(8f);
            DrawGroupedSceneList(
                "Level Scenes",
                "Add Level Scene",
                m_gameplayScenes,
                ref m_defaultGameplaySceneIndex);

            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Load Profile"))
            {
                LoadProfileIntoWindow();
            }

            if (GUILayout.Button("Save Profile"))
            {
                SaveWindowToProfile();
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox(
                "Profile only stores and restores the window values. Edit here, then save or load when needed.",
                MessageType.None);
            EditorGUILayout.EndVertical();
        }

        private void DrawActionSection()
        {
            if (m_profile == null)
            {
                return;
            }

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Use one Apply button after editing the window. It saves the current window state to the profile, updates Build Settings, sets the Play Mode start scene, and adds missing basic scene-flow scaffolding when supported.",
                MessageType.None);

            if (GUILayout.Button("Apply Scene Flow"))
            {
                ApplySceneFlow();
            }

            EditorGUILayout.EndVertical();
        }

        private static SceneAsset DrawSceneField(string label, SceneAsset current)
        {
            return (SceneAsset)EditorGUILayout.ObjectField(
                label,
                current,
                typeof(SceneAsset),
                false);
        }

        private static void DrawGroupedSceneList(
            string label,
            string addButtonLabel,
            List<SceneAsset> scenes,
            ref int defaultIndex)
        {
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Order is preserved in Build Settings. Mark one scene as default for direct transitions to this group.",
                MessageType.None);

            for (int i = 0; i < scenes.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                scenes[i] = DrawSceneField($"Scene {i + 1}", scenes[i]);

                bool isDefault = i == defaultIndex;
                bool nextIsDefault = GUILayout.Toggle(isDefault, "Default", GUILayout.Width(70f));

                if (nextIsDefault && isDefault == false)
                {
                    defaultIndex = i;
                }

                using (new EditorGUI.DisabledScope(i <= 0))
                {
                    if (GUILayout.Button("Up", GUILayout.Width(42f)))
                    {
                        Swap(scenes, i, i - 1);
                        RemapDefaultIndexAfterSwap(ref defaultIndex, i, i - 1);
                    }
                }

                using (new EditorGUI.DisabledScope(i >= scenes.Count - 1))
                {
                    if (GUILayout.Button("Down", GUILayout.Width(52f)))
                    {
                        Swap(scenes, i, i + 1);
                        RemapDefaultIndexAfterSwap(ref defaultIndex, i, i + 1);
                    }
                }

                if (GUILayout.Button("Remove", GUILayout.Width(68f)))
                {
                    scenes.RemoveAt(i);
                    RemapDefaultIndexAfterRemove(ref defaultIndex, i, scenes.Count);
                    EditorGUILayout.EndHorizontal();
                    break;
                }

                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button(addButtonLabel))
            {
                scenes.Add(null);

                if (defaultIndex < 0)
                {
                    defaultIndex = scenes.Count - 1;
                }
            }

            if (scenes.Count <= 0)
            {
                defaultIndex = -1;
            }
            else if (defaultIndex >= scenes.Count)
            {
                defaultIndex = scenes.Count - 1;
            }
        }

        private void LoadProfileIntoWindow()
        {
            m_mainMenuScenes.Clear();
            m_gameplayScenes.Clear();

            if (m_profile == null)
            {
                m_routeViaLoadingScene = true;
                m_bootstrapScene = null;
                m_loadingScene = null;
                m_defaultMainMenuSceneIndex = -1;
                m_defaultGameplaySceneIndex = -1;
                return;
            }

            m_routeViaLoadingScene = m_profile.RouteTransitionsThroughLoadingScene;
            m_bootstrapScene = ResolveSceneAsset(OnitySceneFlowStateId.Bootstrap);
            m_loadingScene = ResolveSceneAsset(OnitySceneFlowStateId.Loading);

            SyncGroupedSceneAssets(
                OnitySceneFlowStateId.MainMenuHub,
                m_mainMenuScenes,
                out m_defaultMainMenuSceneIndex);
            SyncGroupedSceneAssets(
                OnitySceneFlowStateId.Gameplay,
                m_gameplayScenes,
                out m_defaultGameplaySceneIndex);
        }

        private SceneAsset ResolveSceneAsset(OnitySceneFlowStateId stateId)
        {
            if (m_profile.TryGetSceneName(stateId, out string sceneName) == false)
            {
                return null;
            }

            return FindSceneAssetByName(sceneName);
        }

        private void SyncGroupedSceneAssets(
            OnitySceneFlowStateId stateId,
            List<SceneAsset> targetScenes,
            out int defaultSceneIndex)
        {
            List<string> sceneNames = new List<string>(8);
            m_profile.CopySceneNames(stateId, sceneNames);

            for (int i = 0; i < sceneNames.Count; i++)
            {
                targetScenes.Add(FindSceneAssetByName(sceneNames[i]));
            }

            defaultSceneIndex = ResolveDefaultSceneIndex(
                targetScenes,
                m_profile.GetSceneName(stateId));
        }

        private static int ResolveDefaultSceneIndex(List<SceneAsset> scenes, string defaultSceneName)
        {
            if (string.IsNullOrWhiteSpace(defaultSceneName))
            {
                return scenes.Count > 0 ? 0 : -1;
            }

            for (int i = 0; i < scenes.Count; i++)
            {
                SceneAsset scene = scenes[i];

                if (scene != null && string.Equals(scene.name, defaultSceneName))
                {
                    return i;
                }
            }

            return scenes.Count > 0 ? 0 : -1;
        }

        private static SceneAsset FindSceneAssetByName(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                return null;
            }

            string[] sceneGuids = AssetDatabase.FindAssets("t:Scene");

            for (int i = 0; i < sceneGuids.Length; i++)
            {
                string scenePath = AssetDatabase.GUIDToAssetPath(sceneGuids[i]);
                string fileName = Path.GetFileNameWithoutExtension(scenePath);

                if (string.Equals(fileName, sceneName, System.StringComparison.Ordinal) == false)
                {
                    continue;
                }

                return AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
            }

            return null;
        }

        private void SaveWindowToProfile()
        {
            if (m_profile == null)
            {
                return;
            }

            m_profile.SetRouteTransitionsThroughLoadingScene(m_routeViaLoadingScene);
            m_profile.SetSceneName(OnitySceneFlowStateId.Bootstrap, GetSceneName(m_bootstrapScene));
            m_profile.SetSceneName(OnitySceneFlowStateId.Loading, GetSceneName(m_loadingScene));

            SaveGroupedScenes(
                OnitySceneFlowStateId.MainMenuHub,
                m_mainMenuScenes,
                m_defaultMainMenuSceneIndex);
            SaveGroupedScenes(
                OnitySceneFlowStateId.Gameplay,
                m_gameplayScenes,
                m_defaultGameplaySceneIndex);

            EditorUtility.SetDirty(m_profile);
            AssetDatabase.SaveAssets();
        }

        private void SaveGroupedScenes(
            OnitySceneFlowStateId stateId,
            List<SceneAsset> scenes,
            int defaultSceneIndex)
        {
            List<string> sceneNames = new List<string>(scenes.Count);
            string defaultSceneName = string.Empty;
            HashSet<string> seenNames = new HashSet<string>(System.StringComparer.Ordinal);

            for (int i = 0; i < scenes.Count; i++)
            {
                SceneAsset scene = scenes[i];

                if (scene == null || seenNames.Add(scene.name) == false)
                {
                    continue;
                }

                sceneNames.Add(scene.name);

                if (i == defaultSceneIndex)
                {
                    defaultSceneName = scene.name;
                }
            }

            m_profile.SetSceneNames(stateId, sceneNames);
            m_profile.SetDefaultSceneName(stateId, defaultSceneName);
        }

        private static string GetSceneName(SceneAsset sceneAsset)
        {
            return sceneAsset != null ? sceneAsset.name : string.Empty;
        }

        private void ApplySceneFlowToBuildSettings()
        {
            List<EditorBuildSettingsScene> buildScenes = BuildSettingsSceneList();
            EditorBuildSettings.scenes = buildScenes.ToArray();
            Debug.Log($"Applied {buildScenes.Count} grouped scene(s) from Onity scene-flow profile.");
        }

        private void ApplySceneFlow()
        {
            SaveWindowToProfile();
            EnsureProfileSceneSupport();
            ApplySceneFlowToBuildSettings();
            SetPlayModeStartScene();
        }

        private void EnsureProfileSceneSupport()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo() == false)
            {
                return;
            }

            string returnScenePath = SceneManager.GetActiveScene().path;

            try
            {
                EnsureSceneContextsOnProfileScenes();
                InvokeOptionalSceneApplyHooks();
            }
            finally
            {
                if (string.IsNullOrWhiteSpace(returnScenePath) == false
                    && AssetDatabase.LoadAssetAtPath<SceneAsset>(returnScenePath) != null)
                {
                    EditorSceneManager.OpenScene(returnScenePath, OpenSceneMode.Single);
                }
            }
        }

        private List<EditorBuildSettingsScene> BuildSettingsSceneList()
        {
            List<EditorBuildSettingsScene> buildScenes = new List<EditorBuildSettingsScene>(16);
            HashSet<string> seenPaths = new HashSet<string>();

            AddBuildScene(m_bootstrapScene, buildScenes, seenPaths);
            AddBuildScene(m_loadingScene, buildScenes, seenPaths);
            AddBuildScenes(m_mainMenuScenes, buildScenes, seenPaths);
            AddBuildScenes(m_gameplayScenes, buildScenes, seenPaths);

            return buildScenes;
        }

        private void EnsureSceneContextsOnProfileScenes()
        {
            List<SceneAsset> sceneAssets = CollectProfileSceneAssets();

            for (int i = 0; i < sceneAssets.Count; i++)
            {
                SceneAsset sceneAsset = sceneAssets[i];

                if (sceneAsset == null)
                {
                    continue;
                }

                string scenePath = AssetDatabase.GetAssetPath(sceneAsset);

                if (string.IsNullOrWhiteSpace(scenePath))
                {
                    continue;
                }

                Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

                if (m_profile.TryGetStateId(scene.name, out OnitySceneFlowStateId stateId)
                    && stateId == OnitySceneFlowStateId.Bootstrap)
                {
                    continue;
                }

                if (FindFirstComponentInScene<SceneContext>(scene) != null)
                {
                    continue;
                }

                GameObject contextRoot = EnsureRootGameObject(scene, "OnitySceneContext");
                contextRoot.AddComponent<SceneContext>();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
        }

        private void InvokeOptionalSceneApplyHooks()
        {
            InvokeSceneApplyHook("Onity.Editor.Samples.TankArenaSceneFlowApplyHooks, Onity.Samples.Editor");
        }

        private void InvokeSceneApplyHook(string assemblyQualifiedTypeName)
        {
            Type hookType = Type.GetType(assemblyQualifiedTypeName, false);

            if (hookType == null)
            {
                return;
            }

            MethodInfo method = hookType.GetMethod(
                "TryApplyProfile",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(OnitySceneFlowProfile) },
                null);

            if (method == null)
            {
                return;
            }

            try
            {
                method.Invoke(null, new object[] { m_profile });
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                Debug.LogException(exception.InnerException);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private List<SceneAsset> CollectProfileSceneAssets()
        {
            List<SceneAsset> sceneAssets = new List<SceneAsset>(16);
            HashSet<SceneAsset> seenScenes = new HashSet<SceneAsset>();

            AddUniqueSceneAsset(m_bootstrapScene, sceneAssets, seenScenes);
            AddUniqueSceneAsset(m_loadingScene, sceneAssets, seenScenes);

            for (int i = 0; i < m_mainMenuScenes.Count; i++)
            {
                AddUniqueSceneAsset(m_mainMenuScenes[i], sceneAssets, seenScenes);
            }

            for (int i = 0; i < m_gameplayScenes.Count; i++)
            {
                AddUniqueSceneAsset(m_gameplayScenes[i], sceneAssets, seenScenes);
            }

            return sceneAssets;
        }

        private static void AddBuildScenes(
            List<SceneAsset> scenes,
            List<EditorBuildSettingsScene> buildScenes,
            HashSet<string> seenPaths)
        {
            for (int i = 0; i < scenes.Count; i++)
            {
                AddBuildScene(scenes[i], buildScenes, seenPaths);
            }
        }

        private static void AddUniqueSceneAsset(
            SceneAsset sceneAsset,
            List<SceneAsset> sceneAssets,
            HashSet<SceneAsset> seenScenes)
        {
            if (sceneAsset == null || seenScenes.Add(sceneAsset) == false)
            {
                return;
            }

            sceneAssets.Add(sceneAsset);
        }

        private static void AddBuildScene(
            SceneAsset sceneAsset,
            List<EditorBuildSettingsScene> buildScenes,
            HashSet<string> seenPaths)
        {
            if (sceneAsset == null)
            {
                return;
            }

            string scenePath = AssetDatabase.GetAssetPath(sceneAsset);

            if (string.IsNullOrWhiteSpace(scenePath) || seenPaths.Contains(scenePath))
            {
                return;
            }

            seenPaths.Add(scenePath);
            buildScenes.Add(new EditorBuildSettingsScene(scenePath, true));
        }

        private void SetPlayModeStartScene()
        {
            SceneAsset startupScene = ResolveStartupSceneAsset();

            if (startupScene == null)
            {
                Debug.LogWarning(
                    "Cannot set play mode start scene. Assign Bootstrap or at least one default Menu/Level scene.");
                return;
            }

            EditorSceneManager.playModeStartScene = startupScene;
            Debug.Log($"Play Mode start scene set to '{startupScene.name}'.");
        }

        private void OpenStartupScene()
        {
            SceneAsset startupScene = ResolveStartupSceneAsset();

            if (startupScene == null)
            {
                return;
            }

            string scenePath = AssetDatabase.GetAssetPath(startupScene);
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        }

        private SceneAsset ResolveStartupSceneAsset()
        {
            if (m_bootstrapScene != null)
            {
                return m_bootstrapScene;
            }

            SceneAsset groupedStartup = ResolveGroupedDefaultScene(m_mainMenuScenes, m_defaultMainMenuSceneIndex);

            if (groupedStartup != null)
            {
                return groupedStartup;
            }

            groupedStartup = ResolveGroupedDefaultScene(m_gameplayScenes, m_defaultGameplaySceneIndex);

            if (groupedStartup != null)
            {
                return groupedStartup;
            }

            return m_loadingScene;
        }

        private static SceneAsset ResolveGroupedDefaultScene(List<SceneAsset> scenes, int defaultIndex)
        {
            if (defaultIndex >= 0 && defaultIndex < scenes.Count && scenes[defaultIndex] != null)
            {
                return scenes[defaultIndex];
            }

            for (int i = 0; i < scenes.Count; i++)
            {
                if (scenes[i] != null)
                {
                    return scenes[i];
                }
            }

            return null;
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

        private void LoadLastProfile()
        {
            string profilePath = EditorPrefs.GetString(k_lastProfilePathEditorPrefKey, string.Empty);

            if (string.IsNullOrWhiteSpace(profilePath))
            {
                return;
            }

            m_profile = AssetDatabase.LoadAssetAtPath<OnitySceneFlowProfile>(profilePath);
        }

        private void SaveLastProfilePath()
        {
            if (m_profile == null)
            {
                EditorPrefs.DeleteKey(k_lastProfilePathEditorPrefKey);
                return;
            }

            string profilePath = AssetDatabase.GetAssetPath(m_profile);
            EditorPrefs.SetString(k_lastProfilePathEditorPrefKey, profilePath);
        }

        private static void Swap(List<SceneAsset> scenes, int firstIndex, int secondIndex)
        {
            SceneAsset temporary = scenes[firstIndex];
            scenes[firstIndex] = scenes[secondIndex];
            scenes[secondIndex] = temporary;
        }

        private static void RemapDefaultIndexAfterSwap(ref int defaultIndex, int firstIndex, int secondIndex)
        {
            if (defaultIndex == firstIndex)
            {
                defaultIndex = secondIndex;
                return;
            }

            if (defaultIndex == secondIndex)
            {
                defaultIndex = firstIndex;
            }
        }

        private static void RemapDefaultIndexAfterRemove(ref int defaultIndex, int removedIndex, int newCount)
        {
            if (newCount <= 0)
            {
                defaultIndex = -1;
                return;
            }

            if (defaultIndex == removedIndex)
            {
                defaultIndex = Mathf.Clamp(removedIndex, 0, newCount - 1);
                return;
            }

            if (defaultIndex > removedIndex)
            {
                defaultIndex--;
            }
        }
    }
}
