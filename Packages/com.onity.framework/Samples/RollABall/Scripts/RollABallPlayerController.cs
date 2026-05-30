using System;
using System.Reflection;
using Onity.DI;
#if ONITY_UNITY_PHYSICS
using Onity.Unity.Physics;
#endif
using UnityEngine;
using UnityEngine.Rendering;

namespace Onity.Samples.RollABall
{
    /// <summary>
    /// Roll-a-Ball controller with package-aware non-alloc physics probe and trail VFX.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public sealed class RollABallPlayerController : MonoBehaviour
    {
        private const string k_rotationIndicatorName = "RotationIndicator";
        private const string k_cameraFollowTypeName = "Onity.Samples.RollABall.RollABallCameraFollow, Onity.Samples";
        private static readonly Color k_defaultTrailColor = new Color(0.23f, 0.8f, 1f, 0.95f);
        private static readonly Color k_rotationIndicatorColor = new Color(1f, 0.92f, 0.45f, 1f);

        [Inject]
        private RollABallGameSettings m_settings = null;

        [Header("Ground Probe")]
        [Tooltip("Mask used by grounded raycast probe.")]
        [SerializeField] private LayerMask m_groundMask = ~0;

        [Tooltip("When enabled, movement acceleration is reduced while airborne.")]
        [SerializeField] private bool m_useGroundProbe = true;

        [Tooltip("Fallback probe distance used before settings injection is available.")]
        [SerializeField] private float m_fallbackGroundProbeDistance = 1.05f;

        [Header("Trail VFX")]
        [Tooltip("Optional prebuilt trail renderer. Auto-created if empty.")]
        [SerializeField] private TrailRenderer m_trailRenderer;

        [Tooltip("Optional trail material. If empty, one is generated at runtime.")]
        [SerializeField] private Material m_trailMaterial;

        [Header("Rotation Marker")]
        [Tooltip("Optional marker showing sphere rotation. Auto-created if empty.")]
        [SerializeField] private Transform m_rotationIndicator;

        [Tooltip("Local position of rotation marker.")]
        [SerializeField] private Vector3 m_rotationIndicatorLocalPosition = new Vector3(0f, 0.35f, 0.35f);

        [Tooltip("Local uniform scale of rotation marker.")]
        [SerializeField] private float m_rotationIndicatorScale = 0.2f;

        private readonly RaycastHit[] m_groundHits = new RaycastHit[1];

        private Rigidbody m_rigidbody;
        private Vector2 m_moveInput;
        private bool m_cameraBound;

        private void Awake()
        {
            m_rigidbody = GetComponent<Rigidbody>();
            m_rigidbody.interpolation = RigidbodyInterpolation.Interpolate;

            EnsureVisualSetup();
        }

        private void Start()
        {
            BindCameraFollow();
        }

        private void Update()
        {
            ReadMoveInput();
        }

        private void LateUpdate()
        {
            UpdateTrailVisual();
        }

        private void FixedUpdate()
        {
            if (m_rigidbody == null)
            {
                return;
            }

            ApplyMovement();
        }

        private void ReadMoveInput()
        {
            float horizontal = Input.GetAxisRaw("Horizontal");
            float vertical = Input.GetAxisRaw("Vertical");
            Vector2 moveInput = new Vector2(horizontal, vertical);

            if (moveInput.sqrMagnitude > 1f)
            {
                moveInput.Normalize();
            }

            m_moveInput = moveInput;
        }

        private void ApplyMovement()
        {
            float acceleration = m_settings != null ? m_settings.PlayerAcceleration : 24f;
            float maxSpeed = m_settings != null ? m_settings.PlayerMaxSpeed : 8.5f;

            if (acceleration <= 0f)
            {
                return;
            }

            bool isGrounded = m_useGroundProbe == false || IsGrounded();
            float controlMultiplier = isGrounded ? 1f : 0.35f;
            Vector3 accelerationVector =
                new Vector3(m_moveInput.x, 0f, m_moveInput.y) * (acceleration * controlMultiplier);
            m_rigidbody.AddForce(accelerationVector, ForceMode.Acceleration);

            if (maxSpeed <= 0f)
            {
                return;
            }

            Vector3 velocity = m_rigidbody.velocity;
            Vector3 horizontalVelocity = new Vector3(velocity.x, 0f, velocity.z);
            float maxSpeedSquared = maxSpeed * maxSpeed;

            if (horizontalVelocity.sqrMagnitude <= maxSpeedSquared)
            {
                return;
            }

            Vector3 clampedHorizontalVelocity = horizontalVelocity.normalized * maxSpeed;
            m_rigidbody.velocity =
                new Vector3(clampedHorizontalVelocity.x, velocity.y, clampedHorizontalVelocity.z);
        }

        private bool IsGrounded()
        {
            float probeDistance =
                m_settings != null
                    ? Mathf.Max(0.05f, m_settings.PlayerGroundProbeDistance)
                    : Mathf.Max(0.05f, m_fallbackGroundProbeDistance);
            Vector3 origin = transform.position + (Vector3.up * 0.1f);

#if ONITY_UNITY_PHYSICS
            int hitCount = OnityNonAllocPhysics.Raycast(
                origin,
                Vector3.down,
                m_groundHits,
                probeDistance,
                m_groundMask,
                QueryTriggerInteraction.Ignore);
#else
            int hitCount = UnityEngine.Physics.RaycastNonAlloc(
                origin,
                Vector3.down,
                m_groundHits,
                probeDistance,
                m_groundMask,
                QueryTriggerInteraction.Ignore);
#endif
            return hitCount > 0;
        }

        private void UpdateTrailVisual()
        {
            if (m_trailRenderer == null || m_rigidbody == null)
            {
                return;
            }

            Vector3 horizontalVelocity = new Vector3(m_rigidbody.velocity.x, 0f, m_rigidbody.velocity.z);
            float horizontalSpeed = horizontalVelocity.magnitude;
            float speedForMaxTrail = m_settings != null
                ? Mathf.Max(0.1f, m_settings.PlayerSpeedForMaxTrail)
                : 8.5f;
            float minWidth = m_settings != null ? m_settings.PlayerTrailMinWidth : 0.06f;
            float maxWidth = m_settings != null ? m_settings.PlayerTrailMaxWidth : 0.2f;
            float widthLerp = Mathf.Clamp01(horizontalSpeed / speedForMaxTrail);

            m_trailRenderer.widthMultiplier = Mathf.Lerp(minWidth, maxWidth, widthLerp);
            m_trailRenderer.emitting = horizontalSpeed > 0.08f;
        }

        private void EnsureVisualSetup()
        {
            if (m_trailRenderer == null)
            {
                m_trailRenderer = GetComponent<TrailRenderer>();
            }

            if (m_trailRenderer == null)
            {
                m_trailRenderer = gameObject.AddComponent<TrailRenderer>();
            }

            ConfigureTrailRenderer();
            EnsureRotationIndicator();
        }

        private void ConfigureTrailRenderer()
        {
            if (m_trailRenderer == null)
            {
                return;
            }

            if (m_trailMaterial == null)
            {
                m_trailMaterial = CreateDefaultTrailMaterial();
            }

            if (m_trailMaterial != null)
            {
                m_trailRenderer.material = m_trailMaterial;
            }

            float trailTime = m_settings != null ? m_settings.PlayerTrailTime : 0.35f;
            float minWidth = m_settings != null ? m_settings.PlayerTrailMinWidth : 0.06f;

            m_trailRenderer.time = Mathf.Max(0.05f, trailTime);
            m_trailRenderer.widthMultiplier = Mathf.Max(0.01f, minWidth);
            m_trailRenderer.minVertexDistance = 0.08f;
            m_trailRenderer.numCapVertices = 4;
            m_trailRenderer.numCornerVertices = 2;
            m_trailRenderer.shadowCastingMode = ShadowCastingMode.Off;
            m_trailRenderer.receiveShadows = false;
            m_trailRenderer.alignment = LineAlignment.View;
            m_trailRenderer.textureMode = LineTextureMode.Stretch;
            m_trailRenderer.emitting = false;

            Gradient gradient = new Gradient();
            GradientColorKey[] colorKeys =
            {
                new GradientColorKey(k_defaultTrailColor, 0f),
                new GradientColorKey(k_defaultTrailColor, 1f)
            };
            GradientAlphaKey[] alphaKeys =
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(0.82f, 0.15f),
                new GradientAlphaKey(0.35f, 0.65f),
                new GradientAlphaKey(0f, 1f)
            };

            gradient.SetKeys(colorKeys, alphaKeys);
            m_trailRenderer.colorGradient = gradient;
        }

