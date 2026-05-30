using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Onity.Core;
using Onity.DI.Internal;
using Onity.Factory;

namespace Onity.DI
{
    /// <summary>
    /// Lightweight dependency container with parent-scope support.
    /// </summary>
    public sealed class OnityContainer : IResolver, IDisposable
    {
        [ThreadStatic]
        private static Stack<Type> s_resolutionStack;
        [ThreadStatic]
        private static Stack<string> s_bindingSourceStack;
        private static bool s_diagnosticsCollectionEnabled;
        // Phase 1 baked-resolve flag. DEFAULTS TO FALSE so the proven reflection
        // path stays the shipping path and the existing EditMode suite is
        // unaffected. When true, Build() compiles a BakedGraph and Resolve takes a
        // dense-id, array-indexed fast path that drops the per-resolve dictionary
        // lookup. The baked path only fast-paths explicit local bindings; every
        // other contract (parent, implicit concrete, unbound) defers to the same
        // reflection path, so results are identical under both flag values.
        internal static bool s_useBakedResolve = false;

        /// <summary>
        /// Internal Phase 1 toggle for the baked-resolve fast path. Defaults to
        /// false so the reflection path stays the shipping default. Exposed for the
        /// parity test suite that asserts identical results under both values.
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
        private const string k_unknownBindingSource = "Unknown Binding Source";
        private const string k_implicitBindingSource = "Implicit (Auto-Resolve)";

