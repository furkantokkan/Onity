using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Onity.Core;
using Onity.DI.Internal;
using Onity.Factory;

namespace Onity.DI
{
    internal enum BindingLifetime
    {
        Singleton,
        Transient,
        Scoped
    }

    /// <summary>
    /// Lightweight dependency container with parent-scope support.
    /// </summary>
    public sealed class OnityContainer : IResolver, IDisposable, IOnityScopeLifetime
    {
        [ThreadStatic]
        private static Stack<Type> s_resolutionStack;
        [ThreadStatic]
        private static Stack<string> s_bindingSourceStack;
        private static bool s_diagnosticsCollectionEnabled;
        private static readonly int s_containerTypeId = TypeIdRegistry.Register(typeof(OnityContainer));
        private static readonly int s_resolverTypeId = TypeIdRegistry.Register(typeof(IResolver));
        // Baked resolve is the default. Build() compiles a BakedGraph and Resolve takes a
        // dense-id, array-indexed fast path that drops the per-resolve dictionary
        // lookup. The baked path only fast-paths explicit local bindings; every
        // other contract (parent, implicit concrete, unbound) defers to the same
        // reflection path, so results are identical under both flag values.
        internal static bool s_useBakedResolve = true;

        /// <summary>
        /// Internal toggle for the baked-resolve fast path. Defaults to true.
        /// Exposed for the parity test suite that asserts identical results
        /// under both values.
        /// </summary>
        internal static bool UseBakedResolve
        {
            get => s_useBakedResolve;
            set => s_useBakedResolve = value;
        }

        /// <summary>
        /// Forces reflection-based activation and member injection instead of the
        /// compiled <c>Expression.Compile</c> fast path, even on a JIT runtime where
        /// compilation is supported. Defaults to false (auto-detect: compiled on JIT,
        /// reflection on AOT/IL2CPP). Set it true in the Editor to pre-flight a graph
        /// under the same activation strategy an IL2CPP build uses. Set before the
        /// first container <c>Build()</c>; the per-process activator cache is
        /// populated on first use and is not re-evaluated afterward.
        /// </summary>
        internal static bool ForceReflectionActivation
        {
            get => RuntimeCompileSupport.ForceReflection;
            set => RuntimeCompileSupport.ForceReflection = value;
        }

        /// <summary>
        /// True when the DI layer is using the compiled <c>Expression.Compile</c> fast
        /// path (JIT runtimes: the Editor and Mono players); false when it has fallen
        /// back to reflection (AOT/IL2CPP runtimes, or <see cref="ForceReflectionActivation" />).
        /// Read this in an IL2CPP build to confirm which activation strategy is live on
        /// device - the container constructs and injects correctly either way.
        /// </summary>
        public static bool IsCompiledActivationSupported => RuntimeCompileSupport.IsExpressionCompileSupported;
        private static readonly DependencyResolution[] s_emptyDependencyResolutions = new DependencyResolution[0];
        private const int k_initialBindingCapacity = 24;
        private const string k_unknownBindingSource = "Unknown Binding Source";
        private const string k_implicitBindingSource = "Implicit (Auto-Resolve)";

        private readonly OnityContainer m_parent;
        private readonly Dictionary<Type, IProvider> m_providerMap;
        private IProvider[] m_providerByTypeId;
        private readonly Dictionary<Type, IProvider> m_implicitProviderMap;
        private readonly Dictionary<Type, BindingSourceRecord> m_bindingSourceMap;
        private readonly Dictionary<Type, TypeInjectionPlan> m_planMap;
        private readonly List<IProvider> m_ownedProviders;
        private readonly List<Action<IResolver>> m_buildCallbacks;
        private readonly List<Func<IResolver, CancellationToken, Task>> m_asyncBuildCallbacks;
        private bool m_isBuildFinalized;
        private bool m_lifecycleReady;
        private bool m_isInitializingLifecycle;
        private HashSet<object> m_buildLifecycleInstances;
        private Task m_cachedBuildTask;
        private bool m_isDisposed;
        // Scope lifetime: created by the first LifetimeToken request (a container whose token is
        // never requested allocates nothing for it) and canceled first in Dispose(). Lifetime and
        // build state only; the resolve path never reads these fields.
        private ScopeLifetimeSource m_lifetimeSource;
        // Caller-and-lifetime linked sources created by BuildAsync calls that pass their own token;
        // disposed with the scope after the lifetime token is canceled.
        private List<CancellationTokenSource> m_buildTokenSources;
        // True once every async build callback completed in one BuildAsync run; later runs (a retry
        // after an async initializer failed, or late async initializers) skip the callbacks.
        private bool m_asyncBuildCallbacksCompleted;
        // Async lifecycle entry points, collected like m_initializables (lazily allocated), and the
        // number at the front of the list that already completed InitializeAsync.
        private List<IOnityAsyncInitializable> m_asyncInitializables;
        private int m_asyncInitializedCount;
        // Compiled, array-backed view of this scope's explicit local bindings.
        // Non-null after Build() runs while UseBakedResolve is true. Replaced when
        // bindings change after Build() so the Resolve hot path sees current providers.
        private BakedGraph m_baked;
        // Bumped whenever an explicit provider is registered or replaced. Lets the
        // per-plan constructor-dependency resolution cache fast-path a same-scope
        // provider while staying correct if a contract is rebound after resolve:
        // a version mismatch forces re-resolution instead of using a stale provider.
        private int m_bindingVersion;
        // Lifecycle entry points collected at Build() from singleton/instance bindings
        // that implement the lifecycle interfaces. Lazily allocated (null when no such
        // bindings exist) so containers that use none stay allocation-free here.
        // Ticked by the owning Unity context's per-frame pumps.
        private List<IOnityInitializable> m_initializables;
        private List<IOnityTickable> m_tickables;
        private List<IOnityFixedTickable> m_fixedTickables;
        private List<IOnityLateTickable> m_lateTickables;
        // Accumulates every explicit provider per contract (m_providerMap keeps only
        // the last, preserving single-resolve last-wins back-compat). Lazily allocated;
        // read only to synthesize IEnumerable<T>/IReadOnlyList<T>/T[]/List<T> collection
        // resolves, never on the single-resolve hot path.
        private Dictionary<Type, List<IProvider>> m_multiProviderMap;
        // Open generic registrations keyed by open contract definition (e.g. IRepo<>).
        // Lazily allocated. On first resolve of a closed contract (IRepo<Foo>) the closed
        // implementation is built and cached as a normal binding, so later resolves hit
        // the fast path. Never read on the single-resolve hot path.
        private Dictionary<Type, OpenGenericRegistration> m_openGenericMap;
        // Tracks only closed providers generated from open registrations. An open
        // rebind replaces these entries without overriding explicit closed binds.
        private Dictionary<Type, IProvider> m_closedOpenGenericProviders;
        // Identified and consumer-specific bindings never enter the unkeyed baked graph.
        private Dictionary<BindingKey, List<ContextBinding>> m_contextBindings;
        // Dense contract id -> direct unconditional keyed bindings. Built during
        // registration; conditional keys stay on the full selection path.
        private Dictionary<object, ContextBinding>[] m_directKeyedBindings;
        private HashSet<IProvider> m_retiredProviders;
        private Dictionary<IProvider, object> m_lifecycleInstances;
        private Dictionary<IProvider, object> m_scopedInstances;
        private Dictionary<SubContainerProvider, OnityContainer> m_subContainerScopes;
        private List<OnityContainer> m_subContainerScopeList;

        /// <summary>
        /// Enables or disables runtime collection of resolve timing/count metrics used by editor diagnostics.
        /// </summary>
        public static bool DiagnosticsCollectionEnabled
        {
            get => s_diagnosticsCollectionEnabled;
            set => s_diagnosticsCollectionEnabled = value;
        }

        /// <summary>
        /// Initializes a new container.
        /// </summary>
        /// <param name="parent">Optional parent container for fallback resolves.</param>
        public OnityContainer(OnityContainer parent = null)
        {
            m_parent = parent;
            // Common local scopes fit without growth; unused lookup tables keep
            // their backing arrays empty until the first registration or resolve.
            m_providerMap = new Dictionary<Type, IProvider>(k_initialBindingCapacity);
            m_implicitProviderMap = new Dictionary<Type, IProvider>();
            m_bindingSourceMap = new Dictionary<Type, BindingSourceRecord>(k_initialBindingCapacity);
            m_planMap = new Dictionary<Type, TypeInjectionPlan>();
            m_ownedProviders = new List<IProvider>(k_initialBindingCapacity);
            m_buildCallbacks = new List<Action<IResolver>>();
            m_asyncBuildCallbacks = new List<Func<IResolver, CancellationToken, Task>>();
            m_isBuildFinalized = false;
            m_cachedBuildTask = null;
        }

        /// <summary>
        /// Gets a token that is canceled when this container is disposed. <see cref="Dispose" />
        /// cancels it first, before it disposes child scopes, scoped instances and singletons, so
        /// work bound to the scope stops before the services it uses are torn down.
        /// </summary>
        /// <remarks>
        /// The source is created on the first request; a container whose token is never requested
        /// allocates nothing for it. A child container's token is linked to its parent's token, so
        /// disposing the parent also cancels the child's token. A disposed container returns a
        /// canceled token. Thread-safe.
        /// </remarks>
        public CancellationToken LifetimeToken
        {
            get
            {
                ScopeLifetimeSource source = Volatile.Read(ref m_lifetimeSource);
                return source != null ? source.Token : CreateLifetimeToken();
            }
        }

        /// <inheritdoc />
        CancellationToken IOnityScopeLifetime.Token => LifetimeToken;

        /// <summary>
        /// Gets whether <see cref="Dispose" /> has run.
        /// </summary>
        public bool IsDisposed => m_isDisposed;

        /// <summary>
        /// Starts a fluent binding for one contract.
        /// </summary>
        /// <typeparam name="TContract">Contract type.</typeparam>
        /// <returns>Fluent builder.</returns>
        public TypeBindingBuilder<TContract> Bind<TContract>()
        {
            EnsureNotDisposed();
            return new TypeBindingBuilder<TContract>(this);
        }

        /// <summary>Replaces all local bindings for this contract and identifier when a lifetime is selected.</summary>
        /// <typeparam name="TContract">Contract type.</typeparam>
        /// <param name="id">Exact identifier to replace; null selects unkeyed bindings.</param>
        /// <returns>A builder; existing bindings remain until successful registration.</returns>
        public TypeBindingBuilder<TContract> Rebind<TContract>(object id = null)
        {
            EnsureNotDisposed();
            return new TypeBindingBuilder<TContract>(this, true, id);
        }

        /// <summary>Replaces local bindings for a runtime contract and identifier when a lifetime is selected.</summary>
        /// <param name="contractType">Contract type, optionally an open generic definition.</param>
        /// <param name="id">Exact identifier to replace; null selects unkeyed bindings.</param>
        /// <returns>A builder; existing bindings remain until successful registration.</returns>
        public RuntimeTypeBindingBuilder Rebind(Type contractType, object id = null)
        {
            EnsureNotDisposed();

            if (contractType == null)
            {
                throw new OnityBindingException("Contract type cannot be null.");
            }

            return new RuntimeTypeBindingBuilder(this, contractType, true, id);
        }

        /// <summary>Removes all local bindings for the exact contract and identifier, including conditions.</summary>
        /// <typeparam name="TContract">Contract type.</typeparam>
        /// <param name="id">Exact identifier; null removes only unkeyed bindings.</param>
        /// <returns>True when a local registration was removed. Parent registrations are unchanged.</returns>
        public bool Unbind<TContract>(object id = null)
        {
            return Unbind(typeof(TContract), id);
        }

        /// <summary>
        /// Removes local registrations for an exact contract and identifier. Removed singleton instances
        /// stop ticking but remain owned until disposal. Unbound concrete types may still auto-resolve.
        /// </summary>
        /// <param name="contractType">Contract type, optionally an open generic definition.</param>
        /// <param name="id">Exact identifier; null removes only unkeyed bindings.</param>
        /// <returns>True when a local registration was removed.</returns>
        public bool Unbind(Type contractType, object id = null)
        {
            EnsureNotDisposed();

            if (contractType == null)
            {
                throw new OnityBindingException("Contract type cannot be null.");
            }

            bool removed = m_contextBindings != null
                && m_contextBindings.Remove(new BindingKey(contractType, id));
            if (removed)
            {
                SetDirectKeyedBinding(contractType, id, null);
            }

            if (id == null)
            {
                removed |= RemoveLocalProvider(contractType);
                m_bindingSourceMap.Remove(contractType);

                if (m_multiProviderMap != null)
                {
                    removed |= m_multiProviderMap.Remove(contractType);
                }

                if (m_openGenericMap != null)
                {
                    removed |= m_openGenericMap.Remove(contractType);
                }

                if (m_closedOpenGenericProviders != null)
                {
                    m_closedOpenGenericProviders.Remove(contractType);

                    if (contractType.IsGenericTypeDefinition)
                    {
                        List<Type> closedTypes = null;

                        foreach (KeyValuePair<Type, IProvider> pair in m_closedOpenGenericProviders)
                        {
                            if (pair.Key.GetGenericTypeDefinition() == contractType)
                            {
                                (closedTypes ??= new List<Type>()).Add(pair.Key);
                            }
                        }

                        if (closedTypes != null)
                        {
                            for (int i = 0; i < closedTypes.Count; i++)
                            {
                                Type closedType = closedTypes[i];
                                IProvider provider = m_closedOpenGenericProviders[closedType];
                                m_closedOpenGenericProviders.Remove(closedType);
                                RemoveGeneratedProvider(closedType, provider);
                                removed = true;
                            }
                        }
                    }
                }
            }

            if (removed)
            {
                m_bindingVersion++;
                RetireUnboundProviders();

                if (m_baked != null)
                {
                    m_baked = BuildBakedGraph();
                }
            }

            return removed;
        }

        /// <summary>
        /// Starts a fluent binding for a contract given as a runtime <see cref="Type" />.
        /// Supports open generic definitions, e.g.
        /// <c>Bind(typeof(IRepo&lt;&gt;)).To(typeof(Repo&lt;&gt;)).AsSingle()</c>, so a later
        /// resolve of a closed <c>IRepo&lt;Foo&gt;</c> constructs <c>Repo&lt;Foo&gt;</c> on
        /// demand, as well as closed runtime-typed bindings. Open generic resolution uses
        /// <c>MakeGenericType</c>; on IL2CPP the closed type must survive AOT stripping
        /// (reference it statically or preserve it).
        /// </summary>
        /// <param name="contractType">Contract type, open generic definition or closed.</param>
        /// <returns>Fluent builder.</returns>
        public RuntimeTypeBindingBuilder Bind(Type contractType)
        {
            EnsureNotDisposed();

            if (contractType == null)
            {
                throw new OnityBindingException("Contract type cannot be null.");
            }

            return new RuntimeTypeBindingBuilder(this, contractType);
        }

        /// <summary>
        /// Binds implementation type to all implemented interfaces and to itself.
        /// </summary>
        /// <typeparam name="TConcrete">Implementation type.</typeparam>
        /// <returns>Fluent builder.</returns>
        public MultiTypeBindingBuilder BindInterfacesAndSelfTo<TConcrete>()
            where TConcrete : class
        {
            EnsureNotDisposed();
            Type implementationType = typeof(TConcrete);
            Type[] contractTypes = CollectInterfacesAndSelf(implementationType);
            return new MultiTypeBindingBuilder(this, contractTypes, implementationType);
        }

        /// <summary>
        /// Binds implementation type to all implemented interfaces.
        /// </summary>
        /// <typeparam name="TConcrete">Implementation type.</typeparam>
        /// <returns>Fluent builder.</returns>
        public MultiTypeBindingBuilder BindInterfacesTo<TConcrete>()
            where TConcrete : class
        {
            EnsureNotDisposed();
            Type implementationType = typeof(TConcrete);
            Type[] contractTypes = CollectInterfaces(implementationType);
            return new MultiTypeBindingBuilder(this, contractTypes, implementationType);
        }

        /// <summary>
        /// Binds a concrete instance to a contract.
        /// </summary>
        /// <typeparam name="TContract">Contract type.</typeparam>
        /// <param name="instance">Instance to bind.</param>
        public void BindInstance<TContract>(TContract instance)
        {
            EnsureNotDisposed();

            if (ReferenceEquals(instance, null))
            {
                throw new OnityBindingException("Cannot bind a null instance.");
            }

            RegisterProvider(typeof(TContract), new InstanceProvider(instance), false);
        }