        private void EnsureRotationIndicator()
        {
            if (m_rotationIndicator == null)
            {
                Transform existing = transform.Find(k_rotationIndicatorName);

                if (existing != null)
                {
                    m_rotationIndicator = existing;
                }
            }

            if (m_rotationIndicator == null)
            {
                GameObject markerRoot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                markerRoot.name = k_rotationIndicatorName;
                markerRoot.transform.SetParent(transform, false);

                Collider markerCollider = markerRoot.GetComponent<Collider>();

                if (markerCollider != null)
                {
                    markerCollider.enabled = false;
                    Destroy(markerCollider);
                }

                Renderer markerRenderer = markerRoot.GetComponent<Renderer>();

                if (markerRenderer != null)
                {
                    markerRenderer.shadowCastingMode = ShadowCastingMode.Off;
                    markerRenderer.receiveShadows = false;

                    if (markerRenderer.material != null)
                    {
                        markerRenderer.material.color = k_rotationIndicatorColor;
                    }
                }

                m_rotationIndicator = markerRoot.transform;
            }

            m_rotationIndicator.localPosition = m_rotationIndicatorLocalPosition;
            m_rotationIndicator.localScale = Vector3.one * Mathf.Max(0.05f, m_rotationIndicatorScale);
        }

        private Material CreateDefaultTrailMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");

