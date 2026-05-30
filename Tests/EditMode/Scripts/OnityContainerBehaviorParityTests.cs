using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.DI;

namespace Onity.Tests.EditMode
{
    [TestFixture]
    public sealed class OnityContainerBehaviorParityTests
    {
        [Test]
        public void Bind_RebindStyle_LastBindingWins()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<ITestContract>().To<FirstImplementation>().AsSingle();
            container.Bind<ITestContract>().To<SecondImplementation>().AsSingle();

            ITestContract resolved = container.Resolve<ITestContract>();

            Assert.That(resolved, Is.TypeOf<SecondImplementation>());
        }

        [Test]
        public async Task BuildAsync_CancelThenRetry_CompletesOnRetry()
        {
            using OnityContainer container = new OnityContainer();
            int callbackCount = 0;

            container.RegisterBuildCallbackAsync(
                async (_, cancellationToken) =>
                {
                    await Task.Delay(1, cancellationToken);
                    callbackCount++;
                });

            using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            cancellationTokenSource.Cancel();

            try
            {
                await container.BuildAsync(cancellationTokenSource.Token);
                Assert.Fail("Expected OperationCanceledException.");
            }
            catch (OperationCanceledException)
            {
            }

            await container.BuildAsync();
            Assert.That(callbackCount, Is.EqualTo(1));
        }

        [Test]
        public void Resolve_DerivedType_CallsPrivateInjectMethodInBase()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<BaseDependency>().AsSingle();
            container.Bind<DerivedWithPrivateBaseInject>().AsTransient();

            DerivedWithPrivateBaseInject instance = container.Resolve<DerivedWithPrivateBaseInject>();