        /// <summary>Binds a caller-owned instance with an identifier. The container does not dispose it.</summary>
        /// <typeparam name="TContract">Contract type.</typeparam>
        /// <param name="instance">Non-null instance.</param>
        /// <param name="id">Identifier; null selects the ordinary unkeyed binding.</param>
        public void BindInstance<TContract>(TContract instance, object id)
        {
            EnsureNotDisposed();

            if (id == null)
            {
                BindInstance(instance);
                return;
            }

            if (ReferenceEquals(instance, null))
            {
                throw new OnityBindingException("Cannot bind a null instance.");
            }

            IProvider provider = new InstanceProvider(instance);
            m_ownedProviders.Add(provider);
            AddContextBinding(typeof(TContract), id, new ContextBinding(provider, null));
            RegisterLifecycleAfterBuild(provider);
        }

        /// <summary>
        /// Binds a factory implementation as singleton.
        /// </summary>
        /// <typeparam name="TValue">Produced value type.</typeparam>
        /// <typeparam name="TFactory">Factory type.</typeparam>
        public void BindFactory<TValue, TFactory>()
            where TFactory : class, IFactory<TValue>
        {
            BindInterfacesAndSelfTo<TFactory>().AsSingle();
        }

        /// <summary>
        /// Binds a one-parameter factory implementation as singleton.
        /// </summary>
        /// <typeparam name="TParam">Factory parameter type.</typeparam>
        /// <typeparam name="TValue">Produced value type.</typeparam>
        /// <typeparam name="TFactory">Factory type.</typeparam>
        public void BindFactory<TParam, TValue, TFactory>()
            where TFactory : class, IFactory<TParam, TValue>
        {
            BindInterfacesAndSelfTo<TFactory>().AsSingle();
        }

        /// <summary>
        /// Binds a two-parameter factory implementation as singleton.
        /// </summary>
        /// <typeparam name="TParam1">First factory parameter type.</typeparam>
        /// <typeparam name="TParam2">Second factory parameter type.</typeparam>
        /// <typeparam name="TValue">Produced value type.</typeparam>
        /// <typeparam name="TFactory">Factory type.</typeparam>
        public void BindFactory<TParam1, TParam2, TValue, TFactory>()
            where TFactory : class, IFactory<TParam1, TParam2, TValue>
        {
            BindInterfacesAndSelfTo<TFactory>().AsSingle();
        }

        /// <summary>
        /// Registers a synchronous callback that runs during container build.
        /// </summary>
        /// <param name="callback">Callback receiving current resolver scope.</param>
        public void RegisterBuildCallback(Action<IResolver> callback)
        {
            EnsureNotDisposed();

            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            EnsureBuildNotFinalized();
            m_buildCallbacks.Add(callback);
        }

        /// <summary>
        /// Registers an asynchronous callback that runs after synchronous build callbacks.
        /// </summary>
        /// <param name="callback">Async callback receiving current resolver scope.</param>
        public void RegisterBuildCallbackAsync(Func<IResolver, Task> callback)
        {
            EnsureNotDisposed();

            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            RegisterBuildCallbackAsync(
                (resolver, _) =>
                {
                    Task task = callback(resolver);
                    return task ?? Task.CompletedTask;
                });
        }

        /// <summary>
        /// Registers an asynchronous callback that runs after synchronous build callbacks.
        /// </summary>
        /// <param name="callback">Async callback receiving resolver scope and cancellation token.</param>
        public void RegisterBuildCallbackAsync(Func<IResolver, CancellationToken, Task> callback)
        {
            EnsureNotDisposed();

            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            EnsureBuildNotFinalized();
            m_asyncBuildCallbacks.Add(callback);
        }

        /// <summary>
        /// Finalizes container bindings and executes synchronous post-build callbacks.
        /// </summary>
        public void Build()
        {
            EnsureNotDisposed();

            if (m_isBuildFinalized)
            {
                return;
            }

            for (int i = 0; i < m_buildCallbacks.Count; i++)
            {
                m_buildCallbacks[i](this);
            }

            m_isBuildFinalized = true;

            if (UseBakedResolve)
            {
                m_baked = BuildBakedGraph();
            }

            CollectAndInitializeLifecycle();
            m_lifecycleReady = true;
        }

        /// <summary>
        /// Runs <see cref="IOnityTickable.Tick" /> on every collected tickable in
        /// registration order. The owning Unity context calls this once per frame
        /// from <c>Update</c>; call it yourself if you drive the loop manually. A
        /// no-op until <see cref="Build" /> has collected the entry points.
        /// </summary>
        public void Tick()
        {
            if (m_isDisposed)
            {
                return;
            }

            if (m_tickables != null)
            {
                for (int i = 0; i < m_tickables.Count; i++)
                {
                    m_tickables[i].Tick();
                    if (m_isDisposed)
                    {
                        return;
                    }
                }
            }

            if (m_subContainerScopeList == null)
            {
                return;
            }

            for (int i = 0; i < m_subContainerScopeList.Count; i++)
            {
                m_subContainerScopeList[i].Tick();
                if (m_isDisposed)
                {
                    return;
                }
            }
        }

        /// <summary>
        /// Runs <see cref="IOnityFixedTickable.FixedTick" /> on every collected fixed
        /// tickable in registration order. The owning Unity context calls this from
        /// <c>FixedUpdate</c>.
        /// </summary>
        public void FixedTick()
        {
            if (m_isDisposed)
            {
                return;
            }

            if (m_fixedTickables != null)
            {
                for (int i = 0; i < m_fixedTickables.Count; i++)
                {
                    m_fixedTickables[i].FixedTick();
                    if (m_isDisposed)
                    {
                        return;
                    }
                }
            }

            if (m_subContainerScopeList == null)
            {
                return;
            }

            for (int i = 0; i < m_subContainerScopeList.Count; i++)
            {
                m_subContainerScopeList[i].FixedTick();
                if (m_isDisposed)
                {
                    return;
                }
            }
        }

        /// <summary>
        /// Runs <see cref="IOnityLateTickable.LateTick" /> on every collected late
        /// tickable in registration order. The owning Unity context calls this from
        /// <c>LateUpdate</c>.
        /// </summary>
        public void LateTick()
        {
            if (m_isDisposed)
            {
                return;
            }

            if (m_lateTickables != null)
            {
                for (int i = 0; i < m_lateTickables.Count; i++)
                {
                    m_lateTickables[i].LateTick();
                    if (m_isDisposed)
                    {
                        return;
                    }
                }
            }

            if (m_subContainerScopeList == null)
            {
                return;
            }

            for (int i = 0; i < m_subContainerScopeList.Count; i++)
            {
                m_subContainerScopeList[i].LateTick();
                if (m_isDisposed)
                {
                    return;
                }
            }
        }

        // Scans this scope's owned providers once at Build() and collects the
        // singleton/instance entry points implementing the lifecycle interfaces, then
        // runs Initialize() in registration order. Only singleton/instance bindings
        // qualify: a transient has no single stable instance to tick. Lifecycle
        // singletons are created eagerly here (like classic entry points); other
        // singletons stay lazy. IDisposable lifecycle objects are disposed by their
        // provider on container Dispose, so no separate disposable list is needed.
        // Async initializables are collected here too; BuildAsync runs them.
        private void CollectAndInitializeLifecycle()
        {
            try
            {
                for (int i = 0; i < m_ownedProviders.Count; i++)
                {
                    RegisterLifecycle(m_ownedProviders[i], false);
                }

                if (m_scopedInstances != null)
                {
                    foreach (IProvider provider in m_scopedInstances.Keys)
                    {
                        RegisterLifecycle(provider, false);
                    }
                }

                m_isInitializingLifecycle = true;
                if (m_initializables != null)
                {
                    for (int i = 0; i < m_initializables.Count; i++)
                    {
                        m_initializables[i].Initialize();
                        EnsureNotDisposed();
                    }
                }
            }
            finally
            {
                m_isInitializingLifecycle = false;
                m_buildLifecycleInstances = null;
            }
        }

        private void RegisterLifecycleAfterBuild(IProvider provider)
        {
            if (m_lifecycleReady)
            {
                RegisterLifecycle(provider, true);
            }
        }

        private void RegisterLifecycle(IProvider provider, bool initialize)
        {
            if ((m_retiredProviders != null && m_retiredProviders.Contains(provider))
                || (m_lifecycleInstances != null && m_lifecycleInstances.ContainsKey(provider))
                || provider is SubContainerProvider)
            {
                return;
            }

            BakedLifetime lifetime = provider.BakedLifetime;
            if (lifetime != BakedLifetime.Singleton && lifetime != BakedLifetime.Instance
                && lifetime != BakedLifetime.Scoped)
            {
                return;
            }

            Type implementationType = provider.ImplementationType;
            bool isInitializable = typeof(IOnityInitializable).IsAssignableFrom(implementationType);
            bool isTickable = typeof(IOnityTickable).IsAssignableFrom(implementationType);
            bool isFixedTickable = typeof(IOnityFixedTickable).IsAssignableFrom(implementationType);
            bool isLateTickable = typeof(IOnityLateTickable).IsAssignableFrom(implementationType);
            bool isAsyncInitializable = typeof(IOnityAsyncInitializable).IsAssignableFrom(implementationType);
            if (isInitializable == false && isTickable == false
                && isFixedTickable == false && isLateTickable == false
                && isAsyncInitializable == false)
            {
                return;
            }

            object instance = provider.Get(this);
            EnsureNotDisposed();
            if (m_lifecycleInstances != null)
            {
                // Materializing a scoped instance can register it reentrantly.
                if (m_lifecycleInstances.ContainsKey(provider))
                {
                    return;
                }

                if (m_lifecycleReady)
                {
                    foreach (object registered in m_lifecycleInstances.Values)
                    {
                        if (ReferenceEquals(registered, instance))
                        {
                            m_lifecycleInstances.Add(provider, instance);
                            return;
                        }
                    }
                }
            }

            (m_lifecycleInstances ??= new Dictionary<IProvider, object>()).Add(provider, instance);
            if (m_lifecycleReady == false
                && (m_buildLifecycleInstances ??= new HashSet<object>(ReferenceIdentityComparer.Instance))
                    .Add(instance) == false)
            {
                return;
            }

            if (isInitializable)
            {
                (m_initializables ??= new List<IOnityInitializable>()).Add((IOnityInitializable)instance);
            }

            if (isTickable)
            {
                (m_tickables ??= new List<IOnityTickable>()).Add((IOnityTickable)instance);
            }

            if (isFixedTickable)
            {
                (m_fixedTickables ??= new List<IOnityFixedTickable>()).Add((IOnityFixedTickable)instance);
            }

            if (isLateTickable)
            {
                (m_lateTickables ??= new List<IOnityLateTickable>()).Add((IOnityLateTickable)instance);
            }

            if (isAsyncInitializable)
            {
                // Runs in the BuildAsync run in progress, or on the next BuildAsync call.
                (m_asyncInitializables ??= new List<IOnityAsyncInitializable>())
                    .Add((IOnityAsyncInitializable)instance);
            }

            if (initialize && isInitializable)
            {
                ((IOnityInitializable)instance).Initialize();
                EnsureNotDisposed();
            }
        }

        // Compiles this scope's explicit local bindings into a flat, dense-id keyed
        // graph. The baked graph stores the SAME IProvider the dictionary path uses,
        // so a baked resolve returns an instance identical to the reflection path
        // (singleton identity, transient distinctness, member injection, and cycle
        // detection are all owned by the provider, not duplicated here). Only
        // m_providerMap is baked: parent, implicit-concrete, and unbound contracts
        // intentionally stay on the reflection path for identical behavior.
        private BakedGraph BuildBakedGraph()
        {
            BakedGraph.Builder builder = new BakedGraph.Builder(this, m_providerMap.Count);

            foreach (KeyValuePair<Type, IProvider> pair in m_providerMap)
            {
                Type contractType = pair.Key;

                // Self-resolution takes precedence over explicit bindings in
                // TryResolveInternal. Keep the generic baked route consistent.
                if (contractType == typeof(OnityContainer) || contractType == typeof(IResolver))
                {
                    continue;
                }

                IProvider provider = pair.Value;
                int contractTypeId = TypeIdRegistry.Register(contractType);

                builder.Add(
                    contractTypeId,
                    provider.BakedLifetime,
                    provider);
            }

            return builder.Build();
        }

        /// <summary>
        /// Executes asynchronous post-build callbacks once, then runs each collected
        /// <see cref="IOnityAsyncInitializable" /> once, and caches the resulting task.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for callback execution.</param>
        /// <returns>Completion task for all async callbacks and async initializers.</returns>
        /// <remarks>
        /// Order: <see cref="Build" /> (sync callbacks and <see cref="IOnityInitializable.Initialize" />),
        /// then the async build callbacks, then the async initializers one at a time in
        /// binding-registration order. Each step starts on the context the build started on; no
        /// step continues on a thread-pool thread just because the previous step completed there.
        /// Callbacks and initializers receive a token that is canceled when
        /// <paramref name="cancellationToken" /> is canceled or when this container is disposed,
        /// whichever comes first; with no caller token (or with <see cref="LifetimeToken" /> itself)
        /// they receive <see cref="LifetimeToken" />. Disposing the container while the run is in
        /// progress ends the returned task as canceled. A canceled run reports
        /// <paramref name="cancellationToken" /> when the caller canceled, otherwise
        /// <see cref="LifetimeToken" />. A canceled or faulted run is not cached, so
        /// the next call runs again: it re-runs the async build callbacks unless all of them already
        /// completed, and resumes at the first async initializer that has not completed. An async
        /// initializer bound after a completed run is run by the next call.
        /// </remarks>
        public Task BuildAsync(CancellationToken cancellationToken = default)
        {
            EnsureNotDisposed();
            Build();

            Task cachedTask = m_cachedBuildTask;

            if (cachedTask != null
                && cachedTask.IsCanceled == false
                && cachedTask.IsFaulted == false
                && (cachedTask.IsCompleted == false || HasPendingAsyncBuildWork() == false))
            {
                return cachedTask;
            }

            m_cachedBuildTask = HasPendingAsyncBuildWork()
                ? RunAsyncBuildAsync(cancellationToken)
                : Task.CompletedTask;
            return m_cachedBuildTask;
        }

        /// <summary>
        /// Pushes a source label used for subsequent binding registrations in the current thread.
        /// </summary>
        /// <param name="sourceName">Source label shown by diagnostics and inspector tooling.</param>
        /// <returns>Disposable scope token.</returns>
        public IDisposable PushBindingSource(string sourceName)
        {
            EnsureNotDisposed();

            if (string.IsNullOrWhiteSpace(sourceName))
            {
                return BindingSourceScope.Empty;
            }

            Stack<string> sourceStack = s_bindingSourceStack;

            if (sourceStack == null)
            {
                sourceStack = new Stack<string>(8);
                s_bindingSourceStack = sourceStack;
            }

            sourceStack.Push(sourceName);
            return new BindingSourceScope(true);
        }

