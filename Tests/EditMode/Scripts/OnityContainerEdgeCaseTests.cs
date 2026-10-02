using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.DI;

namespace Onity.Tests.EditMode
{
    [TestFixture]
    public sealed class OnityContainerEdgeCaseTests
    {
        [Test]
        public void BindInstance_Null_ThrowsOnityBindingException()
        {
            using OnityContainer container = new OnityContainer();

            Assert.That(
                () => container.BindInstance<ITestContract>(null),
                Throws.TypeOf<OnityBindingException>());
        }

        [Test]
        public void Resolve_NullType_ThrowsOnityResolveException()
        {
            using OnityContainer container = new OnityContainer();

            Assert.That(
                () => container.Resolve((Type)null),
                Throws.TypeOf<OnityResolveException>());
        }

        [Test]
        public void TryResolve_NullType_ReturnsFalseAndNull()
        {
            using OnityContainer container = new OnityContainer();

            bool canResolve = container.TryResolve(null, out object instance);

            Assert.That(canResolve, Is.False);
            Assert.That(instance, Is.Null);
        }

        [Test]
        public void Inject_NullTarget_ThrowsOnityResolveException()
        {
            using OnityContainer container = new OnityContainer();

            Assert.That(
                () => container.Inject(null),
                Throws.TypeOf<OnityResolveException>());
        }

        [Test]
        public void Resolve_GenericTypeDefinition_ThrowsOnityResolveException()
        {
            using OnityContainer container = new OnityContainer();

            Assert.That(
                () => container.Resolve(typeof(List<>)),
                Throws.TypeOf<OnityResolveException>());
        }

        [Test]
        public void Resolve_UnboundAbstractType_ThrowsOnityResolveException()
        {
            using OnityContainer container = new OnityContainer();

            Assert.That(
                () => container.Resolve(typeof(AbstractContract)),
                Throws.TypeOf<OnityResolveException>());
        }

        [Test]
        public void Resolve_CircularDependency_ThrowsOnityResolveException()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<CircularA>().AsTransient();
            container.Bind<CircularB>().AsTransient();

            Assert.That(
                () => container.Resolve<CircularA>(),
                Throws.TypeOf<OnityResolveException>());
        }

        [Test]
        public void Resolve_MultipleInjectConstructors_ThrowsOnityBindingException()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<TestDependency>().AsTransient();
            container.Bind<MultipleInjectConstructorsTarget>().AsTransient();

