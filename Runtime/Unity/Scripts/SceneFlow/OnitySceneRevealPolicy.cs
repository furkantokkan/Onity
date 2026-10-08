using System;
using UnityEngine;

namespace Onity.Unity.SceneFlow
{
    /// <summary>
    /// Decides when a cover may lift after a scene change: the new scene has finished building, a number of frames
    /// have settled after that, or the maximum wait has passed so a scene that never finishes cannot keep the screen
    /// covered.
    /// </summary>
    [Serializable]
    public sealed class OnitySceneRevealPolicy
    {
        /// <summary>
        /// Default settle frames: one frame, so the first frame after a heavy build does not start the reveal.
        /// </summary>
        public const int DefaultSettleFrames = 1;

        /// <summary>
        /// Default maximum wait in unscaled seconds.
        /// </summary>
        public const float DefaultMaxWaitSeconds = 10f;

        [Tooltip("Frames to wait after the scene is ready before the cover may lift. 0 reveals on the ready frame.")]
        [SerializeField] private int m_settleFrames = DefaultSettleFrames;

        [Tooltip("Unscaled seconds after which the cover lifts even when the scene never reports ready.")]
        [SerializeField] private float m_maxWaitSeconds = DefaultMaxWaitSeconds;

        /// <summary>
        /// Initializes a policy with the default settle frames and maximum wait.
        /// </summary>
        public OnitySceneRevealPolicy()
        {
        }

        /// <summary>
        /// Initializes a policy.
        /// </summary>
        /// <param name="settleFrames">Frames to wait after readiness; 0 reveals on the frame readiness is seen.</param>
        /// <param name="maxWaitSeconds">
        /// Unscaled seconds after which the scene is revealed even when it never reports ready.
        /// <see cref="float.PositiveInfinity" /> waits without a limit.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="settleFrames" /> is negative, or <paramref name="maxWaitSeconds" /> is not positive.
        /// </exception>
        public OnitySceneRevealPolicy(int settleFrames, float maxWaitSeconds)
        {
            if (settleFrames < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(settleFrames), settleFrames,
                    "Settle frames cannot be negative.");
            }

            if (float.IsNaN(maxWaitSeconds) || maxWaitSeconds <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(maxWaitSeconds), maxWaitSeconds,
                    "The maximum wait must be positive.");
            }

            m_settleFrames = settleFrames;
            m_maxWaitSeconds = maxWaitSeconds;
        }

        /// <summary>
        /// Gets the frames to wait after the scene is ready.
        /// </summary>
        public int SettleFrames => m_settleFrames;

        /// <summary>
        /// Gets the unscaled seconds after which the scene is revealed even when it never reports ready.
        /// </summary>
        public float MaxWaitSeconds => m_maxWaitSeconds;

        /// <summary>
        /// Returns true when the cover may start to lift. <see cref="OnityActiveSceneReadiness" /> asks once per
        /// frame and reveals on the first true.
        /// </summary>
        /// <param name="isSceneReady">
        /// The active scene's <c>SceneContext</c> finished its build. A failed or destroyed build counts as finished.
        /// </param>
        /// <param name="framesSinceReady">Frames started since readiness was first seen: 0 on that frame.</param>
        /// <param name="secondsWaited">Unscaled seconds since the wait started.</param>
        /// <returns>True when the scene may be revealed.</returns>
        public bool ShouldReveal(bool isSceneReady, int framesSinceReady, float secondsWaited)
        {
            if (secondsWaited >= m_maxWaitSeconds)
            {
                return true;
            }

            return isSceneReady && framesSinceReady >= m_settleFrames;
        }
    }
}