        /// <summary>
        /// Returns whether the container can resolve a type without instantiating it.
        /// </summary>
        /// <param name="serviceType">Service type to evaluate.</param>
        /// <returns>True when type is resolvable in current or parent scope.</returns>
        public bool CanResolve(Type serviceType)
        {
            EnsureNotDisposed();

            if (serviceType == null)
            {
                return false;
            }

            if (serviceType == typeof(OnityContainer) || serviceType == typeof(IResolver))
            {
                return true;
            }

            if (m_providerMap.ContainsKey(serviceType))
            {
                return true;
            }

            if (m_parent != null && m_parent.CanResolve(serviceType))
            {
                return true;
            }

            // A collection type is resolvable when its element type has at least one
            // explicit binding here or in an ancestor, matching TryResolveCollection.
            Type collectionElementType = GetCollectionElementType(serviceType);

            if (collectionElementType != null && HasCollectionElement(collectionElementType))
            {
                return true;
            }

            if (serviceType.IsGenericType
                && serviceType.IsGenericTypeDefinition == false
                && HasOpenGenericRegistration(serviceType.GetGenericTypeDefinition()))
            {
                return true;
            }

            if (serviceType.IsInterface || serviceType.IsAbstract || serviceType.IsGenericTypeDefinition)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Tries to get binding source metadata for a contract from current or parent scope.
        /// </summary>
        /// <param name="contractType">Contract type.</param>
        /// <param name="sourceInfo">Binding source metadata when found.</param>
        /// <returns>True when source metadata exists for the contract.</returns>
        public bool TryGetBindingSource(Type contractType, out OnityBindingSourceInfo sourceInfo)
        {
            EnsureNotDisposed();

            if (contractType == null)
            {
                sourceInfo = default;
                return false;
            }

            return TryGetBindingSourceRecursive(contractType, 0, out sourceInfo);
        }

        /// <summary>
        /// Tries to get binding source metadata only from this scope.
        /// </summary>
        /// <param name="contractType">Contract type.</param>
        /// <param name="sourceInfo">Binding source metadata when found.</param>
        /// <returns>True when source metadata exists in current scope.</returns>
        public bool TryGetLocalBindingSource(Type contractType, out OnityBindingSourceInfo sourceInfo)
        {
            EnsureNotDisposed();

            if (contractType == null)
            {
                sourceInfo = default;
                return false;
            }

            if (m_bindingSourceMap.TryGetValue(contractType, out BindingSourceRecord record) == false)
            {
                sourceInfo = default;
                return false;
            }

            sourceInfo = new OnityBindingSourceInfo(
                contractType,
                record.ImplementationType,
                record.LifetimeName,
                record.SourceName,
                record.IsImplicitRegistration,
                0);

            return true;
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TService Resolve<TService>()
        {
            EnsureNotDisposed();

            int serviceTypeId = TypeIdCache<TService>.Id;

            if (serviceTypeId == s_containerTypeId || serviceTypeId == s_resolverTypeId)
            {
                return (TService)(object)this;
            }

            // Baked fast path: dense-id array lookup with no dictionary hash. Only
            // hits explicit local bindings; misses fall through to the identical
            // reflection path so parent/implicit/unbound behavior is unchanged.
            BakedGraph baked = m_baked;

            if (baked != null && baked.TryResolve(serviceTypeId, out object bakedInstance))
            {
                return (TService)bakedInstance;
            }

            IProvider[] providers = m_providerByTypeId;
            if (providers != null && (uint)serviceTypeId < (uint)providers.Length)
            {
                IProvider provider = providers[serviceTypeId];
                if (provider != null)
                {
                    return (TService)provider.Get(this);
                }
            }

            return ResolveGenericSlow<TService>();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private TService ResolveGenericSlow<TService>()
        {
            // An empty child scope has no local binding to override an inherited
            // baked slot. Resolve it in the parent graph with this scope as owner,
            // without repeating the collection/open-generic and type-id searches.
            if (m_parent != null && m_providerMap.Count == 0 && m_openGenericMap == null
                && m_parent.m_baked != null
                && m_parent.m_baked.TryResolveForScope(
                    TypeIdCache<TService>.Id, this, out object inheritedInstance))
            {
                return (TService)inheritedInstance;
            }

            if (TryResolveInternal(typeof(TService), out object service))
            {
                return (TService)service;
            }

            throw new OnityResolveException(BuildUnresolvableMessage(typeof(TService)));
        }

        /// <inheritdoc />
        public object Resolve(Type serviceType)
        {
            EnsureNotDisposed();

            if (serviceType == null)
            {
                throw new OnityResolveException("Cannot resolve a null service type.");
            }

            if (TryResolveInternal(serviceType, out object instance))
            {
                return instance;
            }

            throw new OnityResolveException(BuildUnresolvableMessage(serviceType));
        }

        /// <inheritdoc />
        public bool TryResolve<TService>(out TService instance)
        {
            EnsureNotDisposed();

            BakedGraph baked = m_baked;

            if (baked != null && baked.TryResolve(TypeIdCache<TService>.Id, out object bakedInstance))
            {
                if (bakedInstance is TService bakedTypedInstance)
                {
                    instance = bakedTypedInstance;
                    return true;
                }

                instance = default;
                return false;
            }

            if (TryResolveInternal(typeof(TService), out object rawInstance) && rawInstance is TService typedInstance)
            {
                instance = typedInstance;
                return true;
            }

            instance = default;
            return false;
        }

        /// <inheritdoc />
        public bool TryResolve(Type serviceType, out object instance)
        {
            EnsureNotDisposed();

            if (serviceType == null)
            {
                instance = null;
                return false;
            }

            return TryResolveInternal(serviceType, out instance);
        }

        /// <inheritdoc />
        public TService Resolve<TService>(object id)
        {
            if (id == null)
            {
                return Resolve<TService>();
            }

            EnsureNotDisposed();

            // The common keyed case has one unconditional closed binding. Keep
            // conditional and open-generic keys on the full selection path.
            Dictionary<object, ContextBinding>[] directBindings = m_directKeyedBindings;
            int typeId = TypeIdCache<TService>.Id;
            if (directBindings != null && (uint)typeId < (uint)directBindings.Length
                && directBindings[typeId] != null
                && directBindings[typeId].TryGetValue(id, out ContextBinding binding))
            {
                if (binding.ReusesInstance && s_diagnosticsCollectionEnabled == false)
                {
                    if (binding.HasCachedInstance)
                    {
                        return (TService)binding.CachedInstance;
                    }

                    object instance = binding.Provider.Get(this);
                    binding.CachedInstance = instance;
                    binding.HasCachedInstance = true;
                    return (TService)instance;
                }

                return (TService)binding.Provider.Get(this);
            }

            return (TService)Resolve(typeof(TService), id);
        }

        /// <inheritdoc />
        public object Resolve(Type serviceType, object id)
        {
            if (id == null)
            {
                return Resolve(serviceType);
            }

            EnsureNotDisposed();

            if (serviceType == null)
            {
                throw new OnityResolveException("Cannot resolve a null service type.");
            }

            return ResolveDependency(serviceType, id, null);
        }

        /// <inheritdoc />
        public bool TryResolve<TService>(object id, out TService instance)
        {
            if (id == null)
            {
                return TryResolve(out instance);
            }

            if (TryResolve(typeof(TService), id, out object value) && value is TService service)
            {
                instance = service;
                return true;
            }

            instance = default;
            return false;
        }

        /// <inheritdoc />
        public bool TryResolve(Type serviceType, object id, out object instance)
        {
            if (id == null)
            {
                return TryResolve(serviceType, out instance);
            }

            EnsureNotDisposed();

            if (serviceType == null)
            {
                instance = null;
                return false;
            }

            return TryResolveContextBinding(serviceType, id, null, out instance);
        }

        /// <inheritdoc />
        public void Inject(object target)
        {
            EnsureNotDisposed();

            if (target == null)
            {
                throw new OnityResolveException("Cannot inject into a null target.");
            }

            TypeInjectionPlan plan = GetOrCreatePlan(target.GetType());
            InjectMembers(target, plan);
        }

        /// <summary>
        /// Returns a lightweight diagnostics snapshot for editor tooling.
        /// </summary>
        /// <returns>Current container diagnostics.</returns>
        public OnityContainerDiagnostics GetDiagnostics()
        {
            EnsureNotDisposed();

            return new OnityContainerDiagnostics(
                m_providerMap.Count + (m_contextBindings?.Count ?? 0),
                m_implicitProviderMap.Count,
                m_planMap.Count,
                m_ownedProviders.Count,
                m_parent != null);
        }

        /// <summary>
        /// Fills binding-level diagnostics for editor monitoring.
        /// </summary>
        /// <param name="results">Destination list that receives current diagnostics rows.</param>
        public void GetBindingDiagnostics(List<OnityBindingDiagnostics> results)
        {
            EnsureNotDisposed();

            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            results.Clear();

            Dictionary<IProvider, BindingDiagnosticsAggregation> aggregations =
                new Dictionary<IProvider, BindingDiagnosticsAggregation>(m_providerMap.Count + m_implicitProviderMap.Count);

            foreach (KeyValuePair<Type, IProvider> pair in m_providerMap)
            {
                if (aggregations.TryGetValue(pair.Value, out BindingDiagnosticsAggregation aggregation) == false)
                {
                    aggregation = new BindingDiagnosticsAggregation(pair.Value.GetDiagnosticsSnapshot(), false);
                    aggregations.Add(pair.Value, aggregation);
                }

                aggregation.AddContract(pair.Key);
            }

            foreach (KeyValuePair<Type, IProvider> pair in m_implicitProviderMap)
            {
                if (aggregations.TryGetValue(pair.Value, out BindingDiagnosticsAggregation aggregation) == false)
                {
                    aggregation = new BindingDiagnosticsAggregation(pair.Value.GetDiagnosticsSnapshot(), true);
                    aggregations.Add(pair.Value, aggregation);
                }
                else
                {
                    aggregation.MarkImplicit();
                }

                aggregation.AddContract(pair.Key);
            }

            if (m_contextBindings != null)
            {
                foreach (KeyValuePair<BindingKey, List<ContextBinding>> pair in m_contextBindings)
                {
                    for (int i = 0; i < pair.Value.Count; i++)
                    {
                        ContextBinding binding = pair.Value[i];

                        if (binding.Provider != null)
                        {
                            AddContextDiagnostics(aggregations, pair.Key.ContractType, binding.Provider);
                        }

                        if (binding.ClosedProviders == null)
                        {
                            continue;
                        }

                        foreach (KeyValuePair<Type, IProvider> closed in binding.ClosedProviders)
                        {
                            AddContextDiagnostics(aggregations, closed.Key, closed.Value);
                        }
                    }
                }
            }

            foreach (BindingDiagnosticsAggregation aggregation in aggregations.Values)
            {
                aggregation.SortContracts();
                ProviderDiagnosticsSnapshot provider = aggregation.Provider;
                long resolveCount = provider.ResolveCount;
                double averageMilliseconds = resolveCount > 0
                    ? ConvertTicksToMilliseconds(provider.TotalResolveTicks) / resolveCount
                    : 0d;

                results.Add(
                    new OnityBindingDiagnostics(
                        provider.ImplementationType,
                        aggregation.ContractTypes.ToArray(),
                        provider.LifetimeName,
                        aggregation.IsImplicit,
                        resolveCount,
                        averageMilliseconds,
                        ConvertTicksToMilliseconds(provider.LastResolveTicks)));
            }
        }

        private static void AddContextDiagnostics(
            Dictionary<IProvider, BindingDiagnosticsAggregation> aggregations, Type contractType, IProvider provider)
        {
            if (aggregations.TryGetValue(provider, out BindingDiagnosticsAggregation aggregation) == false)
            {
                aggregation = new BindingDiagnosticsAggregation(provider.GetDiagnosticsSnapshot(), false);
                aggregations.Add(provider, aggregation);
            }

            aggregation.AddContract(contractType);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (m_isDisposed)
            {
                return;
            }

            m_isDisposed = true;

            List<Exception> disposalErrors = null;

            // Cancel the scope lifetime first so bound async work and AddTo registrations stop
            // before child scopes and owned instances are disposed.
            CancelLifetime(ref disposalErrors);

            if (m_subContainerScopes != null)
            {
                foreach (OnityContainer child in m_subContainerScopes.Values)
                {
                    try
                    {
                        child.Dispose();
                    }
                    catch (Exception exception)
                    {
                        disposalErrors ??= new List<Exception>(1);
                        disposalErrors.Add(exception);
                    }
                }

                m_subContainerScopes.Clear();
            }

            if (m_scopedInstances != null)
            {
                foreach (object instance in m_scopedInstances.Values)
                {
                    if (instance is IDisposable disposable)
                    {
                        try
                        {
                            disposable.Dispose();
                        }
                        catch (Exception exception)
                        {
                            disposalErrors ??= new List<Exception>(1);
                            disposalErrors.Add(exception);
                        }
                    }
                }

                m_scopedInstances.Clear();
            }

            for (int i = m_ownedProviders.Count - 1; i >= 0; i--)
            {
                try
                {
                    m_ownedProviders[i].Dispose();
                }
                catch (Exception exception)
                {
                    disposalErrors ??= new List<Exception>(1);
                    disposalErrors.Add(exception);
                }
            }

            DisposeBuildTokenSources();
            m_ownedProviders.Clear();
            m_providerMap.Clear();
            m_implicitProviderMap.Clear();
            m_bindingSourceMap.Clear();
            m_planMap.Clear();
            m_buildCallbacks.Clear();
            m_asyncBuildCallbacks.Clear();
            m_cachedBuildTask = null;
            m_baked = null;
            m_providerByTypeId = null;
            // Lifecycle instances are owned by their providers (disposed above);
            // just drop the references so a disposed container ticks nothing.
            m_initializables = null;
            m_tickables = null;
            m_fixedTickables = null;
            m_lateTickables = null;
            m_asyncInitializables = null;
            m_multiProviderMap = null;
            m_openGenericMap = null;
            m_closedOpenGenericProviders = null;
            m_contextBindings = null;
            m_directKeyedBindings = null;
            m_retiredProviders = null;
            m_lifecycleInstances = null;
            m_scopedInstances = null;
            m_subContainerScopes = null;
            m_subContainerScopeList = null;

            if (disposalErrors == null)
            {
                return;
            }

            if (disposalErrors.Count == 1)
            {
                ExceptionDispatchInfo.Capture(disposalErrors[0]).Throw();
            }

            throw new AggregateException(disposalErrors);
        }

        internal IBakedProvider RegisterConfigured(
            Type contractType, Type implementationType, BindingLifetime lifetime, object id, Type consumerType, bool replace)
        {
            EnsureNotDisposed();
            bool isOpen = contractType.IsGenericTypeDefinition || implementationType.IsGenericTypeDefinition;

            if (isOpen)
            {
                ValidateOpenGenericBinding(contractType, implementationType);
            }
            else
            {
                ValidateBinding(contractType, implementationType);
            }

            if (replace)
            {
                Unbind(contractType, id);
            }

            if (id == null && consumerType == null)
            {
                RegisterRuntime(contractType, implementationType, lifetime);
                return isOpen ? null : m_providerMap[contractType];
            }

            if (isOpen)
            {
                AddContextBinding(contractType, id, new ContextBinding(implementationType, lifetime, consumerType));
                return null;
            }

            IProvider provider = CreateProvider(implementationType, lifetime);
            m_ownedProviders.Add(provider);
            AddContextBinding(contractType, id, new ContextBinding(provider, consumerType));
            RegisterLifecycleAfterBuild(provider);
            return provider;
        }

        internal IBakedProvider RegisterConfigured(
            Type[] contractTypes, Type implementationType, BindingLifetime lifetime, object id, Type consumerType)
        {
            EnsureNotDisposed();

            if (contractTypes == null || contractTypes.Length == 0)
            {
                throw new OnityBindingException("Contract type list cannot be empty.");
            }

            if (id == null && consumerType == null)
            {
                Register(contractTypes, implementationType, lifetime);
                return m_providerMap[contractTypes[0]];
            }

            for (int i = 0; i < contractTypes.Length; i++)
            {
                ValidateBinding(contractTypes[i], implementationType);
            }

            IProvider provider = CreateProvider(implementationType, lifetime);
            m_ownedProviders.Add(provider);
            ContextBinding binding = new ContextBinding(provider, consumerType);

            for (int i = 0; i < contractTypes.Length; i++)
            {
                AddContextBinding(contractTypes[i], id, binding);
            }

            RegisterLifecycleAfterBuild(provider);
            return provider;
        }

        internal IBakedProvider RegisterSubContainer(
            Type contractType, Action<OnityContainer> install, object id, Type consumerType, bool replace)
        {
            EnsureNotDisposed();

            if (contractType == null || contractType.IsGenericTypeDefinition)
            {
                throw new OnityBindingException("Sub-container exports require a closed contract type.");
            }

            if (install == null)
            {
                throw new OnityBindingException("Sub-container installer cannot be null.");
            }

            if (replace)
            {
                Unbind(contractType, id);
            }

            IProvider provider = new SubContainerProvider(contractType, install);
            if (id == null && consumerType == null)
            {
                RegisterProvider(contractType, provider, false);
            }
            else
            {
                m_ownedProviders.Add(provider);
                AddContextBinding(contractType, id, new ContextBinding(provider, consumerType));
            }

            return provider;
        }

        internal void RegisterNonLazy(IBakedProvider provider)
        {
            RegisterBuildCallback(_ =>
            {
                if (m_retiredProviders == null || m_retiredProviders.Contains((IProvider)provider) == false)
                {
                    provider.Get(this);
                }
            });
        }

        private void AddContextBinding(Type contractType, object id, ContextBinding binding)
        {
            m_contextBindings ??= new Dictionary<BindingKey, List<ContextBinding>>();
            BindingKey key = new BindingKey(contractType, id);

            if (m_contextBindings.TryGetValue(key, out List<ContextBinding> bindings) == false)
            {
                bindings = new List<ContextBinding>(2);
                m_contextBindings.Add(key, bindings);
            }

            bindings.Add(binding);
            SetDirectKeyedBinding(contractType, id,
                bindings.Count == 1 && binding.ConsumerType == null && binding.Provider != null
                    ? binding : null);
            m_bindingVersion++;
        }

        private void SetDirectKeyedBinding(Type contractType, object id, ContextBinding binding)
        {
            if (id == null)
            {
                return;
            }

            if (binding == null)
            {
                if (m_directKeyedBindings != null
                    && TypeIdRegistry.TryGetId(contractType, out int existingTypeId)
                    && existingTypeId < m_directKeyedBindings.Length)
                {
                    m_directKeyedBindings[existingTypeId]?.Remove(id);
                }

                return;
            }

            int typeId = TypeIdRegistry.Register(contractType);
            if (m_directKeyedBindings == null)
            {
                m_directKeyedBindings = new Dictionary<object, ContextBinding>[typeId + 1];
            }
            else if (typeId >= m_directKeyedBindings.Length)
            {
                Array.Resize(ref m_directKeyedBindings,
                    Math.Max(typeId + 1, m_directKeyedBindings.Length * 2));
            }

            Dictionary<object, ContextBinding> byId = m_directKeyedBindings[typeId];
            if (byId == null)
            {
                byId = new Dictionary<object, ContextBinding>();
                m_directKeyedBindings[typeId] = byId;
            }

            byId[id] = binding;
        }

        private object ResolveDependency(Type serviceType, object id, Type consumerType)
        {
            if (id == null && m_contextBindings == null && m_parent == null)
            {
                return Resolve(serviceType);
            }

            if (TryResolveContextBinding(serviceType, id, consumerType, out object instance))
            {
                return instance;
            }

            if (id == null)
            {
                return Resolve(serviceType);
            }

            throw new OnityResolveException(
                $"No binding for '{serviceType.FullName}' with identifier '{id}' matches consumer '{consumerType?.FullName}'.");
        }

        private bool TryResolveContextBinding(Type serviceType, object id, Type consumerType, out object instance)
        {
            return TryResolveContextBinding(serviceType, id, consumerType, this, out instance);
        }

        private bool TryResolveContextBinding(
            Type serviceType, object id, Type consumerType, OnityContainer requester, out object instance)
        {
            EnsureNotDisposed();

            // Unkeyed self-resolution always refers to the container doing the injection.
            if (id == null && (serviceType == typeof(OnityContainer) || serviceType == typeof(IResolver)))
            {
                instance = null;
                return false;
            }

            if (TryGetContextBinding(serviceType, id, consumerType, out ContextBinding binding))
            {
                IProvider provider = GetContextProvider(binding, serviceType);
                instance = provider.Get(provider is IScopeOwnedProvider ? requester : this);
                return true;
            }

            // A local explicit default shadows conditions in ancestor scopes. A
            // closed provider generated from an open registration does not shadow
            // a local conditional open registration after the default is warmed.
            if (id == null && m_providerMap.TryGetValue(serviceType, out IProvider localProvider)
                && (m_closedOpenGenericProviders == null
                    || m_closedOpenGenericProviders.TryGetValue(serviceType, out IProvider generatedProvider) == false
                    || ReferenceEquals(localProvider, generatedProvider) == false))
            {
                instance = null;
                return false;
            }

            if (TryResolveContextCollection(serviceType, id, consumerType, requester, out instance))
            {
                return true;
            }

            if (serviceType.IsConstructedGenericType)
            {
                Type definition = serviceType.GetGenericTypeDefinition();

                if (TryGetContextBinding(definition, id, consumerType, out binding))
                {
                    IProvider provider = GetContextProvider(binding, serviceType);
                    instance = provider.Get(provider is IScopeOwnedProvider ? requester : this);
                    return true;
                }

                if (id == null && m_openGenericMap != null && m_openGenericMap.ContainsKey(definition))
                {
                    instance = null;
                    return false;
                }
            }

            if (m_parent != null)
            {
                return m_parent.TryResolveContextBinding(serviceType, id, consumerType, requester, out instance);
            }

            instance = null;
            return false;
        }

        private bool TryGetContextBinding(Type serviceType, object id, Type consumerType, out ContextBinding binding)
        {
            binding = null;

            if (m_contextBindings == null
                || m_contextBindings.TryGetValue(new BindingKey(serviceType, id), out List<ContextBinding> bindings) == false)
            {
                return false;
            }

            ContextBinding fallback = null;

            for (int i = 0; i < bindings.Count; i++)
            {
                ContextBinding candidate = bindings[i];

                if (candidate.ConsumerType == null)
                {
                    fallback = candidate;
                    continue;
                }

                if (consumerType == null || candidate.ConsumerType.IsAssignableFrom(consumerType) == false)
                {
                    continue;
                }

                if (binding != null)
                {
                    throw new OnityResolveException(
                        $"Multiple conditional bindings for '{serviceType.FullName}' with identifier '{id}' match '{consumerType.FullName}'.");
                }

                binding = candidate;
            }

            binding ??= fallback;
            return binding != null;
        }

        private IProvider GetContextProvider(ContextBinding binding, Type serviceType)
        {
            if (binding.Provider != null)
            {
                return binding.Provider;
            }

            if (serviceType.IsConstructedGenericType == false)
            {
                throw new OnityResolveException("Open generic definitions cannot be resolved directly.");
            }

            if (binding.ClosedProviders != null && binding.ClosedProviders.TryGetValue(serviceType, out IProvider cached))
            {
                return cached;
            }

            Type implementationType = binding.ImplementationDefinition.MakeGenericType(serviceType.GetGenericArguments());
            ValidateBinding(serviceType, implementationType);
            IProvider provider = CreateProvider(implementationType, binding.Lifetime);
            (binding.ClosedProviders ??= new Dictionary<Type, IProvider>()).Add(serviceType, provider);
            m_ownedProviders.Add(provider);
            RegisterLifecycleAfterBuild(provider);
            return provider;
        }

        private bool TryResolveContextCollection(
            Type serviceType, object id, Type consumerType, OnityContainer requester, out object instance)
        {
            Type elementType = GetCollectionElementType(serviceType);

            if (elementType == null)
            {
                instance = null;
                return false;
            }

            List<object> items = new List<object>(4);
            CollectContextItems(elementType, id, consumerType, requester, items);

            if (items.Count == 0)
            {
                instance = null;
                return false;
            }

            instance = MaterializeCollection(serviceType, elementType, items);
            return true;
        }

        private void CollectContextItems(
            Type elementType, object id, Type consumerType, OnityContainer requester, List<object> items)
        {
            EnsureNotDisposed();
            m_parent?.CollectContextItems(elementType, id, consumerType, requester, items);

            if (id == null && elementType.IsConstructedGenericType
                && m_openGenericMap != null
                && m_openGenericMap.TryGetValue(elementType.GetGenericTypeDefinition(), out OpenGenericRegistration registration))
            {
                GetClosedOpenGenericProvider(elementType, registration);
            }

            if (id == null && m_multiProviderMap != null
                && m_multiProviderMap.TryGetValue(elementType, out List<IProvider> providers))
            {
                for (int i = 0; i < providers.Count; i++)
                {
                    IProvider provider = providers[i];
                    items.Add(provider.Get(provider is IScopeOwnedProvider ? requester : this));
                }
            }

            AddContextItems(elementType, elementType, id, consumerType, requester, items);

            if (elementType.IsConstructedGenericType)
            {
                AddContextItems(elementType.GetGenericTypeDefinition(), elementType, id, consumerType, requester, items);
            }
        }

        private void AddContextItems(
            Type contractType, Type elementType, object id, Type consumerType,
            OnityContainer requester, List<object> items)
        {
            if (m_contextBindings == null
                || m_contextBindings.TryGetValue(new BindingKey(contractType, id), out List<ContextBinding> bindings) == false)
            {
                return;
            }

            for (int i = 0; i < bindings.Count; i++)
            {
                ContextBinding binding = bindings[i];

                if (binding.ConsumerType == null
                    || (consumerType != null && binding.ConsumerType.IsAssignableFrom(consumerType)))
                {
                    IProvider provider = GetContextProvider(binding, elementType);
                    items.Add(provider.Get(provider is IScopeOwnedProvider ? requester : this));
                }
            }
        }

        internal void Register(Type contractType, Type implementationType, BindingLifetime lifetime)
        {
            EnsureNotDisposed();
            ValidateBinding(contractType, implementationType);
            RegisterProvider(contractType, CreateProvider(implementationType, lifetime), false);
        }

        internal void Register(Type[] contractTypes, Type implementationType, BindingLifetime lifetime)
        {
            EnsureNotDisposed();

            if (contractTypes == null || contractTypes.Length == 0)
            {
                throw new OnityBindingException("Contract type list cannot be empty.");
            }

            for (int i = 0; i < contractTypes.Length; i++)
            {
                ValidateBinding(contractTypes[i], implementationType);
            }

            IProvider provider = CreateProvider(implementationType, lifetime);
            m_ownedProviders.Add(provider);
            m_bindingVersion++;

            for (int i = 0; i < contractTypes.Length; i++)
            {
                SetLocalProvider(contractTypes[i], provider);
                AddToMultiProviderMap(contractTypes[i], provider);
                RegisterBindingSource(contractTypes[i], provider, false);
            }

            if (m_baked != null)
            {
                m_baked = BuildBakedGraph();
            }

            RegisterLifecycleAfterBuild(provider);
        }

        internal void RegisterRuntime(Type contractType, Type implementationType, BindingLifetime lifetime)
        {
            EnsureNotDisposed();

            if (contractType == null)
            {
                throw new OnityBindingException("Contract type cannot be null.");
            }

            if (implementationType == null)
            {
                throw new OnityBindingException("Implementation type cannot be null.");
            }

            if (contractType.IsGenericTypeDefinition || implementationType.IsGenericTypeDefinition)
            {
                RegisterOpenGeneric(contractType, implementationType, lifetime);
                return;
            }

            Register(contractType, implementationType, lifetime);
        }

        private static void ValidateOpenGenericBinding(Type openContractType, Type openImplementationType)
        {
            if (openContractType.IsGenericTypeDefinition == false)
            {
                throw new OnityBindingException(
                    $"Open generic binding requires an open generic contract such as typeof(IRepo<>), got '{openContractType}'.");
            }

            if (openImplementationType.IsGenericTypeDefinition == false)
            {
                throw new OnityBindingException(
                    $"Open generic binding requires an open generic implementation such as typeof(Repo<>), got '{openImplementationType}'.");
            }

            if (openImplementationType.IsInterface || openImplementationType.IsAbstract)
            {
                throw new OnityBindingException(
                    $"Open generic implementation '{openImplementationType}' must be a concrete type. Use .To(typeof(YourImpl<>)).");
            }

            if (openContractType.GetGenericArguments().Length != openImplementationType.GetGenericArguments().Length)
            {
                throw new OnityBindingException(
                    $"Open generic implementation '{openImplementationType}' has a different type-parameter count than contract '{openContractType}'.");
            }

            if (OpenGenericImplementsContract(openImplementationType, openContractType) == false)
            {
                throw new OnityBindingException(
                    $"Open generic implementation '{openImplementationType}' does not implement or derive from contract '{openContractType}'.");
            }

        }

        private void RegisterOpenGeneric(Type openContractType, Type openImplementationType, BindingLifetime lifetime)
        {
            ValidateOpenGenericBinding(openContractType, openImplementationType);
            List<Type> closedContracts = null;
            List<IProvider> replacementProviders = null;

            if (m_closedOpenGenericProviders != null)
            {
                foreach (KeyValuePair<Type, IProvider> pair in m_closedOpenGenericProviders)
                {
                    Type closedContractType = pair.Key;

                    if (closedContractType.GetGenericTypeDefinition() != openContractType)
                    {
                        continue;
                    }

                    // Stage every replacement before changing the registration. A
                    // new implementation may reject a previously closed type's
                    // generic arguments, in which case the old binding stays valid.
                    Type closedImplementationType = openImplementationType.MakeGenericType(
                        closedContractType.GetGenericArguments());
                    (closedContracts ??= new List<Type>(4)).Add(closedContractType);
                    (replacementProviders ??= new List<IProvider>(4)).Add(
                        CreateProvider(closedImplementationType, lifetime));
                }
            }

            m_openGenericMap ??= new Dictionary<Type, OpenGenericRegistration>(8);
            m_openGenericMap[openContractType] = new OpenGenericRegistration(openImplementationType, lifetime);
            m_bindingVersion++;

            if (closedContracts == null)
            {
                return;
            }

            for (int i = 0; i < closedContracts.Count; i++)
            {
                Type closedContractType = closedContracts[i];
                IProvider oldProvider = m_closedOpenGenericProviders[closedContractType];
                IProvider newProvider = replacementProviders[i];
                m_closedOpenGenericProviders[closedContractType] = newProvider;
                m_ownedProviders.Add(newProvider);

                if (m_providerMap.TryGetValue(closedContractType, out IProvider currentProvider)
                    && ReferenceEquals(currentProvider, oldProvider))
                {
                    SetLocalProvider(closedContractType, newProvider);
                    RegisterBindingSource(closedContractType, newProvider, false);
                }

                if (m_multiProviderMap != null
                    && m_multiProviderMap.TryGetValue(closedContractType, out List<IProvider> providers))
                {
                    for (int providerIndex = 0; providerIndex < providers.Count; providerIndex++)
                    {
                        if (ReferenceEquals(providers[providerIndex], oldProvider))
                        {
                            providers[providerIndex] = newProvider;
                            break;
                        }
                    }
                }
            }

            if (m_baked != null)
            {
                m_baked = BuildBakedGraph();
            }

            RetireUnboundProviders();
            for (int i = 0; i < replacementProviders.Count; i++)
            {
                RegisterLifecycleAfterBuild(replacementProviders[i]);
            }
        }

        // Confirms an open implementation definition satisfies an open contract
        // definition (Repo<> implements IRepo<>, or derives from a Base<>), so binding
        // errors surface at registration instead of at MakeGenericType on resolve.
        private static bool OpenGenericImplementsContract(Type openImplementation, Type openContract)
        {
            if (openImplementation == openContract)
            {
                return true;
            }

            if (openContract.IsInterface)
            {
                Type[] interfaces = openImplementation.GetInterfaces();

                for (int i = 0; i < interfaces.Length; i++)
                {
                    if (interfaces[i].IsGenericType && interfaces[i].GetGenericTypeDefinition() == openContract)
                    {
                        return true;
                    }
                }

                return false;
            }

            Type current = openImplementation.BaseType;

            while (current != null && current != typeof(object))
            {
                if (current.IsGenericType && current.GetGenericTypeDefinition() == openContract)
                {
                    return true;
                }

                current = current.BaseType;
            }

            return false;
        }

        // One BuildAsync run: the async build callbacks (unless an earlier run completed them all),
        // then the async initializers that have not completed. Awaits keep the caller's context
        // (no ConfigureAwait(false)), so a step that completes on a worker thread does not make the
        // next callback or initializer start there; in Play Mode every step starts on the main thread.
        // Token checks come before the bounds checks, so a run that outlives Dispose (which clears
        // the lists) ends canceled instead of completing. Steps receive the linked build token; when
        // a step ends with that token's cancellation, the run reports the token that caused it (the
        // caller's, otherwise the scope's), never the internal linked token.
        private async Task RunAsyncBuildAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CancellationToken lifetimeToken = LifetimeToken;
            CancellationToken buildToken = CreateBuildToken(cancellationToken, lifetimeToken);

            if (m_asyncBuildCallbacksCompleted == false)
            {
                for (int i = 0; ; i++)
                {
                    ThrowIfBuildCanceled(cancellationToken, lifetimeToken);

                    if (i >= m_asyncBuildCallbacks.Count)
                    {
                        break;
                    }

                    Task task = m_asyncBuildCallbacks[i](this, buildToken);

                    if (task == null)
                    {
                        continue;
                    }

                    try
                    {
                        await task;
                    }
                    catch (OperationCanceledException) when (IsBuildCanceled(cancellationToken, lifetimeToken))
                    {
                        throw CreateBuildCanceledException(cancellationToken, lifetimeToken);
                    }
                }

                m_asyncBuildCallbacksCompleted = true;
            }

            while (true)
            {
                ThrowIfBuildCanceled(cancellationToken, lifetimeToken);
                List<IOnityAsyncInitializable> initializables = m_asyncInitializables;

                if (initializables == null || m_asyncInitializedCount >= initializables.Count)
                {
                    break;
                }

                IOnityAsyncInitializable initializable = initializables[m_asyncInitializedCount];

                try
                {
                    await initializable.InitializeAsync(buildToken);
                }
                catch (OperationCanceledException) when (IsBuildCanceled(cancellationToken, lifetimeToken))
                {
                    throw CreateBuildCanceledException(cancellationToken, lifetimeToken);
                }

                MarkAsyncInitialized(initializable);
            }
        }

        private bool HasPendingAsyncBuildWork()
        {
            return (m_asyncBuildCallbacksCompleted == false && m_asyncBuildCallbacks.Count > 0)
                || (m_asyncInitializables != null && m_asyncInitializedCount < m_asyncInitializables.Count);
        }

        // Advances past the initializer that just completed. Unbinding while it ran may have shifted
        // the list (see RemoveAsyncInitializable), so the cursor only moves when it still points at it.
        private void MarkAsyncInitialized(IOnityAsyncInitializable initializable)
        {
            List<IOnityAsyncInitializable> initializables = m_asyncInitializables;
            int index = m_asyncInitializedCount;

            if (initializables != null
                && index < initializables.Count
                && ReferenceEquals(initializables[index], initializable))
            {
                m_asyncInitializedCount = index + 1;
            }
        }

        private void RemoveAsyncInitializable(object instance)
        {
            List<IOnityAsyncInitializable> initializables = m_asyncInitializables;

            if (initializables == null)
            {
                return;
            }

            for (int i = initializables.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(initializables[i], instance) == false)
                {
                    continue;
                }

                initializables.RemoveAt(i);

                if (i < m_asyncInitializedCount)
                {
                    m_asyncInitializedCount--;
                }
            }
        }

        // The token handed to async build work: the scope lifetime token, linked with the caller's
        // token when the caller passes its own cancelable token. Linked sources live until the scope
        // is disposed, so work that keeps the token after the build still observes the scope end.
        private CancellationToken CreateBuildToken(CancellationToken cancellationToken, CancellationToken lifetimeToken)
        {
            if (cancellationToken.CanBeCanceled == false || cancellationToken == lifetimeToken)
            {
                return lifetimeToken;
            }

            CancellationTokenSource linkedSource =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetimeToken);
            (m_buildTokenSources ??= new List<CancellationTokenSource>(1)).Add(linkedSource);
            return linkedSource.Token;
        }

