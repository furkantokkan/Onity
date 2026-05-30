using Onity.DI;
using UnityEngine;

namespace Onity.Samples.RollABall
{
    /// <summary>
    /// Simple smooth follow camera for Roll-a-Ball player.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RollABallCameraFollow : MonoBehaviour
    {
        [Inject]
        private RollABallGameSettings m_settings = null;

        [Tooltip("Target transform followed by camera.")]
        [SerializeField] private Transform m_target;

        [Tooltip("Fallback follow offset used when settings are unavailable.")]
        [SerializeField] private Vector3 m_fallbackOffset = new Vector3(0f, 9f, -6f);

        [Tooltip("Fallback smoothing time for camera movement.")]
        [SerializeField] private float m_fallbackSmoothTime = 0.12f;

        [Tooltip("Fallback look interpolation speed.")]
        [SerializeField] private float m_fallbackLookLerpSpeed = 8f;

        [Tooltip("When enabled, camera resolves target by Player tag if none is assigned.")]
        [SerializeField] private bool m_findTargetByTag = true;

        private Vector3 m_positionVelocity;
        private bool m_hasSnapToTarget;

        /// <summary>
        /// Assigns follow target.
        /// </summary>
        /// <param name="target">Target transform.</param>
        public void SetTarget(Transform target)
        {
            m_target = target;
            m_hasSnapToTarget = false;
        }

        /// <summary>
        /// Assigns settings reference from runtime bootstrap.
        /// </summary>
        /// <param name="settings">Roll-a-Ball settings asset.</param>
        public void ApplySettings(RollABallGameSettings settings)
        {
            m_settings = settings;
        }

        private void LateUpdate()
        {
            ResolveTargetIfNeeded();

            if (m_target == null)
            {
                return;
            }

            Vector3 followOffset = m_settings != null
                ? m_settings.CameraFollowOffset
                : m_fallbackOffset;
            float smoothTime = m_settings != null
                ? Mathf.Max(0.01f, m_settings.CameraPositionSmoothTime)
                : Mathf.Max(0.01f, m_fallbackSmoothTime);
            float lookLerpSpeed = m_settings != null
                ? Mathf.Max(0.1f, m_settings.CameraLookLerpSpeed)
                : Mathf.Max(0.1f, m_fallbackLookLerpSpeed);
            Vector3 desiredPosition = m_target.position + followOffset;

            if (m_hasSnapToTarget == false)
            {
                transform.position = desiredPosition;
                m_positionVelocity = Vector3.zero;
                m_hasSnapToTarget = true;
            }
            else
            {
                transform.position = Vector3.SmoothDamp(
                    transform.position,
                    desiredPosition,
                    ref m_positionVelocity,
                    smoothTime);
            }

            Vector3 lookDirection = m_target.position - transform.position;

            if (lookDirection.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            Quaternion lookRotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
            float lerp = 1f - Mathf.Exp(-lookLerpSpeed * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, lookRotation, lerp);
        }

        private void ResolveTargetIfNeeded()
        {
            if (m_target != null || m_findTargetByTag == false)
            {
                return;
            }

            GameObject player = GameObject.FindGameObjectWithTag("Player");

            if (player == null)
            {
                return;
            }

            m_target = player.transform;
            m_hasSnapToTarget = false;
        }
    }
}
