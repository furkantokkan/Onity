using Onity.DI;
using Onity.Messaging;
using Onity.Reactive;
using UnityEngine;

namespace OnityShowcase
{
    /// <summary>
    /// Thin HUD view that brings all three pillars together. It injects reactive state and an
    /// event channel (DI), binds the score and countdown <c>ReactiveProperty</c>s to display
    /// strings through reactive operators (Reactive), and subscribes to the one-shot
    /// <see cref="GameOverMessage"/> (Events). It holds no game logic — only formatting and
    /// drawing — and every subscription is tied to this object's lifetime via <c>Subscriptions</c>.
    /// Rendering uses IMGUI so the showcase runs with zero Canvas asset wiring.
    /// </summary>
    public sealed class HudBehaviour : ShowcaseBehaviour
    {
        [Inject] private IScoreService m_scoreService;
        [Inject] private ICountdownService m_countdownService;
        [Inject] private ISubscriber<GameOverMessage> m_gameOver;

        private string m_scoreText = "Score: 0";
        private string m_timeText = "Time: 0.0";
        private string m_statusText = "Collect the coins!";
        private bool m_lowTime;
        private GUIStyle m_style;

        /// <inheritdoc />
        public override void OnInjected()
        {
            // Reactive state: a ReactiveProperty subscription emits the current value first, so the
            // HUD shows correct values immediately, then updates on every change. Formatting stays
            // in the view; the domain only exposes the value.
            m_scoreService.Score
                .Subscribe(value => m_scoreText = $"Score: {value}")
                .AddTo(Subscriptions);

            m_countdownService.TimeRemaining
                .Subscribe(seconds => m_timeText = $"Time: {seconds:0.0}")
                .AddTo(Subscriptions);

            // Reactive operators: LowTimeWarning is a Select + DistinctUntilChanged pipeline built
            // by the service, so the HUD only reacts when the warning edge actually flips.
            m_countdownService.LowTimeWarning
                .Subscribe(isLow => m_lowTime = isLow)
                .AddTo(Subscriptions);

            // Events: react to the single game-over notification published by the countdown.
            m_gameOver
                .Subscribe(OnGameOver)
                .AddTo(Subscriptions);
        }

        private void OnGameOver(GameOverMessage message)
        {
            m_statusText = $"Game Over! Final score: {message.FinalScore}";
        }

        private void OnGUI()
        {
            if (m_style == null)
            {
                m_style = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
            }

            const float x = 16f;
            float y = 12f;
            GUI.Label(new Rect(x, y, 600f, 32f), m_scoreText, m_style);
            y += 32f;

            Color previousColor = GUI.color;
            GUI.color = m_lowTime ? Color.red : previousColor;
            GUI.Label(new Rect(x, y, 600f, 32f), m_timeText, m_style);
            GUI.color = previousColor;
            y += 32f;

            GUI.Label(new Rect(x, y, 600f, 32f), m_statusText, m_style);
        }
    }
}
