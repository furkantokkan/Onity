using UnityEngine;

namespace Onity.Samples.RollABall
{
    /// <summary>
    /// ScriptableObject configuration used by the Roll-a-Ball sample.
    /// </summary>
    [CreateAssetMenu(
        fileName = "OnityRollABallGameSettings",
        menuName = "Onity/Samples/Roll A Ball/Game Settings",
        order = 0)]
    public sealed class RollABallGameSettings : ScriptableObject
    {
        [Header("Arena")]
        [Tooltip("Half extents used for random pickup spawn positions.")]
        [SerializeField] private Vector2 m_arenaHalfExtents = new Vector2(8f, 8f);

        [Tooltip("Padding from arena border when spawning pickups.")]
        [SerializeField] private float m_spawnPadding = 0.8f;

        [Tooltip("Y height used for spawned pickups.")]
        [SerializeField] private float m_pickupHeight = 0.8f;

        [Header("Player")]
        [Tooltip("Acceleration applied from input in FixedUpdate.")]
        [SerializeField] private float m_playerAcceleration = 24f;

        [Tooltip("Maximum horizontal speed of the player sphere.")]
        [SerializeField] private float m_playerMaxSpeed = 8.5f;

        [Tooltip("Ground probe ray distance used by player controller.")]
        [SerializeField] private float m_playerGroundProbeDistance = 1.05f;

        [Header("Camera")]
        [Tooltip("World-space offset from player used by follow camera.")]
        [SerializeField] private Vector3 m_cameraFollowOffset = new Vector3(0f, 9f, -6f);

        [Tooltip("Smoothing time used by follow camera position interpolation.")]
        [SerializeField] private float m_cameraPositionSmoothTime = 0.12f;

        [Tooltip("How quickly camera rotates to look at the player.")]
        [SerializeField] private float m_cameraLookLerpSpeed = 8f;

        [Header("Trail VFX")]
        [Tooltip("Trail lifetime in seconds.")]
        [SerializeField] private float m_playerTrailTime = 0.35f;

        [Tooltip("Trail minimum width when player is nearly stationary.")]
        [SerializeField] private float m_playerTrailMinWidth = 0.06f;

        [Tooltip("Trail maximum width when player reaches target trail speed.")]
        [SerializeField] private float m_playerTrailMaxWidth = 0.2f;

        [Tooltip("Horizontal speed value that maps to max trail width.")]
        [SerializeField] private float m_playerSpeedForMaxTrail = 8.5f;

        [Header("Pickup")]
        [Tooltip("Initial pickup amount kept active in scene.")]
        [SerializeField] private int m_initialPickupCount = 12;

        [Tooltip("Score awarded when one pickup is collected.")]
        [SerializeField] private int m_pointsPerPickup = 1;

        [Tooltip("Visual rotation speed of pickups.")]
        [SerializeField] private float m_pickupRotationSpeed = 90f;

        /// <summary>
        /// Arena half extents used by pickup spawner.
        /// </summary>
        public Vector2 ArenaHalfExtents => m_arenaHalfExtents;

        /// <summary>
        /// Spawn padding from arena border.
        /// </summary>
        public float SpawnPadding => m_spawnPadding;

        /// <summary>
        /// Pickup spawn height in world space.
        /// </summary>
        public float PickupHeight => m_pickupHeight;

        /// <summary>
        /// Player acceleration multiplier.
        /// </summary>
        public float PlayerAcceleration => m_playerAcceleration;

        /// <summary>
        /// Maximum horizontal speed.
        /// </summary>
        public float PlayerMaxSpeed => m_playerMaxSpeed;

        /// <summary>
        /// Ground probe distance used for grounded check.
        /// </summary>
        public float PlayerGroundProbeDistance => m_playerGroundProbeDistance;

        /// <summary>
        /// Camera follow offset.
        /// </summary>
        public Vector3 CameraFollowOffset => m_cameraFollowOffset;

        /// <summary>
        /// Camera position smoothing time.
        /// </summary>
        public float CameraPositionSmoothTime => m_cameraPositionSmoothTime;

        /// <summary>
        /// Camera look interpolation speed.
        /// </summary>
        public float CameraLookLerpSpeed => m_cameraLookLerpSpeed;

        /// <summary>
        /// Player trail duration.
        /// </summary>
        public float PlayerTrailTime => m_playerTrailTime;

        /// <summary>
        /// Player trail minimum width.
        /// </summary>
        public float PlayerTrailMinWidth => m_playerTrailMinWidth;

        /// <summary>
        /// Player trail maximum width.
        /// </summary>
        public float PlayerTrailMaxWidth => m_playerTrailMaxWidth;

        /// <summary>
        /// Horizontal speed threshold for max trail width.
        /// </summary>
        public float PlayerSpeedForMaxTrail => m_playerSpeedForMaxTrail;

        /// <summary>
        /// Count of pickups kept active by spawner.
        /// </summary>
        public int InitialPickupCount => m_initialPickupCount;

        /// <summary>
        /// Score value per collected pickup.
        /// </summary>
        public int PointsPerPickup => m_pointsPerPickup;

        /// <summary>
        /// Pickup visual spin speed.
        /// </summary>
        public float PickupRotationSpeed => m_pickupRotationSpeed;
    }
}
