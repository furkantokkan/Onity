using System;
using System.Threading;
using Onity.Core;
using Onity.DI;
#if ONITY_ENTITIES
using Onity.DOTS;
#endif
using Onity.Messaging;
using Onity.Reactive;
using Onity.Unity.Reactive;
using Onity.Unity.UI;
using UnityEngine;

namespace Onity.Samples.TankArena2D
{
    /// <summary>
    /// UI presenter for Tank Arena HUD using resolver bridge and reactive operators.
    /// </summary>
    public sealed class TankArenaHudPresenter : OnityUiPresenter<TankArenaHudView>
    {
        [Inject]
        private ITankArenaGameStateService GameStateService { get; set; }

        [Inject]
        private TankArenaEnemySpawner EnemySpawner { get; set; }

        [Inject]
        private IPublisher<TankArenaRestartRequestedMessage> RestartPublisher { get; set; }

        private CompositeDisposable m_runtimeSubscriptions;
        private CancellationTokenSource m_dotsWatcherTokenSource;

        /// <inheritdoc />
        protected override void OnViewAssigned()
        {
            View.SetScore("Score: 0");
            View.SetHealth("Health: 0");
            View.SetWave("Wave: 0");
            View.SetEnemyCount("Enemies: 0");
            View.SetTimer("Time: 0.0");
            View.SetStatus("Status: Initializing");
            View.SetRestartButtonVisible(false);
        }

        /// <inheritdoc />
        public override void OnViewOpened()
        {
            m_runtimeSubscriptions = new CompositeDisposable();
            BindViewEvents();
            BindStateStreams();
            BindWaveEvents();
            StartDotsWatcher();
        }

        /// <inheritdoc />
        public override void OnViewClosing()
        {
            StopDotsWatcher();
            m_runtimeSubscriptions?.Dispose();
            m_runtimeSubscriptions = null;
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            StopDotsWatcher();
            m_runtimeSubscriptions?.Dispose();
            m_runtimeSubscriptions = null;
        }

        private void BindViewEvents()
        {
            OnityObservable
                .FromEvent<Unit>(
                    handler => View.RestartRequested += handler,
                    handler => View.RestartRequested -= handler)
                .Where(_ => GameStateService != null && GameStateService.IsGameOver.Value)
                .Subscribe(
                    _ =>
                    {
                        RestartPublisher?.Publish(new TankArenaRestartRequestedMessage());
                        View.SetDots("DOTS: 0");
                        View.SetStatus("Status: Restarting");
                    })
                .AddTo(m_runtimeSubscriptions);
        }

        private void BindStateStreams()
        {
            if (GameStateService == null)
            {
                return;
            }

            GameStateService
                .Score
                .Subscribe(value => View.SetScore($"Score: {value}"), true)
                .AddTo(m_runtimeSubscriptions);
            GameStateService
                .Health
                .Subscribe(value => View.SetHealth($"Health: {value}"), true)
                .AddTo(m_runtimeSubscriptions);
            GameStateService
                .CurrentWave
                .Subscribe(value => View.SetWave($"Wave: {value}"), true)
                .AddTo(m_runtimeSubscriptions);
            GameStateService
                .ActiveEnemyCount
                .Subscribe(value => View.SetEnemyCount($"Enemies: {value}"), true)
                .AddTo(m_runtimeSubscriptions);
            GameStateService
                .IsGameOver
                .Subscribe(OnGameOverChanged, true)
                .AddTo(m_runtimeSubscriptions);

            OnityUnityObservable
                .EveryUpdate()
                .Select(_ => Time.timeSinceLevelLoad)
                .Subscribe(seconds => View.SetTimer($"Time: {seconds:0.0}"))
                .AddTo(m_runtimeSubscriptions);
        }

        private void BindWaveEvents()
        {
            if (EnemySpawner == null)
            {
                return;
            }

            OnityObservable
                .FromEvent<int>(
                    handler => EnemySpawner.WaveStarted += handler,
                    handler => EnemySpawner.WaveStarted -= handler)
                .Select(value => $"Status: Wave {value} started")
                .Subscribe(View.SetStatus)
                .AddTo(m_runtimeSubscriptions);
        }

        private void OnGameOverChanged(bool isGameOver)
        {
            if (isGameOver)
            {
                View.SetStatus("Status: Game Over");
                View.SetRestartButtonVisible(true);
                return;
            }

            View.SetStatus("Status: Survive");
            View.SetRestartButtonVisible(false);
        }

        private void StartDotsWatcher()
        {
            StopDotsWatcher();
            m_dotsWatcherTokenSource = new CancellationTokenSource();
#if ONITY_ENTITIES
            RunDotsWatcherAsync(m_dotsWatcherTokenSource.Token);
#else
            View.SetDots("DOTS: N/A");
#endif
        }

        private void StopDotsWatcher()
        {
            if (m_dotsWatcherTokenSource == null)
            {
                return;
            }

            m_dotsWatcherTokenSource.Cancel();
            m_dotsWatcherTokenSource.Dispose();
            m_dotsWatcherTokenSource = null;
        }

#if ONITY_ENTITIES
        private async void RunDotsWatcherAsync(CancellationToken cancellationToken)
        {
            View.SetDots("DOTS: 0");

            try
            {
                while (cancellationToken.IsCancellationRequested == false)
                {
                    int value = await OnityDotsIntEventAsync.WaitForNextAccumulatorUpdateAsync(cancellationToken);
                    View.SetDots($"DOTS: {value}");
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }
#endif
    }
}