            Assert.That(instance.WasBaseInjectCalled, Is.True);
        }

        [Test]
        public void Resolve_DerivedType_CallsBaseInjectBeforeDerivedInject()
        {
            using OnityContainer container = new OnityContainer();
            InjectionOrderCounter.Reset();
            container.Bind<OrderedInjectDerived>().AsTransient();

            OrderedInjectDerived instance = container.Resolve<OrderedInjectDerived>();

            Assert.That(instance.BaseOrder, Is.EqualTo(0));
            Assert.That(instance.DerivedOrder, Is.EqualTo(1));
        }

        [Test]
        public void Resolve_VirtualInjectMethodOverrideWithoutAttribute_IsInvokedOnce()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<VirtualInjectDerived>().AsTransient();

            VirtualInjectDerived instance = container.Resolve<VirtualInjectDerived>();

            Assert.That(instance.InjectCallCount, Is.EqualTo(1));
        }

        [Test]
        public void Resolve_WithoutInjectConstructor_PrefersPublicConstructor()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<ConstructorChoiceTarget>().AsTransient();

            ConstructorChoiceTarget instance = container.Resolve<ConstructorChoiceTarget>();

            Assert.That(instance.UsedPublicConstructor, Is.True);
            Assert.That(instance.Dependency, Is.Null);
        }

        [Test]
        public void Resolve_WithoutInjectConstructor_ChoosesPublicCtorWithMostParameters()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<BaseDependency>().AsSingle();
            container.Bind<ParameterPriorityTarget>().AsTransient();

            ParameterPriorityTarget instance = container.Resolve<ParameterPriorityTarget>();

            Assert.That(instance.ConstructorKind, Is.EqualTo("with-dependency"));
            Assert.That(instance.Dependency, Is.Not.Null);
        }

        [Test]
        public void Resolve_VirtualInjectMethodOverrideWithAttribute_IsInvokedOnce()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<VirtualInjectDerivedWithAttribute>().AsTransient();

            VirtualInjectDerivedWithAttribute instance = container.Resolve<VirtualInjectDerivedWithAttribute>();

            Assert.That(instance.InjectCallCount, Is.EqualTo(1));
        }

        [Test]
        public void BindInterfacesAndSelfTo_AsTransient_ProducesFreshInstancesPerResolve()
        {
            using OnityContainer container = new OnityContainer();
            container.BindInterfacesAndSelfTo<TransientMultiContractService>().AsTransient();

            ITestContract firstContract = container.Resolve<ITestContract>();
            ISecondaryContract firstSecondary = container.Resolve<ISecondaryContract>();
            TransientMultiContractService firstConcrete = container.Resolve<TransientMultiContractService>();
            ITestContract secondContract = container.Resolve<ITestContract>();

            Assert.That(firstContract, Is.Not.SameAs(firstSecondary));
            Assert.That(firstSecondary, Is.Not.SameAs(firstConcrete));
            Assert.That(firstContract, Is.Not.SameAs(firstConcrete));
            Assert.That(secondContract, Is.Not.SameAs(firstContract));
        }

        [Test]
        public async Task BuildAsync_MultipleAsyncCallbacks_RunInRegistrationOrder()
        {
            using OnityContainer container = new OnityContainer();
            List<int> callbackOrder = new List<int>(4);

            container.RegisterBuildCallbackAsync(
                async (_, _) =>
                {
                    await Task.Yield();
                    callbackOrder.Add(1);
                });
            container.RegisterBuildCallbackAsync(
                async (_, _) =>
                {
                    await Task.Yield();
                    callbackOrder.Add(2);
                });
            container.RegisterBuildCallbackAsync(
                async (_, _) =>
                {
                    await Task.Yield();
                    callbackOrder.Add(3);
                });

            await container.BuildAsync();

            Assert.That(callbackOrder.Count, Is.EqualTo(3));
            Assert.That(callbackOrder[0], Is.EqualTo(1));
            Assert.That(callbackOrder[1], Is.EqualTo(2));
            Assert.That(callbackOrder[2], Is.EqualTo(3));
        }

        [Test]
        public void PushBindingSource_NestedScopes_RecordsExpectedSources()
        {
            using OnityContainer container = new OnityContainer();

            using (container.PushBindingSource("RootInstaller"))
            {
                container.Bind<RootScopedService>().AsSingle();

                using (container.PushBindingSource("NestedInstaller"))
                {
                    container.Bind<NestedScopedService>().AsSingle();
                }

                container.Bind<RootScopedService2>().AsSingle();
            }

            bool rootFound = container.TryGetBindingSource(typeof(RootScopedService), out OnityBindingSourceInfo rootInfo);
            bool nestedFound = container.TryGetBindingSource(typeof(NestedScopedService), out OnityBindingSourceInfo nestedInfo);
            bool rootFound2 = container.TryGetBindingSource(typeof(RootScopedService2), out OnityBindingSourceInfo rootInfo2);

            Assert.That(rootFound, Is.True);
            Assert.That(nestedFound, Is.True);
            Assert.That(rootFound2, Is.True);

            Assert.That(rootInfo.SourceName, Is.EqualTo("RootInstaller"));
            Assert.That(nestedInfo.SourceName, Is.EqualTo("NestedInstaller"));
            Assert.That(rootInfo2.SourceName, Is.EqualTo("RootInstaller"));
        }

        [Test]
        public void Resolve_ImplicitConcreteType_CachesInjectionPlan()
        {
            using OnityContainer container = new OnityContainer();
            OnityContainerDiagnostics before = container.GetDiagnostics();

            _ = container.Resolve<PlanCacheProbe>();
            _ = container.Resolve<PlanCacheProbe>();

            OnityContainerDiagnostics after = container.GetDiagnostics();

            Assert.That(before.CachedPlanCount, Is.EqualTo(0));
            Assert.That(after.CachedPlanCount, Is.EqualTo(1));
            Assert.That(after.ImplicitBindingCount, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void Dispose_CanBeCalledMultipleTimes()
        {
            OnityContainer container = new OnityContainer();
            container.Dispose();

            Assert.That(() => container.Dispose(), Throws.Nothing);
        }

        private interface ITestContract
        {
        }

        private sealed class FirstImplementation : ITestContract
        {
        }

        private sealed class SecondImplementation : ITestContract
        {
        }

        private sealed class BaseDependency
        {
        }

        private class BaseWithPrivateInject
        {
            public bool WasBaseInjectCalled { get; private set; }

            [Inject]
            private void InitializeBase(BaseDependency dependency)
            {
                WasBaseInjectCalled = dependency != null;
            }
        }

        private sealed class DerivedWithPrivateBaseInject : BaseWithPrivateInject
        {
        }

        private class OrderedInjectBase
        {
            public int BaseOrder { get; private set; } = -1;

            [Inject]
            private void InitializeBase()
            {
                BaseOrder = InjectionOrderCounter.Next();
            }
        }

        private sealed class OrderedInjectDerived : OrderedInjectBase
        {
            public int DerivedOrder { get; private set; } = -1;

            [Inject]
            private void InitializeDerived()
            {
                DerivedOrder = InjectionOrderCounter.Next();
            }
        }

        private class VirtualInjectBase
        {
            public int InjectCallCount { get; private set; }

            [Inject]
            protected virtual void OnInjected()
            {
                InjectCallCount++;
            }
        }

        private sealed class VirtualInjectDerived : VirtualInjectBase
        {
            protected override void OnInjected()
            {
                base.OnInjected();
            }
        }

        private sealed class VirtualInjectDerivedWithAttribute : VirtualInjectBase
        {
            [Inject]
            protected override void OnInjected()
            {
                base.OnInjected();
            }
        }

        private interface ISecondaryContract
        {
        }

        private sealed class TransientMultiContractService : ITestContract, ISecondaryContract
        {
        }

        private sealed class ConstructorChoiceTarget
        {
            public ConstructorChoiceTarget()
            {
                UsedPublicConstructor = true;
            }

            private ConstructorChoiceTarget(BaseDependency dependency)
            {
                UsedPublicConstructor = false;
                Dependency = dependency;
            }

            public BaseDependency Dependency { get; }

            public bool UsedPublicConstructor { get; }
        }

        private sealed class ParameterPriorityTarget
        {
            public ParameterPriorityTarget()
            {
                ConstructorKind = "parameterless";
            }

            public ParameterPriorityTarget(BaseDependency dependency)
            {
                ConstructorKind = "with-dependency";
                Dependency = dependency;
            }

            public string ConstructorKind { get; }

            public BaseDependency Dependency { get; }
        }

        private sealed class RootScopedService
        {
        }

        private sealed class NestedScopedService
        {
        }

        private sealed class RootScopedService2
        {
        }

        private sealed class PlanCacheProbe
        {
        }

        private static class InjectionOrderCounter
        {
            private static int s_order;

            public static void Reset()
            {
                s_order = 0;
            }

            public static int Next()
            {
                return s_order++;
            }
        }
    }
}