        // The linked build token is canceled exactly when one of its two sources is, so the run checks
        // the sources and reports the one that caused it: the caller's token first, then the scope's.
        private static bool IsBuildCanceled(CancellationToken cancellationToken, CancellationToken lifetimeToken)
        {
            return cancellationToken.IsCancellationRequested || lifetimeToken.IsCancellationRequested;
        }

        private static void ThrowIfBuildCanceled(CancellationToken cancellationToken, CancellationToken lifetimeToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lifetimeToken.ThrowIfCancellationRequested();
        }

        private static OperationCanceledException CreateBuildCanceledException(
            CancellationToken cancellationToken,
            CancellationToken lifetimeToken)
        {
            return new OperationCanceledException(
                cancellationToken.IsCancellationRequested ? cancellationToken : lifetimeToken);
        }

        private CancellationToken CreateLifetimeToken()
        {
            if (Volatile.Read(ref m_isDisposed))
            {
                return new CancellationToken(true);
            }

            ScopeLifetimeSource created = new ScopeLifetimeSource(m_parent);
            ScopeLifetimeSource published = Interlocked.CompareExchange(ref m_lifetimeSource, created, null);

            if (published != null)
            {
                // Another thread published first; release the unused source and its parent link.
                created.Close();
                return published.Token;
            }

            // Pairs with the interlocked read in CancelLifetime: if Dispose ran before this source was
            // published, it could not see it, so the source is closed here.
            if (Volatile.Read(ref m_isDisposed))
            {
                created.Close();
            }

            return created.Token;
        }

