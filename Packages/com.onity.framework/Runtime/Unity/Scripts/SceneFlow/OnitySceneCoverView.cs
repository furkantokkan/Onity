using System.Threading;
using System.Threading.Tasks;
using Onity.Unity.Async;
using UnityEngine;
using UnityEngine.UIElements;

namespace Onity.Unity.SceneFlow
{
    /// <summary>
    /// Base class for a screen cover drawn by one named element of a <see cref="UIDocument" />. It owns the cover
    /// animation for every cover style: durations, curves, unscaled frame timing, cancellation, input blocking and
    /// hiding. A derived cover only draws a progress value in <see cref="ApplyProgress" />.
    /// </summary>
    /// <remarks>
    /// Owner: this component, usually on the persistent <c>ProjectContext</c> object so it survives scene loads.
    /// Its panel needs a <c>PanelSettings</c> asset with a sort order above every other panel. While visible the
    /// element blocks presses on the panels below; when clear it is <c>display: none</c>. Stop points: each
    /// animation ends by itself, the caller's token stops it where it is, a newer animation replaces it, and
    /// <see cref="OnDisable" /> stops it, clears and hides the cover. The animation waits frames with the token-less
    /// <see cref="OnityTask.NextFrame()" /> and checks for a stop once per frame, so it allocates nothing per frame.
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    [DefaultExecutionOrder(100)] // UIDocument builds its tree before this view binds and hides the authoring preview.
    public abstract class OnitySceneCoverView : MonoBehaviour, IOnitySceneCover
    {
        // Longest frame step the animation advances by; the frame after a hitch reports the hitch as its delta.
        private const float k_maxFrameStepSeconds = 1f / 30f;

        [Header("Cover Animation")]
        [Tooltip("Unscaled seconds from clear to fully covered.")]
        [SerializeField] private float m_showSeconds = 0.25f;

        [Tooltip("Unscaled seconds from fully covered to clear.")]
        [SerializeField] private float m_hideSeconds = 0.3f;

        [Tooltip("Eases the show: time 0..1 to the fraction 0..1 of the way covered.")]
        [SerializeField] private AnimationCurve m_showCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Tooltip("Eases the hide: time 0..1 to the fraction 0..1 of the way cleared.")]
        [SerializeField] private AnimationCurve m_hideCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        private VisualElement m_element;
        private CancellationTokenSource m_animation;
        private float m_progress;
        private bool m_isShown;

        /// <inheritdoc />
        public bool IsVisible => m_isShown;

        /// <summary>
        /// Gets the progress last drawn: 0 is clear and 1 is fully covered.
        /// </summary>
        public float Progress => m_progress;

        /// <summary>
        /// Gets the bound cover element, or null while the view is disabled or its element is missing.
        /// </summary>
        protected VisualElement Element => m_element;

        /// <summary>
        /// Gets the name of the cover element in the view's <see cref="UIDocument" />.
        /// </summary>
        protected abstract string ElementName { get; }

        /// <summary>
        /// Binds the cover element and hides it, which also hides the authoring preview before the first frame.
        /// </summary>
        protected virtual void OnEnable()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            m_element = root?.Q(ElementName);

            if (m_element == null)
            {
                Debug.LogError(
                    $"{GetType().Name}: the UI document has no '{ElementName}' element; scene changes run uncovered.",
                    this);
                return;
            }

            OnBind(m_element);
            SetProgress(0f);
            HideElement();
        }

        /// <summary>
        /// Stops a running animation, clears and hides the cover, and unbinds the element.
        /// </summary>
        protected virtual void OnDisable()
        {
            StopAnimation();

            if (m_element != null)
            {
                SetProgress(0f);
                HideElement();
                OnUnbind(m_element);
            }

            m_element = null;
        }

        /// <inheritdoc />
        /// <remarks>Completes at once when the view is disabled or has no cover element.</remarks>
        public Task ShowAsync(CancellationToken cancellationToken)
        {
            return AnimateAsync(1f, m_showSeconds, m_showCurve, cancellationToken).AsTask();
        }

        /// <inheritdoc />
        /// <remarks>Completes at once when the view is disabled or has no cover element.</remarks>
        public Task HideAsync(CancellationToken cancellationToken)
        {
            return AnimateAsync(0f, m_hideSeconds, m_hideCurve, cancellationToken).AsTask();
        }

        /// <summary>
        /// Draws <paramref name="progress" /> on the cover element. Called on every animation frame and when the
        /// cover is cleared; it must not allocate.
        /// </summary>
        /// <param name="progress">0 is clear and 1 is fully covered.</param>
        protected abstract void ApplyProgress(float progress);

        /// <summary>
        /// Called when the cover element is bound in <see cref="OnEnable" />, before the first
        /// <see cref="ApplyProgress" />.
        /// </summary>
        /// <param name="element">The cover element.</param>
        protected virtual void OnBind(VisualElement element)
        {
        }

        /// <summary>
        /// Called when the cover element is unbound in <see cref="OnDisable" />.
        /// </summary>
        /// <param name="element">The cover element.</param>
        protected virtual void OnUnbind(VisualElement element)
        {
        }

        // Owner: the caller, which awaits the returned task; the animation belongs to this view. Stops: the target
        // progress, the caller's token or a newer animation (where it is), or OnDisable (cleared and hidden). An
        // OnityTask method, so the per-frame awaits run on Onity's pooled runner and allocate nothing.
        private async OnityTask AnimateAsync(
            float target,
            float seconds,
            AnimationCurve curve,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (m_element == null)
            {
                return;
            }

            CancellationTokenSource animation = StartAnimation(cancellationToken);
            CancellationToken token = animation.Token;

            try
            {
                ShowElement();
                float from = m_progress;
                float duration = Mathf.Max(0f, seconds) * Mathf.Abs(target - from);
                float elapsed = 0f;

                while (elapsed < duration)
                {
                    // The token-less frame wait allocates nothing; a stop is seen on the next frame, before any draw.
                    await OnityTask.NextFrame();
                    token.ThrowIfCancellationRequested();
                    elapsed += Mathf.Min(Time.unscaledDeltaTime, k_maxFrameStepSeconds);
                    float time = Mathf.Clamp01(elapsed / duration);
                    float eased = curve != null ? curve.Evaluate(time) : time;
                    SetProgress(Mathf.LerpUnclamped(from, target, eased));
                }

                SetProgress(target);

                if (m_progress <= 0f)
                {
                    HideElement();
                }
            }
            finally
            {
                EndAnimation(animation);
            }
        }

        // Replaces a running animation; the new one stops with the caller's token too.
        private CancellationTokenSource StartAnimation(CancellationToken cancellationToken)
        {
            StopAnimation();
            m_animation = cancellationToken.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                : new CancellationTokenSource();
            return m_animation;
        }

        private void EndAnimation(CancellationTokenSource animation)
        {
            if (ReferenceEquals(m_animation, animation))
            {
                m_animation = null;
            }

            animation.Dispose();
        }

        private void StopAnimation()
        {
            CancellationTokenSource animation = m_animation;
            m_animation = null;
            animation?.Cancel();
        }

        private void SetProgress(float progress)
        {
            m_progress = Mathf.Clamp01(progress);
            ApplyProgress(m_progress);
        }

        private void ShowElement()
        {
            m_element.style.display = DisplayStyle.Flex;
            m_isShown = true;
        }

        private void HideElement()
        {
            m_element.style.display = DisplayStyle.None;
            m_isShown = false;
        }
    }
}
