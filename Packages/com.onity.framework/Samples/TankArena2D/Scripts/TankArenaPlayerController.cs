using Onity.DI;
using Onity.Factory;
using Onity.Messaging;
using Onity.Pooling;
using Onity.Reactive;
#if ENABLE_INPUT_SYSTEM
using Onity.Unity.Input;
using UnityEngine.InputSystem;
#endif
using Onity.Unity.Reactive;
using UnityEngine;

namespace Onity.Samples.TankArena2D
{
    /// <summary>
    /// Player controller that demonstrates Onity reactive input, pooling, and messaging flow.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class TankArenaPlayerController : MonoBehaviour
    {
#if ENABLE_INPUT_SYSTEM
        [Header("Input System (Optional)")]
        [Tooltip("When enabled, movement and fire read from InputAction references.")]
        [SerializeField] private bool m_useInputSystem;

        [Tooltip("Vector2 move action (for example WASD or left stick).")]
        [SerializeField] private InputActionReference m_moveAction;

        [Tooltip("Button fire action.")]
        [SerializeField] private InputActionReference m_fireAction;
#endif

        [Header("Scene References")]
        [Tooltip("Optional projectile spawn origin. This transform is used when set.")]
        [SerializeField] private Transform m_shootOrigin;

        [Header("Fallback Movement")]
        [Tooltip("Fallback move speed used before settings injection.")]
        [SerializeField] private float m_fallbackMoveSpeed = 7f;

        [Tooltip("Fallback turn speed used before settings injection.")]
        [SerializeField] private float m_fallbackTurnSpeed = 300f;

        [Header("Reactive Loop")]
        [Tooltip("SingleThread: main thread. JobMultiThread: C# jobs boundary. BurstJobMultiThread: Burst-friendly jobs boundary. DotsEventDriven: Burst jobs + DOTS accumulator change events.")]
        [SerializeField] private OnityUnityThreadMode m_updateThreadMode = OnityUnityThreadMode.SingleThread;

        [Tooltip("Parallel work item count used by JobMultiThread, BurstJobMultiThread, and DotsEventDriven modes.")]
        [SerializeField] private int m_jobWorkItemCount = 64;

        [Tooltip("Minimum commands per worker job used by JobMultiThread, BurstJobMultiThread, and DotsEventDriven modes.")]
        [SerializeField] private int m_jobMinCommandsPerJob = 32;

        private readonly CompositeDisposable m_runtimeSubscriptions = new CompositeDisposable();
        private Rigidbody m_rigidbody;
        private Vector2 m_moveInput;
        private float m_remainingFireCooldown;
        private Vector3 m_spawnPosition;
        private Quaternion m_spawnRotation;

        private TankArenaGameSettings m_settings;
        private ITankArenaGameStateService m_gameStateService;
        private IFactory<TankArenaProjectile> m_projectileFactory;
        private IPool<TankArenaProjectile> m_projectilePool;
        private IPublisher<TankArenaPlayerDamagedMessage> m_playerDamagedPublisher;
        private ISubscriber<TankArenaRestartRequestedMessage> m_restartSubscriber;

#if ENABLE_INPUT_SYSTEM
        private bool m_isFireHeldFromInputSystem;
        private bool m_areInputSystemBindingsInitialized;
#endif

        private void Awake()
        {
            m_rigidbody = GetComponent<Rigidbody>();
            m_rigidbody.useGravity = false;
            m_rigidbody.constraints =
                RigidbodyConstraints.FreezeRotationX |
                RigidbodyConstraints.FreezeRotationZ |
                RigidbodyConstraints.FreezePositionY;

            m_spawnPosition = transform.position;
            m_spawnRotation = transform.rotation;
        }

        private void OnEnable()
        {
            m_runtimeSubscriptions.Clear();
#if ENABLE_INPUT_SYSTEM
            m_areInputSystemBindingsInitialized = false;
#endif
            BindReactiveInput();
            BindRestartStream();
        }

        private void OnDisable()
        {
            m_runtimeSubscriptions.Clear();
#if ENABLE_INPUT_SYSTEM
            DisableInputSystemActions();
            m_isFireHeldFromInputSystem = false;
#endif
            m_moveInput = Vector2.zero;
        }

        private void FixedUpdate()
        {
            if (IsGameOver())
            {
                return;
            }

            float moveSpeed = m_settings != null ? m_settings.PlayerMoveSpeed : m_fallbackMoveSpeed;
            float turnSpeed = m_settings != null ? m_settings.PlayerTurnSpeed : m_fallbackTurnSpeed;
            Vector3 moveDirection = new Vector3(m_moveInput.x, 0f, m_moveInput.y);

            if (moveDirection.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            Vector3 normalizedMoveDirection = moveDirection.normalized;
            Vector3 delta = normalizedMoveDirection * (moveSpeed * Time.fixedDeltaTime);
            m_rigidbody.MovePosition(m_rigidbody.position + delta);

            Quaternion targetRotation = Quaternion.LookRotation(normalizedMoveDirection, Vector3.up);
            Quaternion nextRotation = Quaternion.RotateTowards(
                m_rigidbody.rotation,
                targetRotation,
                turnSpeed * Time.fixedDeltaTime);
            m_rigidbody.MoveRotation(nextRotation);
        }

        /// <summary>
        /// Applies damage to player state through message channel.
        /// </summary>
        /// <param name="damage">Incoming damage value.</param>
        public void ApplyDamage(int damage)
        {
            if (damage <= 0 || IsGameOver())
            {
                return;
            }

            m_playerDamagedPublisher?.Publish(new TankArenaPlayerDamagedMessage(damage));
        }

        /// <summary>
        /// Resets player transform and motion to initial state.
        /// </summary>
        public void ResetPlayerState()
        {
            m_rigidbody.velocity = Vector3.zero;
            m_rigidbody.angularVelocity = Vector3.zero;
            m_rigidbody.position = m_spawnPosition;
            m_rigidbody.rotation = m_spawnRotation;
            m_moveInput = Vector2.zero;
            m_remainingFireCooldown = 0f;
        }

        [Inject]
        private void Construct(
            TankArenaGameSettings settings,
            ITankArenaGameStateService gameStateService,
            IFactory<TankArenaProjectile> projectileFactory,
            IPool<TankArenaProjectile> projectilePool,
            IPublisher<TankArenaPlayerDamagedMessage> playerDamagedPublisher,
            ISubscriber<TankArenaRestartRequestedMessage> restartSubscriber)
        {
            m_settings = settings;
            m_gameStateService = gameStateService;
            m_projectileFactory = projectileFactory;
            m_projectilePool = projectilePool;
            m_playerDamagedPublisher = playerDamagedPublisher;
            m_restartSubscriber = restartSubscriber;
        }

        private void BindReactiveInput()
        {
#if ENABLE_INPUT_SYSTEM
            InitializeInputSystemBindings();
#endif
            OnityUnityObservable
                .EveryUpdate(
                    m_updateThreadMode,
                    Mathf.Max(1, m_jobWorkItemCount),
                    Mathf.Max(1, m_jobMinCommandsPerJob))
                .Subscribe(_ => TickReactiveUpdate())
                .AddTo(m_runtimeSubscriptions);
        }

        private void BindRestartStream()
        {
            if (m_restartSubscriber == null)
            {
                return;
            }

            m_restartSubscriber.Subscribe(_ => ResetPlayerState()).AddTo(m_runtimeSubscriptions);
        }

        private Vector2 ReadMoveInput()
        {
#if ENABLE_INPUT_SYSTEM
            if (m_useInputSystem && m_moveAction != null && m_moveAction.action != null)
            {
                Vector2 actionValue = m_moveAction.action.ReadValue<Vector2>();

                if (actionValue.sqrMagnitude > 1f)
                {
                    actionValue.Normalize();
                }

                return actionValue;
            }
#endif
            Vector2 fallbackValue = new Vector2(
                Input.GetAxisRaw("Horizontal"),
                Input.GetAxisRaw("Vertical"));

            if (fallbackValue.sqrMagnitude > 1f)
            {
                fallbackValue.Normalize();
            }

            return fallbackValue;
        }

        private bool IsFirePressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (m_useInputSystem && m_fireAction != null && m_fireAction.action != null)
            {
                return m_isFireHeldFromInputSystem;
            }
#endif
            return Input.GetKey(KeyCode.Space);
        }

        private void TryFire()
        {
            if (IsGameOver())
            {
                return;
            }

            if (m_projectileFactory == null || m_projectilePool == null)
            {
                return;
            }

            if (m_remainingFireCooldown > 0f)
            {
                return;
            }

            TankArenaProjectile projectile = m_projectileFactory.Create();

            if (projectile == null)
            {
                return;
            }

            Vector3 launchPosition =
                m_shootOrigin != null
                    ? m_shootOrigin.position
                    : transform.position + (transform.forward * 0.8f);
            Vector3 launchDirection = m_shootOrigin != null ? m_shootOrigin.forward : transform.forward;
            float projectileSpeed = m_settings != null ? m_settings.PlayerProjectileSpeed : 17f;
            float projectileLifetime = m_settings != null ? m_settings.ProjectileLifetimeSeconds : 3f;
            float projectileRadius = m_settings != null ? m_settings.ProjectileHitRadius : 0.45f;
            int projectileDamage = m_settings != null ? m_settings.ProjectileDamage : 1;
            int targetMask = m_settings != null ? m_settings.PlayerProjectileTargetMask : UnityEngine.Physics.AllLayers;

            projectile.Launch(
                transform,
                m_projectilePool,
                launchPosition,
                launchDirection,
                true,
                targetMask,
                projectileSpeed,
                projectileLifetime,
                projectileDamage,
                projectileRadius);

            m_remainingFireCooldown = m_settings != null
                ? m_settings.PlayerFireCooldownSeconds
                : 0.2f;
        }

        private bool IsGameOver()
        {
            return m_gameStateService != null && m_gameStateService.IsGameOver.Value;
        }

        private void TickCooldown()
        {
            if (m_remainingFireCooldown <= 0f)
            {
                return;
            }

            m_remainingFireCooldown -= Time.deltaTime;

            if (m_remainingFireCooldown < 0f)
            {
                m_remainingFireCooldown = 0f;
            }
        }

        private void TickReactiveUpdate()
        {
            m_moveInput = ReadMoveInput();
            TickCooldown();

            if (IsFirePressed())
            {
                TryFire();
            }
        }

#if ENABLE_INPUT_SYSTEM
        private void InitializeInputSystemBindings()
        {
            if (m_useInputSystem == false || m_areInputSystemBindingsInitialized)
            {
                return;
            }

            m_areInputSystemBindingsInitialized = true;

            if (m_moveAction != null && m_moveAction.action != null)
            {
                m_moveAction.action.Enable();
            }

            if (m_fireAction != null && m_fireAction.action != null)
            {
                m_fireAction.action.Enable();
                m_isFireHeldFromInputSystem = m_fireAction.action.IsPressed();
                m_fireAction.action
                    .PerformedAsObservable()
                    .Subscribe(_ => m_isFireHeldFromInputSystem = true)
                    .AddTo(m_runtimeSubscriptions);
                m_fireAction.action
                    .CanceledAsObservable()
                    .Subscribe(_ => m_isFireHeldFromInputSystem = false)
                    .AddTo(m_runtimeSubscriptions);
            }
        }

        private void DisableInputSystemActions()
        {
            m_areInputSystemBindingsInitialized = false;

            if (m_moveAction != null && m_moveAction.action != null)
            {
                m_moveAction.action.Disable();
            }

            if (m_fireAction != null && m_fireAction.action != null)
            {
                m_fireAction.action.Disable();
            }
        }
#endif
    }
}
