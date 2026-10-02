using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Onity.DI;
using Onity.Messaging;
using Onity.Unity.Async;
using Onity.Unity.Installers;
using Onity.Unity.Messaging;
using UnityEngine;

namespace Onity.Unity.Contexts
{
    /// <summary>
    /// Base context scope that owns a container.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-9500)]
    public abstract class OnityContext : MonoBehaviour
    {
        private static readonly List<OnityContext> s_activeContexts = new List<OnityContext>(8);

        [Header("Context Setup")]
        [Tooltip("Installers executed during context initialization.")]
        [SerializeField] private MonoInstaller[] m_installers = Array.Empty<MonoInstaller>();

        [Tooltip("Optional explicit parent context. Leave null for automatic parent discovery.")]
        [SerializeField] private OnityContext m_parentContext;

        [Tooltip("Inject all MonoBehaviours under this context root during Awake.")]
        [SerializeField] private bool m_autoInjectHierarchy = true;

        [Tooltip("Runs BuildAsync in Start: async post-build callbacks, then IOnityAsyncInitializable entry points.")]
        [SerializeField] private bool m_runAsyncBuildCallbacks = true;

        private OnityContainer m_container;
        private bool m_isDestroyed;
        private readonly TaskCompletionSource<bool> m_readyCompletionSource =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        // Native counterpart of m_readyCompletionSource, created by the first pending WaitReadyAsync.
        private OnityTaskCompletionSource m_readySource;

        /// <summary>
        /// Container instance owned by this context.
        /// </summary>
        public OnityContainer Container => m_container;

        /// <summary>
        /// Gets a token that is canceled when this context's scope ends. <see cref="OnDestroy" />
        /// disposes the container, which cancels the token before the scope's services are disposed.
        /// Main thread only.
        /// </summary>
        /// <remarks>
        /// This is the container's <see cref="OnityContainer.LifetimeToken" />; services in the scope
        /// get the same token by injecting <see cref="IOnityScopeLifetime" />. Before <c>Awake</c>
        /// creates the container (an inactive context), the context's destroy token is returned. A
        /// destroyed context returns a canceled token.
        /// </remarks>
        public CancellationToken LifetimeToken
        {
            get
            {
                OnityContainer container = m_container;

                if (container != null)
                {
                    return container.LifetimeToken;
                }

                return m_isDestroyed ? new CancellationToken(true) : this.GetCancellationTokenOnDestroy();
            }
        }

        /// <summary>
        /// Gets whether synchronous and configured asynchronous container build callbacks completed,
        /// including every <see cref="IOnityAsyncInitializable" /> entry point.
        /// </summary>
        public bool IsReady { get; private set; }

        /// <summary>
        /// Completes when synchronous and configured asynchronous container build callbacks complete,
        /// including every <see cref="IOnityAsyncInitializable" /> entry point.
        /// </summary>
        public Task ReadyTask => m_readyCompletionSource.Task;

        /// <summary>
        /// Waits until the context is ready (<see cref="IsReady" />): the native
        /// <see cref="OnityTask" /> counterpart of <see cref="ReadyTask" />. Main thread only.
        /// </summary>
        /// <param name="cancellationToken">Cancels this wait only; the build keeps running.</param>
        /// <returns>
        /// A completed task when the context is already ready. Otherwise a task that completes when
        /// the context becomes ready, faults with the build failure, or is canceled when the context
        /// is destroyed first or <paramref name="cancellationToken" /> is canceled.
        /// </returns>
        /// <remarks>
        /// The context becomes ready on Unity's main thread, so the awaiting code resumes there. The
        /// returned task can be awaited once.
        /// </remarks>
        public OnityTask WaitReadyAsync(CancellationToken cancellationToken = default)
        {
            if (IsReady)
            {
                return OnityTask.CompletedTask;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return OnityTask.FromCanceled(cancellationToken);
            }

            OnityTaskCompletionSource source = m_readySource ??= CreateReadySource();
            return source.Task.AttachExternalCancellation(cancellationToken);
        }

