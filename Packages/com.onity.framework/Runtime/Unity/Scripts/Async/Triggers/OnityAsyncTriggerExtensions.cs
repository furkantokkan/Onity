using System;
using UnityEngine;

namespace Onity.Unity.Async.Triggers
{
    /// <summary>
    /// Entry points that get or add a trigger component on a GameObject (UniTask
    /// <c>AsyncTriggerExtensions</c> parity). Each trigger type is added at most once per
    /// GameObject and reused. Every member requires Unity's main thread.
    /// </summary>
    public static partial class OnityAsyncTriggerExtensions
    {
        /// <summary>Gets or adds the awake trigger. Main thread only.</summary>
        /// <param name="gameObject">Live owner GameObject.</param>
        /// <returns>The single <see cref="OnityAsyncAwakeTrigger"/> on the GameObject.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="gameObject"/> is null.</exception>
        public static OnityAsyncAwakeTrigger GetAsyncAwakeTrigger(this GameObject gameObject)
        {
            return GetOrAddComponent<OnityAsyncAwakeTrigger>(gameObject);
        }

        /// <summary>Gets or adds the awake trigger on the component's GameObject. Main thread only.</summary>
        /// <param name="component">Live owner component.</param>
        /// <returns>The single <see cref="OnityAsyncAwakeTrigger"/> on the GameObject.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="component"/> is null.</exception>
        public static OnityAsyncAwakeTrigger GetAsyncAwakeTrigger(this Component component)
        {
            return GetGameObject(component).GetAsyncAwakeTrigger();
        }

        /// <summary>Gets or adds the start trigger. Main thread only.</summary>
        /// <param name="gameObject">Live owner GameObject.</param>
        /// <returns>The single <see cref="OnityAsyncStartTrigger"/> on the GameObject.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="gameObject"/> is null.</exception>
        public static OnityAsyncStartTrigger GetAsyncStartTrigger(this GameObject gameObject)
        {
            return GetOrAddComponent<OnityAsyncStartTrigger>(gameObject);
        }

        /// <summary>Gets or adds the start trigger on the component's GameObject. Main thread only.</summary>
        /// <param name="component">Live owner component.</param>
        /// <returns>The single <see cref="OnityAsyncStartTrigger"/> on the GameObject.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="component"/> is null.</exception>
        public static OnityAsyncStartTrigger GetAsyncStartTrigger(this Component component)
        {
            return GetGameObject(component).GetAsyncStartTrigger();
        }

        /// <summary>Gets or adds the enable trigger. Main thread only.</summary>
        /// <param name="gameObject">Live owner GameObject.</param>
        /// <returns>The single <see cref="OnityAsyncEnableTrigger"/> on the GameObject.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="gameObject"/> is null.</exception>
        public static OnityAsyncEnableTrigger GetAsyncEnableTrigger(this GameObject gameObject)
        {
            return GetOrAddComponent<OnityAsyncEnableTrigger>(gameObject);
        }

        /// <summary>Gets or adds the enable trigger on the component's GameObject. Main thread only.</summary>
        /// <param name="component">Live owner component.</param>
        /// <returns>The single <see cref="OnityAsyncEnableTrigger"/> on the GameObject.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="component"/> is null.</exception>
        public static OnityAsyncEnableTrigger GetAsyncEnableTrigger(this Component component)
        {
            return GetGameObject(component).GetAsyncEnableTrigger();
        }

        /// <summary>Gets or adds the disable trigger. Main thread only.</summary>
        /// <param name="gameObject">Live owner GameObject.</param>
        /// <returns>The single <see cref="OnityAsyncDisableTrigger"/> on the GameObject.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="gameObject"/> is null.</exception>
        public static OnityAsyncDisableTrigger GetAsyncDisableTrigger(this GameObject gameObject)
        {
            return GetOrAddComponent<OnityAsyncDisableTrigger>(gameObject);
        }

        /// <summary>Gets or adds the disable trigger on the component's GameObject. Main thread only.</summary>
        /// <param name="component">Live owner component.</param>
        /// <returns>The single <see cref="OnityAsyncDisableTrigger"/> on the GameObject.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="component"/> is null.</exception>
        public static OnityAsyncDisableTrigger GetAsyncDisableTrigger(this Component component)
        {
            return GetGameObject(component).GetAsyncDisableTrigger();
        }