        private void CancelLifetime(ref List<Exception> disposalErrors)
        {
            // Interlocked read: orders the m_isDisposed write before it (see CreateLifetimeToken).
            ScopeLifetimeSource source = Interlocked.CompareExchange(ref m_lifetimeSource, null, null);

            if (source == null)
            {
                return;
            }

            try
            {
                source.Close();
            }
            catch (Exception exception)
            {
                disposalErrors ??= new List<Exception>(1);
                disposalErrors.Add(exception);
            }
        }

        private void DisposeBuildTokenSources()
        {
            List<CancellationTokenSource> sources = m_buildTokenSources;

            if (sources == null)
            {
                return;
            }

            m_buildTokenSources = null;

            // Each source is already canceled through its link to the lifetime token; disposing
            // removes its registration from the caller's token.
            for (int i = 0; i < sources.Count; i++)
            {
                sources[i].Dispose();
            }
        }

        private sealed class ReferenceIdentityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceIdentityComparer Instance = new ReferenceIdentityComparer();

            bool IEqualityComparer<object>.Equals(object left, object right)
            {
                return ReferenceEquals(left, right);
            }

            public int GetHashCode(object value)
            {
                return value == null ? 0 : RuntimeHelpers.GetHashCode(value);
            }
        }

        // The scope's cancellation source. A child scope links it to the parent's token through one
        // registration that Close removes, so a disposed child leaves nothing behind in a long-lived
        // parent. It is never disposed, so Token stays readable after the scope ends.
        private sealed class ScopeLifetimeSource : CancellationTokenSource
        {
            private static readonly Action<object> s_cancelFromParent = CancelFromParent;

            private readonly CancellationTokenRegistration m_parentRegistration;
            private int m_isClosed;

            public ScopeLifetimeSource(OnityContainer parent)
            {
                if (parent != null)
                {
                    // Runs inline (canceling this source) when the parent has already ended.
                    m_parentRegistration = OnityScopeLifetimeExtensions.RegisterWithoutContext(
                        parent.LifetimeToken, s_cancelFromParent, this);
                }
            }

            // Cancels once and unlinks from the parent. Callback exceptions propagate to Dispose.
            public void Close()
            {
                if (Interlocked.Exchange(ref m_isClosed, 1) != 0)
                {
                    return;
                }

                try
                {
                    Cancel();
                }
                finally
                {
                    m_parentRegistration.Dispose();
                }
            }

            private static void CancelFromParent(object state)
            {
                ((ScopeLifetimeSource)state).Cancel();
            }
        }

        private static Type[] CollectInterfacesAndSelf(Type implementationType)
        {
            Type[] interfaces = implementationType.GetInterfaces();
            Type[] contracts = new Type[interfaces.Length + 1];
            contracts[0] = implementationType;

            for (int i = 0; i < interfaces.Length; i++)
            {
                contracts[i + 1] = interfaces[i];
            }

            return contracts;
        }

        private static Type[] CollectInterfaces(Type implementationType)
        {
            Type[] interfaces = implementationType.GetInterfaces();

            if (interfaces.Length == 0)
            {
                throw new OnityBindingException(
                    $"Type '{implementationType.FullName}' does not implement any interfaces.");
            }

            return interfaces;
        }

        private static void ValidateBinding(Type contractType, Type implementationType)
        {
            if (contractType == null)
            {
                throw new OnityBindingException("Contract type cannot be null.");
            }

            if (implementationType == null)
            {
                throw new OnityBindingException("Implementation type cannot be null.");
            }

            if (implementationType.IsAbstract || implementationType.IsInterface)
            {
                throw new OnityBindingException(
                    $"Implementation type '{implementationType.FullName}' must be a concrete class.");
            }

            if (contractType.IsAssignableFrom(implementationType) == false)
            {
                throw new OnityBindingException(
                    $"Implementation '{implementationType.FullName}' does not satisfy contract '{contractType.FullName}'.");
            }
        }

        private bool TryGetBindingSourceRecursive(Type contractType, int scopeDepth, out OnityBindingSourceInfo sourceInfo)
        {
            if (m_bindingSourceMap.TryGetValue(contractType, out BindingSourceRecord record))
            {
                sourceInfo = new OnityBindingSourceInfo(
                    contractType,
                    record.ImplementationType,
                    record.LifetimeName,
                    record.SourceName,
                    record.IsImplicitRegistration,
                    scopeDepth);

                return true;
            }

            if (m_parent != null)
            {
                return m_parent.TryGetBindingSourceRecursive(contractType, scopeDepth + 1, out sourceInfo);
            }

            sourceInfo = default;
            return false;
        }

        private void RegisterBindingSource(Type contractType, IProvider provider, bool isImplicitRegistration)
        {
            if (contractType == null || provider == null)
            {
                return;
            }

            ProviderDiagnosticsSnapshot snapshot = provider.GetDiagnosticsSnapshot();
            string sourceName = GetCurrentBindingSource();

            if (string.IsNullOrWhiteSpace(sourceName))
            {
                sourceName = isImplicitRegistration ? k_implicitBindingSource : k_unknownBindingSource;
            }

            m_bindingSourceMap[contractType] = new BindingSourceRecord(
                snapshot.ImplementationType,
                snapshot.LifetimeName,
                sourceName,
                isImplicitRegistration);
        }

        private static string GetCurrentBindingSource()
        {
            Stack<string> sourceStack = s_bindingSourceStack;

            if (sourceStack == null || sourceStack.Count == 0)
            {
                return string.Empty;
            }

            return sourceStack.Peek();
        }

        private void RegisterProvider(Type contractType, IProvider provider, bool isImplicitRegistration)
        {
            SetLocalProvider(contractType, provider);
            m_ownedProviders.Add(provider);
            m_bindingVersion++;

            // Implicit auto-resolved concretes are not collection members; only
            // explicit bindings participate in IEnumerable<T>/List<T> synthesis.
            if (isImplicitRegistration == false)
            {
                AddToMultiProviderMap(contractType, provider);
            }

            RegisterBindingSource(contractType, provider, isImplicitRegistration);

            if (m_baked != null)
            {
                m_baked = BuildBakedGraph();
            }

            RegisterLifecycleAfterBuild(provider);
        }

        private void RemoveGeneratedProvider(Type contractType, IProvider provider)
        {
            List<IProvider> remaining = null;

            if (m_multiProviderMap != null && m_multiProviderMap.TryGetValue(contractType, out remaining))
            {
                remaining.Remove(provider);

                if (remaining.Count == 0)
                {
                    m_multiProviderMap.Remove(contractType);
                }
            }

            if (m_providerMap.TryGetValue(contractType, out IProvider current) && ReferenceEquals(current, provider))
            {
                RemoveLocalProvider(contractType);
                m_bindingSourceMap.Remove(contractType);

                if (remaining != null && remaining.Count > 0)
                {
                    IProvider replacement = remaining[remaining.Count - 1];
                    SetLocalProvider(contractType, replacement);
                    RegisterBindingSource(contractType, replacement, false);
                }
            }
        }

        private void RetireUnboundProviders()
        {
            for (int i = 0; i < m_ownedProviders.Count; i++)
            {
                IProvider provider = m_ownedProviders[i];

                if (IsProviderRegistered(provider))
                {
                    continue;
                }

                (m_retiredProviders ??= new HashSet<IProvider>()).Add(provider);

                if (m_lifecycleInstances == null || m_lifecycleInstances.TryGetValue(provider, out object instance) == false)
                {
                    continue;
                }

                m_lifecycleInstances.Remove(provider);
                bool shared = false;

                foreach (KeyValuePair<IProvider, object> pair in m_lifecycleInstances)
                {
                    if (ReferenceEquals(pair.Value, instance) && IsProviderRegistered(pair.Key))
                    {
                        shared = true;
                        break;
                    }
                }

                if (shared)
                {
                    continue;
                }

                RemoveLifecycleInstance(m_initializables, instance);
                RemoveLifecycleInstance(m_tickables, instance);
                RemoveLifecycleInstance(m_fixedTickables, instance);
                RemoveLifecycleInstance(m_lateTickables, instance);
                RemoveAsyncInitializable(instance);
            }
        }