            if (shader == null)
            {
                shader = Shader.Find("Particles/Standard Unlit");
            }

            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            if (shader == null)
            {
                return null;
            }

            Material trailMaterial = new Material(shader)
            {
                name = "OnityRollABallTrailRuntimeMaterial"
            };

            if (trailMaterial.HasProperty("_BaseColor"))
            {
                trailMaterial.SetColor("_BaseColor", k_defaultTrailColor);
            }

            if (trailMaterial.HasProperty("_Color"))
            {
                trailMaterial.SetColor("_Color", k_defaultTrailColor);
            }

            return trailMaterial;
        }

        private void BindCameraFollow()
        {
            if (m_cameraBound)
            {
                return;
            }

            Camera mainCamera = Camera.main;

            if (mainCamera == null)
            {
                return;
            }

            Type cameraFollowType = Type.GetType(k_cameraFollowTypeName);

            if (cameraFollowType == null)
            {
                return;
            }

            Component cameraFollow = mainCamera.GetComponent(cameraFollowType);

            if (cameraFollow == null)
            {
                cameraFollow = mainCamera.gameObject.AddComponent(cameraFollowType);
            }

            if (cameraFollow == null)
            {
                return;
            }

            MethodInfo setTargetMethod = cameraFollowType.GetMethod(
                "SetTarget",
                BindingFlags.Instance | BindingFlags.Public);

            if (setTargetMethod != null)
            {
                setTargetMethod.Invoke(cameraFollow, new object[] { transform });
            }

            MethodInfo applySettingsMethod = cameraFollowType.GetMethod(
                "ApplySettings",
                BindingFlags.Instance | BindingFlags.Public);

            if (applySettingsMethod != null)
            {
                applySettingsMethod.Invoke(cameraFollow, new object[] { m_settings });
            }

            m_cameraBound = true;
        }
    }
}