        /// <summary>
        /// Gets or adds the destroy trigger. It is the same hidden component that backs
        /// <c>GetCancellationTokenOnDestroy()</c> for GameObjects and non-MonoBehaviour components.
        /// Main thread only.
        /// </summary>
        /// <param name="gameObject">Live owner GameObject.</param>
        /// <returns>The single <see cref="OnityAsyncDestroyTrigger"/> on the GameObject.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="gameObject"/> is null.</exception>
        public static OnityAsyncDestroyTrigger GetAsyncDestroyTrigger(this GameObject gameObject)
        {
            if (ReferenceEquals(gameObject, null))
            {
                throw new ArgumentNullException(nameof(gameObject));
            }

            return OnityAsyncDestroyTrigger.GetOrAdd(gameObject);
        }

        /// <summary>Gets or adds the destroy trigger on the component's GameObject. Main thread only.</summary>
        /// <param name="component">Live owner component.</param>
        /// <returns>The single <see cref="OnityAsyncDestroyTrigger"/> on the GameObject.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="component"/> is null.</exception>
        public static OnityAsyncDestroyTrigger GetAsyncDestroyTrigger(this Component component)
        {
            return GetGameObject(component).GetAsyncDestroyTrigger();
        }

        /// <summary>
        /// Waits until the GameObject is destroyed. Completes immediately for an already destroyed
        /// owner (UniTask throws there). Main thread only.
        /// </summary>
        /// <param name="gameObject">Owner GameObject.</param>
        /// <returns>A single-consumption task that completes successfully on destroy.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="gameObject"/> is null.</exception>
        public static OnityTask OnDestroyAsync(this GameObject gameObject)
        {
            if (ReferenceEquals(gameObject, null))
            {
                throw new ArgumentNullException(nameof(gameObject));
            }

            if (gameObject == null)
            {
                return OnityTask.CompletedTask;
            }

            return OnityAsyncDestroyTrigger.GetOrAdd(gameObject).OnDestroyAsync();
        }

        /// <summary>
        /// Waits until the component's GameObject is destroyed. Completes immediately for an
        /// already destroyed owner. Main thread only.
        /// </summary>
        /// <param name="component">Owner component.</param>
        /// <returns>A single-consumption task that completes successfully on destroy.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="component"/> is null.</exception>
        public static OnityTask OnDestroyAsync(this Component component)
        {
            if (ReferenceEquals(component, null))
            {
                throw new ArgumentNullException(nameof(component));
            }

            if (component == null)
            {
                return OnityTask.CompletedTask;
            }

            return component.gameObject.OnDestroyAsync();
        }

        /// <summary>Waits for the GameObject's start trigger <c>Start</c>. Main thread only.</summary>
        /// <param name="gameObject">Live owner GameObject.</param>
        /// <returns>See <see cref="OnityAsyncStartTrigger.StartAsync"/>.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="gameObject"/> is null.</exception>
        public static OnityTask StartAsync(this GameObject gameObject)
        {
            return gameObject.GetAsyncStartTrigger().StartAsync();
        }

        /// <summary>Waits for the start trigger <c>Start</c> on the component's GameObject. Main thread only.</summary>
        /// <param name="component">Live owner component.</param>
        /// <returns>See <see cref="OnityAsyncStartTrigger.StartAsync"/>.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="component"/> is null.</exception>
        public static OnityTask StartAsync(this Component component)
        {
            return component.GetAsyncStartTrigger().StartAsync();
        }

        /// <summary>Waits for the GameObject's awake trigger <c>Awake</c>. Main thread only.</summary>
        /// <param name="gameObject">Live owner GameObject.</param>
        /// <returns>See <see cref="OnityAsyncAwakeTrigger.AwakeAsync"/>.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="gameObject"/> is null.</exception>
        public static OnityTask AwakeAsync(this GameObject gameObject)
        {
            return gameObject.GetAsyncAwakeTrigger().AwakeAsync();
        }

        /// <summary>Waits for the awake trigger <c>Awake</c> on the component's GameObject. Main thread only.</summary>
        /// <param name="component">Live owner component.</param>
        /// <returns>See <see cref="OnityAsyncAwakeTrigger.AwakeAsync"/>.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="component"/> is null.</exception>
        public static OnityTask AwakeAsync(this Component component)
        {
            return component.GetAsyncAwakeTrigger().AwakeAsync();
        }

        private static T GetOrAddComponent<T>(GameObject gameObject)
            where T : Component
        {
            if (ReferenceEquals(gameObject, null))
            {
                throw new ArgumentNullException(nameof(gameObject));
            }

            if (!gameObject.TryGetComponent(out T component))
            {
                component = gameObject.AddComponent<T>();
            }

            return component;
        }

        private static GameObject GetGameObject(Component component)
        {
            if (ReferenceEquals(component, null))
            {
                throw new ArgumentNullException(nameof(component));
            }

            return component.gameObject;
        }
    }
}
