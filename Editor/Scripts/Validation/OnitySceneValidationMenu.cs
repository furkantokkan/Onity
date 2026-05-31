using System;
using System.Collections.Generic;
using System.IO;
using Onity.Unity.Contexts;
using Onity.Unity.Installers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Onity.Editor.Validation
{
    /// <summary>
    /// Scene validation commands for Onity contexts and installers.
    /// </summary>
    public static class OnitySceneValidationMenu
    {
        private const string k_validateSceneMenuPath = "Onity/Validation/Validate Scene";
        private const string k_validateAllScenesMenuPath = "Onity/Validation/Validate All Scenes";
        private const string k_dialogTitle = "Onity Scene Validation";
        private const int k_validateScenePriority = 2200;
        private const int k_validateAllScenesPriority = 2201;

        [MenuItem(k_validateSceneMenuPath, false, k_validateScenePriority)]
        private static void ValidateSceneMenu()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            List<ValidationIssue> issues = new List<ValidationIssue>(64);
            ValidateLoadedScene(activeScene, issues);
            ReportResult($"Validate Scene: {GetSceneDisplayName(activeScene)}", issues);
        }

        [MenuItem(k_validateSceneMenuPath, true)]
        private static bool ValidateSceneMenuEnabled()
        {
            return SceneManager.GetActiveScene().IsValid();
        }

        [MenuItem(k_validateAllScenesMenuPath, false, k_validateAllScenesPriority)]
        private static void ValidateAllScenesMenu()
        {
            string[] scenePaths = GetEnabledBuildScenePaths();

            if (scenePaths.Length == 0)
            {
                EditorUtility.DisplayDialog(
                    k_dialogTitle,
                    "No enabled scenes found in Build Settings.",
                    "OK");
                return;
            }

            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo() == false)
            {
                return;
            }

            SceneSetup[] initialSetup = EditorSceneManager.GetSceneManagerSetup();
            List<ValidationIssue> issues = new List<ValidationIssue>(256);

            try
            {
                for (int i = 0; i < scenePaths.Length; i++)
                {
                    string scenePath = scenePaths[i];
                    float progress = (i + 1f) / scenePaths.Length;
                    string sceneName = Path.GetFileNameWithoutExtension(scenePath);
                    EditorUtility.DisplayProgressBar(
                        k_dialogTitle,
                        $"Validating scene {i + 1}/{scenePaths.Length}: {sceneName}",
                        progress);

                    Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                    ValidateLoadedScene(scene, issues);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                EditorSceneManager.RestoreSceneManagerSetup(initialSetup);
            }

            ReportResult($"Validate All Scenes ({scenePaths.Length})", issues);
        }

        [MenuItem(k_validateAllScenesMenuPath, true)]
        private static bool ValidateAllScenesMenuEnabled()
        {
            return GetEnabledBuildScenePaths().Length > 0;
        }

        private static void ValidateLoadedScene(Scene scene, List<ValidationIssue> issues)
        {
            string scenePath = scene.path;

            if (scene.IsValid() == false)
            {
                AddIssue(issues, ValidationSeverity.Error, scenePath, "Scene is not valid.", null);
                return;
            }

            if (scene.isLoaded == false)
            {
                AddIssue(issues, ValidationSeverity.Error, scenePath, "Scene is not loaded.", null);
                return;
            }

            List<OnityContext> contexts = GatherContexts(scene);

            if (contexts.Count == 0)
            {
                AddIssue(
                    issues,
                    ValidationSeverity.Warning,
                    scenePath,
                    "No Onity context found in scene.",
                    null);
                return;
            }

            int projectContextCount = 0;
            int sceneContextCount = 0;

            for (int i = 0; i < contexts.Count; i++)
            {
                OnityContext context = contexts[i];

                if (context is ProjectContext)
                {
                    projectContextCount++;
                }

                if (context is SceneContext)
                {
                    sceneContextCount++;
                }

                ValidateContext(scene, context, issues);
            }

            if (projectContextCount > 1)
            {
                AddIssue(
                    issues,
                    ValidationSeverity.Error,
                    scenePath,
                    $"Multiple ProjectContext instances found ({projectContextCount}).",
                    null);
            }

            if (sceneContextCount > 1)
            {
                AddIssue(
                    issues,
                    ValidationSeverity.Warning,
                    scenePath,
                    $"Multiple SceneContext instances found ({sceneContextCount}).",
                    null);
            }
        }

        private static void ValidateContext(Scene scene, OnityContext context, List<ValidationIssue> issues)
        {
            if (context == null)
            {
                return;
            }

            string scenePath = scene.path;
            ValidateMissingScripts(scenePath, context, issues);
            ValidateInstallerList(scenePath, context, issues);
            ValidateParentReference(scenePath, context, issues);
            ValidateNestedContextAutoInject(scenePath, context, issues);

            if (context is SceneContext sceneContext)
            {
                ValidateSceneContext(scenePath, sceneContext, issues);
            }
            else if (context is GameObjectContext gameObjectContext)
            {
                ValidateGameObjectContext(scenePath, gameObjectContext, issues);
            }
        }

        private static void ValidateMissingScripts(string scenePath, OnityContext context, List<ValidationIssue> issues)
        {
            Component[] components = context.GetComponents<Component>();

            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] != null)
                {
                    continue;
                }

                AddIssue(
                    issues,
                    ValidationSeverity.Error,
                    scenePath,
                    $"Missing script on '{GetHierarchyPath(context.transform)}'.",
                    context.gameObject);
            }
        }

        private static void ValidateInstallerList(string scenePath, OnityContext context, List<ValidationIssue> issues)
        {
            SerializedObject serializedContext = new SerializedObject(context);
            SerializedProperty installersProperty = serializedContext.FindProperty("m_installers");

            if (installersProperty == null || installersProperty.isArray == false)
            {
                return;
            }

            HashSet<int> installerIds = new HashSet<int>();

            for (int i = 0; i < installersProperty.arraySize; i++)
            {
                SerializedProperty installerProperty = installersProperty.GetArrayElementAtIndex(i);
                UnityEngine.Object installerObject = installerProperty.objectReferenceValue;

                if (installerObject == null)
                {
                    AddIssue(
                        issues,
                        ValidationSeverity.Warning,
                        scenePath,
                        $"Null installer entry at index {i} on '{GetHierarchyPath(context.transform)}'.",
                        context);
                    continue;
                }

                if (installerObject is MonoInstaller installer == false)
                {
                    AddIssue(
                        issues,
                        ValidationSeverity.Error,
                        scenePath,
                        $"Installer entry at index {i} is not a MonoInstaller.",
                        context);
                    continue;
                }

                if (installer.gameObject.scene != context.gameObject.scene)
                {
                    AddIssue(
                        issues,
                        ValidationSeverity.Warning,
                        scenePath,
                        $"Installer '{installer.name}' references another scene.",
                        installer);
                }

                int installerId = installer.GetInstanceID();

                if (installerIds.Add(installerId) == false)
                {
                    AddIssue(
                        issues,
                        ValidationSeverity.Warning,
                        scenePath,
                        $"Duplicate installer reference '{installer.name}' on context '{context.name}'.",
                        context);
                }
            }
        }

        private static void ValidateParentReference(string scenePath, OnityContext context, List<ValidationIssue> issues)
        {
            SerializedObject serializedContext = new SerializedObject(context);
            SerializedProperty parentContextProperty = serializedContext.FindProperty("m_parentContext");

            if (parentContextProperty == null)
            {
                return;
            }

            if (parentContextProperty.objectReferenceValue is OnityContext parentContext == false)
            {
                return;
            }

            if (ReferenceEquals(parentContext, context))
            {
                AddIssue(
                    issues,
                    ValidationSeverity.Error,
                    scenePath,
                    "Context cannot reference itself as parent.",
                    context);
            }
        }

        private static void ValidateNestedContextAutoInject(string scenePath, OnityContext context, List<ValidationIssue> issues)
        {
            SerializedObject serializedContext = new SerializedObject(context);
            SerializedProperty autoInjectProperty = serializedContext.FindProperty("m_autoInjectHierarchy");

            if (autoInjectProperty == null || autoInjectProperty.boolValue == false)
            {
                return;
            }

            OnityContext[] nestedContexts = context.GetComponentsInChildren<OnityContext>(true);

            for (int i = 0; i < nestedContexts.Length; i++)
            {
                OnityContext nestedContext = nestedContexts[i];

                if (nestedContext == null || ReferenceEquals(nestedContext, context))
                {
                    continue;
                }

                AddIssue(
                    issues,
                    ValidationSeverity.Warning,
                    scenePath,
                    $"AutoInjectHierarchy is enabled while nested context '{nestedContext.name}' exists under '{context.name}'.",
                    context);
                return;
            }
        }

        private static void ValidateSceneContext(string scenePath, SceneContext context, List<ValidationIssue> issues)
        {
            SerializedObject serializedContext = new SerializedObject(context);
            SerializedProperty projectContextProperty = serializedContext.FindProperty("m_projectContext");

            if (projectContextProperty?.objectReferenceValue != null)
            {
                return;
            }

            ProjectContext[] projectContexts = UnityEngine.Object.FindObjectsByType<ProjectContext>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            if (projectContexts.Length > 0)
            {
                return;
            }

            ProjectContext resourcesProjectContext = Resources.Load<ProjectContext>(ProjectContextBootstrap.ResourcePath);

            if (resourcesProjectContext != null)
            {
                return;
            }

            AddIssue(
                issues,
                ValidationSeverity.Warning,
                scenePath,
                $"SceneContext '{context.name}' has no explicit ProjectContext reference, no loaded ProjectContext, and no prefab at Resources/{ProjectContextBootstrap.ResourcePath}.",
                context);
        }

        private static void ValidateGameObjectContext(string scenePath, GameObjectContext context, List<ValidationIssue> issues)
        {
            SerializedObject serializedContext = new SerializedObject(context);
            SerializedProperty parentContextProperty = serializedContext.FindProperty("m_parentContext");
            SerializedProperty findParentProperty = serializedContext.FindProperty("m_findParentInHierarchy");
            SerializedProperty fallbackProperty = serializedContext.FindProperty("m_fallbackToProjectContext");

            bool hasExplicitParent = parentContextProperty?.objectReferenceValue != null;
            bool findParentInHierarchy = findParentProperty?.boolValue ?? true;
            bool fallbackToProject = fallbackProperty?.boolValue ?? true;

            if (hasExplicitParent || findParentInHierarchy || fallbackToProject)
            {
                return;
            }

            AddIssue(
                issues,
                ValidationSeverity.Warning,
                scenePath,
                $"GameObjectContext '{context.name}' is isolated (no explicit parent, hierarchy search disabled, project fallback disabled).",
                context);
        }

        private static List<OnityContext> GatherContexts(Scene scene)
        {
            List<OnityContext> contexts = new List<OnityContext>(16);
            GameObject[] rootObjects = scene.GetRootGameObjects();

            for (int i = 0; i < rootObjects.Length; i++)
            {
                rootObjects[i].GetComponentsInChildren(true, contexts);
            }

            return contexts;
        }

        private static string[] GetEnabledBuildScenePaths()
        {
            EditorBuildSettingsScene[] buildScenes = EditorBuildSettings.scenes;
            List<string> scenePaths = new List<string>(buildScenes.Length);

            for (int i = 0; i < buildScenes.Length; i++)
            {
                EditorBuildSettingsScene buildScene = buildScenes[i];

                if (buildScene.enabled == false || string.IsNullOrWhiteSpace(buildScene.path))
                {
                    continue;
                }

                scenePaths.Add(buildScene.path);
            }

            return scenePaths.ToArray();
        }

        private static void ReportResult(string title, List<ValidationIssue> issues)
        {
            int errorCount = 0;
            int warningCount = 0;
            int infoCount = 0;

            for (int i = 0; i < issues.Count; i++)
            {
                ValidationIssue issue = issues[i];
                string prefix = $"[Onity Validation] [{issue.Severity}] [{issue.ScenePath}]";
                string message = $"{prefix} {issue.Message}";

                switch (issue.Severity)
                {
                    case ValidationSeverity.Error:
                        errorCount++;
                        Debug.LogError(message, issue.Context);
                        break;
                    case ValidationSeverity.Warning:
                        warningCount++;
                        Debug.LogWarning(message, issue.Context);
                        break;
                    default:
                        infoCount++;
                        Debug.Log(message, issue.Context);
                        break;
                }
            }

            string summary =
                $"{title}\n" +
                $"Errors: {errorCount}\n" +
                $"Warnings: {warningCount}\n" +
                $"Info: {infoCount}\n\n" +
                "See Console for details.";

            EditorUtility.DisplayDialog(k_dialogTitle, summary, "OK");
        }

        private static void AddIssue(
            List<ValidationIssue> issues,
            ValidationSeverity severity,
            string scenePath,
            string message,
            UnityEngine.Object context)
        {
            issues.Add(new ValidationIssue(severity, scenePath, message, context));
        }

        private static string GetSceneDisplayName(Scene scene)
        {
            if (scene.IsValid() == false)
            {
                return "InvalidScene";
            }

            return string.IsNullOrEmpty(scene.path) ? scene.name : scene.path;
        }

        private static string GetHierarchyPath(Transform transform)
        {
            string path = transform.name;
            Transform current = transform.parent;

            while (current != null)
            {
                path = $"{current.name}/{path}";
                current = current.parent;
            }

            return path;
        }

        private enum ValidationSeverity
        {
            Info,
            Warning,
            Error
        }

        private readonly struct ValidationIssue
        {
            public readonly ValidationSeverity Severity;
            public readonly string ScenePath;
            public readonly string Message;
            public readonly UnityEngine.Object Context;

            public ValidationIssue(
                ValidationSeverity severity,
                string scenePath,
                string message,
                UnityEngine.Object context)
            {
                Severity = severity;
                ScenePath = string.IsNullOrEmpty(scenePath) ? "<NoScenePath>" : scenePath;
                Message = message;
                Context = context;
            }
        }
    }
}
