using System;
using System.Collections.Generic;
using Onity.DI;
using Onity.Messaging;
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

        [Tooltip("Runs async post-build callbacks after initial context setup.")]
        [SerializeField] private bool m_runAsyncBuildCallbacks = true;

        private OnityContainer m_container;

        /// <summary>
        /// Container instance owned by this context.
        /// </summary>
        public OnityContainer Container => m_container;

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
        /// Executes asynchronous post-build callbacks after initial setup.
        /// </summary>
        protected virtual async void Start()
        {
            if (m_runAsyncBuildCallbacks == false || m_container == null)
            {
                return;
            }

            try
            {
                await m_container.BuildAsync();
            }
            catch (Exception exception)
            {
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
            UnregisterActiveContext();
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

        private void RegisterDefaultBindings()
        {
            using (m_container.PushBindingSource(BuildDefaultBindingSource()))
            {
                m_container.BindInstance(m_container);
                m_container.BindInstance<IResolver>(m_container);
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