        private bool IsProviderRegistered(IProvider provider)
        {
            foreach (IProvider candidate in m_implicitProviderMap.Values)
            {
                if (ReferenceEquals(candidate, provider))
                {
                    return true;
                }
            }

            if (m_multiProviderMap != null)
            {
                foreach (List<IProvider> providers in m_multiProviderMap.Values)
                {
                    if (providers.Contains(provider))
                    {
                        return true;
                    }
                }
            }

            if (m_contextBindings != null)
            {
                foreach (List<ContextBinding> bindings in m_contextBindings.Values)
                {
                    for (int i = 0; i < bindings.Count; i++)
                    {
                        ContextBinding binding = bindings[i];

                        if (ReferenceEquals(binding.Provider, provider)
                            || (binding.ClosedProviders != null && binding.ClosedProviders.ContainsValue(provider)))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static void RemoveLifecycleInstance<T>(List<T> instances, object instance)
        {
            if (instances == null)
            {
                return;
            }

            for (int i = instances.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(instances[i], instance))
                {
                    instances.RemoveAt(i);
                }
            }
        }

        private void SetLocalProvider(Type contractType, IProvider provider)
        {
            m_providerMap[contractType] = provider;

            int typeId = TypeIdRegistry.Register(contractType);
            IProvider[] providers = m_providerByTypeId;

            if (providers == null || typeId >= providers.Length)
            {
                int newLength = providers == null ? 16 : providers.Length;
                while (newLength <= typeId)
                {
                    newLength *= 2;
                }

                Array.Resize(ref m_providerByTypeId, newLength);
                providers = m_providerByTypeId;
            }

            providers[typeId] = provider;
        }

        private bool RemoveLocalProvider(Type contractType)
        {
            if (m_providerMap.Remove(contractType) == false)
            {
                return false;
            }

            IProvider[] providers = m_providerByTypeId;
            if (providers != null && TypeIdRegistry.TryGetId(contractType, out int typeId)
                && (uint)typeId < (uint)providers.Length)
            {
                providers[typeId] = null;
            }

            return true;
        }

        private void AddToMultiProviderMap(Type contractType, IProvider provider)
        {
            m_multiProviderMap ??= new Dictionary<Type, List<IProvider>>(k_initialBindingCapacity);

            if (m_multiProviderMap.TryGetValue(contractType, out List<IProvider> providers) == false)
            {
                providers = new List<IProvider>(2);
                m_multiProviderMap.Add(contractType, providers);
            }

            providers.Add(provider);
        }

        // Synthesizes a collection resolve. Fires only when serviceType is a supported
        // collection type (IEnumerable<T> / IReadOnlyList<T> / IReadOnlyCollection<T> /
        // IList<T> / ICollection<T> / List<T> / T[]) AND at least one explicit binding
        // of element type T exists in this container or an ancestor. An explicit
        // binding of the collection type itself wins (matched by m_providerMap before
        // this runs); an element type with no bindings falls through unchanged.
        private bool TryResolveCollection(Type serviceType, OnityContainer requester, out object instance)
        {
            Type elementType = GetCollectionElementType(serviceType);

            if (elementType == null)
            {
                instance = null;
                return false;
            }

            List<object> items = new List<object>(4);
            CollectCollectionItems(elementType, requester, items);

            if (items.Count == 0)
            {
                instance = null;
                return false;
            }

            instance = MaterializeCollection(serviceType, elementType, items);
            return true;
        }

        // Gathers resolved instances of the element type across the scope hierarchy,
        // ancestors first, each resolved in its owning container so its own
        // dependencies bind correctly.
        private void CollectCollectionItems(Type elementType, OnityContainer requester, List<object> items)
        {
            if (m_parent != null)
            {
                m_parent.CollectCollectionItems(elementType, requester, items);
            }

            if (m_multiProviderMap == null
                || m_multiProviderMap.TryGetValue(elementType, out List<IProvider> providers) == false)
            {
                return;
            }

            for (int i = 0; i < providers.Count; i++)
            {
                IProvider provider = providers[i];
                items.Add(provider.Get(provider is IScopeOwnedProvider ? requester : this));
            }
        }

        private bool HasCollectionElement(Type elementType)
        {
            if (m_multiProviderMap != null && m_multiProviderMap.ContainsKey(elementType))
            {
                return true;
            }

            return m_parent != null && m_parent.HasCollectionElement(elementType);
        }

        private static Type GetCollectionElementType(Type serviceType)
        {
            if (serviceType.IsArray)
            {
                return serviceType.GetArrayRank() == 1 ? serviceType.GetElementType() : null;
            }

            if (serviceType.IsGenericType == false)
            {
                return null;
            }

            Type definition = serviceType.GetGenericTypeDefinition();

            if (definition == typeof(IEnumerable<>)
                || definition == typeof(IReadOnlyList<>)
                || definition == typeof(IReadOnlyCollection<>)
                || definition == typeof(IList<>)
                || definition == typeof(ICollection<>)
                || definition == typeof(List<>))
            {
                return serviceType.GetGenericArguments()[0];
            }

            return null;
        }

        // Builds a typed elementType[] from the gathered items, returning it directly
        // for array / IEnumerable / IReadOnlyList / IReadOnlyCollection / IList /
        // ICollection requests (an array satisfies all of those), or copying it into a
        // concrete List<elementType> when that exact type was requested.
        private static object MaterializeCollection(Type serviceType, Type elementType, List<object> items)
        {
            Array array = Array.CreateInstance(elementType, items.Count);

            for (int i = 0; i < items.Count; i++)
            {
                array.SetValue(items[i], i);
            }

            if (serviceType.IsArray == false
                && serviceType.GetGenericTypeDefinition() == typeof(List<>))
            {
                return Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType), new object[] { array });
            }

            return array;
        }

        // Resolves a closed generic (IRepo<Foo>) from an open generic registration on
        // THIS container (IRepo<> -> Repo<>). Ancestor open registrations are reached
        // through the normal parent walk, so a child open binding overrides a parent one
        // exactly as a closed binding does.
        private bool TryResolveOpenGeneric(Type serviceType, OnityContainer requester, out object instance)
        {
            if (m_openGenericMap == null || serviceType.IsGenericType == false)
            {
                instance = null;
                return false;
            }

            if (m_openGenericMap.TryGetValue(serviceType.GetGenericTypeDefinition(), out OpenGenericRegistration registration) == false)
            {
                instance = null;
                return false;
            }

            instance = ResolveClosedFromOpenGeneric(serviceType, registration, requester);
            return true;
        }

        private object ResolveClosedFromOpenGeneric(
            Type closedContractType, OpenGenericRegistration registration, OnityContainer requester)
        {
            IProvider provider = GetClosedOpenGenericProvider(closedContractType, registration);
            return provider.Get(provider is IScopeOwnedProvider ? requester : this);
        }

        private IProvider GetClosedOpenGenericProvider(Type closedContractType, OpenGenericRegistration registration)
        {
            if (m_closedOpenGenericProviders != null
                && m_closedOpenGenericProviders.TryGetValue(closedContractType, out IProvider cached))
            {
                return cached;
            }

            Type[] typeArguments = closedContractType.GetGenericArguments();
            Type closedImplementationType = registration.ImplementationDefinition.MakeGenericType(typeArguments);
            ValidateBinding(closedContractType, closedImplementationType);
            IProvider provider = CreateProvider(closedImplementationType, registration.Lifetime);

            m_closedOpenGenericProviders ??= new Dictionary<Type, IProvider>(8);
            m_closedOpenGenericProviders[closedContractType] = provider;

            // An explicit closed binding keeps single-resolve precedence, while
            // collections still include the open registration exactly once.
            if (m_providerMap.ContainsKey(closedContractType))
            {
                m_ownedProviders.Add(provider);
                AddToMultiProviderMap(closedContractType, provider);
                m_bindingVersion++;
                RegisterLifecycleAfterBuild(provider);
            }
            else
            {
                RegisterProvider(closedContractType, provider, false);
            }

            return provider;
        }

        private bool HasOpenGenericRegistration(Type definition)
        {
            if (m_openGenericMap != null && m_openGenericMap.ContainsKey(definition))
            {
                return true;
            }

            return m_parent != null && m_parent.HasOpenGenericRegistration(definition);
        }

        private static IProvider CreateProvider(Type implementationType, BindingLifetime lifetime)
        {
            if (lifetime == BindingLifetime.Singleton)
            {
                return new SingletonProvider(implementationType);
            }

            if (lifetime == BindingLifetime.Scoped)
            {
                return new ScopedProvider(implementationType);
            }

            return new TransientProvider(implementationType);
        }

        private bool TryResolveInternal(Type serviceType, out object instance)
        {
            return TryResolveInternal(serviceType, this, out instance);
        }

        private bool TryResolveInternal(Type serviceType, OnityContainer requester, out object instance)
        {
            if (serviceType == typeof(OnityContainer) || serviceType == typeof(IResolver))
            {
                instance = this;
                return true;
            }

            // Baked fast path for Resolve(Type), nested constructor-dependency
            // resolves, Inject, and TryResolve. A baked slot wraps the same local
            // IProvider the dictionary holds, so a hit returns the identical
            // instance the dictionary branch below would. Misses fall through, so
            // parent/implicit/unbound behavior is unchanged.
            BakedGraph baked = m_baked;

            if (baked != null && TypeIdRegistry.TryGetId(serviceType, out int serviceTypeId)
                && (ReferenceEquals(requester, this)
                    ? baked.TryResolve(serviceTypeId, out instance)
                    : baked.TryResolveForScope(serviceTypeId, requester, out instance)))
            {
                return true;
            }

            if (m_providerMap.TryGetValue(serviceType, out IProvider provider))
            {
                instance = provider.Get(provider is IScopeOwnedProvider ? requester : this);
                return true;
            }

            // An explicit binding of the collection type itself already returned above;
            // otherwise synthesize IEnumerable<T>/IReadOnlyList<T>/T[]/List<T> from every
            // explicit element binding across this scope and its ancestors.
            if (TryResolveCollection(serviceType, requester, out instance))
            {
                return true;
            }

            // This scope's open generic registrations (IRepo<> -> Repo<>) close on demand.
            // Checked before the parent walk so a child open binding overrides a parent's.
            if (TryResolveOpenGeneric(serviceType, requester, out instance))
            {
                return true;
            }

            if (m_parent != null && m_parent.TryResolveInternal(serviceType, requester, out instance))
            {
                return true;
            }

            if (serviceType.IsInterface || serviceType.IsAbstract)
            {
                instance = null;
                return false;
            }

            if (serviceType.IsGenericTypeDefinition)
            {
                instance = null;
                return false;
            }

            if (m_implicitProviderMap.TryGetValue(serviceType, out provider) == false)
            {
                provider = new TransientProvider(serviceType);
                m_implicitProviderMap[serviceType] = provider;
                m_ownedProviders.Add(provider);
                RegisterBindingSource(serviceType, provider, true);
            }

            instance = provider.Get(this);
            return true;
        }

        private object CreateAndInject(Type implementationType)
        {
            TypeInjectionPlan cachedPlan = null;
            return CreateAndInjectCached(implementationType, ref cachedPlan);
        }

        private object GetOrCreateScoped(IProvider provider, Type implementationType)
        {
            EnsureNotDisposed();

            if (m_scopedInstances != null && m_scopedInstances.TryGetValue(provider, out object instance))
            {
                return instance;
            }

            instance = CreateAndInject(implementationType);
            (m_scopedInstances ??= new Dictionary<IProvider, object>()).Add(provider, instance);
            if (m_lifecycleReady || m_isInitializingLifecycle)
            {
                RegisterLifecycle(provider, m_lifecycleReady);
            }

            return instance;
        }

        private OnityContainer GetOrCreateSubContainer(SubContainerProvider provider)
        {
            EnsureNotDisposed();

            if (m_subContainerScopes != null
                && m_subContainerScopes.TryGetValue(provider, out OnityContainer cached))
            {
                return cached;
            }

            OnityContainer child = new OnityContainer(this);
            try
            {
                provider.Install(child);
                child.Build();

                if (child.HasLocalExport(provider.ContractType) == false)
                {
                    throw new OnityBindingException(
                        $"Sub-container installer must bind '{provider.ContractType.FullName}' locally.");
                }

                (m_subContainerScopes ??= new Dictionary<SubContainerProvider, OnityContainer>()).Add(provider, child);
                (m_subContainerScopeList ??= new List<OnityContainer>()).Add(child);
                return child;
            }
            catch
            {
                child.Dispose();
                throw;
            }
        }

        private bool HasLocalExport(Type serviceType)
        {
            if (m_providerMap.ContainsKey(serviceType)
                || TryGetContextBinding(serviceType, null, null, out _))
            {
                return true;
            }

            if (serviceType.IsConstructedGenericType == false)
            {
                return false;
            }

            Type definition = serviceType.GetGenericTypeDefinition();
            return (m_openGenericMap != null && m_openGenericMap.ContainsKey(definition))
                || TryGetContextBinding(definition, null, null, out _);
        }

        private object ResolveLocalExport(Type serviceType)
        {
            if (m_providerMap.TryGetValue(serviceType, out IProvider provider))
            {
                return provider.Get(this);
            }

            if (TryGetContextBinding(serviceType, null, null, out ContextBinding binding))
            {
                return GetContextProvider(binding, serviceType).Get(this);
            }

            if (serviceType.IsConstructedGenericType)
            {
                if (TryGetContextBinding(serviceType.GetGenericTypeDefinition(), null, null, out binding))
                {
                    return GetContextProvider(binding, serviceType).Get(this);
                }

                if (TryResolveOpenGeneric(serviceType, this, out object instance))
                {
                    return instance;
                }
            }

            throw new OnityResolveException(
                $"Sub-container has no local binding for '{serviceType.FullName}'.");
        }

        private object CreateAndInjectCached(Type implementationType, ref TypeInjectionPlan cachedPlan)
        {
            Stack<Type> resolutionStack = s_resolutionStack;

            if (resolutionStack == null)
            {
                resolutionStack = new Stack<Type>(32);
                s_resolutionStack = resolutionStack;
            }

            if (resolutionStack.Contains(implementationType))
            {
                throw new OnityResolveException(
                    $"Circular dependency detected while creating '{implementationType.FullName}'. " +
                    $"Resolution chain: {BuildResolutionChain(resolutionStack, implementationType)}. " +
                    "Break the cycle by depending on an interface and binding it elsewhere, " +
                    "or by injecting a factory (IFactory<T> / Func<T>) instead of the concrete type.");
            }

            resolutionStack.Push(implementationType);

            try
            {
                TypeInjectionPlan plan = GetOrCreatePlan(implementationType);
                object instance = CreateInstance(plan);
                InjectMembers(instance, plan);
                return instance;
            }
            finally
            {
                resolutionStack.Pop();
            }
        }

        // Renders the active resolution path as "Root -> ... -> Current -> Repeated"
        // so a circular dependency message names every type in the cycle. Allocation
        // here is acceptable because it only runs on the throw path, never on a
        // successful resolve. Stack<T> enumerates most-recent-first, so the buffer is
        // walked in reverse to print the root-most frame first.
        private static string BuildResolutionChain(Stack<Type> resolutionStack, Type repeatedType)
        {
            Type[] frames = resolutionStack.ToArray();
            System.Text.StringBuilder builder = new System.Text.StringBuilder(frames.Length * 24 + 24);

            for (int i = frames.Length - 1; i >= 0; i--)
            {
                builder.Append(frames[i].FullName);
                builder.Append(" -> ");
            }

            builder.Append(repeatedType.FullName);
            return builder.ToString();
        }

        // Classifies why a contract could not be resolved and pairs it with the exact
        // fix, so the message is actionable rather than just naming the missing type.
        // Interfaces and abstract classes need an explicit binding; open generic
        // definitions can never be resolved directly; a concrete class normally falls
        // back to an implicit transient, so reaching here means construction is the
        // real problem.
        private static string BuildUnresolvableMessage(Type serviceType)
        {
            if (serviceType.IsInterface)
            {
                return $"Could not resolve service type '{serviceType.FullName}'. " +
                    "It is an unbound interface. " +
                    $"Bind it with container.Bind<{serviceType.Name}>().To<Impl>() or register an instance.";
            }

            if (serviceType.IsAbstract)
            {
                return $"Could not resolve service type '{serviceType.FullName}'. " +
                    "It is an unbound abstract type. " +
                    $"Bind it with container.Bind<{serviceType.Name}>().To<Impl>() or register an instance.";
            }

            if (serviceType.IsGenericTypeDefinition)
            {
                return $"Could not resolve service type '{serviceType.FullName}'. " +
                    "It is an open generic type definition and cannot be resolved directly. " +
                    "Resolve a closed constructed type (for example List<int>) " +
                    "or bind a closed type with container.Bind<T>().To<Impl>().";
            }

            return $"Could not resolve service type '{serviceType.FullName}'. " +
                "Add an explicit binding with container.Bind<T>().To<Impl>(), register an instance, " +
                "or use a resolvable concrete type.";
        }

        // Names the selected constructor and its parameter types for failed-construction
        // diagnostics. Only runs on the throw path, so the StringBuilder allocation is
        // acceptable.
        private static string DescribeConstructor(ConstructorInfo constructor)
        {
            if (constructor == null)
            {
                return "<unknown>";
            }

            ParameterInfo[] parameters = constructor.GetParameters();
            System.Text.StringBuilder builder = new System.Text.StringBuilder(parameters.Length * 24 + 32);
            builder.Append(constructor.DeclaringType != null ? constructor.DeclaringType.FullName : "<unknown>");
            builder.Append('(');

            for (int i = 0; i < parameters.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(parameters[i].ParameterType.FullName);
            }

            builder.Append(')');
            return builder.ToString();
        }

        private object CreateInstance(TypeInjectionPlan plan)
        {
            int dependencyCount = plan.ConstructorDependencies.Length;

            if (dependencyCount == 0)
            {
                return plan.Activator(Array.Empty<object>());
            }

            object[] arguments = ArgumentArrayPool.Rent(dependencyCount);

            try
            {
                for (int i = 0; i < dependencyCount; i++)
                {
                    arguments[i] = ResolveConstructorDependency(plan, i);
                }

                return plan.Activator(arguments);
            }
            catch (OnityResolveException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new OnityResolveException(
                    $"Failed to instantiate '{plan.ImplementationType.FullName}' " +
                    $"using constructor '{DescribeConstructor(plan.Constructor)}'. Error: {ex.Message}. " +
                    "Check that the constructor body does not throw and that every parameter type is resolvable.");
            }
            finally
            {
                ArgumentArrayPool.Return(arguments, dependencyCount);
            }
        }

        // Resolves one constructor dependency, reusing a per-plan, per-slot cached
        // outcome to skip the self-resolve compares and the dictionary lookup on the
        // steady-state hot path. The cache only short-circuits cases that are
        // provably identical to TryResolveInternal: self-resolve, and a same-scope
        // provider whose binding version is unchanged. Everything else (parent,
        // implicit, abstract, unresolved) defers to Resolve so cross-scope, lazy
        // implicit, rebind, and exception behavior remain identical.
        private object ResolveConstructorDependency(TypeInjectionPlan plan, int slot)
        {
            object id = plan.ConstructorIds == null ? null : plan.ConstructorIds[slot];

            if (id != null)
            {
                return ResolveDependency(plan.ConstructorDependencies[slot], id, plan.ImplementationType);
            }

            if ((m_contextBindings != null || m_parent != null)
                && TryResolveContextBinding(plan.ConstructorDependencies[slot], null, plan.ImplementationType, out object contextual))
            {
                return contextual;
            }

            DependencyResolution resolution = plan.ConstructorDependencyCache[slot];

            if (resolution != null)
            {
                if (resolution.Kind == DependencyResolutionKind.SameScopeProvider
                    && resolution.CapturedBindingVersion == m_bindingVersion)
                {
                    return resolution.CapturedProvider.Get(this);
                }

                if (resolution.Kind == DependencyResolutionKind.SelfResolve)
                {
                    return this;
                }

                if (resolution.Kind == DependencyResolutionKind.Deferred)
                {
                    // Already classified as parent/implicit/unresolved. Defer to the
                    // full Resolve path without re-writing the cache slot, so deferred
                    // ctor dependencies stay allocation-free on repeated resolves.
                    return Resolve(plan.ConstructorDependencies[slot]);
                }
            }

            Type dependencyType = plan.ConstructorDependencies[slot];

            if (dependencyType == typeof(OnityContainer) || dependencyType == typeof(IResolver))
            {
                plan.ConstructorDependencyCache[slot] =
                    new DependencyResolution(DependencyResolutionKind.SelfResolve, null, 0);
                return this;
            }

            if (m_providerMap.TryGetValue(dependencyType, out IProvider provider))
            {
                plan.ConstructorDependencyCache[slot] =
                    new DependencyResolution(DependencyResolutionKind.SameScopeProvider, provider, m_bindingVersion);
                return provider.Get(this);
            }

            plan.ConstructorDependencyCache[slot] =
                new DependencyResolution(DependencyResolutionKind.Deferred, null, 0);
            return Resolve(dependencyType);
        }

        private void InjectMembers(object instance, TypeInjectionPlan plan)
        {
            for (int i = 0; i < plan.Fields.Length; i++)
            {
                InjectedField member = plan.Fields[i];
                object dependency = ResolveDependency(member.DependencyType, member.Id, plan.ImplementationType);
                member.Setter(instance, dependency);
            }

            for (int i = 0; i < plan.Properties.Length; i++)
            {
                InjectedProperty member = plan.Properties[i];
                object dependency = ResolveDependency(member.DependencyType, member.Id, plan.ImplementationType);
                member.Setter(instance, dependency);
            }

            for (int i = 0; i < plan.Methods.Length; i++)
            {
                InjectedMethod method = plan.Methods[i];
                int dependencyCount = method.DependencyTypes.Length;

                if (dependencyCount == 0)
                {
                    method.Invoker(instance, Array.Empty<object>());
                    continue;
                }

                object[] arguments = ArgumentArrayPool.Rent(dependencyCount);

                try
                {
                    for (int dependencyIndex = 0; dependencyIndex < dependencyCount; dependencyIndex++)
                    {
                        object id = method.DependencyIds == null ? null : method.DependencyIds[dependencyIndex];
                        arguments[dependencyIndex] = ResolveDependency(
                            method.DependencyTypes[dependencyIndex], id, plan.ImplementationType);
                    }

                    method.Invoker(instance, arguments);
                }
                finally
                {
                    ArgumentArrayPool.Return(arguments, dependencyCount);
                }
            }
        }

        private TypeInjectionPlan GetOrCreatePlan(Type implementationType)
        {
            if (m_planMap.TryGetValue(implementationType, out TypeInjectionPlan plan))
            {
                return plan;
            }

            plan = BuildPlan(implementationType);
            m_planMap.Add(implementationType, plan);
            return plan;
        }

        private static TypeInjectionPlan BuildPlan(Type implementationType)
        {
            ConstructorInfo constructor = SelectConstructor(implementationType);
            Type[] constructorDependencyTypes = ExtractDependencyTypes(constructor.GetParameters());

            List<InjectedField> fields = new List<InjectedField>(8);
            List<InjectedProperty> properties = new List<InjectedProperty>(8);
            List<InjectedMethod> methods = new List<InjectedMethod>(4);
            HashSet<MethodInfo> injectedMethodBaseDefinitions = new HashSet<MethodInfo>();

            Type[] hierarchy = GetTypeHierarchy(implementationType);

            for (int hierarchyIndex = hierarchy.Length - 1; hierarchyIndex >= 0; hierarchyIndex--)
            {
                Type currentType = hierarchy[hierarchyIndex];
                CollectInjectedFields(currentType, fields);
                CollectInjectedProperties(currentType, properties);
                CollectInjectedMethods(currentType, methods, injectedMethodBaseDefinitions);
            }

            ActivatorDelegate activator = ActivatorCompiler.Compile(constructor);

            return new TypeInjectionPlan(
                implementationType,
                constructor,
                activator,
                constructorDependencyTypes,
                ExtractDependencyIds(constructor.GetParameters()),
                fields.ToArray(),
                properties.ToArray(),
                methods.ToArray());
        }

        private static Type[] GetTypeHierarchy(Type type)
        {
            List<Type> types = new List<Type>(8);
            Type current = type;

            while (current != null && current != typeof(object))
            {
                types.Add(current);
                current = current.BaseType;
            }

            return types.ToArray();
        }

        private static ConstructorInfo SelectConstructor(Type implementationType)
        {
            ConstructorInfo[] constructors =
                implementationType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (constructors.Length == 0)
            {
                throw new OnityBindingException(
                    $"Type '{implementationType.FullName}' has no accessible constructor. " +
                    "Add a public or non-public constructor the container can invoke, " +
                    "or bind a concrete implementation with container.Bind<T>().To<Impl>().");
            }

            ConstructorInfo attributedConstructor = null;

            for (int i = 0; i < constructors.Length; i++)
            {
                if (constructors[i].IsDefined(typeof(InjectAttribute), true) == false)
                {
                    continue;
                }

                if (attributedConstructor != null)
                {
                    throw new OnityBindingException(
                        $"Type '{implementationType.FullName}' contains multiple [Inject] constructors. " +
                        "Mark exactly one constructor with [Inject], or remove the attribute and let the " +
                        "container pick the constructor with the most parameters.");
                }

                attributedConstructor = constructors[i];
            }

            if (attributedConstructor != null)
            {
                return attributedConstructor;
            }

            ConstructorInfo selected = constructors[0];
            int selectedScore = GetConstructorScore(selected);

            for (int i = 1; i < constructors.Length; i++)
            {
                int score = GetConstructorScore(constructors[i]);

                if (score > selectedScore)
                {
                    selected = constructors[i];
                    selectedScore = score;
                }
            }

            return selected;
        }

        private static int GetConstructorScore(ConstructorInfo constructor)
        {
            int score = constructor.GetParameters().Length;

            if (constructor.IsPublic)
            {
                score += 1000;
            }

            return score;
        }

        private static void CollectInjectedFields(Type type, List<InjectedField> fields)
        {
            FieldInfo[] fieldInfos =
                type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

            for (int i = 0; i < fieldInfos.Length; i++)
            {
                FieldInfo fieldInfo = fieldInfos[i];

                if (fieldInfo.IsDefined(typeof(InjectAttribute), true) == false)
                {
                    continue;
                }

                fields.Add(
                    new InjectedField(
                        fieldInfo,
                        fieldInfo.FieldType,
                        fieldInfo.GetCustomAttribute<InjectAttribute>(true).Id,
                        MemberSetterCompiler.CompileFieldSetter(fieldInfo)));
            }
        }

        private static void CollectInjectedProperties(Type type, List<InjectedProperty> properties)
        {
            PropertyInfo[] propertyInfos =
                type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

            for (int i = 0; i < propertyInfos.Length; i++)
            {
                PropertyInfo propertyInfo = propertyInfos[i];

                if (propertyInfo.IsDefined(typeof(InjectAttribute), true) == false)
                {
                    continue;
                }

                MethodInfo setMethod = propertyInfo.GetSetMethod(true);

                if (setMethod == null)
                {
                    throw new OnityBindingException(
                        $"[Inject] property '{propertyInfo.Name}' on '{type.FullName}' must have a setter. " +
                        "Add a (private) set accessor, or move the [Inject] attribute to a backing field " +
                        "or an Initialize method.");
                }

                if (propertyInfo.GetIndexParameters().Length > 0)
                {
                    throw new OnityBindingException(
                        $"[Inject] property '{propertyInfo.Name}' on '{type.FullName}' cannot be an indexer. " +
                        "Inject into a non-indexed property, a field, or a method parameter instead.");
                }

                properties.Add(
                    new InjectedProperty(
                        propertyInfo,
                        propertyInfo.PropertyType,
                        propertyInfo.GetCustomAttribute<InjectAttribute>(true).Id,
                        MemberSetterCompiler.CompilePropertySetter(propertyInfo)));
            }
        }

        private static void CollectInjectedMethods(
            Type type,
            List<InjectedMethod> methods,
            HashSet<MethodInfo> injectedMethodBaseDefinitions)
        {
            MethodInfo[] methodInfos =
                type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

            for (int i = 0; i < methodInfos.Length; i++)
            {
                MethodInfo methodInfo = methodInfos[i];

                if (methodInfo.IsDefined(typeof(InjectAttribute), false) == false)
                {
                    continue;
                }

                if (methodInfo.ContainsGenericParameters)
                {
                    throw new OnityBindingException(
                        $"[Inject] method '{methodInfo.Name}' on '{type.FullName}' cannot be generic. " +
                        "Use a non-generic [Inject] method whose parameter types are concrete and resolvable.");
                }

                MethodInfo baseDefinition = methodInfo.GetBaseDefinition();

                if (injectedMethodBaseDefinitions.Add(baseDefinition) == false)
                {
                    continue;
                }

                Type[] dependencyTypes = ExtractDependencyTypes(methodInfo.GetParameters());
                methods.Add(
                    new InjectedMethod(
                        methodInfo,
                        dependencyTypes,
                        ExtractDependencyIds(methodInfo.GetParameters()),
                        MemberSetterCompiler.CompileMethodInvoker(methodInfo)));
            }
        }

        private static object[] ExtractDependencyIds(ParameterInfo[] parameters)
        {
            object[] ids = null;

            for (int i = 0; i < parameters.Length; i++)
            {
                InjectAttribute attribute = parameters[i].GetCustomAttribute<InjectAttribute>();

                if (attribute?.Id != null)
                {
                    ids ??= new object[parameters.Length];
                    ids[i] = attribute.Id;
                }
            }

            return ids;
        }

        private static Type[] ExtractDependencyTypes(ParameterInfo[] parameters)
        {
            if (parameters == null || parameters.Length == 0)
            {
                return Type.EmptyTypes;
            }

            Type[] dependencyTypes = new Type[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                dependencyTypes[i] = parameters[i].ParameterType;
            }

            return dependencyTypes;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureNotDisposed()
        {
            if (m_isDisposed)
            {
                throw new OnityResolveException("Container has already been disposed.");
            }
        }

        private void EnsureBuildNotFinalized()
        {
            if (m_isBuildFinalized)
            {
                throw new OnityBindingException(
                    "Build callbacks cannot be registered after container build has been finalized.");
            }
        }

        private static double ConvertTicksToMilliseconds(long ticks)
        {
            if (ticks <= 0)
            {
                return 0d;
            }

            return ticks * 1000d / Stopwatch.Frequency;
        }

        private readonly struct BindingSourceScope : IDisposable
        {
            public static BindingSourceScope Empty => default;

            private readonly bool m_isActive;

            public BindingSourceScope(bool isActive)
            {
                m_isActive = isActive;
            }

            public void Dispose()
            {
                if (m_isActive == false)
                {
                    return;
                }

                Stack<string> sourceStack = s_bindingSourceStack;

                if (sourceStack == null || sourceStack.Count == 0)
                {
                    return;
                }

                sourceStack.Pop();
            }
        }

        private readonly struct BindingSourceRecord
        {
            public readonly Type ImplementationType;
            public readonly string LifetimeName;
            public readonly string SourceName;
            public readonly bool IsImplicitRegistration;

            public BindingSourceRecord(
                Type implementationType,
                string lifetimeName,
                string sourceName,
                bool isImplicitRegistration)
            {
                ImplementationType = implementationType;
                LifetimeName = lifetimeName;
                SourceName = sourceName;
                IsImplicitRegistration = isImplicitRegistration;
            }
        }

        private sealed class BindingDiagnosticsAggregation
        {
            public readonly ProviderDiagnosticsSnapshot Provider;
            public readonly List<Type> ContractTypes;
            public bool IsImplicit;

            public BindingDiagnosticsAggregation(ProviderDiagnosticsSnapshot provider, bool isImplicit)
            {
                Provider = provider;
                IsImplicit = isImplicit;
                ContractTypes = new List<Type>(4);
            }

            public void AddContract(Type contractType)
            {
                if (contractType == null)
                {
                    return;
                }

                for (int i = 0; i < ContractTypes.Count; i++)
                {
                    if (ContractTypes[i] == contractType)
                    {
                        return;
                    }
                }

                ContractTypes.Add(contractType);
            }

            public void MarkImplicit()
            {
                IsImplicit = true;
            }

            public void SortContracts()
            {
                ContractTypes.Sort(
                    (left, right) => string.Compare(left.FullName, right.FullName, StringComparison.Ordinal));
            }
        }

        private readonly struct ProviderDiagnosticsSnapshot
        {
            public readonly Type ImplementationType;
            public readonly string LifetimeName;
            public readonly long ResolveCount;
            public readonly long TotalResolveTicks;
            public readonly long LastResolveTicks;

            public ProviderDiagnosticsSnapshot(
                Type implementationType,
                string lifetimeName,
                long resolveCount,
                long totalResolveTicks,
                long lastResolveTicks)
            {
                ImplementationType = implementationType;
                LifetimeName = lifetimeName;
                ResolveCount = resolveCount;
                TotalResolveTicks = totalResolveTicks;
                LastResolveTicks = lastResolveTicks;
            }
        }

        // One open generic registration: the open implementation definition (Repo<>)
        // and the lifetime to use when its closed form is built on resolve.
        private readonly struct BindingKey : IEquatable<BindingKey>
        {
            public readonly Type ContractType;
            private readonly object m_id;

            public BindingKey(Type contractType, object id)
            {
                ContractType = contractType;
                m_id = id;
            }

            public bool Equals(BindingKey other)
            {
                return ContractType == other.ContractType && Equals(m_id, other.m_id);
            }

            public override bool Equals(object obj)
            {
                return obj is BindingKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (ContractType.GetHashCode() * 397) ^ (m_id?.GetHashCode() ?? 0);
                }
            }
        }

        private sealed class ContextBinding
        {
            public readonly IProvider Provider;
            public readonly Type ConsumerType;
            public readonly Type ImplementationDefinition;
            public readonly BindingLifetime Lifetime;
            public readonly bool ReusesInstance;
            public Dictionary<Type, IProvider> ClosedProviders;
            public object CachedInstance;
            public bool HasCachedInstance;

            public ContextBinding(IProvider provider, Type consumerType)
            {
                Provider = provider;
                ConsumerType = consumerType;
                BakedLifetime lifetime = provider.BakedLifetime;
                ReusesInstance = lifetime == BakedLifetime.Singleton
                    || lifetime == BakedLifetime.Instance;
            }

            public ContextBinding(Type implementationDefinition, BindingLifetime lifetime, Type consumerType)
            {
                ImplementationDefinition = implementationDefinition;
                Lifetime = lifetime;
                ConsumerType = consumerType;
            }
        }

        private readonly struct OpenGenericRegistration
        {
            public readonly Type ImplementationDefinition;
            public readonly BindingLifetime Lifetime;

            public OpenGenericRegistration(Type implementationDefinition, BindingLifetime lifetime)
            {
                ImplementationDefinition = implementationDefinition;
                Lifetime = lifetime;
            }
        }

        internal interface IBakedProvider
        {
            object Get(OnityContainer container);

            // Creation strategy and concrete type, read only at Build() time to
            // populate the baked graph. Never touched on the resolve hot path.
            BakedLifetime BakedLifetime { get; }
        }

        internal interface IScopeOwnedProvider
        {
        }

        private interface IProvider : IBakedProvider, IDisposable
        {
            ProviderDiagnosticsSnapshot GetDiagnosticsSnapshot();

            // Concrete type is read only for diagnostics and registration setup.
            Type ImplementationType { get; }
        }

        private sealed class InstanceProvider : IProvider
        {
            private const string k_lifetimeName = "Instance";

            private readonly object m_instance;
            private readonly Type m_implementationType;
            private long m_resolveCount;
            private long m_totalResolveTicks;
            private long m_lastResolveTicks;

            public InstanceProvider(object instance)
            {
                m_instance = instance;
                m_implementationType = instance.GetType();
            }

            public BakedLifetime BakedLifetime => BakedLifetime.Instance;

            public Type ImplementationType => m_implementationType;

            public object Get(OnityContainer container)
            {
                if (s_diagnosticsCollectionEnabled == false)
                {
                    return m_instance;
                }

                long startTimestamp = Stopwatch.GetTimestamp();

                try
                {
                    return m_instance;
                }
                finally
                {
                    RecordResolve(Stopwatch.GetTimestamp() - startTimestamp);
                }
            }

            public ProviderDiagnosticsSnapshot GetDiagnosticsSnapshot()
            {
                return new ProviderDiagnosticsSnapshot(
                    m_implementationType,
                    k_lifetimeName,
                    Interlocked.Read(ref m_resolveCount),
                    Interlocked.Read(ref m_totalResolveTicks),
                    Interlocked.Read(ref m_lastResolveTicks));
            }

            private void RecordResolve(long elapsedTicks)
            {
                Interlocked.Increment(ref m_resolveCount);
                Interlocked.Add(ref m_totalResolveTicks, elapsedTicks);
                Interlocked.Exchange(ref m_lastResolveTicks, elapsedTicks);
            }

            public void Dispose()
            {
            }
        }

        private sealed class TransientProvider : IProvider
        {
            private const string k_lifetimeName = "Transient";

            private readonly Type m_implementationType;
            private long m_resolveCount;
            private long m_totalResolveTicks;
            private long m_lastResolveTicks;

            public TransientProvider(Type implementationType)
            {
                m_implementationType = implementationType;
            }

            public BakedLifetime BakedLifetime => BakedLifetime.Transient;

            public Type ImplementationType => m_implementationType;

            public object Get(OnityContainer container)
            {
                if (s_diagnosticsCollectionEnabled == false)
                {
                    return container.CreateAndInject(m_implementationType);
                }

                long startTimestamp = Stopwatch.GetTimestamp();

                try
                {
                    return container.CreateAndInject(m_implementationType);
                }
                finally
                {
                    RecordResolve(Stopwatch.GetTimestamp() - startTimestamp);
                }
            }

            public ProviderDiagnosticsSnapshot GetDiagnosticsSnapshot()
            {
                return new ProviderDiagnosticsSnapshot(
                    m_implementationType,
                    k_lifetimeName,
                    Interlocked.Read(ref m_resolveCount),
                    Interlocked.Read(ref m_totalResolveTicks),
                    Interlocked.Read(ref m_lastResolveTicks));
            }

            private void RecordResolve(long elapsedTicks)
            {
                Interlocked.Increment(ref m_resolveCount);
                Interlocked.Add(ref m_totalResolveTicks, elapsedTicks);
                Interlocked.Exchange(ref m_lastResolveTicks, elapsedTicks);
            }

            public void Dispose()
            {
            }
        }

        private sealed class SubContainerProvider : IProvider, IScopeOwnedProvider
        {
            private const string k_lifetimeName = "SubContainer";

            private readonly Type m_contractType;
            private readonly Action<OnityContainer> m_install;
            private long m_resolveCount;
            private long m_totalResolveTicks;
            private long m_lastResolveTicks;

            public SubContainerProvider(Type contractType, Action<OnityContainer> install)
            {
                m_contractType = contractType;
                m_install = install;
            }

            public Type ContractType => m_contractType;

            public BakedLifetime BakedLifetime => BakedLifetime.Scoped;

            public Type ImplementationType => m_contractType;

            public void Install(OnityContainer child)
            {
                m_install(child);
            }

            public object Get(OnityContainer container)
            {
                if (s_diagnosticsCollectionEnabled == false)
                {
                    return container.GetOrCreateSubContainer(this).ResolveLocalExport(m_contractType);
                }

                long startTimestamp = Stopwatch.GetTimestamp();
                try
                {
                    return container.GetOrCreateSubContainer(this).ResolveLocalExport(m_contractType);
                }
                finally
                {
                    long elapsedTicks = Stopwatch.GetTimestamp() - startTimestamp;
                    Interlocked.Increment(ref m_resolveCount);
                    Interlocked.Add(ref m_totalResolveTicks, elapsedTicks);
                    Interlocked.Exchange(ref m_lastResolveTicks, elapsedTicks);
                }
            }

            public ProviderDiagnosticsSnapshot GetDiagnosticsSnapshot()
            {
                return new ProviderDiagnosticsSnapshot(
                    m_contractType,
                    k_lifetimeName,
                    Interlocked.Read(ref m_resolveCount),
                    Interlocked.Read(ref m_totalResolveTicks),
                    Interlocked.Read(ref m_lastResolveTicks));
            }

            public void Dispose()
            {
                // The requesting container owns and disposes each installed child.
            }
        }

        private sealed class ScopedProvider : IProvider, IScopeOwnedProvider
        {
            private const string k_lifetimeName = "Scoped";

            private readonly Type m_implementationType;
            private long m_resolveCount;
            private long m_totalResolveTicks;
            private long m_lastResolveTicks;

            public ScopedProvider(Type implementationType)
            {
                m_implementationType = implementationType;
            }

            public BakedLifetime BakedLifetime => BakedLifetime.Scoped;

            public Type ImplementationType => m_implementationType;

            public object Get(OnityContainer container)
            {
                if (s_diagnosticsCollectionEnabled == false)
                {
                    return container.GetOrCreateScoped(this, m_implementationType);
                }

                long startTimestamp = Stopwatch.GetTimestamp();
                try
                {
                    return container.GetOrCreateScoped(this, m_implementationType);
                }
                finally
                {
                    long elapsedTicks = Stopwatch.GetTimestamp() - startTimestamp;
                    Interlocked.Increment(ref m_resolveCount);
                    Interlocked.Add(ref m_totalResolveTicks, elapsedTicks);
                    Interlocked.Exchange(ref m_lastResolveTicks, elapsedTicks);
                }
            }

            public ProviderDiagnosticsSnapshot GetDiagnosticsSnapshot()
            {
                return new ProviderDiagnosticsSnapshot(
                    m_implementationType,
                    k_lifetimeName,
                    Interlocked.Read(ref m_resolveCount),
                    Interlocked.Read(ref m_totalResolveTicks),
                    Interlocked.Read(ref m_lastResolveTicks));
            }

            public void Dispose()
            {
                // Each resolving container owns and disposes its cached instance.
            }
        }

        private sealed class SingletonProvider : IProvider
        {
            private const string k_lifetimeName = "Singleton";

            private readonly Type m_implementationType;
            private readonly object m_gate;
            private object m_instance;
            private bool m_hasInstance;
            private long m_resolveCount;
            private long m_totalResolveTicks;
            private long m_lastResolveTicks;

            public SingletonProvider(Type implementationType)
            {
                m_implementationType = implementationType;
                m_gate = new object();
            }

            public BakedLifetime BakedLifetime => BakedLifetime.Singleton;

            public Type ImplementationType => m_implementationType;

            public object Get(OnityContainer container)
            {
                if (s_diagnosticsCollectionEnabled == false)
                {
                    return GetOrCreateInstance(container);
                }

                long startTimestamp = Stopwatch.GetTimestamp();

                try
                {
                    return GetOrCreateInstance(container);
                }
                finally
                {
                    RecordResolve(Stopwatch.GetTimestamp() - startTimestamp);
                }
            }

            public ProviderDiagnosticsSnapshot GetDiagnosticsSnapshot()
            {
                return new ProviderDiagnosticsSnapshot(
                    m_implementationType,
                    k_lifetimeName,
                    Interlocked.Read(ref m_resolveCount),
                    Interlocked.Read(ref m_totalResolveTicks),
                    Interlocked.Read(ref m_lastResolveTicks));
            }

            public void Dispose()
            {
                if (m_hasInstance && m_instance is IDisposable disposable)
                {
                    disposable.Dispose();
                }

                m_hasInstance = false;
                m_instance = null;
            }

            private object GetOrCreateInstance(OnityContainer container)
            {
                if (m_hasInstance)
                {
                    return m_instance;
                }

                lock (m_gate)
                {
                    if (m_hasInstance == false)
                    {
                        m_instance = container.CreateAndInject(m_implementationType);
                        m_hasInstance = true;
                    }
                }

                return m_instance;
            }

            private void RecordResolve(long elapsedTicks)
            {
                Interlocked.Increment(ref m_resolveCount);
                Interlocked.Add(ref m_totalResolveTicks, elapsedTicks);
                Interlocked.Exchange(ref m_lastResolveTicks, elapsedTicks);
            }
        }

        private readonly struct InjectedField
        {
            public readonly FieldInfo FieldInfo;
            public readonly Type DependencyType;
            public readonly object Id;
            public readonly MemberSetterDelegate Setter;

            public InjectedField(FieldInfo fieldInfo, Type dependencyType, object id, MemberSetterDelegate setter)
            {
                FieldInfo = fieldInfo;
                DependencyType = dependencyType;
                Id = id;
                Setter = setter;
            }
        }

        private readonly struct InjectedProperty
        {
            public readonly PropertyInfo PropertyInfo;
            public readonly Type DependencyType;
            public readonly object Id;
            public readonly MemberSetterDelegate Setter;

            public InjectedProperty(PropertyInfo propertyInfo, Type dependencyType, object id, MemberSetterDelegate setter)
            {
                PropertyInfo = propertyInfo;
                DependencyType = dependencyType;
                Id = id;
                Setter = setter;
            }
        }

        private readonly struct InjectedMethod
        {
            public readonly MethodInfo MethodInfo;
            public readonly Type[] DependencyTypes;
            public readonly object[] DependencyIds;
            public readonly MethodInvokerDelegate Invoker;

            public InjectedMethod(MethodInfo methodInfo, Type[] dependencyTypes, object[] dependencyIds, MethodInvokerDelegate invoker)
            {
                MethodInfo = methodInfo;
                DependencyTypes = dependencyTypes;
                DependencyIds = dependencyIds;
                Invoker = invoker;
            }
        }

        private enum DependencyResolutionKind
        {
            // Fall through to the full Resolve(Type) path every time. Used for
            // parent-chain, implicit-concrete, and not-yet-resolvable dependencies
            // so cross-scope, lazy-implicit, and throw behavior stay identical.
            Deferred = 0,

            // Dependency type is OnityContainer or IResolver; return this container.
            SelfResolve = 1,

            // Dependency is bound in THIS scope. CapturedProvider is the same
            // IProvider stored in m_providerMap, valid while CapturedBindingVersion
            // matches the container's current binding version.
            SameScopeProvider = 2
        }

        // Published to a plan's per-slot cache as a single atomic reference write.
        // All fields are readonly so a reader on another thread observes either null
        // (and classifies) or a fully constructed, consistent entry. No torn reads,
        // no lock on the hot path.
        private sealed class DependencyResolution
        {
            public readonly DependencyResolutionKind Kind;
            public readonly IProvider CapturedProvider;
            public readonly int CapturedBindingVersion;

            public DependencyResolution(
                DependencyResolutionKind kind,
                IProvider capturedProvider,
                int capturedBindingVersion)
            {
                Kind = kind;
                CapturedProvider = capturedProvider;
                CapturedBindingVersion = capturedBindingVersion;
            }
        }

        private sealed class TypeInjectionPlan
        {
            public readonly Type ImplementationType;
            public readonly ConstructorInfo Constructor;
            public readonly ActivatorDelegate Activator;
            public readonly Type[] ConstructorDependencies;
            public readonly object[] ConstructorIds;
            public readonly DependencyResolution[] ConstructorDependencyCache;
            public readonly InjectedField[] Fields;
            public readonly InjectedProperty[] Properties;
            public readonly InjectedMethod[] Methods;

            public TypeInjectionPlan(
                Type implementationType,
                ConstructorInfo constructor,
                ActivatorDelegate activator,
                Type[] constructorDependencies,
                object[] constructorIds,
                InjectedField[] fields,
                InjectedProperty[] properties,
                InjectedMethod[] methods)
            {
                ImplementationType = implementationType;
                Constructor = constructor;
                Activator = activator;
                ConstructorDependencies = constructorDependencies;
                ConstructorIds = constructorIds;
                ConstructorDependencyCache = constructorDependencies.Length == 0
                    ? s_emptyDependencyResolutions
                    : new DependencyResolution[constructorDependencies.Length];
                Fields = fields;
                Properties = properties;
                Methods = methods;
            }
        }

        private static class ArgumentArrayPool
        {
            // Per-thread, per-length free-lists. The resolve hot path is recursive:
            // CreateInstance rents a buffer, then resolves each dependency, which
            // recurses into nested CreateInstance frames that rent simultaneously.
            // At depth D there are D buffers checked out at once, and two frames
            // needing the same length must receive DISTINCT buffers. Popping from a
            // per-length stack gives each outstanding rent exclusive ownership of
            // its buffer until Return pushes it back, so recursion stays correct.
            // Array indexing avoids a dictionary lookup on every rent and return.
            // Buffers are recycled per thread without steady-state allocations.
            [ThreadStatic]
            private static Stack<object[]>[] s_freeListsByLength;

            public static object[] Rent(int length)
            {
                Stack<object[]>[] freeLists = s_freeListsByLength;

                if (freeLists != null && (uint)length < (uint)freeLists.Length)
                {
                    Stack<object[]> bucket = freeLists[length];

                    if (bucket != null && bucket.Count > 0)
                    {
                        return bucket.Pop();
                    }
                }

                return new object[length];
            }

            public static void Return(object[] arguments, int usedLength)
            {
                if (arguments == null)
                {
                    return;
                }

                Array.Clear(arguments, 0, usedLength);

                Stack<object[]>[] freeLists = s_freeListsByLength;

                if (freeLists == null)
                {
                    freeLists = new Stack<object[]>[Math.Max(16, arguments.Length + 1)];
                    s_freeListsByLength = freeLists;
                }
                else if (arguments.Length >= freeLists.Length)
                {
                    Array.Resize(ref freeLists, Math.Max(freeLists.Length * 2, arguments.Length + 1));
                    s_freeListsByLength = freeLists;
                }

                Stack<object[]> bucket = freeLists[arguments.Length];

                if (bucket == null)
                {
                    bucket = new Stack<object[]>(8);
                    freeLists[arguments.Length] = bucket;
                }

                bucket.Push(arguments);
            }
        }
    }

