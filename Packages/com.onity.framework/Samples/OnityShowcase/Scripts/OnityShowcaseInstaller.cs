using System;
using Onity.DI;
using Onity.Messaging;
using UnityEngine;

namespace OnityShowcase
{
    /// <summary>
    /// The single installer that wires the whole Coin Rush loop: settings, the message broker and
    /// its typed channels (Events), the reactive services that own the score and countdown state
    /// (Reactive), and the spawn planner — all as constructor-injected bindings (DI).
    ///
    /// In the full Onity package <c>MessageBroker</c> and the typed channels are auto-bound by the
    /// context and registered with <c>container.BindMessageChannel&lt;T&gt;()</c>. The Example Game
    /// embeds only the engine-free cores, so this installer constructs one shared broker and binds
    /// the <see cref="IPublisher{T}"/>/<see cref="ISubscriber{T}"/> pairs from it explicitly. Every
    /// channel resolves to the SAME broker, so a publish reaches the subscribers.
    /// </summary>
    public sealed class OnityShowcaseInstaller : OnityMonoInstaller
    {
        [Header("Round Tuning")]
        [Tooltip("Round length in seconds before game-over is published.")]
        [SerializeField] private float m_roundDurationSeconds = 30f;

        [Tooltip("Score awarded per collected coin.")]
        [SerializeField] private int m_coinScoreValue = 10;

        [Tooltip("Average seconds between coin spawns.")]
        [SerializeField] private float m_spawnIntervalSeconds = 1.25f;

        [Tooltip("Half-extent of the square play area coins spawn within (world units).")]
        [SerializeField] private float m_spawnAreaHalfSize = 4f;

        /// <inheritdoc />
        public override void InstallBindings(OnityContainer container)
        {
            ShowcaseSettings settings = new ShowcaseSettings(
                m_roundDurationSeconds,
                m_coinScoreValue,
                m_spawnIntervalSeconds,
                m_spawnAreaHalfSize);
            container.BindInstance(settings);

            // One shared broker, bound under both its interface and concrete type.
            MessageBroker broker = new MessageBroker();
            container.BindInstance<IMessageBroker>(broker);
            container.BindInstance(broker);

            // Typed channels from that broker (engine-free equivalent of BindMessageChannel<T>()).
            container.BindInstance(broker.GetPublisher<CoinCollectedMessage>());
            container.BindInstance(broker.GetSubscriber<CoinCollectedMessage>());
            container.BindInstance(broker.GetPublisher<GameOverMessage>());
            container.BindInstance(broker.GetSubscriber<GameOverMessage>());

            // Random source seam for the spawn planner; Unity-backed in play, swappable in tests.
            container.BindInstance<Func<float>>(static () => UnityEngine.Random.value);

            // Reactive, single-owner services shared across the round.
            container.BindInterfacesAndSelfTo<ScoreService>().AsSingle();
            container.BindInterfacesAndSelfTo<CountdownService>().AsSingle().NonLazy();
            container.BindInterfacesAndSelfTo<CoinSpawnService>().AsSingle();
        }
    }
}
