using System;

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
    /// Stores one pending transition target for loading-scene handoff.
    /// </summary>
    public static class OnitySceneTransitionStore
    {
        private static string s_pendingTargetScene;
        private static IOnitySceneEnterData s_pendingEnterData;
        private static IOnitySceneEnterData s_activeEnterData;

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
        /// Clears pending and active transition data.
        /// </summary>
        public static void Clear()
        {
            s_pendingTargetScene = null;
            s_pendingEnterData = null;
            s_activeEnterData = null;
        }
    }
}