    /// <summary>
    /// Snapshot of container cache and binding counts for diagnostics tools.
    /// </summary>
    public readonly struct OnityContainerDiagnostics
    {
        /// <summary>
        /// Number of explicit contract bindings in this container.
        /// </summary>
        public int ExplicitBindingCount { get; }

        /// <summary>
        /// Number of implicitly created concrete providers.
        /// </summary>
        public int ImplicitBindingCount { get; }

        /// <summary>
        /// Number of cached type injection plans.
        /// </summary>
        public int CachedPlanCount { get; }

        /// <summary>
        /// Number of internally owned providers tracked for disposal.
        /// </summary>
        public int OwnedProviderCount { get; }

        /// <summary>
        /// True when this container has a parent scope.
        /// </summary>
        public bool HasParent { get; }

        /// <summary>
        /// Initializes a diagnostics snapshot.
        /// </summary>
        /// <param name="explicitBindingCount">Explicit binding count.</param>
        /// <param name="implicitBindingCount">Implicit binding count.</param>
        /// <param name="cachedPlanCount">Cached plan count.</param>
        /// <param name="ownedProviderCount">Owned provider count.</param>
        /// <param name="hasParent">Parent scope flag.</param>
        public OnityContainerDiagnostics(
            int explicitBindingCount,
            int implicitBindingCount,
            int cachedPlanCount,
            int ownedProviderCount,
            bool hasParent)
        {
            ExplicitBindingCount = explicitBindingCount;
            ImplicitBindingCount = implicitBindingCount;
            CachedPlanCount = cachedPlanCount;
            OwnedProviderCount = ownedProviderCount;
            HasParent = hasParent;
        }
    }