        private readonly OnityContainer m_parent;
        private readonly Dictionary<Type, IProvider> m_providerMap;
        private readonly Dictionary<Type, IProvider> m_implicitProviderMap;
        private readonly Dictionary<Type, BindingSourceRecord> m_bindingSourceMap;
        private readonly Dictionary<Type, TypeInjectionPlan> m_planMap;
        private readonly List<IProvider> m_ownedProviders;
        private readonly List<Action<IResolver>> m_buildCallbacks;
        private readonly List<Func<IResolver, CancellationToken, Task>> m_asyncBuildCallbacks;
        private bool m_isBuildFinalized;
        private Task m_cachedBuildTask;
        private bool m_isDisposed;
        // Compiled, array-backed view of this scope's explicit local bindings.
        // Non-null only after Build() runs while UseBakedResolve is true. Read on
        // the Resolve hot path; never mutated after Build.
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
            m_providerMap = new Dictionary<Type, IProvider>(128);
            m_implicitProviderMap = new Dictionary<Type, IProvider>(32);
            m_bindingSourceMap = new Dictionary<Type, BindingSourceRecord>(160);
            m_planMap = new Dictionary<Type, TypeInjectionPlan>(128);
            m_ownedProviders = new List<IProvider>(64);
            m_buildCallbacks = new List<Action<IResolver>>(8);
            m_asyncBuildCallbacks = new List<Func<IResolver, CancellationToken, Task>>(4);
            m_isBuildFinalized = false;
            m_cachedBuildTask = null;
        }

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
        }

        /// <summary>
        /// Runs <see cref="IOnityTickable.Tick" /> on every collected tickable in
        /// registration order. The owning Unity context calls this once per frame
        /// from <c>Update</c>; call it yourself if you drive the loop manually. A
        /// no-op until <see cref="Build" /> has collected the entry points.
        /// </summary>
        public void Tick()
        {
            if (m_tickables == null)
            {
                return;
            }

            for (int i = 0; i < m_tickables.Count; i++)
            {
                m_tickables[i].Tick();
            }
        }

        /// <summary>
        /// Runs <see cref="IOnityFixedTickable.FixedTick" /> on every collected fixed
        /// tickable in registration order. The owning Unity context calls this from
        /// <c>FixedUpdate</c>.
        /// </summary>
        public void FixedTick()
        {
            if (m_fixedTickables == null)
            {
                return;
            }

            for (int i = 0; i < m_fixedTickables.Count; i++)
            {
                m_fixedTickables[i].FixedTick();
            }
        }

        /// <summary>
        /// Runs <see cref="IOnityLateTickable.LateTick" /> on every collected late
        /// tickable in registration order. The owning Unity context calls this from
        /// <c>LateUpdate</c>.
        /// </summary>
        public void LateTick()
        {
            if (m_lateTickables == null)
            {
                return;
            }

            for (int i = 0; i < m_lateTickables.Count; i++)
            {
                m_lateTickables[i].LateTick();
            }
        }

        // Scans this scope's owned providers once at Build() and collects the
        // singleton/instance entry points implementing the lifecycle interfaces, then
        // runs Initialize() in registration order. Only singleton/instance bindings
        // qualify: a transient has no single stable instance to tick. Lifecycle
        // singletons are created eagerly here (like classic entry points); other
        // singletons stay lazy. IDisposable lifecycle objects are disposed by their
        // provider on container Dispose, so no separate disposable list is needed.
        private void CollectAndInitializeLifecycle()
        {
            HashSet<object> seen = null;

            for (int i = 0; i < m_ownedProviders.Count; i++)
            {
                IProvider provider = m_ownedProviders[i];
                BakedLifetime lifetime = provider.BakedLifetime;

                if (lifetime != BakedLifetime.Singleton && lifetime != BakedLifetime.Instance)
                {
                    continue;
                }

                Type implementationType = provider.ImplementationType;
                bool isInitializable = typeof(IOnityInitializable).IsAssignableFrom(implementationType);
                bool isTickable = typeof(IOnityTickable).IsAssignableFrom(implementationType);
                bool isFixedTickable = typeof(IOnityFixedTickable).IsAssignableFrom(implementationType);
                bool isLateTickable = typeof(IOnityLateTickable).IsAssignableFrom(implementationType);

                if (isInitializable == false
                    && isTickable == false
                    && isFixedTickable == false
                    && isLateTickable == false)
                {
                    continue;
                }

                object instance = provider.Get(this);

                // The same instance can be bound twice (e.g. two BindInstance calls),
                // producing two providers; collect each entry point only once.
                seen ??= new HashSet<object>();

                if (seen.Add(instance) == false)
                {
                    continue;
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
            }

            if (m_initializables == null)
            {
                return;
            }

            for (int i = 0; i < m_initializables.Count; i++)
            {
                m_initializables[i].Initialize();
            }
        }

        // Compiles this scope's explicit local bindings into a flat, dense-id keyed
        // graph. Each baked producer wraps the SAME IProvider the dictionary path
        // uses, so a baked resolve returns an instance identical to the reflection
        // path (singleton identity, transient distinctness, member injection, and
        // cycle detection are all owned by the provider, not duplicated here). Only
        // m_providerMap is baked: parent, implicit-concrete, and unbound contracts
        // intentionally stay on the reflection path for identical behavior.
        private BakedGraph BuildBakedGraph()
        {
            BakedGraph.Builder builder = new BakedGraph.Builder(m_providerMap.Count);

            foreach (KeyValuePair<Type, IProvider> pair in m_providerMap)
            {
                Type contractType = pair.Key;
                IProvider provider = pair.Value;
                int contractTypeId = TypeIdRegistry.Register(contractType);

                int[] dependencyTypeIds = CollectBakedDependencyIds(provider);
                ActivatorDelegate activator = TryGetActivator(provider);
                IProvider capturedProvider = provider;
                BakedProducer producer = () => capturedProvider.Get(this);

                builder.Add(
                    contractTypeId,
                    provider.BakedLifetime,
                    producer,
                    activator,
                    dependencyTypeIds);
            }

            return builder.Build();
        }

        // Flattens a provider's constructor dependency types into dense ids for the
        // baked dependency list. Instance bindings have no constructor to inspect.
        // Built once per binding at Build() time, never on the resolve hot path.
        private int[] CollectBakedDependencyIds(IProvider provider)
        {
            if (provider.BakedLifetime == BakedLifetime.Instance)
            {
                return null;
            }

            TypeInjectionPlan plan = GetOrCreatePlan(provider.ImplementationType);
            Type[] dependencies = plan.ConstructorDependencies;

            if (dependencies.Length == 0)
            {
                return null;
            }

            int[] dependencyTypeIds = new int[dependencies.Length];

            for (int i = 0; i < dependencies.Length; i++)
            {
                dependencyTypeIds[i] = TypeIdRegistry.Register(dependencies[i]);
            }

            return dependencyTypeIds;
        }

        // Returns the compiled activator for a constructable binding so the baked
        // graph carries the documented activator array. Instance bindings have none.
        private ActivatorDelegate TryGetActivator(IProvider provider)
        {
            if (provider.BakedLifetime == BakedLifetime.Instance)
            {
                return null;
            }

            return GetOrCreatePlan(provider.ImplementationType).Activator;
        }

        /// <summary>
        /// Executes asynchronous post-build callbacks once and caches the resulting task.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for callback execution.</param>
        /// <returns>Completion task for all async callbacks.</returns>
        public Task BuildAsync(CancellationToken cancellationToken = default)
        {
            EnsureNotDisposed();
            Build();

            if (m_cachedBuildTask != null)
            {
                return m_cachedBuildTask;
            }

            if (m_asyncBuildCallbacks.Count == 0)
            {
                m_cachedBuildTask = Task.CompletedTask;
                return m_cachedBuildTask;
            }

            m_cachedBuildTask = ExecuteBuildCallbacksWithRetryAsync(cancellationToken);
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
        public TService Resolve<TService>()
        {
            EnsureNotDisposed();

            // Baked fast path: dense-id array lookup with no dictionary hash. Only
            // hits explicit local bindings; misses fall through to the identical
            // reflection path so parent/implicit/unbound behavior is unchanged.
            BakedGraph baked = m_baked;

            if (baked != null && baked.TryResolve(TypeIdCache<TService>.Id, out object bakedInstance))
            {
                return (TService)bakedInstance;
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
                m_providerMap.Count,
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

        /// <inheritdoc />
        public void Dispose()
        {
            if (m_isDisposed)
            {
                return;
            }

            m_isDisposed = true;

            for (int i = m_ownedProviders.Count - 1; i >= 0; i--)
            {
                m_ownedProviders[i].Dispose();
            }

            m_ownedProviders.Clear();
            m_providerMap.Clear();
            m_implicitProviderMap.Clear();
            m_bindingSourceMap.Clear();
            m_planMap.Clear();
            m_buildCallbacks.Clear();
            m_asyncBuildCallbacks.Clear();
            m_cachedBuildTask = null;
            m_baked = null;
            // Lifecycle instances are owned by their providers (disposed above);
            // just drop the references so a disposed container ticks nothing.
            m_initializables = null;
            m_tickables = null;
            m_fixedTickables = null;
            m_lateTickables = null;
            m_multiProviderMap = null;
            m_openGenericMap = null;
        }

        internal void Register(Type contractType, Type implementationType, Lifetime lifetime)
        {
            EnsureNotDisposed();
            ValidateBinding(contractType, implementationType);
            RegisterProvider(contractType, CreateProvider(implementationType, lifetime), false);
        }

        internal void Register(Type[] contractTypes, Type implementationType, Lifetime lifetime)
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
                m_providerMap[contractTypes[i]] = provider;
                AddToMultiProviderMap(contractTypes[i], provider);
                RegisterBindingSource(contractTypes[i], provider, false);
            }
        }

        internal void RegisterRuntime(Type contractType, Type implementationType, Lifetime lifetime)
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

        private void RegisterOpenGeneric(Type openContractType, Type openImplementationType, Lifetime lifetime)
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

            m_openGenericMap ??= new Dictionary<Type, OpenGenericRegistration>(8);
            m_openGenericMap[openContractType] = new OpenGenericRegistration(openImplementationType, lifetime);
            m_bindingVersion++;
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

        private async Task ExecuteBuildCallbacksWithRetryAsync(CancellationToken cancellationToken)
        {
            try
            {
                for (int i = 0; i < m_asyncBuildCallbacks.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    Task task = m_asyncBuildCallbacks[i](this, cancellationToken);

                    if (task == null)
                    {
                        continue;
                    }

                    await task.ConfigureAwait(false);
                }
            }
            catch
            {
                // Allow callers to retry BuildAsync after cancellation/failure.
                m_cachedBuildTask = null;
                throw;
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
            m_providerMap[contractType] = provider;
            m_ownedProviders.Add(provider);
            m_bindingVersion++;

            // Implicit auto-resolved concretes are not collection members; only
            // explicit bindings participate in IEnumerable<T>/List<T> synthesis.
            if (isImplicitRegistration == false)
            {
                AddToMultiProviderMap(contractType, provider);
            }

            RegisterBindingSource(contractType, provider, isImplicitRegistration);
        }

        private void AddToMultiProviderMap(Type contractType, IProvider provider)
        {
            m_multiProviderMap ??= new Dictionary<Type, List<IProvider>>(32);

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
        private bool TryResolveCollection(Type serviceType, out object instance)
        {
            Type elementType = GetCollectionElementType(serviceType);

            if (elementType == null)
            {
                instance = null;
                return false;
            }

            List<object> items = new List<object>(4);
            CollectCollectionItems(elementType, items);

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
        private void CollectCollectionItems(Type elementType, List<object> items)
        {
            if (m_parent != null)
            {
                m_parent.CollectCollectionItems(elementType, items);
            }

            if (m_multiProviderMap == null
                || m_multiProviderMap.TryGetValue(elementType, out List<IProvider> providers) == false)
            {
                return;
            }

            for (int i = 0; i < providers.Count; i++)
            {
                items.Add(providers[i].Get(this));
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
        private bool TryResolveOpenGeneric(Type serviceType, out object instance)
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

            instance = ResolveClosedFromOpenGeneric(serviceType, registration);
            return true;
        }

        private object ResolveClosedFromOpenGeneric(Type closedContractType, OpenGenericRegistration registration)
        {
            Type[] typeArguments = closedContractType.GetGenericArguments();
            Type closedImplementationType = registration.ImplementationDefinition.MakeGenericType(typeArguments);
            IProvider provider = CreateProvider(closedImplementationType, registration.Lifetime);

            // Cache the now-closed binding so later resolves of this closed contract take
            // the normal fast path and its singleton lifetime is owned by this scope.
            RegisterProvider(closedContractType, provider, false);
            return provider.Get(this);
        }

        private bool HasOpenGenericRegistration(Type definition)
        {
            if (m_openGenericMap != null && m_openGenericMap.ContainsKey(definition))
            {
                return true;
            }

            return m_parent != null && m_parent.HasOpenGenericRegistration(definition);
        }

        private static IProvider CreateProvider(Type implementationType, Lifetime lifetime)
        {
            if (lifetime == Lifetime.Singleton)
            {
                return new SingletonProvider(implementationType);
            }

            return new TransientProvider(implementationType);
        }

        private bool TryResolveInternal(Type serviceType, out object instance)
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

            if (baked != null
                && TypeIdRegistry.TryGetId(serviceType, out int serviceTypeId)
                && baked.TryResolve(serviceTypeId, out instance))
            {
                return true;
            }

            if (m_providerMap.TryGetValue(serviceType, out IProvider provider))
            {
                instance = provider.Get(this);
                return true;
            }

            // An explicit binding of the collection type itself already returned above;
            // otherwise synthesize IEnumerable<T>/IReadOnlyList<T>/T[]/List<T> from every
            // explicit element binding across this scope and its ancestors.
            if (TryResolveCollection(serviceType, out instance))
            {
                return true;
            }

            // This scope's open generic registrations (IRepo<> -> Repo<>) close on demand.
            // Checked before the parent walk so a child open binding overrides a parent's.
            if (TryResolveOpenGeneric(serviceType, out instance))
            {
                return true;
            }

            if (m_parent != null && m_parent.TryResolveInternal(serviceType, out instance))
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
                object dependency = Resolve(member.DependencyType);
                member.Setter(instance, dependency);
            }

            for (int i = 0; i < plan.Properties.Length; i++)
            {
                InjectedProperty member = plan.Properties[i];
                object dependency = Resolve(member.DependencyType);
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
                        arguments[dependencyIndex] = Resolve(method.DependencyTypes[dependencyIndex]);
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
                        MemberSetterCompiler.CompileMethodInvoker(methodInfo)));
            }
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
        private readonly struct OpenGenericRegistration
        {
            public readonly Type ImplementationDefinition;
            public readonly Lifetime Lifetime;

            public OpenGenericRegistration(Type implementationDefinition, Lifetime lifetime)
            {
                ImplementationDefinition = implementationDefinition;
                Lifetime = lifetime;
            }
        }

        private interface IProvider : IDisposable
        {
            object Get(OnityContainer container);
            ProviderDiagnosticsSnapshot GetDiagnosticsSnapshot();

            // Creation strategy and concrete type, read only at Build() time to
            // populate the baked graph. Never touched on the resolve hot path.
            BakedLifetime BakedLifetime { get; }
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
            public readonly MemberSetterDelegate Setter;

            public InjectedField(FieldInfo fieldInfo, Type dependencyType, MemberSetterDelegate setter)
            {
                FieldInfo = fieldInfo;
                DependencyType = dependencyType;
                Setter = setter;
            }
        }

        private readonly struct InjectedProperty
        {
            public readonly PropertyInfo PropertyInfo;
            public readonly Type DependencyType;
            public readonly MemberSetterDelegate Setter;

            public InjectedProperty(PropertyInfo propertyInfo, Type dependencyType, MemberSetterDelegate setter)
            {
                PropertyInfo = propertyInfo;
                DependencyType = dependencyType;
                Setter = setter;
            }
        }

        private readonly struct InjectedMethod
        {
            public readonly MethodInfo MethodInfo;
            public readonly Type[] DependencyTypes;
            public readonly MethodInvokerDelegate Invoker;

            public InjectedMethod(MethodInfo methodInfo, Type[] dependencyTypes, MethodInvokerDelegate invoker)
            {
                MethodInfo = methodInfo;
                DependencyTypes = dependencyTypes;
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
            public readonly DependencyResolution[] ConstructorDependencyCache;
            public readonly InjectedField[] Fields;
            public readonly InjectedProperty[] Properties;
            public readonly InjectedMethod[] Methods;

            public TypeInjectionPlan(
                Type implementationType,
                ConstructorInfo constructor,
                ActivatorDelegate activator,
                Type[] constructorDependencies,
                InjectedField[] fields,
                InjectedProperty[] properties,
                InjectedMethod[] methods)
            {
                ImplementationType = implementationType;
                Constructor = constructor;
                Activator = activator;
                ConstructorDependencies = constructorDependencies;
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
            // Thread-isolated storage removes the lock from the hot path while
            // staying zero-allocation in steady state (buffers recycled per thread).
            [ThreadStatic]
            private static Dictionary<int, Stack<object[]>> s_freeListsByLength;

            public static object[] Rent(int length)
            {
                Dictionary<int, Stack<object[]>> freeLists = s_freeListsByLength;

                if (freeLists != null
                    && freeLists.TryGetValue(length, out Stack<object[]> bucket)
                    && bucket.Count > 0)
                {
                    return bucket.Pop();
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

                Dictionary<int, Stack<object[]>> freeLists = s_freeListsByLength;

                if (freeLists == null)
                {
                    freeLists = new Dictionary<int, Stack<object[]>>(16);
                    s_freeListsByLength = freeLists;
                }

                if (freeLists.TryGetValue(arguments.Length, out Stack<object[]> bucket) == false)
                {
                    bucket = new Stack<object[]>(8);
                    freeLists.Add(arguments.Length, bucket);
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
