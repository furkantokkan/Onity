using Onity.DI;
using Onity.Unity.Installers;
using Onity.Unity.Messaging;
using UnityEngine;

namespace Onity.Samples.RollABall
{
    /// <summary>
    /// Registers Roll-a-Ball sample services, messaging, and pooled factory bindings.
    /// </summary>
    public sealed class RollABallInstaller : MonoInstaller
    {
        [Header("Config")]
        [Tooltip("Game settings ScriptableObject used by this sample.")]
        [SerializeField] private RollABallGameSettings m_settings;

        [Header("Pickup Pool")]
        [Tooltip("Pickup prefab used by pooled factory binding.")]
        [SerializeField] private RollABallPickup m_pickupPrefab;

        [Tooltip("Optional parent for pooled pickup instances.")]
        [SerializeField] private Transform m_poolRoot;

        [Tooltip("Initial pooled pickup count.")]
        [SerializeField] private int m_defaultPoolCapacity = 24;

        [Tooltip("Maximum pooled pickup count.")]
        [SerializeField] private int m_maxPoolSize = 128;

        /// <inheritdoc />
        public override void InstallBindings(OnityContainer container)
        {
            if (m_settings == null)
            {
                Debug.LogError("RollABallInstaller requires a RollABallGameSettings asset.", this);
                return;
            }

            if (m_pickupPrefab == null)
            {
                Debug.LogError("RollABallInstaller requires a pickup prefab reference.", this);
                return;
            }

            container.BindScriptableObject<RollABallGameSettings>(m_settings);
            container.BindMessageChannel<RollABallPickupCollectedMessage>();
            container.BindInterfacesAndSelfTo<RollABallScoreService>().AsSingle();
            container.BindPooledFactory(m_pickupPrefab, m_poolRoot, m_defaultPoolCapacity, m_maxPoolSize);
        }
    }
}