    /// <summary>
    /// Binding-level diagnostics row used by editor monitoring tools.
    /// </summary>
    public readonly struct OnityBindingDiagnostics
    {
        /// <summary>
        /// Concrete implementation type behind the binding provider.
        /// </summary>
        public Type ImplementationType { get; }

        /// <summary>
        /// Contract types mapped to this provider.
        /// </summary>
        public Type[] ContractTypes { get; }

        /// <summary>
        /// Lifetime label displayed by diagnostics tools.
        /// </summary>
        public string Lifetime { get; }

        /// <summary>
        /// True when this row originated from an implicit concrete resolve.
        /// </summary>
        public bool IsImplicitRegistration { get; }

        /// <summary>
        /// Number of resolve calls observed while diagnostics collection is enabled.
        /// </summary>
        public long ResolveCount { get; }

        /// <summary>
        /// Average resolve time in milliseconds.
        /// </summary>
        public double AverageResolveMilliseconds { get; }

        /// <summary>
        /// Last resolve time in milliseconds.
        /// </summary>
        public double LastResolveMilliseconds { get; }

        /// <summary>
        /// Initializes a binding diagnostics row.
        /// </summary>
        /// <param name="implementationType">Concrete implementation type.</param>
        /// <param name="contractTypes">All contract types mapped to provider.</param>
        /// <param name="lifetime">Lifetime label.</param>
        /// <param name="isImplicitRegistration">Implicit registration flag.</param>
        /// <param name="resolveCount">Total resolve count.</param>
        /// <param name="averageResolveMilliseconds">Average resolve milliseconds.</param>
        /// <param name="lastResolveMilliseconds">Last resolve milliseconds.</param>
        public OnityBindingDiagnostics(
            Type implementationType,
            Type[] contractTypes,
            string lifetime,
            bool isImplicitRegistration,
            long resolveCount,
            double averageResolveMilliseconds,
            double lastResolveMilliseconds)
        {
            ImplementationType = implementationType;
            ContractTypes = contractTypes ?? Type.EmptyTypes;
            Lifetime = lifetime ?? string.Empty;
            IsImplicitRegistration = isImplicitRegistration;
            ResolveCount = resolveCount;
            AverageResolveMilliseconds = averageResolveMilliseconds;
            LastResolveMilliseconds = lastResolveMilliseconds;
        }
    }

    /// <summary>
    /// Binding source metadata for a contract type.
    /// </summary>
    public readonly struct OnityBindingSourceInfo
    {
        /// <summary>
        /// Requested contract type.
        /// </summary>
        public Type ContractType { get; }

        /// <summary>
        /// Bound implementation type.
        /// </summary>
        public Type ImplementationType { get; }

        /// <summary>
        /// Provider lifetime label.
        /// </summary>
        public string Lifetime { get; }

        /// <summary>
        /// Source label captured when binding was registered.
        /// </summary>
        public string SourceName { get; }

        /// <summary>
        /// True when binding came from implicit concrete auto-resolution.
        /// </summary>
        public bool IsImplicitRegistration { get; }

        /// <summary>
        /// Parent scope distance. Zero means current scope.
        /// </summary>
        public int ScopeDepth { get; }

        /// <summary>
        /// Initializes binding source metadata.
        /// </summary>
        /// <param name="contractType">Requested contract type.</param>
        /// <param name="implementationType">Bound implementation type.</param>
        /// <param name="lifetime">Provider lifetime label.</param>
        /// <param name="sourceName">Source label captured during registration.</param>
        /// <param name="isImplicitRegistration">Implicit registration flag.</param>
        /// <param name="scopeDepth">Parent scope distance, zero for current scope.</param>
        public OnityBindingSourceInfo(
            Type contractType,
            Type implementationType,
            string lifetime,
            string sourceName,
            bool isImplicitRegistration,
            int scopeDepth)
        {
            ContractType = contractType;
            ImplementationType = implementationType;
            Lifetime = lifetime ?? string.Empty;
            SourceName = sourceName ?? string.Empty;
            IsImplicitRegistration = isImplicitRegistration;
            ScopeDepth = scopeDepth;
        }
    }
}
