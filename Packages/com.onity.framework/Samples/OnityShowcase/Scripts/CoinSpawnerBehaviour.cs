using Onity.DI;
using Onity.Messaging;
using UnityEngine;

namespace OnityShowcase
{
    /// <summary>
    /// Thin spawner view. Each frame it forwards <c>Time.deltaTime</c> to the engine-free
    /// <see cref="ICoinSpawnService"/> (DI) and, when the service signals a spawn, instantiates a
    /// clickable coin primitive. It contains no timing or placement math — that lives in the
    /// service — and it stops spawning once the countdown reports the round is finished.
    /// </summary>
    public sealed class CoinSpawnerBehaviour : ShowcaseBehaviour
    {
        [Inject] private ICoinSpawnService m_spawnService;
        [Inject] private ICountdownService m_countdown;
        [Inject] private IPublisher<CoinCollectedMessage> m_collectedPublisher;
        [Inject] private ShowcaseSettings m_settings;

        [Header("Coin Appearance")]
        [Tooltip("World-space Y height coins spawn at.")]
        [SerializeField] private float m_coinHeight = 0.5f;

        [Tooltip("Uniform scale applied to spawned coin primitives.")]
        [SerializeField] private float m_coinScale = 0.6f;

        private void Update()
        {
            if (m_spawnService == null || m_countdown == null || m_countdown.IsFinished.Value)
            {
                return;
            }

            if (m_spawnService.TryGetNextSpawn(Time.deltaTime, out float x, out float z))
            {
                SpawnCoin(x, z);
            }
        }

        private void SpawnCoin(float x, float z)
        {
            GameObject coin = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            coin.name = "Coin";
            coin.transform.SetParent(transform, false);
            coin.transform.localPosition = new Vector3(x, m_coinHeight, z);
            coin.transform.localScale = new Vector3(m_coinScale, m_coinScale, m_coinScale);

            CoinBehaviour coinBehaviour = coin.AddComponent<CoinBehaviour>();
            coinBehaviour.Configure(m_collectedPublisher, m_settings.CoinScoreValue);
        }
    }
}
