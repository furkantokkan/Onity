using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Onity.Unity.SceneFlow
{
    /// <summary>
    /// Scene target + typed payload pair consumed by a loading scene.
    /// </summary>
    public readonly struct OnitySceneTransitionPayload
    {
        /// <summary>
        /// Initializes transition payload.
        /// </summary>
        /// <param name="targetSceneName">Target scene name.</param>
        /// <param name="enterData">Typed scene entry payload.</param>
        public OnitySceneTransitionPayload(
            string targetSceneName,
            IOnitySceneEnterData enterData)
        {
            TargetSceneName = targetSceneName;
            EnterData = enterData;
        }

        /// <summary>
        /// Target scene name.
        /// </summary>
        public string TargetSceneName { get; }

        /// <summary>
        /// Typed scene entry payload.
        /// </summary>
        public IOnitySceneEnterData EnterData { get; }
    }

    /// <summary>
    /// Stores one pending transition target for loading-scene handoff, the enter data of the scene that loads next,
    /// and the enter data <see cref="IOnitySceneService" /> keeps per destination scene.
    /// </summary>
    public static class OnitySceneTransitionStore
    {
        // Enter data per destination scene handle while its scene-service operation runs. Main thread only.
        private static readonly Dictionary<int, IOnitySceneEnterData> s_sceneEnterData =
            new Dictionary<int, IOnitySceneEnterData>(4);

        private static string s_pendingTargetScene;
        private static IOnitySceneEnterData s_pendingEnterData;
        private static IOnitySceneEnterData s_activeEnterData;

        // The scene-service change the Loading scene with this handle takes over. Main thread only.
        private static Scene s_handoffScene;
        private static IOnityLoadingSceneHandoff s_handoff;

        /// <summary>
        /// Pending transition scene name.
        /// </summary>
        public static string PendingTargetScene => s_pendingTargetScene;

        /// <summary>
        /// Pending typed entry payload.
        /// </summary>
        public static IOnitySceneEnterData PendingEnterData => s_pendingEnterData;

        /// <summary>
        /// Stores next transition target and optional typed payload.
        /// </summary>
        /// <param name="targetSceneName">Target scene name.</param>
        /// <param name="enterData">Optional typed scene payload.</param>
        public static void SetNextTarget(
            string targetSceneName,
            IOnitySceneEnterData enterData = null)
        {
            if (string.IsNullOrWhiteSpace(targetSceneName))
            {
                throw new ArgumentException("Target scene name cannot be empty.", nameof(targetSceneName));
            }

            s_pendingTargetScene = targetSceneName;
            s_pendingEnterData = enterData;
        }

        /// <summary>
        /// Consumes pending transition target once.
        /// </summary>
        /// <param name="fallbackSceneName">Fallback target when no request exists.</param>
        /// <returns>Resolved target scene name.</returns>
        public static string ConsumePendingOrDefault(string fallbackSceneName)
        {
            if (TryConsumePendingOrDefault(
                    fallbackSceneName,
                    null,
                    out OnitySceneTransitionPayload payload) == false)
            {
                return fallbackSceneName;
            }

            return payload.TargetSceneName;
        }

        /// <summary>
        /// Consumes pending transition target + payload once.
        /// </summary>
        /// <param name="fallbackSceneName">Fallback target when no request exists.</param>
        /// <param name="fallbackEnterData">Fallback payload when no request exists.</param>
        /// <param name="payload">Resolved transition payload.</param>
        /// <returns>True when payload resolves; otherwise false.</returns>
        public static bool TryConsumePendingOrDefault(
            string fallbackSceneName,
            IOnitySceneEnterData fallbackEnterData,
            out OnitySceneTransitionPayload payload)
        {
            if (string.IsNullOrWhiteSpace(s_pendingTargetScene))
            {
                if (string.IsNullOrWhiteSpace(fallbackSceneName))
                {
                    payload = default;
                    return false;
                }

                payload = new OnitySceneTransitionPayload(fallbackSceneName, fallbackEnterData);
                return true;
            }

            payload = new OnitySceneTransitionPayload(s_pendingTargetScene, s_pendingEnterData);
            s_pendingTargetScene = null;
            s_pendingEnterData = null;
            return true;
        }

        /// <summary>
        /// Stores active payload for the newly loaded target scene.
        /// </summary>
        /// <param name="enterData">Typed scene payload.</param>
        public static void SetActiveEnterData(IOnitySceneEnterData enterData)
        {
            s_activeEnterData = enterData;
        }

        /// <summary>
        /// Consumes active payload for current scene once.
        /// </summary>
        /// <typeparam name="TEnterData">Expected payload type.</typeparam>
        /// <param name="enterData">Resolved payload instance.</param>
        /// <returns>True when matching payload exists; otherwise false.</returns>
        public static bool TryConsumeActiveEnterData<TEnterData>(out TEnterData enterData)
            where TEnterData : class, IOnitySceneEnterData
        {
            if (s_activeEnterData is TEnterData typedData)
            {
                enterData = typedData;
                s_activeEnterData = null;
                return true;
            }

            enterData = null;
            return false;
        }

        /// <summary>
        /// Returns the enter data <see cref="IOnitySceneService" /> registered for <paramref name="scene" />. Call it
        /// with the scene's own handle (<c>gameObject.scene</c>), for example from its installer: the data is set
        /// before the scene wakes and removed when the operation that loads it ends, so copy what the scene needs
        /// during its build. Another scene, even one with the same name, never receives it.
        /// </summary>
        /// <typeparam name="TEnterData">The expected enter data type.</typeparam>
        /// <param name="scene">The scene being installed or built.</param>
        /// <param name="enterData">The enter data, or null when it returns false.</param>
        /// <returns>True while the scene service loads <paramref name="scene" /> with data of that type.</returns>
        public static bool TryGetEnterData<TEnterData>(Scene scene, out TEnterData enterData)
            where TEnterData : class, IOnitySceneEnterData
        {
            if (scene.IsValid()
                && s_sceneEnterData.TryGetValue(scene.handle, out IOnitySceneEnterData data)
                && data is TEnterData typedData)
            {
                enterData = typedData;
                return true;
            }

            enterData = null;
            return false;
        }

        /// <summary>
        /// Clears pending and active transition data, the enter data kept per destination scene and an untaken
        /// Loading-scene handoff.
        /// </summary>
        public static void Clear()
        {
            s_pendingTargetScene = null;
            s_pendingEnterData = null;
            s_activeEnterData = null;
            ClearServiceRecords();
        }

        internal static void SetSceneEnterData(Scene scene, IOnitySceneEnterData enterData)
        {
            if (scene.IsValid() && enterData != null)
            {
                s_sceneEnterData[scene.handle] = enterData;
            }
        }

        // Keyed by handle, so the entry of a scene that already unloaded can still be removed.
        internal static void RemoveSceneEnterData(Scene scene)
        {
            if (scene.handle != 0)
            {
                s_sceneEnterData.Remove(scene.handle);
            }
        }

        // Clears the shared active slot only while it still holds this operation's data.
        internal static void ClearActiveEnterData(IOnitySceneEnterData enterData)
        {
            if (enterData != null && ReferenceEquals(s_activeEnterData, enterData))
            {
                s_activeEnterData = null;
            }
        }

        internal static void SetLoadingSceneHandoff(Scene loadingScene, IOnityLoadingSceneHandoff handoff)
        {
            s_handoffScene = loadingScene;
            s_handoff = handoff;
        }

        /// <summary>
        /// Takes the scene-service change registered for <paramref name="loadingScene" />, once. A Loading scene
        /// that finds none runs the uncorrelated pending-target flow.
        /// </summary>
        /// <param name="loadingScene">The Loading scene asking, by its own handle.</param>
        /// <param name="handoff">The change, or null when it returns false.</param>
        /// <returns>True when a change waits for exactly this scene.</returns>
        internal static bool TryTakeLoadingSceneHandoff(Scene loadingScene, out IOnityLoadingSceneHandoff handoff)
        {
            if (s_handoff == null || loadingScene.IsValid() == false || s_handoffScene != loadingScene)
            {
                handoff = null;
                return false;
            }

            handoff = s_handoff;
            s_handoff = null;
            s_handoffScene = default;
            return true;
        }

        // Removes an untaken handoff only while it is still this operation's.
        internal static void ClearLoadingSceneHandoff(IOnityLoadingSceneHandoff handoff)
        {
            if (handoff != null && ReferenceEquals(s_handoff, handoff))
            {
                s_handoff = null;
                s_handoffScene = default;
            }
        }

        // Clears the scene-service records left from an earlier Play session when domain reload is disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ClearServiceRecords()
        {
            s_sceneEnterData.Clear();
            s_handoff = null;
            s_handoffScene = default;
        }
    }
}
