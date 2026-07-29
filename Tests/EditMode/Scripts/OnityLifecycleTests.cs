using System;
using System.Collections.Generic;
using NUnit.Framework;
using Onity.DI;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Verifies the automatic entry-point lifecycle: a bound singleton (or instance)
    /// implementing <see cref="IOnityInitializable" /> is initialized automatically at
    /// <see cref="OnityContainer.Build" /> with no manual registration, and tickables
    /// are collected so the owning context's per-frame pumps
    /// (<see cref="OnityContainer.Tick" /> / <see cref="OnityContainer.FixedTick" /> /
    /// <see cref="OnityContainer.LateTick" />) dispatch to them. This is the
    /// "automatic like Zenject" convenience that VContainer makes manual.
    /// </summary>
    [TestFixture]
    public sealed class OnityLifecycleTests
    {
        private static readonly List<string> s_initOrder = new List<string>();

        [SetUp]
        public void SetUp()
        {
            OnityContainer.DiagnosticsCollectionEnabled = false;
            s_initOrder.Clear();
        }

        [Test]
        public void Initializable_RunsAutomaticallyAtBuild_ExactlyOnce()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<InitOnlyService>().AsSingle();

            container.Build();

            InitOnlyService service = container.Resolve<InitOnlyService>();
            Assert.That(service.InitializeCount, Is.EqualTo(1));
        }

        [Test]
        public void Initializable_DoesNotRunBeforeBuild()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<InitOnlyService>().AsSingle();

            // Resolving early creates the singleton but must not initialize it; only
            // Build() runs the lifecycle.
            InitOnlyService service = container.Resolve<InitOnlyService>();
            Assert.That(service.InitializeCount, Is.EqualTo(0));

            container.Build();
            Assert.That(service.InitializeCount, Is.EqualTo(1));
        }

        [Test]
        public void Initializable_RunsInRegistrationOrder()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<InitAlpha>().AsSingle();
            container.Bind<InitBeta>().AsSingle();

            container.Build();

            Assert.That(s_initOrder, Is.EqualTo(new[] { "A", "B" }));
        }

        [Test]
        public void Tickable_TickDispatchesToSingletonEachCall()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<FullLifecycleService>().AsSingle();

            container.Build();
            FullLifecycleService service = container.Resolve<FullLifecycleService>();
            Assert.That(service.TickCount, Is.EqualTo(0));

            container.Tick();
            container.Tick();
            container.Tick();

            Assert.That(service.TickCount, Is.EqualTo(3));
        }

        [Test]
        public void FixedAndLateTickables_DispatchOnTheirOwnPumps()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<FullLifecycleService>().AsSingle();

            container.Build();
            FullLifecycleService service = container.Resolve<FullLifecycleService>();

            container.FixedTick();
            container.FixedTick();
            container.LateTick();

            Assert.That(service.FixedTickCount, Is.EqualTo(2));
            Assert.That(service.LateTickCount, Is.EqualTo(1));
            Assert.That(service.TickCount, Is.EqualTo(0));
        }

        [Test]
        public void InitializableAndTickable_BothWireUpFromOneBinding()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<FullLifecycleService>().AsSingle();

            container.Build();
            FullLifecycleService service = container.Resolve<FullLifecycleService>();

            Assert.That(service.InitializeCount, Is.EqualTo(1));
            container.Tick();
            Assert.That(service.TickCount, Is.EqualTo(1));
        }

        [Test]
        public void Tickable_TransientBinding_IsNotCollected()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<FullLifecycleService>().AsTransient();

            container.Build();
            container.Tick();

            // A transient has no single stable instance to tick; a freshly resolved
            // one must show no ticks.
            FullLifecycleService service = container.Resolve<FullLifecycleService>();
            Assert.That(service.TickCount, Is.EqualTo(0));
            Assert.That(service.InitializeCount, Is.EqualTo(0));
        }

        [Test]
        public void Tickable_BoundToManyContractsViaInterfaces_TicksOnceNotPerContract()
        {
            using OnityContainer container = new OnityContainer();
            container.BindInterfacesAndSelfTo<MultiContractService>().AsSingle();

            container.Build();
            container.Tick();

            MultiContractService service = (MultiContractService)container.Resolve<IAlphaService>();
            Assert.That(service, Is.SameAs(container.Resolve<IBetaService>()));
            Assert.That(service.TickCount, Is.EqualTo(1));
        }

        [Test]
        public void DisposableLifecycle_DisposedOnContainerDispose()
        {
            OnityContainer container = new OnityContainer();
            container.Bind<DisposableInitService>().AsSingle();
            container.Build();

            DisposableInitService service = container.Resolve<DisposableInitService>();
            Assert.That(service.Initialized, Is.True);
            Assert.That(service.Disposed, Is.False);

            container.Dispose();
            Assert.That(service.Disposed, Is.True);
        }

        private sealed class InitOnlyService : IOnityInitializable
        {
            public int InitializeCount { get; private set; }

            public void Initialize()
            {
                InitializeCount++;
            }
        }

        private sealed class InitAlpha : IOnityInitializable
        {
            public void Initialize()
            {
                s_initOrder.Add("A");
            }
        }

        private sealed class InitBeta : IOnityInitializable
        {
            public void Initialize()
            {
                s_initOrder.Add("B");
            }
        }

        private sealed class FullLifecycleService
            : IOnityInitializable, IOnityTickable, IOnityFixedTickable, IOnityLateTickable
        {
            public int InitializeCount { get; private set; }

            public int TickCount { get; private set; }

            public int FixedTickCount { get; private set; }

            public int LateTickCount { get; private set; }

            public void Initialize()
            {
                InitializeCount++;
            }

            public void Tick()
            {
                TickCount++;
            }

            public void FixedTick()
            {
                FixedTickCount++;
            }

            public void LateTick()
            {
                LateTickCount++;
            }
        }

        private interface IAlphaService
        {
        }

        private interface IBetaService
        {
        }

        private sealed class MultiContractService : IAlphaService, IBetaService, IOnityTickable
        {
            public int TickCount { get; private set; }

            public void Tick()
            {
                TickCount++;
            }
        }

        private sealed class DisposableInitService : IOnityInitializable, IDisposable
        {
            public bool Initialized { get; private set; }

            public bool Disposed { get; private set; }

            public void Initialize()
            {
                Initialized = true;
            }

            public void Dispose()
            {
                Disposed = true;
            }
        }
    }
}
