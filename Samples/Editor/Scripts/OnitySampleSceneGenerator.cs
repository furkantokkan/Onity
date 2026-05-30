using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Onity.DI;
using Onity.Messaging;
using Onity.Samples.BasicGameplay;
using Onity.Samples.GameObjectContextScope;
using Onity.Unity.Contexts;
using Onity.Unity.Installers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Onity.Editor.Samples
{
    /// <summary>
    /// Generates Onity sample scenes and required assets.
    /// </summary>
    public static class OnitySampleSceneGenerator
    {
        private const string k_panelSettingsPath = "Assets/Onity-Packages/Onity/Samples/Common/UI/OnityPanelSettings.asset";
        private const string k_projectilePrefabPath = "Assets/Onity-Packages/Onity/Samples/BasicGameplay/Prefabs/OnitySampleProjectile.prefab";
        private const string k_rollABallPickupPrefabPath = "Assets/Onity-Packages/Onity/Samples/RollABall/Prefabs/OnityRollABallPickup.prefab";
        private const string k_rollABallSettingsPath = "Assets/Onity-Packages/Onity/Samples/RollABall/Data/OnityRollABallGameSettings.asset";
        private const string k_rollABallArenaMaterialPath = "Assets/Onity-Packages/Onity/Samples/RollABall/Materials/OnityRollABallArena.mat";
        private const string k_rollABallPlayerMaterialPath = "Assets/Onity-Packages/Onity/Samples/RollABall/Materials/OnityRollABallPlayer.mat";
        private const string k_rollABallPickupMaterialPath = "Assets/Onity-Packages/Onity/Samples/RollABall/Materials/OnityRollABallPickup.mat";
        private const string k_rollABallTrailMaterialPath = "Assets/Onity-Packages/Onity/Samples/RollABall/Materials/OnityRollABallTrail.mat";
        private const string k_playerLayerName = "Player";
        private const string k_environmentLayerName = "Environment";
        private const string k_pickupLayerName = "AttractableItem";
        private const string k_basicHudPath = "Assets/Onity-Packages/Onity/Samples/BasicGameplay/UI/OnityBasicHud.uxml";
        private const string k_rollABallHudPath = "Assets/Onity-Packages/Onity/Samples/RollABall/UI/OnityRollABallHud.uxml";
        private const string k_scopeHudPath = "Assets/Onity-Packages/Onity/Samples/GameObjectContextScope/UI/OnityScopeCounter.uxml";
        private const string k_basicScenePath = "Assets/Onity-Packages/Onity/Samples/BasicGameplay/Scenes/OnityBasicGameplaySample.unity";
        private const string k_rollABallBootstrapScenePath = "Assets/Onity-Packages/Onity/Samples/RollABall/Scenes/BoostrapScene.unity";
        private const string k_rollABallScenePath = "Assets/Onity-Packages/Onity/Samples/RollABall/Scenes/Game.unity";
        private const string k_scopeScenePath = "Assets/Onity-Packages/Onity/Samples/GameObjectContextScope/Scenes/OnityGameObjectScopeSample.unity";

        [MenuItem("Onity/Samples/Setup Samples")]
        private static void SetupSamplesMenu()
        {
            GenerateAllSampleScenes();
        }

        /// <summary>
        /// Generates all sample scenes and assets.
        /// </summary>
        public static void GenerateAllSampleScenes()
        {
            List<string> failures = new List<string>();

            RunSampleGenerationStep("Basic Gameplay", GenerateBasicGameplayScene, failures);
            RunSampleGenerationStep("GameObject Context Scope", GenerateGameObjectScopeScene, failures);
            RunSampleGenerationStep("Roll-a-Ball", GenerateRollABallScene, failures);
            RunSampleGenerationStep("Tank Arena 2D", TankArenaSceneFlowBuildSettingsMenu.GenerateAndArrangeSceneFlow, failures);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (failures.Count > 0)
            {
                throw new InvalidOperationException(
                    "Sample setup completed with failures: " + string.Join(" | ", failures));
            }

            UnityEngine.Debug.Log("Onity sample scenes generated and configured.");
        }

        /// <summary>
        /// Command-line entry point for Unity batchmode scene generation.
        /// </summary>
        public static void GenerateAllSampleScenesFromCommandLine()
        {
            GenerateAllSampleScenes();
            UnityEngine.Debug.Log("Onity sample scene generation completed in batchmode.");
        }

        private static void RunSampleGenerationStep(
            string sampleName,
            Action generationStep,
            List<string> failures)
        {
            try
            {
                generationStep();
                UnityEngine.Debug.Log($"Configured sample '{sampleName}'.");
            }
            catch (Exception exception)
            {
                failures.Add(sampleName + ": " + exception.Message);
                UnityEngine.Debug.LogException(exception);
            }
        }

        /// <summary>
        /// Command-line entry point for Unity batchmode Roll-a-Ball scene generation.
        /// </summary>
        public static void GenerateRollABallSceneFromCommandLine()
        {
            GenerateRollABallScene();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            UnityEngine.Debug.Log("Onity Roll-a-Ball scene generation completed in batchmode.");
        }

        private static void GenerateBasicGameplayScene()
        {
            EnsureFolderForFile(k_basicScenePath);

            PanelSettings panelSettings = EnsurePanelSettings();
            VisualTreeAsset hudAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_basicHudPath);

            if (hudAsset == null)
            {
                throw new FileNotFoundException($"Could not load HUD UXML at path '{k_basicHudPath}'.");
            }

            SampleProjectile projectilePrefab = EnsureProjectilePrefab();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateDefaultCameraAndLight();

            GameObject contextRoot = new GameObject("OnitySceneContext");
            SceneContext sceneContext = contextRoot.AddComponent<SceneContext>();
            SampleMessagingInstaller messagingInstaller = contextRoot.AddComponent<SampleMessagingInstaller>();
            SampleProjectileInstaller projectileInstaller = contextRoot.AddComponent<SampleProjectileInstaller>();

            GameObject poolRoot = new GameObject("ProjectilePoolRoot");
            poolRoot.transform.SetParent(contextRoot.transform, false);

            SetSerializedObjectReference(projectileInstaller, "m_projectilePrefab", projectilePrefab);
            SetSerializedObjectReference(projectileInstaller, "m_poolRoot", poolRoot.transform);
            SetContextInstallers(sceneContext, new MonoInstaller[] { messagingInstaller, projectileInstaller });

            GameObject gameplayRoot = new GameObject("Gameplay");
            gameplayRoot.transform.SetParent(contextRoot.transform, false);
            SampleDamageEmitter damageEmitter = gameplayRoot.AddComponent<SampleDamageEmitter>();
            SampleProjectileSpawner projectileSpawner = gameplayRoot.AddComponent<SampleProjectileSpawner>();

            GameObject spawnPoint = new GameObject("ProjectileSpawnPoint");
            spawnPoint.transform.SetParent(gameplayRoot.transform, false);
            spawnPoint.transform.position = new Vector3(0f, 1f, -5f);
            spawnPoint.transform.forward = Vector3.forward;
            SetSerializedObjectReference(projectileSpawner, "m_spawnPoint", spawnPoint.transform);

            GameObject hudRoot = new GameObject("HUD");
            hudRoot.transform.SetParent(contextRoot.transform, false);
            UIDocument document = hudRoot.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.visualTreeAsset = hudAsset;
            SetSerializedObjectReference(document, "m_PanelSettings", panelSettings);
            SetSerializedObjectReference(document, "sourceAsset", hudAsset);

            SampleHudController hudController = hudRoot.AddComponent<SampleHudController>();
            SetSerializedObjectReference(hudController, "m_document", document);
            SetSerializedObjectReference(hudController, "m_damageEmitter", damageEmitter);
            SetSerializedObjectReference(hudController, "m_projectileSpawner", projectileSpawner);

            SaveScene(scene, k_basicScenePath);
        }

        private static void GenerateGameObjectScopeScene()
        {
            EnsureFolderForFile(k_scopeScenePath);

            PanelSettings panelSettings = EnsurePanelSettings();
            VisualTreeAsset scopeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_scopeHudPath);

            if (scopeAsset == null)
            {
                throw new FileNotFoundException($"Could not load HUD UXML at path '{k_scopeHudPath}'.");
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateDefaultCameraAndLight();

            GameObject sceneContextRoot = new GameObject("OnitySceneContext");
            SceneContext sceneContext = sceneContextRoot.AddComponent<SceneContext>();
            SetContextInstallers(sceneContext, new MonoInstaller[0]);

            CreateScopeCard(
                sceneContextRoot.transform,
                panelSettings,
                scopeAsset,
                "Scope-A",
                "Scope A",
                new Vector3(-2.5f, 0f, 0f));

            CreateScopeCard(
                sceneContextRoot.transform,
                panelSettings,
                scopeAsset,
                "Scope-B",
                "Scope B",
                new Vector3(2.5f, 0f, 0f));

            SaveScene(scene, k_scopeScenePath);
        }

        private static void GenerateRollABallScene()
        {
            EnsureFolderForFile(k_rollABallBootstrapScenePath);
            EnsureFolderForFile(k_rollABallScenePath);

            PanelSettings panelSettings = EnsurePanelSettings();
            VisualTreeAsset hudAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_rollABallHudPath);

            if (hudAsset == null)
            {
                throw new FileNotFoundException($"Could not load HUD UXML at path '{k_rollABallHudPath}'.");
            }

            ScriptableObject settingsAsset = EnsureRollABallSettingsAsset();
            Material arenaMaterial = EnsureRollABallMaterial(
                k_rollABallArenaMaterialPath,
                "OnityRollABallArena",
                new Color(0.18f, 0.22f, 0.28f, 1f));
            Material playerMaterial = EnsureRollABallMaterial(
                k_rollABallPlayerMaterialPath,
                "OnityRollABallPlayer",
                new Color(0.22f, 0.72f, 1f, 1f));
            Material pickupMaterial = EnsureRollABallMaterial(
                k_rollABallPickupMaterialPath,
                "OnityRollABallPickup",
                new Color(1f, 0.85f, 0.28f, 1f));
            Material trailMaterial = EnsureRollABallTrailMaterial(
                k_rollABallTrailMaterialPath,
                "OnityRollABallTrail",
                new Color(0.23f, 0.8f, 1f, 0.95f));
            int playerLayer = ResolveLayer(k_playerLayerName);
            int environmentLayer = ResolveLayer(k_environmentLayerName);
            int pickupLayer = ResolveLayer(k_pickupLayerName);
            Component pickupPrefab = EnsureRollABallPickupPrefab(pickupMaterial, pickupLayer);
            int initialPickupCount = ReadSerializedInt(settingsAsset, "m_initialPickupCount", 12);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Camera rollABallCamera = CreateRollABallCameraAndLight();

            GameObject contextRoot = new GameObject("OnitySceneContext");
            SceneContext sceneContext = contextRoot.AddComponent<SceneContext>();
            MonoInstaller installer = AddRollABallComponent<MonoInstaller>(contextRoot, "RollABallInstaller");

            GameObject poolRoot = new GameObject("PickupPoolRoot");
            poolRoot.transform.SetParent(contextRoot.transform, false);

            SetSerializedObjectReference(installer, "m_settings", settingsAsset);
            SetSerializedObjectReference(installer, "m_pickupPrefab", pickupPrefab);
            SetSerializedObjectReference(installer, "m_poolRoot", poolRoot.transform);
            SetSerializedInt(installer, "m_defaultPoolCapacity", Mathf.Max(8, initialPickupCount));
            SetSerializedInt(installer, "m_maxPoolSize", Mathf.Max(32, initialPickupCount * 4));

            SetContextInstallers(sceneContext, new MonoInstaller[] { installer });

            GameObject gameplayRoot = new GameObject("Gameplay");
            gameplayRoot.transform.SetParent(contextRoot.transform, false);

            CreateRollABallArena(gameplayRoot.transform, settingsAsset, arenaMaterial, environmentLayer);

            GameObject playerRoot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            playerRoot.name = "Player";
            playerRoot.tag = "Player";
            playerRoot.layer = playerLayer;
            playerRoot.transform.SetParent(gameplayRoot.transform, false);
            playerRoot.transform.position = new Vector3(0f, 0.6f, 0f);
            SetRendererMaterial(playerRoot, playerMaterial);
            Rigidbody playerBody = playerRoot.AddComponent<Rigidbody>();
            playerBody.mass = 1f;
            playerBody.drag = 0.25f;
            playerBody.angularDrag = 0.05f;
            Component playerController = AddRollABallComponent<Component>(playerRoot, "RollABallPlayerController");
            SetSerializedObjectReference(playerController, "m_trailMaterial", trailMaterial);
            Component cameraFollow = AddRollABallComponent<Component>(rollABallCamera.gameObject, "RollABallCameraFollow");
            SetSerializedObjectReference(cameraFollow, "m_target", playerRoot.transform);
            SetSerializedBoolean(cameraFollow, "m_findTargetByTag", true);

            GameObject spawnerRoot = new GameObject("PickupSpawner");
            spawnerRoot.transform.SetParent(gameplayRoot.transform, false);
            Component pickupSpawner = AddRollABallComponent<Component>(spawnerRoot, "RollABallPickupSpawner");

            GameObject hudRoot = new GameObject("HUD");
            hudRoot.transform.SetParent(contextRoot.transform, false);
            UIDocument document = hudRoot.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.visualTreeAsset = hudAsset;
            SetSerializedObjectReference(document, "m_PanelSettings", panelSettings);
            SetSerializedObjectReference(document, "sourceAsset", hudAsset);

            Component hudController = AddRollABallComponent<Component>(hudRoot, "RollABallHudController");
            SetSerializedObjectReference(hudController, "m_document", document);
            SetSerializedObjectReference(hudController, "m_pickupSpawner", pickupSpawner);
            SetSerializedBoolean(hudController, "m_enableTextMeshProFallback", true);

            SaveScene(scene, k_rollABallScenePath);
            GenerateRollABallBootstrapScene();
        }

        private static void GenerateRollABallBootstrapScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject bootstrapRoot = new GameObject("BootstrapFlow");
            Component loader = AddRollABallComponent<Component>(bootstrapRoot, "RollABallBootstrapSceneLoader");
            SetSerializedString(loader, "m_gameSceneName", "Game");
            SetSerializedBoolean(loader, "m_autoLoadOnStart", true);

            SaveScene(scene, k_rollABallBootstrapScenePath);
        }

        private static void ArrangeRollABallBootstrapAndGameScenes()
        {
            SceneAsset bootstrapScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(k_rollABallBootstrapScenePath);

            if (bootstrapScene == null)
            {
                throw new FileNotFoundException(
                    $"Could not find Roll-a-Ball bootstrap scene at '{k_rollABallBootstrapScenePath}'.");
            }

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(k_rollABallBootstrapScenePath, true),
                new EditorBuildSettingsScene(k_rollABallScenePath, true)
            };
            EditorSceneManager.playModeStartScene = bootstrapScene;
            EditorSceneManager.OpenScene(k_rollABallBootstrapScenePath, OpenSceneMode.Single);
        }

        private static void ArrangeRollABallSingleScene()
        {
            SceneAsset gameScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(k_rollABallScenePath);

            if (gameScene == null)
            {
                throw new FileNotFoundException(
                    $"Could not find Roll-a-Ball game scene at '{k_rollABallScenePath}'.");
            }

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(k_rollABallScenePath, true)
            };
            EditorSceneManager.playModeStartScene = gameScene;
            EditorSceneManager.OpenScene(k_rollABallScenePath, OpenSceneMode.Single);
        }

        private static void CreateScopeCard(
            Transform parent,
            PanelSettings panelSettings,
            VisualTreeAsset visualTreeAsset,
            string objectName,
            string scopeTitle,
            Vector3 localPosition)
        {
            GameObject scopeRoot = new GameObject(objectName);
            scopeRoot.transform.SetParent(parent, false);
            scopeRoot.transform.localPosition = localPosition;

            GameObjectContext gameObjectContext = scopeRoot.AddComponent<GameObjectContext>();
            SampleGameObjectScopeInstaller installer = scopeRoot.AddComponent<SampleGameObjectScopeInstaller>();
            SetContextInstallers(gameObjectContext, new MonoInstaller[] { installer });

            UIDocument document = scopeRoot.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.visualTreeAsset = visualTreeAsset;
            SetSerializedObjectReference(document, "m_PanelSettings", panelSettings);
            SetSerializedObjectReference(document, "sourceAsset", visualTreeAsset);

            SampleScopeCounterPresenter presenter = scopeRoot.AddComponent<SampleScopeCounterPresenter>();
            SetSerializedObjectReference(presenter, "m_document", document);
            SetSerializedString(presenter, "m_scopeTitle", scopeTitle);
        }

        private static ScriptableObject EnsureRollABallSettingsAsset()
        {
            EnsureFolderForFile(k_rollABallSettingsPath);

            ScriptableObject settingsAsset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(k_rollABallSettingsPath);

            if (settingsAsset != null)
            {
                return settingsAsset;
            }

            Type settingsType = ResolveRollABallType("RollABallGameSettings");
            settingsAsset = ScriptableObject.CreateInstance(settingsType);

            if (settingsAsset == null)
            {
                throw new InvalidOperationException("Failed to create Roll-a-Ball settings ScriptableObject.");
            }

            AssetDatabase.CreateAsset(settingsAsset, k_rollABallSettingsPath);
            AssetDatabase.SaveAssets();
            return settingsAsset;
        }

        private static Component EnsureRollABallPickupPrefab(Material pickupMaterial, int pickupLayer)
        {
            EnsureFolderForFile(k_rollABallPickupPrefabPath);
            Type pickupType = ResolveRollABallType("RollABallPickup");
            GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(k_rollABallPickupPrefabPath);

            if (prefabAsset != null)
            {
                Component pickupPrefab = prefabAsset.GetComponent(pickupType);

                if (pickupPrefab == null)
                {
                    throw new MissingComponentException("Roll-a-Ball pickup prefab is missing RollABallPickup component.");
                }

                MeshRenderer meshRenderer = prefabAsset.GetComponent<MeshRenderer>();
                bool changed = false;

                if (meshRenderer != null && meshRenderer.sharedMaterial != pickupMaterial)
                {
                    meshRenderer.sharedMaterial = pickupMaterial;
                    EditorUtility.SetDirty(meshRenderer);
                    changed = true;
                }

                if (prefabAsset.layer != pickupLayer)
                {
                    prefabAsset.layer = pickupLayer;
                    EditorUtility.SetDirty(prefabAsset);
                    changed = true;
                }

                if (changed)
                {
                    PrefabUtility.SavePrefabAsset(prefabAsset);
                    AssetDatabase.SaveAssets();
                }

                return pickupPrefab;
            }

            GameObject tempRoot = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tempRoot.name = "OnityRollABallPickup";
            tempRoot.layer = pickupLayer;
            tempRoot.transform.localScale = Vector3.one * 0.65f;
            SetRendererMaterial(tempRoot, pickupMaterial);

            Collider collider = tempRoot.GetComponent<Collider>();

            if (collider != null)
            {
                collider.isTrigger = true;
            }

            tempRoot.AddComponent(pickupType);

            GameObject prefabRoot = PrefabUtility.SaveAsPrefabAsset(tempRoot, k_rollABallPickupPrefabPath);
            UnityEngine.Object.DestroyImmediate(tempRoot);

            Component createdPickup = prefabRoot.GetComponent(pickupType);

            if (createdPickup == null)
            {
                throw new MissingComponentException("Roll-a-Ball pickup prefab is missing RollABallPickup component.");
            }

            return createdPickup;
        }

        private static Material EnsureRollABallMaterial(string materialPath, string materialName, Color baseColor)
        {
            EnsureFolderForFile(materialPath);

            Shader shader = ResolveRollABallShader();
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);

            if (material == null)
            {
                material = new Material(shader)
                {
                    name = materialName
                };

                ApplyRollABallMaterialDefaults(material, baseColor);
                AssetDatabase.CreateAsset(material, materialPath);
                AssetDatabase.SaveAssets();
                return material;
            }

            bool changed = false;

            if (material.shader != shader)
            {
                material.shader = shader;
                changed = true;
            }

            if (ApplyRollABallMaterialDefaults(material, baseColor))
            {
                changed = true;
            }

            if (changed)
            {
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssets();
            }

            return material;
        }

        private static Shader ResolveRollABallShader()
        {
            RenderPipelineAsset renderPipelineAsset = GraphicsSettings.currentRenderPipeline;

            if (renderPipelineAsset == null)
            {
                Shader builtInShader = Shader.Find("Standard");

                if (builtInShader != null)
                {
                    return builtInShader;
                }
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");

            if (shader != null)
            {
                return shader;
            }

            shader = Shader.Find("URP/Lit");

            if (shader != null)
            {
                return shader;
            }

            shader = Shader.Find("HDRP/Lit");

            if (shader != null)
            {
                return shader;
            }

            shader = Shader.Find("Standard");

            if (shader != null)
            {
                return shader;
            }

            throw new MissingMemberException("Could not find a compatible lit shader for Roll-a-Ball materials.");
        }

        private static bool ApplyRollABallMaterialDefaults(Material material, Color baseColor)
        {
            bool changed = false;

            if (material.HasProperty("_BaseColor") && material.GetColor("_BaseColor") != baseColor)
            {
                material.SetColor("_BaseColor", baseColor);
                changed = true;
            }

            if (material.HasProperty("_Color") && material.GetColor("_Color") != baseColor)
            {
                material.SetColor("_Color", baseColor);
                changed = true;
            }

            if (material.HasProperty("_Smoothness") && Mathf.Approximately(material.GetFloat("_Smoothness"), 0.28f) == false)
            {
                material.SetFloat("_Smoothness", 0.28f);
                changed = true;
            }

            if (material.HasProperty("_Metallic") && Mathf.Approximately(material.GetFloat("_Metallic"), 0f) == false)
            {
                material.SetFloat("_Metallic", 0f);
                changed = true;
            }

            return changed;
        }

        private static SampleProjectile EnsureProjectilePrefab()
        {
            EnsureFolderForFile(k_projectilePrefabPath);

            SampleProjectile projectilePrefab = AssetDatabase.LoadAssetAtPath<SampleProjectile>(k_projectilePrefabPath);

            if (projectilePrefab != null)
            {
                return projectilePrefab;
            }

            GameObject tempRoot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            tempRoot.name = "OnitySampleProjectile";
            tempRoot.transform.localScale = Vector3.one * 0.25f;
            tempRoot.AddComponent<SampleProjectile>();

            GameObject prefabRoot = PrefabUtility.SaveAsPrefabAsset(tempRoot, k_projectilePrefabPath);
            UnityEngine.Object.DestroyImmediate(tempRoot);

            projectilePrefab = prefabRoot.GetComponent<SampleProjectile>();

            if (projectilePrefab == null)
            {
                throw new MissingComponentException("Sample projectile prefab was created without SampleProjectile component.");
            }

            return projectilePrefab;
        }

        private static PanelSettings EnsurePanelSettings()
        {
            EnsureFolderForFile(k_panelSettingsPath);

            PanelSettings panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(k_panelSettingsPath);

            if (panelSettings != null)
            {
                return panelSettings;
            }

            panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
            panelSettings.clearColor = false;
            AssetDatabase.CreateAsset(panelSettings, k_panelSettingsPath);
            AssetDatabase.SaveAssets();
            return panelSettings;
        }

        private static void CreateDefaultCameraAndLight()
        {
            GameObject cameraRoot = new GameObject("Main Camera");
            Camera camera = cameraRoot.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.1f, 0.12f);
            cameraRoot.tag = "MainCamera";
            cameraRoot.transform.position = new Vector3(0f, 2f, -10f);
            cameraRoot.transform.rotation = Quaternion.Euler(8f, 0f, 0f);

            GameObject lightRoot = new GameObject("Directional Light");
            Light light = lightRoot.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            lightRoot.transform.rotation = Quaternion.Euler(35f, -25f, 0f);
        }

        private static Camera CreateRollABallCameraAndLight()
        {
            GameObject cameraRoot = new GameObject("Main Camera");
            Camera camera = cameraRoot.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.09f, 0.12f, 0.16f);
            cameraRoot.tag = "MainCamera";
            cameraRoot.transform.position = new Vector3(0f, 12f, -6f);
            cameraRoot.transform.rotation = Quaternion.Euler(58f, 0f, 0f);

            GameObject lightRoot = new GameObject("Directional Light");
            Light light = lightRoot.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            lightRoot.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
            return camera;
        }

        private static Material EnsureRollABallTrailMaterial(string materialPath, string materialName, Color baseColor)
        {
            EnsureFolderForFile(materialPath);

            Shader shader = ResolveRollABallTrailShader();
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);

            if (material == null)
            {
                material = new Material(shader)
                {
                    name = materialName
                };

                ApplyRollABallTrailMaterialDefaults(material, baseColor);
                AssetDatabase.CreateAsset(material, materialPath);
                AssetDatabase.SaveAssets();
                return material;
            }

            bool changed = false;

            if (material.shader != shader)
            {
                material.shader = shader;
                changed = true;
            }

            if (ApplyRollABallTrailMaterialDefaults(material, baseColor))
            {
                changed = true;
            }

            if (changed)
            {
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssets();
            }

            return material;
        }

        private static Shader ResolveRollABallTrailShader()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");

            if (shader != null)
            {
                return shader;
            }

            shader = Shader.Find("Particles/Standard Unlit");

            if (shader != null)
            {
                return shader;
            }

            shader = Shader.Find("Sprites/Default");

            if (shader != null)
            {
                return shader;
            }

            shader = Shader.Find("Unlit/Color");

            if (shader != null)
            {
                return shader;
            }

            return ResolveRollABallShader();
        }

        private static bool ApplyRollABallTrailMaterialDefaults(Material material, Color baseColor)
        {
            bool changed = false;

            if (material.HasProperty("_BaseColor") && material.GetColor("_BaseColor") != baseColor)
            {
                material.SetColor("_BaseColor", baseColor);
                changed = true;
            }

            if (material.HasProperty("_Color") && material.GetColor("_Color") != baseColor)
            {
                material.SetColor("_Color", baseColor);
                changed = true;
            }

            return changed;
        }

        private static void CreateRollABallArena(
            Transform parent,
            ScriptableObject settingsAsset,
            Material arenaMaterial,
            int environmentLayer)
        {
            Vector2 halfExtents = ReadSerializedVector2(settingsAsset, "m_arenaHalfExtents", new Vector2(8f, 8f));
            const float floorHeight = 1f;
            const float wallHeight = 2f;
            const float wallThickness = 0.6f;

            GameObject arenaRoot = new GameObject("Arena");
            arenaRoot.transform.SetParent(parent, false);

            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "ArenaFloor";
            floor.layer = environmentLayer;
            floor.transform.SetParent(arenaRoot.transform, false);
            floor.transform.localPosition = new Vector3(0f, -floorHeight * 0.5f, 0f);
            floor.transform.localScale = new Vector3((halfExtents.x * 2f) + 1.5f, floorHeight, (halfExtents.y * 2f) + 1.5f);
            SetRendererMaterial(floor, arenaMaterial);

            CreateArenaWall(
                arenaRoot.transform,
                "ArenaWallNorth",
                new Vector3(0f, wallHeight * 0.5f, halfExtents.y + (wallThickness * 0.5f)),
                new Vector3((halfExtents.x * 2f) + wallThickness, wallHeight, wallThickness),
                arenaMaterial,
                environmentLayer);

            CreateArenaWall(
                arenaRoot.transform,
                "ArenaWallSouth",
                new Vector3(0f, wallHeight * 0.5f, -(halfExtents.y + (wallThickness * 0.5f))),
                new Vector3((halfExtents.x * 2f) + wallThickness, wallHeight, wallThickness),
                arenaMaterial,
                environmentLayer);

            CreateArenaWall(
                arenaRoot.transform,
                "ArenaWallEast",
                new Vector3(halfExtents.x + (wallThickness * 0.5f), wallHeight * 0.5f, 0f),
                new Vector3(wallThickness, wallHeight, (halfExtents.y * 2f) + wallThickness),
                arenaMaterial,
                environmentLayer);

            CreateArenaWall(
                arenaRoot.transform,
                "ArenaWallWest",
                new Vector3(-(halfExtents.x + (wallThickness * 0.5f)), wallHeight * 0.5f, 0f),
                new Vector3(wallThickness, wallHeight, (halfExtents.y * 2f) + wallThickness),
                arenaMaterial,
                environmentLayer);
        }

        private static void CreateArenaWall(
            Transform parent,
            string objectName,
            Vector3 localPosition,
            Vector3 localScale,
            Material material,
            int layer)
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = objectName;
            wall.layer = layer;
            wall.transform.SetParent(parent, false);
            wall.transform.localPosition = localPosition;
            wall.transform.localScale = localScale;
            SetRendererMaterial(wall, material);
        }

        private static int ResolveLayer(string layerName)
        {
            int layer = LayerMask.NameToLayer(layerName);

            if (layer >= 0)
            {
                return layer;
            }

            throw new MissingMemberException(
                $"Layer '{layerName}' is required for Roll-a-Ball sample but was not found in TagManager.");
        }

        private static void SetRendererMaterial(GameObject root, Material material)
        {
            if (material == null)
            {
                return;
            }

            MeshRenderer meshRenderer = root.GetComponent<MeshRenderer>();

            if (meshRenderer != null)
            {
                meshRenderer.sharedMaterial = material;
            }
        }

        private static void SetContextInstallers(OnityContext context, MonoInstaller[] installers)
        {
            SerializedObject serializedObject = new SerializedObject(context);
            SerializedProperty installersProperty = serializedObject.FindProperty("m_installers");

            if (installersProperty == null)
            {
                throw new MissingFieldException("OnityContext serialized field 'm_installers' could not be found.");
            }

            installersProperty.arraySize = installers.Length;

            for (int i = 0; i < installers.Length; i++)
            {
                installersProperty.GetArrayElementAtIndex(i).objectReferenceValue = installers[i];
            }

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetSerializedObjectReference(
            UnityEngine.Object target,
            string propertyName,
            UnityEngine.Object reference)
        {
            SerializedObject serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(propertyName);

            if (property == null)
            {
                throw new MissingFieldException($"Serialized field '{propertyName}' was not found on '{target.GetType().Name}'.");
            }

            property.objectReferenceValue = reference;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetSerializedString(UnityEngine.Object target, string propertyName, string value)
        {
            SerializedObject serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(propertyName);

            if (property == null)
            {
                throw new MissingFieldException($"Serialized field '{propertyName}' was not found on '{target.GetType().Name}'.");
            }

            property.stringValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetSerializedInt(UnityEngine.Object target, string propertyName, int value)
        {
            SerializedObject serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(propertyName);

            if (property == null)
            {
                throw new MissingFieldException($"Serialized field '{propertyName}' was not found on '{target.GetType().Name}'.");
            }

            property.intValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetSerializedBoolean(UnityEngine.Object target, string propertyName, bool value)
        {
            SerializedObject serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(propertyName);

            if (property == null)
            {
                throw new MissingFieldException($"Serialized field '{propertyName}' was not found on '{target.GetType().Name}'.");
            }

            property.boolValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static int ReadSerializedInt(UnityEngine.Object target, string propertyName, int fallbackValue)
        {
            if (target == null)
            {
                return fallbackValue;
            }

            SerializedObject serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            return property != null ? property.intValue : fallbackValue;
        }

        private static Vector2 ReadSerializedVector2(UnityEngine.Object target, string propertyName, Vector2 fallbackValue)
        {
            if (target == null)
            {
                return fallbackValue;
            }

            SerializedObject serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            return property != null ? property.vector2Value : fallbackValue;
        }

        private static TComponent AddRollABallComponent<TComponent>(GameObject root, string typeName)
            where TComponent : Component
        {
            Type componentType = ResolveRollABallType(typeName);
            Component component = root.AddComponent(componentType);

            if (component is TComponent typedComponent)
            {
                return typedComponent;
            }

            throw new InvalidCastException(
                $"Could not cast Roll-a-Ball component '{componentType.FullName}' to '{typeof(TComponent).FullName}'.");
        }

        private static Type ResolveRollABallType(string typeName)
        {
            string fullName = $"Onity.Samples.RollABall.{typeName}";
            Type resolvedType = Type.GetType($"{fullName}, Onity.Samples");

            if (resolvedType != null)
            {
                return resolvedType;
            }

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

            for (int i = 0; i < assemblies.Length; i++)
            {
                resolvedType = assemblies[i].GetType(fullName, false);

                if (resolvedType != null)
                {
                    return resolvedType;
                }
            }

            throw new MissingMemberException(
                $"Could not resolve type '{fullName}'. Ensure Onity.Samples assembly is compiled.");
        }

        private static void SaveScene(Scene scene, string scenePath)
        {
            bool saveResult = EditorSceneManager.SaveScene(scene, scenePath);

            if (saveResult == false)
            {
                throw new IOException($"Failed to save scene at path '{scenePath}'.");
            }
        }

        private static void EnsureFolderForFile(string filePath)
        {
            string directoryPath = Path.GetDirectoryName(filePath);

            if (string.IsNullOrEmpty(directoryPath))
            {
                return;
            }

            if (Directory.Exists(directoryPath))
            {
                return;
            }

            Directory.CreateDirectory(directoryPath);
            AssetDatabase.Refresh();
        }
    }
}
