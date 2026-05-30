using System;
using System.IO;
using Onity.Samples.TankArena2D;
using Onity.Samples.TankArena2D.SceneFlow;
using Onity.Unity.Contexts;
using Onity.Unity.Installers;
using Onity.Unity.SceneFlow;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Onity.Editor.Samples
{
    /// <summary>
    /// Generates Tank Arena 2D sample assets and 4-scene flow:
    /// Bootstrap -> MainMenu, then Loading -> Gameplay / MainMenu transitions.
    /// </summary>
    public static class OnityTankArenaSceneGenerator
    {
        private const string k_bootstrapScenePath = "Assets/Onity-Packages/Onity/Samples/TankArena2D/Scenes/BostrapScene - 1.unity";
        private const string k_loadingScenePath = "Assets/Onity-Packages/Onity/Samples/TankArena2D/Scenes/LoadingScene.unity";
        private const string k_mainMenuScenePath = "Assets/Onity-Packages/Onity/Samples/TankArena2D/Scenes/MainMenuHub - 2.unity";
        private const string k_gameplayScenePath = "Assets/Onity-Packages/Onity/Samples/TankArena2D/Scenes/GameModeOrGameScene - 3.unity";

        private const string k_settingsPath = "Assets/Onity-Packages/Onity/Samples/TankArena2D/Data/OnityTankArenaSettings.asset";
        private const string k_playerPrefabPath = "Assets/Onity-Packages/Onity/Samples/TankArena2D/Prefabs/OnityTankPlayer.prefab";
        private const string k_enemyPrefabPath = "Assets/Onity-Packages/Onity/Samples/TankArena2D/Prefabs/OnityTankEnemy.prefab";
        private const string k_projectilePrefabPath = "Assets/Onity-Packages/Onity/Samples/TankArena2D/Prefabs/OnityTankProjectile.prefab";
        private const string k_sceneFlowProfilePath =
            "Assets/Onity-Packages/Onity/Samples/TankArena2D/Data/OnityTankArenaSceneFlowProfile.asset";
        private const string k_playerMaterialPath = "Assets/Onity-Packages/Onity/Samples/TankArena2D/Materials/OnityTankPlayer.mat";
        private const string k_enemyMaterialPath = "Assets/Onity-Packages/Onity/Samples/TankArena2D/Materials/OnityTankEnemy.mat";
        private const string k_projectileMaterialPath = "Assets/Onity-Packages/Onity/Samples/TankArena2D/Materials/OnityTankProjectile.mat";
        private const string k_arenaMaterialPath = "Assets/Onity-Packages/Onity/Samples/TankArena2D/Materials/OnityTankArena.mat";

        private const string k_hudUxmlPath = "Assets/Onity-Packages/Onity/Samples/TankArena2D/UI/OnityTankArenaHud.uxml";
        private const string k_loadingUxmlPath = "Assets/Onity-Packages/Onity/Samples/TankArena2D/UI/OnityTankArenaLoading.uxml";
        private const string k_mainMenuUxmlPath = "Assets/Onity-Packages/Onity/Samples/TankArena2D/UI/OnityTankArenaMainMenu.uxml";
        private const string k_gameShellUxmlPath = "Assets/Onity-Packages/Onity/Samples/TankArena2D/UI/OnityTankArenaGameShell.uxml";
        private const string k_sceneFlowUssPath = "Assets/Onity-Packages/Onity/Samples/TankArena2D/UI/OnityTankArenaSceneFlow.uss";

        private const string k_panelSettingsPath = "Assets/Onity-Packages/Onity/Samples/Common/UI/OnityPanelSettings.asset";
        private const string k_projectContextPrefabPath = "Assets/Onity-Packages/Onity/Samples/Common/Resources/Onity/ProjectContext.prefab";

        private const string k_playerTexturePath =
            "Assets/Onity-Packages/Onity/Samples/Art/2D Tanks/PNG/Default size/tanks_tankGreen_body2.png";
        private const string k_enemyTexturePath =
            "Assets/Onity-Packages/Onity/Samples/Art/2D Tanks/PNG/Default size/tanks_tankNavy_body2.png";
        private const string k_projectileTexturePath =
            "Assets/Onity-Packages/Onity/Samples/Art/2D Tanks/PNG/Default size/tank_bullet5.png";
        private const string k_playerTurretTexturePath =
            "Assets/Onity-Packages/Onity/Samples/Art/2D Tanks/PNG/Default size/tanks_turret1.png";
        private const string k_enemyTurretTexturePath =
            "Assets/Onity-Packages/Onity/Samples/Art/2D Tanks/PNG/Default size/tanks_turret4.png";
        private const string k_loadingBackdropTexturePath =
            "Assets/Onity-Packages/Onity/Samples/Art/2D Tanks/PNG/Default size/tanks_tankTracks3.png";
        private const string k_menuBackdropTexturePath =
            "Assets/Onity-Packages/Onity/Samples/Art/2D Tanks/PNG/Default size/tanks_tankTracks5.png";
        private const string k_crateTexturePath =
            "Assets/Onity-Packages/Onity/Samples/Art/2D Tanks/PNG/Default size/tanks_crateWood.png";
        private const string k_barrelTexturePath =
            "Assets/Onity-Packages/Onity/Samples/Art/2D Tanks/PNG/Default size/tanks_barrelGreen.png";

        private const string k_sceneFlowUssContent =
@".tank-flow__fullscreen
{
    flex-grow: 1;
    align-items: center;
    justify-content: center;
    background-color: rgba(7, 14, 24, 0.86);
}

.tank-flow__card
{
    width: 450px;
    padding-top: 20px;
    padding-bottom: 20px;
    padding-left: 18px;
    padding-right: 18px;
    border-top-left-radius: 12px;
    border-top-right-radius: 12px;
    border-bottom-left-radius: 12px;
    border-bottom-right-radius: 12px;
    border-left-width: 1px;
    border-right-width: 1px;
    border-top-width: 1px;
    border-bottom-width: 1px;
    border-left-color: rgba(152, 196, 237, 0.45);
    border-right-color: rgba(152, 196, 237, 0.45);
    border-top-color: rgba(152, 196, 237, 0.45);
    border-bottom-color: rgba(152, 196, 237, 0.45);
    background-color: rgba(11, 26, 40, 0.92);
}

.tank-flow__title
{
    font-size: 30px;
    -unity-font-style: bold;
    color: rgb(233, 245, 255);
    margin-bottom: 8px;
}

.tank-flow__subtitle
{
    font-size: 15px;
    color: rgb(170, 202, 233);
    margin-bottom: 10px;
}

.tank-flow__line
{
    font-size: 17px;
    color: rgb(255, 226, 136);
    margin-top: 4px;
    margin-bottom: 8px;
}

.tank-flow__button
{
    height: 34px;
    margin-top: 6px;
    background-color: rgb(24, 90, 154);
    color: rgb(234, 247, 255);
}

.tank-flow__button--danger
{
    background-color: rgb(86, 50, 66);
}

.tank-flow__hint
{
    margin-top: 8px;
    font-size: 12px;
    color: rgb(158, 188, 216);
}

.tank-shell__root
{
    flex-grow: 1;
    justify-content: flex-end;
    align-items: flex-end;
    padding-right: 14px;
    padding-bottom: 14px;
}

.tank-shell__panel
{
    width: 190px;
    padding: 10px;
    border-top-left-radius: 8px;
    border-top-right-radius: 8px;
    border-bottom-left-radius: 8px;
    border-bottom-right-radius: 8px;
    background-color: rgba(8, 18, 28, 0.72);
}

.tank-shell__button
{
    height: 28px;
    margin-top: 4px;
    background-color: rgb(36, 90, 147);
    color: rgb(236, 246, 255);
}";

        private const string k_loadingUxmlContent =
@"<?xml version=""1.0"" encoding=""utf-8""?>
<ui:UXML xmlns:ui=""UnityEngine.UIElements"">
    <ui:Style src=""OnityTankArenaSceneFlow.uss"" />
    <ui:VisualElement class=""tank-flow__fullscreen"">
        <ui:VisualElement class=""tank-flow__card"">
            <ui:Label class=""tank-flow__title"" text=""Onity Tank Arena"" />
            <ui:Label class=""tank-flow__subtitle"" text=""Preparing scene transition"" />
            <ui:Label name=""loading-progress"" class=""tank-flow__line"" text=""Loading..."" />
        </ui:VisualElement>
    </ui:VisualElement>
</ui:UXML>";

        private const string k_mainMenuUxmlContent =
@"<?xml version=""1.0"" encoding=""utf-8""?>
<ui:UXML xmlns:ui=""UnityEngine.UIElements"">
    <ui:Style src=""OnityTankArenaSceneFlow.uss"" />
    <ui:VisualElement class=""tank-flow__fullscreen"">
        <ui:VisualElement class=""tank-flow__card"">
            <ui:Label class=""tank-flow__title"" text=""Tank Arena 2D"" />
            <ui:Label class=""tank-flow__subtitle"" text=""Built with Onity DI + Reactive + Messaging + Pooling + DOTS Bridge"" />
            <ui:Button name=""start-button"" class=""tank-flow__button"" text=""Start Battle"" />
            <ui:Button name=""quit-button"" class=""tank-flow__button tank-flow__button--danger"" text=""Quit"" />
            <ui:Label class=""tank-flow__hint"" text=""Keyboard: Enter to start, Escape to quit (editor: stop play mode)"" />
        </ui:VisualElement>
    </ui:VisualElement>
</ui:UXML>";

        private const string k_gameShellUxmlContent =
@"<?xml version=""1.0"" encoding=""utf-8""?>
<ui:UXML xmlns:ui=""UnityEngine.UIElements"">
    <ui:Style src=""OnityTankArenaSceneFlow.uss"" />
    <ui:VisualElement class=""tank-shell__root"">
        <ui:VisualElement class=""tank-shell__panel"">
            <ui:Button name=""restart-run-button"" class=""tank-shell__button"" text=""Restart Run"" />
            <ui:Button name=""main-menu-button"" class=""tank-shell__button"" text=""Main Menu"" />
        </ui:VisualElement>
    </ui:VisualElement>
</ui:UXML>";

        /// <summary>
        /// Generates Tank Arena sample scene-flow and dependent assets.
        /// </summary>
        public static void GenerateTankArenaScene()
        {
            EnsureFolderForFile(k_bootstrapScenePath);
            EnsureSceneFlowUiAssets();
            EnsureProjectContextPrefab();
            OnitySceneFlowProfile sceneFlowProfile = EnsureSceneFlowProfile();

            PanelSettings panelSettings = EnsurePanelSettings();
            VisualTreeAsset hudAsset = LoadRequiredAsset<VisualTreeAsset>(k_hudUxmlPath);
            VisualTreeAsset loadingAsset = LoadRequiredAsset<VisualTreeAsset>(k_loadingUxmlPath);
            VisualTreeAsset mainMenuAsset = LoadRequiredAsset<VisualTreeAsset>(k_mainMenuUxmlPath);
            VisualTreeAsset gameShellAsset = LoadRequiredAsset<VisualTreeAsset>(k_gameShellUxmlPath);

            TankArenaGameSettings settingsAsset = EnsureSettingsAsset();
            Material playerMaterial = EnsureTexturedMaterial(
                k_playerMaterialPath,
                "OnityTankPlayerMaterial",
                new Color(0.25f, 0.78f, 0.48f, 1f),
                k_playerTexturePath);
            Material enemyMaterial = EnsureTexturedMaterial(
                k_enemyMaterialPath,
                "OnityTankEnemyMaterial",
                new Color(0.31f, 0.61f, 0.97f, 1f),
                k_enemyTexturePath);
            Material projectileMaterial = EnsureTexturedMaterial(
                k_projectileMaterialPath,
                "OnityTankProjectileMaterial",
                new Color(0.97f, 0.92f, 0.36f, 1f),
                k_projectileTexturePath);
            Material playerTurretMaterial = EnsureTexturedMaterial(
                "Assets/Onity-Packages/Onity/Samples/TankArena2D/Materials/OnityTankPlayerTurret.mat",
                "OnityTankPlayerTurretMaterial",
                new Color(0.32f, 0.88f, 0.54f, 1f),
                k_playerTurretTexturePath);
            Material enemyTurretMaterial = EnsureTexturedMaterial(
                "Assets/Onity-Packages/Onity/Samples/TankArena2D/Materials/OnityTankEnemyTurret.mat",
                "OnityTankEnemyTurretMaterial",
                new Color(0.42f, 0.72f, 1f, 1f),
                k_enemyTurretTexturePath);
            Material loadingBackdropMaterial = EnsureTexturedMaterial(
                "Assets/Onity-Packages/Onity/Samples/TankArena2D/Materials/OnityTankLoadingBackdrop.mat",
                "OnityTankLoadingBackdropMaterial",
                new Color(0.31f, 0.41f, 0.55f, 1f),
                k_loadingBackdropTexturePath);
            Material menuBackdropMaterial = EnsureTexturedMaterial(
                "Assets/Onity-Packages/Onity/Samples/TankArena2D/Materials/OnityTankMenuBackdrop.mat",
                "OnityTankMenuBackdropMaterial",
                new Color(0.22f, 0.3f, 0.43f, 1f),
                k_menuBackdropTexturePath);
            Material crateMaterial = EnsureTexturedMaterial(
                "Assets/Onity-Packages/Onity/Samples/TankArena2D/Materials/OnityTankCrate.mat",
                "OnityTankCrateMaterial",
                new Color(0.71f, 0.57f, 0.37f, 1f),
                k_crateTexturePath);
            Material barrelMaterial = EnsureTexturedMaterial(
                "Assets/Onity-Packages/Onity/Samples/TankArena2D/Materials/OnityTankBarrel.mat",
                "OnityTankBarrelMaterial",
                new Color(0.44f, 0.7f, 0.42f, 1f),
                k_barrelTexturePath);
            Material arenaMaterial = EnsureColorMaterial(
                k_arenaMaterialPath,
                "OnityTankArenaMaterial",
                new Color(0.15f, 0.2f, 0.29f, 1f));

            TankArenaPlayerController playerPrefab = EnsurePlayerPrefab(playerMaterial, playerTurretMaterial);
            TankArenaEnemyController enemyPrefab = EnsureEnemyPrefab(enemyMaterial, enemyTurretMaterial);
            TankArenaProjectile projectilePrefab = EnsureProjectilePrefab(projectileMaterial);

            GenerateBootstrapScene(sceneFlowProfile);
            GenerateLoadingScene(
                sceneFlowProfile,
                panelSettings,
                loadingAsset,
                loadingBackdropMaterial,
                enemyMaterial);
            GenerateMainMenuScene(
                sceneFlowProfile,
                panelSettings,
                mainMenuAsset,
                menuBackdropMaterial,
                enemyMaterial,
                crateMaterial);
            GenerateGameplayScene(
                sceneFlowProfile,
                panelSettings,
                hudAsset,
                gameShellAsset,
                settingsAsset,
                arenaMaterial,
                crateMaterial,
                barrelMaterial,
                playerPrefab,
                enemyPrefab,
                projectilePrefab);

            ApplySceneFlowToBuildSettings();
            EditorSceneManager.OpenScene(k_bootstrapScenePath, OpenSceneMode.Single);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Generated Tank Arena 2D flow scenes (Bootstrap, Loading, MainMenu, Gameplay).");
        }

        private static void GenerateBootstrapScene(OnitySceneFlowProfile sceneFlowProfile)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CreateOrthoCameraAndLight();

            GameObject cameraRoot = GameObject.Find("Main Camera");

            if (cameraRoot == null)
            {
                cameraRoot = new GameObject("Main Camera");
                cameraRoot.tag = "MainCamera";
                cameraRoot.AddComponent<Camera>();
            }

            TankArenaBootstrapSceneController bootstrapController =
                cameraRoot.AddComponent<TankArenaBootstrapSceneController>();
            SetSerializedObjectReference(bootstrapController, "m_sceneFlowProfile", sceneFlowProfile);

            SaveScene(scene, k_bootstrapScenePath);
        }

        private static void GenerateLoadingScene(
            OnitySceneFlowProfile sceneFlowProfile,
            PanelSettings panelSettings,
            VisualTreeAsset loadingAsset,
            Material backdropMaterial,
            Material tankMaterial)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CreateOrthoCameraAndLight();

            GameObject contextRoot = new GameObject("OnitySceneContext");
            SceneContext sceneContext = contextRoot.AddComponent<SceneContext>();
            SetContextInstallers(sceneContext, Array.Empty<MonoInstaller>());

            GameObject loadingDecorRoot = new GameObject("LoadingDecor");
            loadingDecorRoot.transform.SetParent(contextRoot.transform, false);
            CreateVisualQuad(
                loadingDecorRoot.transform,
                "LoadingBackdrop",
                backdropMaterial,
                new Vector3(11f, 1f, 11f),
                new Vector3(0f, -0.05f, 0f));
            CreateVisualQuad(
                loadingDecorRoot.transform,
                "LoadingTank",
                tankMaterial,
                new Vector3(2.2f, 1f, 2.2f),
                new Vector3(0f, 0.3f, 2.5f));

            GameObject loadingRoot = new GameObject("LoadingFlow");
            loadingRoot.transform.SetParent(contextRoot.transform, false);
            TankArenaLoadingSceneController controller = loadingRoot.AddComponent<TankArenaLoadingSceneController>();
            SetSerializedObjectReference(controller, "m_sceneFlowProfile", sceneFlowProfile);

            GameObject documentRoot = new GameObject("LoadingUI");
            documentRoot.transform.SetParent(loadingRoot.transform, false);
            UIDocument document = documentRoot.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.visualTreeAsset = loadingAsset;

            SetSerializedObjectReference(controller, "m_document", document);

            SaveScene(scene, k_loadingScenePath);
        }

        private static void GenerateMainMenuScene(
            OnitySceneFlowProfile sceneFlowProfile,
            PanelSettings panelSettings,
            VisualTreeAsset mainMenuAsset,
            Material menuBackdropMaterial,
            Material enemyMaterial,
            Material crateMaterial)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CreateOrthoCameraAndLight();

            GameObject contextRoot = new GameObject("OnitySceneContext");
            SceneContext sceneContext = contextRoot.AddComponent<SceneContext>();
            SetContextInstallers(sceneContext, Array.Empty<MonoInstaller>());

            GameObject stageRoot = new GameObject("MenuStage");
            stageRoot.transform.SetParent(contextRoot.transform, false);
            CreateMainMenuBackdrop(stageRoot.transform, menuBackdropMaterial);
            CreateVisualQuad(
                stageRoot.transform,
                "DecorTankLeft",
                enemyMaterial,
                new Vector3(1.6f, 1f, 1.9f),
                new Vector3(-5f, 0.25f, -3f));
            CreateVisualQuad(
                stageRoot.transform,
                "DecorTankRight",
                enemyMaterial,
                new Vector3(1.6f, 1f, 1.9f),
                new Vector3(5f, 0.25f, -3f));
            CreateVisualQuad(
                stageRoot.transform,
                "DecorCrateLeft",
                crateMaterial,
                new Vector3(1.2f, 1f, 1.2f),
                new Vector3(-3.6f, 0.2f, 2.8f));
            CreateVisualQuad(
                stageRoot.transform,
                "DecorCrateRight",
                crateMaterial,
                new Vector3(1.2f, 1f, 1.2f),
                new Vector3(3.6f, 0.2f, 2.8f));

            GameObject menuRoot = new GameObject("MainMenuFlow");
            menuRoot.transform.SetParent(contextRoot.transform, false);
            TankArenaMainMenuSceneController controller = menuRoot.AddComponent<TankArenaMainMenuSceneController>();
            SetSerializedObjectReference(controller, "m_sceneFlowProfile", sceneFlowProfile);

            GameObject documentRoot = new GameObject("MainMenuUI");
            documentRoot.transform.SetParent(menuRoot.transform, false);
            UIDocument document = documentRoot.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.visualTreeAsset = mainMenuAsset;

            SetSerializedObjectReference(controller, "m_document", document);

            SaveScene(scene, k_mainMenuScenePath);
        }

        private static void GenerateGameplayScene(
            OnitySceneFlowProfile sceneFlowProfile,
            PanelSettings panelSettings,
            VisualTreeAsset hudAsset,
            VisualTreeAsset gameShellAsset,
            TankArenaGameSettings settingsAsset,
            Material arenaMaterial,
            Material crateMaterial,
            Material barrelMaterial,
            TankArenaPlayerController playerPrefab,
            TankArenaEnemyController enemyPrefab,
            TankArenaProjectile projectilePrefab)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CreateOrthoCameraAndLight();

            GameObject contextRoot = new GameObject("OnitySceneContext");
            SceneContext sceneContext = contextRoot.AddComponent<SceneContext>();
            TankArenaInstaller installer = contextRoot.AddComponent<TankArenaInstaller>();
            SetContextInstallers(sceneContext, new[] { installer });

            GameObject enemyPoolRoot = new GameObject("EnemyPoolRoot");
            enemyPoolRoot.transform.SetParent(contextRoot.transform, false);

            GameObject projectilePoolRoot = new GameObject("ProjectilePoolRoot");
            projectilePoolRoot.transform.SetParent(contextRoot.transform, false);

            GameObject gameplayRoot = new GameObject("Gameplay");
            gameplayRoot.transform.SetParent(contextRoot.transform, false);

            CreateArenaGeometry(gameplayRoot.transform, arenaMaterial);
            CreateGameplayDecor(gameplayRoot.transform, crateMaterial, barrelMaterial);

            GameObject playerInstanceRoot =
                PrefabUtility.InstantiatePrefab(playerPrefab.gameObject, scene) as GameObject;
            TankArenaPlayerController playerInstance = playerInstanceRoot != null
                ? playerInstanceRoot.GetComponent<TankArenaPlayerController>()
                : null;

            if (playerInstance == null)
            {
                throw new FileNotFoundException("Could not instantiate Tank Arena player prefab.");
            }

            playerInstance.transform.SetParent(gameplayRoot.transform, true);
            playerInstance.transform.position = new Vector3(0f, 0.5f, -4f);
            playerInstance.transform.rotation = Quaternion.identity;

            GameObject runtimeEnemyRoot = new GameObject("RuntimeEnemies");
            runtimeEnemyRoot.transform.SetParent(gameplayRoot.transform, false);

            GameObject spawnCenter = new GameObject("SpawnCenter");
            spawnCenter.transform.SetParent(gameplayRoot.transform, false);
            spawnCenter.transform.localPosition = Vector3.zero;

            GameObject spawnerRoot = new GameObject("EnemySpawner");
            spawnerRoot.transform.SetParent(gameplayRoot.transform, false);
            TankArenaEnemySpawner enemySpawner = spawnerRoot.AddComponent<TankArenaEnemySpawner>();
            SetSerializedObjectReference(enemySpawner, "m_playerTransform", playerInstance.transform);
            SetSerializedObjectReference(enemySpawner, "m_spawnCenter", spawnCenter.transform);
            SetSerializedObjectReference(enemySpawner, "m_runtimeEnemyRoot", runtimeEnemyRoot.transform);
            SetSerializedInt(enemySpawner, "m_maxSpawnAttempts", 10);
            SetSerializedFloat(enemySpawner, "m_spawnHeight", 0.5f);

            SetSerializedObjectReference(installer, "m_settings", settingsAsset);
            SetSerializedObjectReference(installer, "m_enemySpawner", enemySpawner);
            SetSerializedObjectReference(installer, "m_playerController", playerInstance);
            SetSerializedObjectReference(installer, "m_enemyPrefab", enemyPrefab);
            SetSerializedObjectReference(installer, "m_projectilePrefab", projectilePrefab);
            SetSerializedObjectReference(installer, "m_enemyPoolRoot", enemyPoolRoot.transform);
            SetSerializedObjectReference(installer, "m_projectilePoolRoot", projectilePoolRoot.transform);
            SetSerializedInt(installer, "m_enemyPoolCapacity", 20);
            SetSerializedInt(installer, "m_enemyPoolMaxSize", 128);
            SetSerializedInt(installer, "m_projectilePoolCapacity", 64);
            SetSerializedInt(installer, "m_projectilePoolMaxSize", 512);

            GameObject uiScopeRoot = new GameObject("UiScope");
            uiScopeRoot.transform.SetParent(contextRoot.transform, false);
            GameObjectContext uiScopeContext = uiScopeRoot.AddComponent<GameObjectContext>();
            SetContextInstallers(uiScopeContext, Array.Empty<MonoInstaller>());

            GameObject hudRoot = new GameObject("TankArenaHud");
            hudRoot.transform.SetParent(uiScopeRoot.transform, false);
            UIDocument hudDocument = hudRoot.AddComponent<UIDocument>();
            hudDocument.panelSettings = panelSettings;
            hudDocument.visualTreeAsset = hudAsset;

            TankArenaHudView hudView = hudRoot.AddComponent<TankArenaHudView>();
            TankArenaHudController hudController = hudRoot.AddComponent<TankArenaHudController>();
            SetSerializedObjectReference(hudView, "m_document", hudDocument);
            SetSerializedObjectReference(hudController, "m_view", hudView);

            GameObject shellRoot = new GameObject("TankArenaGameShell");
            shellRoot.transform.SetParent(uiScopeRoot.transform, false);
            UIDocument shellDocument = shellRoot.AddComponent<UIDocument>();
            shellDocument.panelSettings = panelSettings;
            shellDocument.visualTreeAsset = gameShellAsset;
            SetSerializedInt(shellDocument, "m_SortingOrder", 10);

            GameObject flowRoot = new GameObject("GameFlow");
            flowRoot.transform.SetParent(contextRoot.transform, false);
            TankArenaGameSceneController gameSceneController = flowRoot.AddComponent<TankArenaGameSceneController>();
            SetSerializedObjectReference(gameSceneController, "m_sceneFlowProfile", sceneFlowProfile);
            SetSerializedObjectReference(gameSceneController, "m_document", shellDocument);

            SaveScene(scene, k_gameplayScenePath);
        }

        private static void ApplySceneFlowToBuildSettings()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(k_bootstrapScenePath, true),
                new EditorBuildSettingsScene(k_loadingScenePath, true),
                new EditorBuildSettingsScene(k_mainMenuScenePath, true),
                new EditorBuildSettingsScene(k_gameplayScenePath, true)
            };

            SceneAsset bootstrapScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(k_bootstrapScenePath);
            EditorSceneManager.playModeStartScene = bootstrapScene;
        }

        private static void EnsureProjectContextPrefab()
        {
            ProjectContext existing = AssetDatabase.LoadAssetAtPath<ProjectContext>(k_projectContextPrefabPath);

            if (existing != null)
            {
                return;
            }

            EnsureFolderForFile(k_projectContextPrefabPath);

            GameObject root = new GameObject("ProjectContext");
            root.AddComponent<ProjectContext>();
            PrefabUtility.SaveAsPrefabAsset(root, k_projectContextPrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
        }

        private static void EnsureSceneFlowUiAssets()
        {
            EnsureTextAsset(k_sceneFlowUssPath, k_sceneFlowUssContent);
            EnsureTextAsset(k_loadingUxmlPath, k_loadingUxmlContent);
            EnsureTextAsset(k_mainMenuUxmlPath, k_mainMenuUxmlContent);
            EnsureTextAsset(k_gameShellUxmlPath, k_gameShellUxmlContent);
            AssetDatabase.Refresh();
        }

        private static void EnsureTextAsset(string assetPath, string content)
        {
            EnsureFolderForFile(assetPath);
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            string absolutePath = Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
            string normalizedContent = content.Replace("\r\n", "\n");

            if (File.Exists(absolutePath))
            {
                string existing = File.ReadAllText(absolutePath);

                if (string.Equals(existing, normalizedContent, StringComparison.Ordinal))
                {
                    return;
                }
            }

            File.WriteAllText(absolutePath, normalizedContent);
        }

        private static TAsset LoadRequiredAsset<TAsset>(string assetPath)
            where TAsset : UnityEngine.Object
        {
            TAsset asset = AssetDatabase.LoadAssetAtPath<TAsset>(assetPath);

            if (asset == null)
            {
                throw new FileNotFoundException($"Could not load required asset at path '{assetPath}'.");
            }

            return asset;
        }

        private static TankArenaGameSettings EnsureSettingsAsset()
        {
            TankArenaGameSettings settings = AssetDatabase.LoadAssetAtPath<TankArenaGameSettings>(k_settingsPath);

            if (settings != null)
            {
                return settings;
            }

            EnsureFolderForFile(k_settingsPath);
            settings = ScriptableObject.CreateInstance<TankArenaGameSettings>();
            AssetDatabase.CreateAsset(settings, k_settingsPath);
            EditorUtility.SetDirty(settings);
            return settings;
        }

        private static OnitySceneFlowProfile EnsureSceneFlowProfile()
        {
            OnitySceneFlowProfile profile = AssetDatabase.LoadAssetAtPath<OnitySceneFlowProfile>(k_sceneFlowProfilePath);

            if (profile == null)
            {
                EnsureFolderForFile(k_sceneFlowProfilePath);
                profile = ScriptableObject.CreateInstance<OnitySceneFlowProfile>();
                AssetDatabase.CreateAsset(profile, k_sceneFlowProfilePath);
            }

            profile.SetRouteTransitionsThroughLoadingScene(true);
            profile.SetSceneName(OnitySceneFlowStateId.Bootstrap, TankArenaSceneIds.Bootstrap);
            profile.SetSceneName(OnitySceneFlowStateId.Loading, TankArenaSceneIds.Loading);
            profile.SetSceneName(OnitySceneFlowStateId.MainMenuHub, TankArenaSceneIds.MainMenu);
            profile.SetSceneName(OnitySceneFlowStateId.Gameplay, TankArenaSceneIds.Gameplay);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static TankArenaPlayerController EnsurePlayerPrefab(
            Material playerMaterial,
            Material turretMaterial)
        {
            TankArenaPlayerController prefab = AssetDatabase.LoadAssetAtPath<TankArenaPlayerController>(k_playerPrefabPath);

            if (prefab != null)
            {
                AssetDatabase.DeleteAsset(k_playerPrefabPath);
            }

            EnsureFolderForFile(k_playerPrefabPath);

            GameObject root = new GameObject("OnityTankPlayer");
            TankArenaPlayerController controller = root.AddComponent<TankArenaPlayerController>();
            Rigidbody body = root.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.constraints =
                RigidbodyConstraints.FreezeRotationX |
                RigidbodyConstraints.FreezeRotationZ |
                RigidbodyConstraints.FreezePositionY;

            CapsuleCollider collider = root.AddComponent<CapsuleCollider>();
            collider.radius = 0.42f;
            collider.height = 1.1f;
            collider.center = new Vector3(0f, 0.55f, 0f);

            CreateVisualQuad(
                root.transform,
                "TankVisual",
                playerMaterial,
                new Vector3(1.4f, 1f, 1.6f),
                new Vector3(0f, 0.62f, 0f));
            CreateVisualQuad(
                root.transform,
                "TurretVisual",
                turretMaterial,
                new Vector3(0.82f, 1f, 1.18f),
                new Vector3(0f, 0.65f, 0.24f));

            GameObject shootOrigin = new GameObject("ShootOrigin");
            shootOrigin.transform.SetParent(root.transform, false);
            shootOrigin.transform.localPosition = new Vector3(0f, 0.72f, 1.02f);
            shootOrigin.transform.localRotation = Quaternion.identity;
            SetSerializedObjectReference(controller, "m_shootOrigin", shootOrigin.transform);

            GameObject prefabRoot = PrefabUtility.SaveAsPrefabAsset(root, k_playerPrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            return prefabRoot.GetComponent<TankArenaPlayerController>();
        }

        private static TankArenaEnemyController EnsureEnemyPrefab(
            Material enemyMaterial,
            Material turretMaterial)
        {
            TankArenaEnemyController prefab = AssetDatabase.LoadAssetAtPath<TankArenaEnemyController>(k_enemyPrefabPath);

            if (prefab != null)
            {
                AssetDatabase.DeleteAsset(k_enemyPrefabPath);
            }

            EnsureFolderForFile(k_enemyPrefabPath);

            GameObject root = new GameObject("OnityTankEnemy");
            TankArenaEnemyController controller = root.AddComponent<TankArenaEnemyController>();
            CapsuleCollider collider = root.AddComponent<CapsuleCollider>();
            collider.radius = 0.42f;
            collider.height = 1.1f;
            collider.center = new Vector3(0f, 0.55f, 0f);

            CreateVisualQuad(
                root.transform,
                "TankVisual",
                enemyMaterial,
                new Vector3(1.4f, 1f, 1.6f),
                new Vector3(0f, 0.62f, 0f));
            CreateVisualQuad(
                root.transform,
                "TurretVisual",
                turretMaterial,
                new Vector3(0.82f, 1f, 1.18f),
                new Vector3(0f, 0.65f, 0.24f));

            GameObject shootOrigin = new GameObject("ShootOrigin");
            shootOrigin.transform.SetParent(root.transform, false);
            shootOrigin.transform.localPosition = new Vector3(0f, 0.72f, 1.02f);
            shootOrigin.transform.localRotation = Quaternion.identity;
            SetSerializedObjectReference(controller, "m_shootOrigin", shootOrigin.transform);

            GameObject prefabRoot = PrefabUtility.SaveAsPrefabAsset(root, k_enemyPrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            return prefabRoot.GetComponent<TankArenaEnemyController>();
        }

        private static TankArenaProjectile EnsureProjectilePrefab(Material projectileMaterial)
        {
            TankArenaProjectile prefab = AssetDatabase.LoadAssetAtPath<TankArenaProjectile>(k_projectilePrefabPath);

            if (prefab != null)
            {
                AssetDatabase.DeleteAsset(k_projectilePrefabPath);
            }

            EnsureFolderForFile(k_projectilePrefabPath);

            GameObject root = new GameObject("OnityTankProjectile");
            root.AddComponent<TankArenaProjectile>();

            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Quad);
            visual.name = "ProjectileVisual";
            visual.transform.SetParent(root.transform, false);
            visual.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            visual.transform.localScale = Vector3.one * 0.5f;
            visual.transform.localPosition = Vector3.zero;
            Collider visualCollider = visual.GetComponent<Collider>();

            if (visualCollider != null)
            {
                UnityEngine.Object.DestroyImmediate(visualCollider);
            }

            Renderer renderer = visual.GetComponent<Renderer>();

            if (renderer != null && projectileMaterial != null)
            {
                renderer.sharedMaterial = projectileMaterial;
            }

            GameObject prefabRoot = PrefabUtility.SaveAsPrefabAsset(root, k_projectilePrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            return prefabRoot.GetComponent<TankArenaProjectile>();
        }

        private static void CreateArenaGeometry(Transform parent, Material arenaMaterial)
        {
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "ArenaFloor";
            floor.transform.SetParent(parent, false);
            floor.transform.localScale = new Vector3(3.2f, 1f, 2.2f);
            floor.transform.localPosition = Vector3.zero;
            SetRendererMaterial(floor, arenaMaterial);

            CreateArenaWall(parent, "WallTop", new Vector3(0f, 1f, 11f), new Vector3(34f, 2f, 1f), arenaMaterial);
            CreateArenaWall(parent, "WallBottom", new Vector3(0f, 1f, -11f), new Vector3(34f, 2f, 1f), arenaMaterial);
            CreateArenaWall(parent, "WallLeft", new Vector3(-16f, 1f, 0f), new Vector3(1f, 2f, 24f), arenaMaterial);
            CreateArenaWall(parent, "WallRight", new Vector3(16f, 1f, 0f), new Vector3(1f, 2f, 24f), arenaMaterial);
        }

        private static void CreateMainMenuBackdrop(Transform parent, Material arenaMaterial)
        {
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "MenuFloor";
            floor.transform.SetParent(parent, false);
            floor.transform.localScale = new Vector3(2.4f, 1f, 1.8f);
            floor.transform.localPosition = new Vector3(0f, -0.01f, 0f);
            SetRendererMaterial(floor, arenaMaterial);
        }

        private static void CreateGameplayDecor(
            Transform parent,
            Material crateMaterial,
            Material barrelMaterial)
        {
            CreateVisualQuad(
                parent,
                "DecorCrateTopLeft",
                crateMaterial,
                new Vector3(1.25f, 1f, 1.25f),
                new Vector3(-9.5f, 0.15f, 7f));
            CreateVisualQuad(
                parent,
                "DecorCrateTopRight",
                crateMaterial,
                new Vector3(1.25f, 1f, 1.25f),
                new Vector3(9.5f, 0.15f, 7f));
            CreateVisualQuad(
                parent,
                "DecorBarrelBottomLeft",
                barrelMaterial,
                new Vector3(1.1f, 1f, 1.1f),
                new Vector3(-11f, 0.15f, -7.5f));
            CreateVisualQuad(
                parent,
                "DecorBarrelBottomRight",
                barrelMaterial,
                new Vector3(1.1f, 1f, 1.1f),
                new Vector3(11f, 0.15f, -7.5f));
        }

        private static void CreateArenaWall(
            Transform parent,
            string wallName,
            Vector3 localPosition,
            Vector3 localScale,
            Material wallMaterial)
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = wallName;
            wall.transform.SetParent(parent, false);
            wall.transform.localPosition = localPosition;
            wall.transform.localScale = localScale;
            SetRendererMaterial(wall, wallMaterial);
        }

        private static void CreateVisualQuad(
            Transform parent,
            string objectName,
            Material material,
            Vector3 localScale,
            Vector3 localPosition)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = objectName;
            quad.transform.SetParent(parent, false);
            quad.transform.localPosition = localPosition;
            quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            quad.transform.localScale = localScale;

            Collider collider = quad.GetComponent<Collider>();

            if (collider != null)
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }

            SetRendererMaterial(quad, material);
        }

        private static void CreateOrthoCameraAndLight()
        {
            GameObject cameraRoot = new GameObject("Main Camera");
            Camera camera = cameraRoot.AddComponent<Camera>();
            cameraRoot.tag = "MainCamera";
            cameraRoot.transform.position = new Vector3(0f, 24f, 0f);
            cameraRoot.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.06f, 0.1f, 0.16f, 1f);
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 200f;
            camera.orthographic = true;
            camera.orthographicSize = 11f;

            GameObject lightRoot = new GameObject("Directional Light");
            Light light = lightRoot.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.color = new Color(1f, 0.97f, 0.92f, 1f);
            lightRoot.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        private static Material EnsureTexturedMaterial(
            string materialPath,
            string materialName,
            Color fallbackColor,
            string texturePath)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            bool created = false;

            if (material == null)
            {
                EnsureFolderForFile(materialPath);
                Shader shader = ResolveShader();
                material = new Material(shader)
                {
                    name = materialName
                };
                AssetDatabase.CreateAsset(material, materialPath);
                created = true;
            }

            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);

            if (texture != null)
            {
                AssignTexture(material, texture);
            }

            AssignColor(material, fallbackColor);

            if (created == false)
            {
                material.name = materialName;
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material EnsureColorMaterial(string materialPath, string materialName, Color color)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            bool created = false;

            if (material == null)
            {
                EnsureFolderForFile(materialPath);
                Shader shader = ResolveShader();
                material = new Material(shader)
                {
                    name = materialName
                };
                AssetDatabase.CreateAsset(material, materialPath);
                created = true;
            }

            AssignColor(material, color);

            if (created == false)
            {
                material.name = materialName;
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        private static void AssignTexture(Material material, Texture2D texture)
        {
            if (material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", texture);
            }

            if (material.HasProperty("_MainTex"))
            {
                material.SetTexture("_MainTex", texture);
            }
        }

        private static void AssignColor(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
        }

        private static Shader ResolveShader()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");

            if (shader != null)
            {
                return shader;
            }

            shader = Shader.Find("Unlit/Texture");

            if (shader != null)
            {
                return shader;
            }

            shader = Shader.Find("Standard");

            if (shader != null)
            {
                return shader;
            }

            throw new FileNotFoundException("No compatible shader found for Tank Arena material creation.");
        }

        private static PanelSettings EnsurePanelSettings()
        {
            PanelSettings panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(k_panelSettingsPath);

            if (panelSettings != null)
            {
                return panelSettings;
            }

            EnsureFolderForFile(k_panelSettingsPath);
            panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = new Vector2Int(1920, 1080);
            panelSettings.themeStyleSheet = null;
            AssetDatabase.CreateAsset(panelSettings, k_panelSettingsPath);
            EditorUtility.SetDirty(panelSettings);
            return panelSettings;
        }

        private static void SetRendererMaterial(GameObject gameObject, Material material)
        {
            if (gameObject == null || material == null)
            {
                return;
            }

            Renderer renderer = gameObject.GetComponent<Renderer>();

            if (renderer != null)
            {
                renderer.sharedMaterial = material;
                EditorUtility.SetDirty(renderer);
            }
        }

        private static void SetContextInstallers(OnityContext context, MonoInstaller[] installers)
        {
            SerializedObject serializedObject = new SerializedObject(context);
            SerializedProperty installersProperty = serializedObject.FindProperty("m_installers");
            installersProperty.arraySize = installers.Length;

            for (int i = 0; i < installers.Length; i++)
            {
                installersProperty.GetArrayElementAtIndex(i).objectReferenceValue = installers[i];
            }

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(context);
        }

        private static void SetSerializedObjectReference(UnityEngine.Object target, string propertyPath, UnityEngine.Object value)
        {
            SerializedObject serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(propertyPath);

            if (property == null)
            {
                throw new FileNotFoundException($"Could not find serialized property '{propertyPath}' on '{target.name}'.");
            }

            property.objectReferenceValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        private static void SetSerializedInt(UnityEngine.Object target, string propertyPath, int value)
        {
            SerializedObject serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(propertyPath);

            if (property == null)
            {
                throw new FileNotFoundException($"Could not find serialized property '{propertyPath}' on '{target.name}'.");
            }

            property.intValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        private static void SetSerializedFloat(UnityEngine.Object target, string propertyPath, float value)
        {
            SerializedObject serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(propertyPath);

            if (property == null)
            {
                throw new FileNotFoundException($"Could not find serialized property '{propertyPath}' on '{target.name}'.");
            }

            property.floatValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        private static void SaveScene(Scene scene, string scenePath)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            bool saved = EditorSceneManager.SaveScene(scene, scenePath);

            if (saved == false)
            {
                throw new IOException($"Failed to save generated scene '{scenePath}'.");
            }
        }

        private static void EnsureFolderForFile(string filePath)
        {
            string normalizedPath = filePath.Replace('\\', '/');
            int lastSlashIndex = normalizedPath.LastIndexOf('/');

            if (lastSlashIndex <= 0)
            {
                return;
            }

            string directoryPath = normalizedPath.Substring(0, lastSlashIndex);

            if (AssetDatabase.IsValidFolder(directoryPath))
            {
                return;
            }

            string[] parts = directoryPath.Split('/');

            if (parts.Length == 0 || parts[0] != "Assets")
            {
                return;
            }

            string currentPath = parts[0];

            for (int i = 1; i < parts.Length; i++)
            {
                string nextPath = currentPath + "/" + parts[i];

                if (AssetDatabase.IsValidFolder(nextPath) == false)
                {
                    AssetDatabase.CreateFolder(currentPath, parts[i]);
                }

                currentPath = nextPath;
            }
        }
    }
}