            Assert.That(
                () => container.Resolve<MultipleInjectConstructorsTarget>(),
                Throws.TypeOf<OnityBindingException>());
        }

        [Test]
        public void Resolve_InjectPropertyWithoutSetter_ThrowsOnityBindingException()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<TestDependency>().AsTransient();
            container.Bind<InjectPropertyWithoutSetterTarget>().AsTransient();

            Assert.That(
                () => container.Resolve<InjectPropertyWithoutSetterTarget>(),
                Throws.TypeOf<OnityBindingException>());
        }

        [Test]
        public void Resolve_InjectIndexerProperty_ThrowsOnityBindingException()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<TestDependency>().AsTransient();
            container.Bind<InjectIndexerPropertyTarget>().AsTransient();

            Assert.That(
                () => container.Resolve<InjectIndexerPropertyTarget>(),
                Throws.TypeOf<OnityBindingException>());
        }

        [Test]
        public void Resolve_InjectGenericMethod_ThrowsOnityBindingException()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<TestDependency>().AsTransient();
            container.Bind<InjectGenericMethodTarget>().AsTransient();

            Assert.That(
                () => container.Resolve<InjectGenericMethodTarget>(),
                Throws.TypeOf<OnityBindingException>());
        }

        [Test]
        public void Dispose_DisposesSingletonInstance()
        {
            OnityContainer container = new OnityContainer();
            container.Bind<IDisposableContract>().To<DisposableContract>().AsSingle();

            IDisposableContract contract = container.Resolve<IDisposableContract>();
            container.Dispose();

            Assert.That(contract.IsDisposed, Is.True);
        }

        [Test]
        public async Task BuildAsync_CallbackReturningNullTask_DoesNotThrow()
        {
            using OnityContainer container = new OnityContainer();
            container.RegisterBuildCallbackAsync((_, _) => null);

            await container.BuildAsync();
        }

        [Test]
        public async Task BuildAsync_ExecutesSyncCallbacksBeforeAsyncCallbacks()
        {
            using OnityContainer container = new OnityContainer();
            List<string> order = new List<string>(2);

            container.RegisterBuildCallback(_ => order.Add("sync"));
            container.RegisterBuildCallbackAsync(
                async (_, _) =>
                {
                    await Task.Yield();
                    order.Add("async");
                });

            await container.BuildAsync();

            Assert.That(order.Count, Is.EqualTo(2));
            Assert.That(order[0], Is.EqualTo("sync"));
            Assert.That(order[1], Is.EqualTo("async"));
        }

        [Test]
        public void ChildBinding_OverridesParentBinding()
        {
            using OnityContainer parent = new OnityContainer();
            using OnityContainer child = new OnityContainer(parent);

            parent.Bind<ITestContract>().To<ParentContract>().AsSingle();
            child.Bind<ITestContract>().To<ChildContract>().AsSingle();

            ITestContract parentResolved = parent.Resolve<ITestContract>();
            ITestContract childResolved = child.Resolve<ITestContract>();

            Assert.That(parentResolved, Is.TypeOf<ParentContract>());
            Assert.That(childResolved, Is.TypeOf<ChildContract>());
        }

        [Test]
        public void BindInterfacesAndSelfTo_NoInterfaces_StillBindsConcreteType()
        {
            using OnityContainer container = new OnityContainer();
            container.BindInterfacesAndSelfTo<NoInterfaceConcrete>().AsSingle();

            NoInterfaceConcrete first = container.Resolve<NoInterfaceConcrete>();
            NoInterfaceConcrete second = container.Resolve<NoInterfaceConcrete>();

            Assert.That(first, Is.Not.Null);
            Assert.That(first, Is.SameAs(second));
        }

        [Test]
        public void BindInterfacesTo_WithIdAndNoInterfaces_ThrowsOnityBindingException()
        {
            using OnityContainer container = new OnityContainer();

            Assert.That(
                () => container.BindInterfacesTo<NoInterfaceConcrete>().WithId("x").AsSingle(),
                Throws.TypeOf<OnityBindingException>());
        }

        [Test]
        public void Resolve_UnboundConcreteType_UsesImplicitTransientBinding()
        {
            using OnityContainer container = new OnityContainer();

            AutoConcreteTarget first = container.Resolve<AutoConcreteTarget>();
            AutoConcreteTarget second = container.Resolve<AutoConcreteTarget>();

            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.Not.Null);
            Assert.That(first, Is.Not.SameAs(second));
        }

        [Test]
        public void WithId_ResolvesMatchingBindingAndKeepsDefaultSeparate()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<ITestContract>().To<ParentContract>().AsSingle();
            container.Bind<ITestContract>().To<ChildContract>().WithId("blue").AsSingle();
            container.Build();

            string equalId = new string(new[] { 'b', 'l', 'u', 'e' });
            ITestContract keyed = container.Resolve<ITestContract>(equalId);

            Assert.That(container.Resolve<ITestContract>(), Is.TypeOf<ParentContract>());
            Assert.That(keyed, Is.TypeOf<ChildContract>());
            Assert.That(container.Resolve(typeof(ITestContract), "blue"), Is.SameAs(keyed));
            Assert.That(container.TryResolve("blue", out ITestContract found), Is.True);
            Assert.That(found, Is.SameAs(keyed));
            Assert.That(container.TryResolve("missing", out ITestContract missing), Is.False);
            Assert.That(missing, Is.Null);
            Assert.That(() => container.Resolve<ITestContract>("missing"),
                Throws.TypeOf<OnityResolveException>());
        }

        [Test]
        public void WithId_WarmedSingletonUpdatesAfterRebindAndRecordsDiagnostics()
        {
            bool originalDiagnostics = OnityContainer.DiagnosticsCollectionEnabled;
            try
            {
                OnityContainer.DiagnosticsCollectionEnabled = false;
                using OnityContainer container = new OnityContainer();
                container.Bind<ITestContract>().To<ParentContract>().WithId("slot").AsSingle();
                container.Build();

                ITestContract first = container.Resolve<ITestContract>("slot");
                Assert.That(container.Resolve<ITestContract>("slot"), Is.SameAs(first));

                container.Rebind<ITestContract>("slot").To<ChildContract>().AsSingle();
                ITestContract replacement = container.Resolve<ITestContract>("slot");
                Assert.That(replacement, Is.TypeOf<ChildContract>());
                Assert.That(replacement, Is.Not.SameAs(first));
                Assert.That(container.Resolve<ITestContract>("slot"), Is.SameAs(replacement));

                OnityContainer.DiagnosticsCollectionEnabled = true;
                _ = container.Resolve<ITestContract>("slot");
                _ = container.Resolve<ITestContract>("slot");
                List<OnityBindingDiagnostics> rows = new List<OnityBindingDiagnostics>();
                container.GetBindingDiagnostics(rows);
                Assert.That(rows.Exists(row => row.ImplementationType == typeof(ChildContract)
                    && row.ResolveCount >= 2), Is.True);
            }
            finally
            {
                OnityContainer.DiagnosticsCollectionEnabled = originalDiagnostics;
            }
        }

        [Test]
        public void WithId_LaterConditionalBindingPreservesUnconditionalResolve()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<ITestContract>().To<ParentContract>().WithId("slot").AsSingle();
            container.Build();

            ITestContract original = container.Resolve<ITestContract>("slot");
            container.Bind<ITestContract>().To<ChildContract>().WithId("slot")
                .WhenInjectedInto<DerivedConditionalConsumer>().AsSingle();

            Assert.That(container.Resolve<ITestContract>("slot"), Is.SameAs(original));
        }

        [Test]
        public void Inject_UsesIdsOnConstructorFieldPropertyAndMethodParameters()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<ITestContract>().To<ParentContract>().WithId("red").AsSingle();
            container.Bind<ITestContract>().To<ChildContract>().WithId("blue").AsSingle();
            container.Bind<KeyedInjectionTarget>().AsTransient();
            container.Build();

            KeyedInjectionTarget target = container.Resolve<KeyedInjectionTarget>();
            ITestContract red = container.Resolve<ITestContract>("red");
            ITestContract blue = container.Resolve<ITestContract>("blue");

            Assert.That(target.ConstructorValue, Is.SameAs(red));
            Assert.That(target.FieldValue, Is.SameAs(blue));
            Assert.That(target.PropertyValue, Is.SameAs(blue));
            Assert.That(target.MethodValue, Is.SameAs(red));
        }

        [Test]
        public void ConditionalBinding_MatchesDerivedConsumerAndRejectsAmbiguity()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<ITestContract>().To<ParentContract>().AsSingle();
            container.Bind<ITestContract>().To<ChildContract>()
                .WhenInjectedInto<BaseConditionalConsumer>().AsSingle();
            container.Bind<DerivedConditionalConsumer>().AsTransient();

            Assert.That(container.Resolve<ITestContract>(), Is.TypeOf<ParentContract>());
            Assert.That(container.Resolve<DerivedConditionalConsumer>().Dependency,
                Is.TypeOf<ChildContract>());

            container.Bind<ITestContract>().To<ParentContract>()
                .WhenInjectedInto<DerivedConditionalConsumer>().AsSingle();
            Assert.That(() => container.Resolve<DerivedConditionalConsumer>(),
                Throws.TypeOf<OnityResolveException>());
        }

        [Test]
        public void UnbindAndRebind_KeyedChildBindingLeavesParentUnchanged()
        {
            using OnityContainer parent = new OnityContainer();
            parent.Bind<ITestContract>().To<ParentContract>().WithId("slot").AsSingle();
            using OnityContainer child = new OnityContainer(parent);
            child.Bind<ITestContract>().To<ChildContract>().WithId("slot").AsSingle();

            ITestContract parentValue = parent.Resolve<ITestContract>("slot");
            Assert.That(child.Resolve<ITestContract>("slot"), Is.TypeOf<ChildContract>());
            Assert.That(child.Unbind<ITestContract>("slot"), Is.True);
            Assert.That(child.Resolve<ITestContract>("slot"), Is.SameAs(parentValue));
            Assert.That(child.Unbind<ITestContract>("slot"), Is.False);

            child.Rebind<ITestContract>("slot").To<ChildContract>().AsSingle();
            Assert.That(child.Resolve<ITestContract>("slot"), Is.TypeOf<ChildContract>());
            Assert.That(parent.Resolve<ITestContract>("slot"), Is.SameAs(parentValue));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AsScoped_ReusesPerRequesterAndDisposesWithThatScope(bool useBaked)
        {
            PropertyInfo bakedFlag = typeof(OnityContainer).GetProperty(
                "UseBakedResolve", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(bakedFlag, Is.Not.Null);
            bool originalFlag = (bool)bakedFlag.GetValue(null);

            try
            {
                bakedFlag.SetValue(null, useBaked);
                using OnityContainer parent = new OnityContainer();
                parent.Bind<IScopeDependency>().To<RootScopeDependency>().AsSingle();
                parent.Bind<IScopedContract>().To<ScopedContract>().AsScoped();
                parent.Build();

                using OnityContainer firstChild = new OnityContainer(parent);
                firstChild.Bind<IScopeDependency>().To<ChildScopeDependency>().AsSingle();
                firstChild.Build();
                using OnityContainer secondChild = new OnityContainer(parent);
                secondChild.Bind<IScopeDependency>().To<ChildScopeDependency>().AsSingle();
                secondChild.Build();

                IScopedContract rootValue = parent.Resolve<IScopedContract>();
                IScopedContract first = firstChild.Resolve<IScopedContract>();
                IScopedContract second = secondChild.Resolve<IScopedContract>();

                Assert.That(firstChild.Resolve<IScopedContract>(), Is.SameAs(first));
                Assert.That(parent.Resolve<IScopedContract>(), Is.SameAs(rootValue));
                Assert.That(first, Is.Not.SameAs(rootValue));
                Assert.That(first, Is.Not.SameAs(second));
                Assert.That(rootValue.Dependency, Is.TypeOf<RootScopeDependency>());
                Assert.That(first.Dependency, Is.TypeOf<ChildScopeDependency>());

                firstChild.Dispose();
                Assert.That(first.IsDisposed, Is.True);
                Assert.That(second.IsDisposed, Is.False);
                Assert.That(rootValue.IsDisposed, Is.False);

                secondChild.Dispose();
                Assert.That(second.IsDisposed, Is.True);
                Assert.That(rootValue.IsDisposed, Is.False);
                parent.Dispose();
                Assert.That(rootValue.IsDisposed, Is.True);
            }
            finally
            {
                bakedFlag.SetValue(null, originalFlag);
            }
        }

        [Test]
        public void AsScoped_KeyedAndOpenGenericBindingsFollowRequestingScope()
        {
            using OnityContainer parent = new OnityContainer();
            parent.Bind<IScopedContract>().To<ScopedContract>().WithId("session").AsScoped();
            parent.Bind<IScopeDependency>().To<RootScopeDependency>().AsSingle();
            parent.Bind(typeof(IKeyedRepository<>)).To(typeof(DefaultRepository<>)).AsScoped();
            parent.Build();

            using OnityContainer firstChild = new OnityContainer(parent);
            using OnityContainer secondChild = new OnityContainer(parent);

            IScopedContract firstKeyed = firstChild.Resolve<IScopedContract>("session");
            Assert.That(firstChild.Resolve<IScopedContract>("session"), Is.SameAs(firstKeyed));
            Assert.That(secondChild.Resolve<IScopedContract>("session"), Is.Not.SameAs(firstKeyed));

            IKeyedRepository<int> firstGeneric = firstChild.Resolve<IKeyedRepository<int>>();
            Assert.That(firstChild.Resolve<IKeyedRepository<int>>(), Is.SameAs(firstGeneric));
            Assert.That(secondChild.Resolve<IKeyedRepository<int>>(), Is.Not.SameAs(firstGeneric));
        }

        [Test]
        public void FromSubContainerResolve_ExportsLocalBindingPerRequestingScope()
        {
            using OnityContainer parent = new OnityContainer();
            parent.Bind<IScopeDependency>().To<RootScopeDependency>().AsSingle();
            parent.Bind<IScopedContract>().FromSubContainerResolve(
                scope => scope.Bind<IScopedContract>().To<ScopedContract>().AsScoped());
            parent.Build();

            using OnityContainer child = new OnityContainer(parent);
            child.Bind<IScopeDependency>().To<ChildScopeDependency>().AsSingle();
            child.Build();

            IScopedContract rootValue = parent.Resolve<IScopedContract>();
            IScopedContract childValue = child.Resolve<IScopedContract>();

            Assert.That(parent.Resolve<IScopedContract>(), Is.SameAs(rootValue));
            Assert.That(child.Resolve<IScopedContract>(), Is.SameAs(childValue));
            Assert.That(childValue, Is.Not.SameAs(rootValue));
            Assert.That(rootValue.Dependency, Is.TypeOf<RootScopeDependency>());
            Assert.That(childValue.Dependency, Is.TypeOf<ChildScopeDependency>());

            child.Dispose();
            Assert.That(childValue.IsDisposed, Is.True);
            Assert.That(rootValue.IsDisposed, Is.False);
            parent.Dispose();
            Assert.That(rootValue.IsDisposed, Is.True);
        }

        [Test]
        public void FromSubContainerResolve_ForwardsTickToInstalledChild()
        {
            using OnityContainer parent = new OnityContainer();
            parent.Bind<ITickContract>().FromSubContainerResolve(
                scope => scope.Bind<ITickContract>().To<TickContractB>().AsSingle());
            parent.Build();

            TickContractB exported = (TickContractB)parent.Resolve<ITickContract>();
            parent.Tick();

            Assert.That(exported.TickCount, Is.EqualTo(1));
            parent.Dispose();
            Assert.That(exported.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public void FromSubContainerResolve_RequiresLocalChildBinding()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<ITestContract>().FromSubContainerResolve(_ => { });
            container.Build();

            Assert.That(() => container.Resolve<ITestContract>(),
                Throws.TypeOf<OnityBindingException>());
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void OpenGenericConditional_UsesConsumerBindingRegardlessOfDefaultWarmup(
            bool useBaked, bool warmDefault)
        {
            PropertyInfo bakedFlag = typeof(OnityContainer).GetProperty(
                "UseBakedResolve", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(bakedFlag, Is.Not.Null);
            bool originalFlag = (bool)bakedFlag.GetValue(null);

            try
            {
                bakedFlag.SetValue(null, useBaked);
                using OnityContainer container = new OnityContainer();
                container.Bind(typeof(IKeyedRepository<>)).To(typeof(DefaultRepository<>)).AsSingle();
                container.Bind(typeof(IKeyedRepository<>)).To(typeof(ConditionalRepository<>))
                    .WhenInjectedInto<RepositoryConsumer>().AsSingle();
                container.Bind<RepositoryConsumer>().AsTransient();
                container.Build();

                if (warmDefault)
                {
                    Assert.That(container.Resolve<IKeyedRepository<int>>(), Is.TypeOf<DefaultRepository<int>>());
                }

                Assert.That(container.Resolve<RepositoryConsumer>().Repository,
                    Is.TypeOf<ConditionalRepository<int>>());
                Assert.That(container.Resolve<IKeyedRepository<int>>(), Is.TypeOf<DefaultRepository<int>>());
                Assert.That(container.Resolve<RepositoryConsumer>().Repository,
                    Is.TypeOf<ConditionalRepository<int>>());
            }
            finally
            {
                bakedFlag.SetValue(null, originalFlag);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OpenGenericCollection_IncludesDefaultAndConditionalBeforeAndAfterWarmup(bool warmDefault)
        {
            using OnityContainer container = new OnityContainer();
            container.Bind(typeof(IKeyedRepository<>)).To(typeof(DefaultRepository<>)).AsSingle();
            container.Bind(typeof(IKeyedRepository<>)).To(typeof(ConditionalRepository<>))
                .WhenInjectedInto<RepositoryCollectionConsumer>().AsSingle();
            container.Bind<RepositoryCollectionConsumer>().AsTransient();
            container.Build();

            if (warmDefault)
            {
                container.Resolve<IKeyedRepository<int>>();
            }

            RepositoryCollectionConsumer consumer = container.Resolve<RepositoryCollectionConsumer>();
            Assert.That(consumer.Repositories, Has.Count.EqualTo(2));
            Assert.That(consumer.Repositories[0], Is.TypeOf<DefaultRepository<int>>());
            Assert.That(consumer.Repositories[1], Is.TypeOf<ConditionalRepository<int>>());
        }

        [Test]
        public void Rebind_AfterBuild_StopsOldTickableAndStartsReplacement()
        {
            OnityContainer container = new OnityContainer();
            try
            {
                container.Bind<ITickContract>().To<TickContractA>().AsSingle();
                container.Build();
                TickContractA old = (TickContractA)container.Resolve<ITickContract>();
                container.Tick();

                container.Rebind<ITickContract>().To<TickContractB>().AsSingle();
                TickContractB replacement = (TickContractB)container.Resolve<ITickContract>();
                container.Tick();

                Assert.That(old.TickCount, Is.EqualTo(1));
                Assert.That(replacement.TickCount, Is.EqualTo(1));
                Assert.That(old.DisposeCount, Is.Zero);
            }
            finally
            {
                container.Dispose();
            }
        }

        private interface ITestContract
        {
        }

        private abstract class AbstractContract
        {
        }

        private interface IDisposableContract
        {
            bool IsDisposed { get; }
        }

        private sealed class ParentContract : ITestContract
        {
        }

        private sealed class ChildContract : ITestContract
        {
        }

        private sealed class TestDependency
        {
        }

        private sealed class CircularA
        {
            public CircularA(CircularB dependency)
            {
            }
        }

        private sealed class CircularB
        {
            public CircularB(CircularA dependency)
            {
            }
        }

        private sealed class MultipleInjectConstructorsTarget
        {
            [Inject]
            public MultipleInjectConstructorsTarget()
            {
            }

            [Inject]
            public MultipleInjectConstructorsTarget(TestDependency dependency)
            {
            }
        }

        private sealed class InjectPropertyWithoutSetterTarget
        {
            [Inject]
            public TestDependency Dependency { get; }
        }

        private sealed class InjectIndexerPropertyTarget
        {
            [Inject]
            public TestDependency this[int index]
            {
                get => null;
                set
                {
                }
            }
        }

        private sealed class InjectGenericMethodTarget
        {
            [Inject]
            private void Initialize<TValue>(TValue value)
            {
            }
        }

        private sealed class DisposableContract : IDisposableContract, IDisposable
        {
            public bool IsDisposed { get; private set; }

            public void Dispose()
            {
                IsDisposed = true;
            }
        }

        private sealed class NoInterfaceConcrete
        {
        }

        private sealed class AutoConcreteTarget
        {
        }

        private sealed class KeyedInjectionTarget
        {
            [Inject(Id = "blue")]
            private ITestContract m_fieldValue;

            [Inject]
            public KeyedInjectionTarget([Inject(Id = "red")] ITestContract constructorValue)
            {
                ConstructorValue = constructorValue;
            }

            public ITestContract ConstructorValue { get; }

            public ITestContract FieldValue => m_fieldValue;

            [Inject(Id = "blue")]
            public ITestContract PropertyValue { get; set; }

            public ITestContract MethodValue { get; private set; }

            [Inject]
            private void SetMethod([Inject(Id = "red")] ITestContract value)
            {
                MethodValue = value;
            }
        }

        private class BaseConditionalConsumer
        {
        }

        private sealed class DerivedConditionalConsumer : BaseConditionalConsumer
        {
            public DerivedConditionalConsumer(ITestContract dependency)
            {
                Dependency = dependency;
            }

            public ITestContract Dependency { get; }
        }

        private interface IScopeDependency
        {
        }

        private sealed class RootScopeDependency : IScopeDependency
        {
        }

        private sealed class ChildScopeDependency : IScopeDependency
        {
        }

        private interface IScopedContract
        {
            IScopeDependency Dependency { get; }
            bool IsDisposed { get; }
        }

        private sealed class ScopedContract : IScopedContract, IDisposable
        {
            public ScopedContract(IScopeDependency dependency)
            {
                Dependency = dependency;
            }

            public IScopeDependency Dependency { get; }

            public bool IsDisposed { get; private set; }

            public void Dispose()
            {
                IsDisposed = true;
            }
        }

        private interface IKeyedRepository<T>
        {
        }

        private sealed class DefaultRepository<T> : IKeyedRepository<T>
        {
        }

        private sealed class ConditionalRepository<T> : IKeyedRepository<T>
        {
        }

        private sealed class RepositoryConsumer
        {
            public RepositoryConsumer(IKeyedRepository<int> repository)
            {
                Repository = repository;
            }

            public IKeyedRepository<int> Repository { get; }
        }

        private sealed class RepositoryCollectionConsumer
        {
            public RepositoryCollectionConsumer(IEnumerable<IKeyedRepository<int>> repositories)
            {
                Repositories = new List<IKeyedRepository<int>>(repositories);
            }

            public List<IKeyedRepository<int>> Repositories { get; }
        }

        private interface ITickContract
        {
        }

        private sealed class TickContractA : ITickContract, IOnityTickable, IDisposable
        {
            public int TickCount { get; private set; }
            public int DisposeCount { get; private set; }

            public void Tick()
            {
                TickCount++;
            }

            public void Dispose()
            {
                DisposeCount++;
            }
        }

        private sealed class TickContractB : ITickContract, IOnityTickable, IDisposable
        {
            public int TickCount { get; private set; }
            public int DisposeCount { get; private set; }

            public void Tick()
            {
                TickCount++;
            }

            public void Dispose()
            {
                DisposeCount++;
            }
        }
    }
}