        /// <summary>
        /// Creates and configures the container.
        /// </summary>
        protected virtual void Awake()
        {
            CreateContainer();
            RegisterDefaultBindings();
            InstallBindings();
            m_container.Build();
            RegisterActiveContext();

            if (m_autoInjectHierarchy)
            {
                InjectGameObject(gameObject);
            }
        }

        /// <summary>
        /// Executes asynchronous post-build callbacks after initial setup. The callbacks receive
        /// <see cref="LifetimeToken" />, so destroying the context cancels them.
        /// </summary>
        protected virtual async void Start()
        {
            OnityContainer container = m_container;

            if (m_runAsyncBuildCallbacks == false || container == null)
            {
                SetReady();
                return;
            }

            try
            {
                await container.BuildAsync(container.LifetimeToken);

                if (container.IsDisposed == false)
                {
                    SetReady();
                }
            }
            catch (OperationCanceledException) when (container.IsDisposed)
            {
                // Destroyed during the async build: OnDestroy already canceled ReadyTask.
            }
            catch (Exception exception)
            {
                m_readyCompletionSource.TrySetException(exception);
                m_readySource?.TrySetException(exception);
                Debug.LogException(exception, this);
            }
        }

        /// <summary>
        /// Pumps the container's per-frame <see cref="IOnityTickable" /> entry points.
        /// </summary>
        protected virtual void Update()
        {
            m_container?.Tick();
        }

        /// <summary>
        /// Pumps the container's physics-step <see cref="IOnityFixedTickable" /> entry points.
        /// </summary>
        protected virtual void FixedUpdate()
        {
            m_container?.FixedTick();
        }

        /// <summary>
        /// Pumps the container's late <see cref="IOnityLateTickable" /> entry points.
        /// </summary>
        protected virtual void LateUpdate()
        {
            m_container?.LateTick();
        }

        /// <summary>
        /// Disposes container on context teardown.
        /// </summary>
        protected virtual void OnDestroy()
        {
            m_isDestroyed = true;
            UnregisterActiveContext();
            m_readyCompletionSource.TrySetCanceled();
            m_readySource?.TrySetCanceled();
            m_container?.Dispose();
            m_container = null;
        }

        /// <summary>
        /// Injects all MonoBehaviours found under the provided root.
        /// </summary>
        /// <param name="root">Root object for hierarchy scan.</param>
        public void InjectGameObject(GameObject root)
        {
            if (root == null || m_container == null)
            {
                return;
            }

            MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);

            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];

                if (behaviour == null)
                {
                    continue;
                }

                if (ReferenceEquals(behaviour, this))
                {
                    continue;
                }

                if (behaviour is MonoInstaller || behaviour is OnityContext)
                {
                    continue;
                }

                OnityContext nearestContext = behaviour.GetComponentInParent<OnityContext>(true);

                if (nearestContext != null && nearestContext != this)
                {
                    continue;
                }

