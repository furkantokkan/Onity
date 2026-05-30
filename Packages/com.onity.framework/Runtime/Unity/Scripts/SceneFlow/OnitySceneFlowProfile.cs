using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace Onity.Unity.SceneFlow
{
    /// <summary>
    /// Shared scene-flow profile used by SEP runtime state transitions.
    /// Bootstrap and Loading remain optional singleton groups.
    /// MainMenuHub and Gameplay can each contain multiple grouped scenes.
    /// </summary>
    [CreateAssetMenu(
        fileName = "OnitySceneFlowProfile",
        menuName = "Onity/Scene Flow/Profile")]
    public sealed class OnitySceneFlowProfile : ScriptableObject
    {
        [Header("Routing")]
        [Tooltip("Route transitions through Loading scene before opening target gameplay/menu scene.")]
        [SerializeField] private bool m_routeTransitionsThroughLoadingScene = true;

        [Header("Scene Names")]
        [Tooltip("Bootstrap scene name in Build Settings.")]
        [SerializeField] private string m_bootstrapSceneName = "BostrapScene - 1";

        [Tooltip("Loading scene name in Build Settings.")]
        [SerializeField] private string m_loadingSceneName = "LoadingScene";

        [Header("Grouped Scene Defaults")]
        [Tooltip("Default main menu scene name in Build Settings.")]
        [FormerlySerializedAs("m_mainMenuSceneName")]
        [SerializeField] private string m_defaultMainMenuSceneName = "MainMenuHub - 2";

        [Tooltip("Default gameplay scene name in Build Settings.")]
        [FormerlySerializedAs("m_gameplaySceneName")]
        [SerializeField] private string m_defaultGameplaySceneName = "GameModeOrGameScene - 3";

        [Header("Grouped Scene Lists")]
        [Tooltip("All grouped main menu scenes. Default scene is selected from this list.")]
        [SerializeField] private List<string> m_mainMenuSceneNames = new List<string>();

        [Tooltip("All grouped gameplay scenes. Default scene is selected from this list.")]
        [SerializeField] private List<string> m_gameplaySceneNames = new List<string>();

        /// <summary>
        /// Gets whether transitions should route through loading scene by default.
        /// </summary>
        public bool RouteTransitionsThroughLoadingScene => m_routeTransitionsThroughLoadingScene;

        /// <summary>
        /// Gets grouped main menu scene names in configured order.
        /// </summary>
        public IReadOnlyList<string> MainMenuSceneNames => m_mainMenuSceneNames;

        /// <summary>
        /// Gets grouped gameplay scene names in configured order.
        /// </summary>
        public IReadOnlyList<string> GameplaySceneNames => m_gameplaySceneNames;

        private void OnValidate()
        {
            NormalizeSceneGroups();
        }

        /// <summary>
        /// Sets loading-scene routing behavior for this profile.
        /// </summary>
        /// <param name="enabled">True when loading-scene routing is enabled.</param>
        public void SetRouteTransitionsThroughLoadingScene(bool enabled)
        {
            m_routeTransitionsThroughLoadingScene = enabled;
        }

        /// <summary>
        /// Tries to resolve a scene name for the given state id.
        /// </summary>
        /// <param name="stateId">State id.</param>
        /// <param name="sceneName">Resolved scene name.</param>
        /// <returns>True when mapping exists and scene name is not empty.</returns>
        public bool TryGetSceneName(OnitySceneFlowStateId stateId, out string sceneName)
        {
            sceneName = GetSceneName(stateId);
            return string.IsNullOrWhiteSpace(sceneName) == false;
        }

        /// <summary>
        /// Returns scene name for the given state id.
        /// </summary>
        /// <param name="stateId">State id.</param>
        /// <returns>Mapped scene name or empty string.</returns>
        public string GetSceneName(OnitySceneFlowStateId stateId)
        {
            switch (stateId)
            {
                case OnitySceneFlowStateId.Bootstrap:
                    return m_bootstrapSceneName;

                case OnitySceneFlowStateId.Loading:
                    return m_loadingSceneName;

                case OnitySceneFlowStateId.MainMenuHub:
                    return ResolveDefaultSceneName(m_defaultMainMenuSceneName, m_mainMenuSceneNames);

                case OnitySceneFlowStateId.Gameplay:
                    return ResolveDefaultSceneName(m_defaultGameplaySceneName, m_gameplaySceneNames);

                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// Copies all scene names for the requested scene group into the supplied list.
        /// </summary>
        /// <param name="stateId">Scene group id.</param>
        /// <param name="results">Output list cleared then populated in profile order.</param>
        public void CopySceneNames(OnitySceneFlowStateId stateId, List<string> results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            results.Clear();

            switch (stateId)
            {
                case OnitySceneFlowStateId.Bootstrap:
                    AddIfValid(results, m_bootstrapSceneName);
                    return;

                case OnitySceneFlowStateId.Loading:
                    AddIfValid(results, m_loadingSceneName);
                    return;

                case OnitySceneFlowStateId.MainMenuHub:
                    CopyGroupedSceneNames(m_mainMenuSceneNames, m_defaultMainMenuSceneName, results);
                    return;

                case OnitySceneFlowStateId.Gameplay:
                    CopyGroupedSceneNames(m_gameplaySceneNames, m_defaultGameplaySceneName, results);
                    return;
            }
        }

        /// <summary>
        /// Tries to resolve state id from scene name.
        /// </summary>
        /// <param name="sceneName">Scene name.</param>
        /// <param name="stateId">Resolved state id.</param>
        /// <returns>True when scene name matches known state.</returns>
        public bool TryGetStateId(string sceneName, out OnitySceneFlowStateId stateId)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                stateId = OnitySceneFlowStateId.Unknown;
                return false;
            }

            if (string.Equals(sceneName, m_bootstrapSceneName, StringComparison.Ordinal))
            {
                stateId = OnitySceneFlowStateId.Bootstrap;
                return true;
            }

            if (string.Equals(sceneName, m_loadingSceneName, StringComparison.Ordinal))
            {
                stateId = OnitySceneFlowStateId.Loading;
                return true;
            }

            if (ContainsSceneName(m_mainMenuSceneNames, m_defaultMainMenuSceneName, sceneName))
            {
                stateId = OnitySceneFlowStateId.MainMenuHub;
                return true;
            }

            if (ContainsSceneName(m_gameplaySceneNames, m_defaultGameplaySceneName, sceneName))
            {
                stateId = OnitySceneFlowStateId.Gameplay;
                return true;
            }

            stateId = OnitySceneFlowStateId.Unknown;
            return false;
        }

        /// <summary>
        /// Updates scene name mapped to the given state id.
        /// </summary>
        /// <param name="stateId">State id to update.</param>
        /// <param name="sceneName">Scene name to assign.</param>
        public void SetSceneName(OnitySceneFlowStateId stateId, string sceneName)
        {
            string value = sceneName ?? string.Empty;

            switch (stateId)
            {
                case OnitySceneFlowStateId.Bootstrap:
                    m_bootstrapSceneName = value;
                    break;

                case OnitySceneFlowStateId.Loading:
                    m_loadingSceneName = value;
                    break;

                case OnitySceneFlowStateId.MainMenuHub:
                    m_defaultMainMenuSceneName = value;
                    AddUniqueSceneName(m_mainMenuSceneNames, value);
                    break;

                case OnitySceneFlowStateId.Gameplay:
                    m_defaultGameplaySceneName = value;
                    AddUniqueSceneName(m_gameplaySceneNames, value);
                    break;
            }

            NormalizeSceneGroups();
        }

        /// <summary>
        /// Replaces grouped scene names for the requested scene group.
        /// Bootstrap and Loading accept the first non-empty value only.
        /// </summary>
        /// <param name="stateId">Scene group id.</param>
        /// <param name="sceneNames">Scene names to assign.</param>
        public void SetSceneNames(OnitySceneFlowStateId stateId, IEnumerable<string> sceneNames)
        {
            switch (stateId)
            {
                case OnitySceneFlowStateId.Bootstrap:
                    m_bootstrapSceneName = GetFirstSceneName(sceneNames);
                    break;

                case OnitySceneFlowStateId.Loading:
                    m_loadingSceneName = GetFirstSceneName(sceneNames);
                    break;

                case OnitySceneFlowStateId.MainMenuHub:
                    ReplaceSceneNames(m_mainMenuSceneNames, sceneNames);
                    break;

                case OnitySceneFlowStateId.Gameplay:
                    ReplaceSceneNames(m_gameplaySceneNames, sceneNames);
                    break;
            }

            NormalizeSceneGroups();
        }

        /// <summary>
        /// Sets default grouped scene for the requested scene group.
        /// Only MainMenuHub and Gameplay use grouped defaults.
        /// </summary>
        /// <param name="stateId">Scene group id.</param>
        /// <param name="sceneName">Default scene name.</param>
        public void SetDefaultSceneName(OnitySceneFlowStateId stateId, string sceneName)
        {
            string value = SanitizeSceneName(sceneName);

            switch (stateId)
            {
                case OnitySceneFlowStateId.MainMenuHub:
                    m_defaultMainMenuSceneName = value;
                    AddUniqueSceneName(m_mainMenuSceneNames, value);
                    break;

                case OnitySceneFlowStateId.Gameplay:
                    m_defaultGameplaySceneName = value;
                    AddUniqueSceneName(m_gameplaySceneNames, value);
                    break;
            }

            NormalizeSceneGroups();
        }

        /// <summary>
        /// Tries to resolve startup scene in preferred order.
        /// Bootstrap is preferred, then default main menu, then default gameplay, then loading.
        /// </summary>
        /// <param name="sceneName">Resolved startup scene name.</param>
        /// <returns>True when any startup candidate exists.</returns>
        public bool TryGetStartupSceneName(out string sceneName)
        {
            if (TryResolveSceneName(m_bootstrapSceneName, out sceneName))
            {
                return true;
            }

            if (TryGetSceneName(OnitySceneFlowStateId.MainMenuHub, out sceneName))
            {
                return true;
            }

            if (TryGetSceneName(OnitySceneFlowStateId.Gameplay, out sceneName))
            {
                return true;
            }

            return TryResolveSceneName(m_loadingSceneName, out sceneName);
        }

        private void NormalizeSceneGroups()
        {
            m_bootstrapSceneName = SanitizeSceneName(m_bootstrapSceneName);
            m_loadingSceneName = SanitizeSceneName(m_loadingSceneName);
            NormalizeGroupedScenes(m_mainMenuSceneNames, ref m_defaultMainMenuSceneName);
            NormalizeGroupedScenes(m_gameplaySceneNames, ref m_defaultGameplaySceneName);
        }

        private static void NormalizeGroupedScenes(List<string> sceneNames, ref string defaultSceneName)
        {
            defaultSceneName = SanitizeSceneName(defaultSceneName);
            RemoveInvalidAndDuplicateSceneNames(sceneNames);

            if (string.IsNullOrWhiteSpace(defaultSceneName) == false)
            {
                AddUniqueSceneName(sceneNames, defaultSceneName);
            }

            if (string.IsNullOrWhiteSpace(defaultSceneName) && sceneNames.Count > 0)
            {
                defaultSceneName = sceneNames[0];
            }
        }

        private static void RemoveInvalidAndDuplicateSceneNames(List<string> sceneNames)
        {
            if (sceneNames == null)
            {
                return;
            }

            HashSet<string> seenNames = new HashSet<string>(StringComparer.Ordinal);

            for (int i = sceneNames.Count - 1; i >= 0; i--)
            {
                string sceneName = SanitizeSceneName(sceneNames[i]);

                if (string.IsNullOrWhiteSpace(sceneName) || seenNames.Add(sceneName) == false)
                {
                    sceneNames.RemoveAt(i);
                    continue;
                }

                sceneNames[i] = sceneName;
            }

            sceneNames.Reverse();
        }

        private static void ReplaceSceneNames(List<string> targetList, IEnumerable<string> sceneNames)
        {
            targetList.Clear();

            if (sceneNames == null)
            {
                return;
            }

            foreach (string sceneName in sceneNames)
            {
                AddUniqueSceneName(targetList, sceneName);
            }
        }

        private static void CopyGroupedSceneNames(
            List<string> groupedSceneNames,
            string defaultSceneName,
            List<string> results)
        {
            AddIfValid(results, defaultSceneName);

            for (int i = 0; i < groupedSceneNames.Count; i++)
            {
                AddUniqueSceneName(results, groupedSceneNames[i]);
            }
        }

        private static bool ContainsSceneName(
            List<string> groupedSceneNames,
            string defaultSceneName,
            string sceneName)
        {
            string value = SanitizeSceneName(sceneName);

            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            if (string.Equals(value, defaultSceneName, StringComparison.Ordinal))
            {
                return true;
            }

            for (int i = 0; i < groupedSceneNames.Count; i++)
            {
                if (string.Equals(groupedSceneNames[i], value, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string ResolveDefaultSceneName(string defaultSceneName, List<string> groupedSceneNames)
        {
            if (string.IsNullOrWhiteSpace(defaultSceneName) == false)
            {
                return defaultSceneName;
            }

            return groupedSceneNames.Count > 0 ? groupedSceneNames[0] : string.Empty;
        }

        private static string GetFirstSceneName(IEnumerable<string> sceneNames)
        {
            if (sceneNames == null)
            {
                return string.Empty;
            }

            foreach (string sceneName in sceneNames)
            {
                string value = SanitizeSceneName(sceneName);

                if (string.IsNullOrWhiteSpace(value) == false)
                {
                    return value;
                }
            }

            return string.Empty;
        }

        private static string SanitizeSceneName(string sceneName)
        {
            return sceneName?.Trim() ?? string.Empty;
        }

        private static bool TryResolveSceneName(string sceneName, out string result)
        {
            result = SanitizeSceneName(sceneName);
            return string.IsNullOrWhiteSpace(result) == false;
        }

        private static void AddIfValid(List<string> results, string sceneName)
        {
            string value = SanitizeSceneName(sceneName);

            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            results.Add(value);
        }

        private static void AddUniqueSceneName(List<string> sceneNames, string sceneName)
        {
            string value = SanitizeSceneName(sceneName);

            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            for (int i = 0; i < sceneNames.Count; i++)
            {
                if (string.Equals(sceneNames[i], value, StringComparison.Ordinal))
                {
                    return;
                }
            }

            sceneNames.Add(value);
        }
    }
}