                m_container.Inject(behaviour);
            }
        }

        /// <summary>
        /// Override to provide automatic parent lookup behavior.
        /// </summary>
        /// <returns>Discovered parent context or null.</returns>
        protected virtual OnityContext ResolveDefaultParentContext()
        {
            return null;
        }

        internal static bool TryResolveDefault<TService>(out TService service)
        {
            if (TryResolveLastActive<SceneContext, TService>(out service))
            {
                return true;
            }

            if (TryResolveFromContext(ProjectContext.Instance, out service))
            {
                return true;
            }

            for (int i = s_activeContexts.Count - 1; i >= 0; i--)
            {
                OnityContext context = s_activeContexts[i];

                if (context == null)
                {
                    s_activeContexts.RemoveAt(i);
                    continue;
                }

                if (context is SceneContext || context is ProjectContext)
                {
                    continue;
                }

                if (TryResolveFromContext(context, out service))
                {
                    return true;
                }
            }

            service = default;
            return false;
        }

        internal static bool TryResolveNearest<TService>(Component owner, out TService service)
        {
            if (owner == null)
            {
                service = default;
                return false;
            }

            OnityContext context = owner as OnityContext;

            if (context == null)
            {
                context = owner.GetComponentInParent<OnityContext>(true);
            }

            return TryResolveFromContext(context, out service);
        }

        private static bool TryResolveLastActive<TContext, TService>(out TService service)
            where TContext : OnityContext
        {
            for (int i = s_activeContexts.Count - 1; i >= 0; i--)
            {
                OnityContext context = s_activeContexts[i];

                if (context == null)
                {
                    s_activeContexts.RemoveAt(i);
                    continue;
                }

                if (context is TContext && TryResolveFromContext(context, out service))
                {
                    return true;
                }
            }

            service = default;
            return false;
        }

        private static bool TryResolveFromContext<TService>(OnityContext context, out TService service)
        {
            if (context != null
                && context.m_container != null
                && context.m_container.TryResolve(out service))
            {
                return true;
            }

            service = default;
            return false;
        }

        private void CreateContainer()
        {
            OnityContainer parent = null;

            if (m_parentContext != null && m_parentContext != this)
            {
                parent = m_parentContext.Container;
            }

            if (parent == null)
            {
                OnityContext discoveredParent = ResolveDefaultParentContext();

                if (discoveredParent != null)
                {
                    parent = discoveredParent.Container;
                }
            }

            m_container = new OnityContainer(parent);
        }

        private void RegisterActiveContext()
        {
            s_activeContexts.Remove(this);
            s_activeContexts.Add(this);
        }

        private void UnregisterActiveContext()
        {
            s_activeContexts.Remove(this);
        }

        private void SetReady()
        {
            IsReady = true;
            m_readyCompletionSource.TrySetResult(true);
            m_readySource?.TrySetResult();
        }

        // Mirrors the readiness reached before the first pending WaitReadyAsync call.
        private OnityTaskCompletionSource CreateReadySource()
        {
            OnityTaskCompletionSource source = new OnityTaskCompletionSource();
            Task readyTask = m_readyCompletionSource.Task;

            if (readyTask.IsCanceled)
            {
                source.TrySetCanceled();
            }
            else if (readyTask.IsFaulted)
            {
                source.TrySetException(readyTask.Exception.InnerException ?? readyTask.Exception);
            }
            else if (readyTask.IsCompleted)
            {
                source.TrySetResult();
            }

            return source;
        }

        private void RegisterDefaultBindings()
        {
            using (m_container.PushBindingSource(BuildDefaultBindingSource()))
            {
                m_container.BindInstance(m_container);
                m_container.BindInstance<IResolver>(m_container);
                m_container.BindInstance<IOnityScopeLifetime>(m_container);
                m_container.BindInstance(this);
                m_container.BindInterfacesAndSelfTo<MessageBroker>().AsSingle();
                m_container.BindInterfacesAndSelfTo<OnityEventHub>().AsSingle();
            }
        }

        private void InstallBindings()
        {
            if (m_installers == null)
            {
                return;
            }

            for (int i = 0; i < m_installers.Length; i++)
            {
                MonoInstaller installer = m_installers[i];

                if (installer == null)
                {
                    continue;
                }

                using (m_container.PushBindingSource(BuildInstallerBindingSource(installer, i)))
                {
                    installer.InstallBindings(m_container);
                }
            }
        }

        private string BuildDefaultBindingSource()
        {
            string contextPath = GetHierarchyPath(transform);
            return $"Default Bindings: {GetType().Name} ({contextPath})";
        }

        private string BuildInstallerBindingSource(MonoInstaller installer, int index)
        {
            string contextPath = GetHierarchyPath(transform);
            string installerPath = GetHierarchyPath(installer.transform);
            return $"Installer Step {index + 1}: {installer.GetType().Name} ({installerPath}) in {contextPath}";
        }

        private static string GetHierarchyPath(Transform transform)
        {
            if (transform == null)
            {
                return "<null>";
            }

            string path = transform.name;
            Transform current = transform.parent;

            while (current != null)
            {
                path = $"{current.name}/{path}";
                current = current.parent;
            }

            return path;
        }
    }
}
