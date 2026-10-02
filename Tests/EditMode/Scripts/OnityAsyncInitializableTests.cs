using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.DI;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Covers <see cref="IOnityAsyncInitializable" />: collection, ordering after the sync lifecycle
    /// and the async build callbacks, one-at-a-time execution, scope cancellation, retry, and the
    /// rule that each step starts on the context the build started on.
    /// </summary>
    [TestFixture]
    public sealed class OnityAsyncInitializableTests
    {
        [SetUp]
        public void SetUp()
        {
            OnityContainer.DiagnosticsCollectionEnabled = false;
            TransientInitializer.CallCount = 0;
        }

        [Test]
        public async Task BuildAsync_RunsInitializeThenCallbacksThenAsyncInitializers()
        {
            List<string> order = new List<string>();
            using OnityContainer container = new OnityContainer();
            container.BindInstance(order);
            container.Bind<SyncAndAsyncService>().AsSingle();
            container.RegisterBuildCallbackAsync(
                async (_, _) =>
                {
                    await Task.Yield();
                    order.Add("callback");
                });

            await container.BuildAsync();

            Assert.That(order, Is.EqualTo(new[] { "initialize", "callback", "initialize-async" }));
        }

        [Test]
        public async Task AsyncInitializers_RunOneAtATimeInRegistrationOrder()
        {
            List<string> order = new List<string>();
            using OnityContainer container = new OnityContainer();
            container.BindInstance(order);
            container.Bind<YieldingInitializerA>().AsSingle();
            container.Bind<YieldingInitializerB>().AsSingle();
            container.BindInstance(new YieldingInitializerC(order));

            await container.BuildAsync();

            Assert.That(order, Is.EqualTo(new[] { "A+", "A-", "B+", "B-", "C+", "C-" }));
        }

        [Test]
        public async Task Dispose_CancelsRunningAsyncInitializer()
        {
            OnityContainer container = new OnityContainer();
            container.Bind<BlockingInitializer>().AsSingle();

            Task build = container.BuildAsync();
            BlockingInitializer initializer = container.Resolve<BlockingInitializer>();

            Assert.That(initializer.IsStarted, Is.True);
            Assert.That(build.IsCompleted, Is.False);
            Assert.That(initializer.Token, Is.EqualTo(container.LifetimeToken));

            container.Dispose();

            try
            {
                await build;
                Assert.Fail("Expected the build to end canceled.");
            }
            catch (OperationCanceledException)
            {
            }

            Assert.That(initializer.WasCanceled, Is.True);
        }

        [Test]
        public async Task AsyncInitializer_CallerCancellation_ReportsCallerToken()
        {
            using OnityContainer container = new OnityContainer();
            using CancellationTokenSource caller = new CancellationTokenSource();
            container.Bind<BlockingInitializer>().AsSingle();

            Task build = container.BuildAsync(caller.Token);
            caller.Cancel();

            try
            {
                await build;
                Assert.Fail("Expected the build to end canceled.");
            }
            catch (OperationCanceledException exception)
            {
                Assert.That(exception.CancellationToken, Is.EqualTo(caller.Token));
            }

            Assert.That(container.Resolve<BlockingInitializer>().WasCanceled, Is.True);
        }

        [Test]
        public async Task FaultingAsyncInitializer_SurfacesThroughBuildAsync_AndResumesOnRetry()
        {
            using OnityContainer container = new OnityContainer();
            int callbackCount = 0;
            container.RegisterBuildCallbackAsync(
                (_, _) =>
                {
                    callbackCount++;
                    return Task.CompletedTask;
                });
            container.Bind<CountingInitializer>().AsSingle();
            container.Bind<FailOnceInitializer>().AsSingle();

            try
            {
                await container.BuildAsync();
                Assert.Fail("Expected the async initializer fault.");
            }
            catch (InvalidOperationException)
            {
            }

            await container.BuildAsync();

            Assert.That(callbackCount, Is.EqualTo(1));
            Assert.That(container.Resolve<CountingInitializer>().CallCount, Is.EqualTo(1));
            Assert.That(container.Resolve<FailOnceInitializer>().CallCount, Is.EqualTo(2));
        }

        [Test]
        public async Task TransientAsyncInitializer_IsNotCollected()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<TransientInitializer>().AsTransient();

            await container.BuildAsync();
            _ = container.Resolve<TransientInitializer>();

            Assert.That(TransientInitializer.CallCount, Is.EqualTo(0));
        }

        [Test]
        public async Task ChildScope_RunsOnlyItsOwnAsyncInitializers()
        {
            using OnityContainer parent = new OnityContainer();
            parent.Bind<CountingInitializer>().AsSingle();
            await parent.BuildAsync();

            using OnityContainer child = new OnityContainer(parent);
            child.Bind<LateInitializer>().AsSingle();
            await child.BuildAsync();

            Assert.That(parent.Resolve<CountingInitializer>().CallCount, Is.EqualTo(1));
            Assert.That(child.Resolve<LateInitializer>().CallCount, Is.EqualTo(1));
        }

        [Test]
        public async Task AsyncInitializerBoundAfterCompletedRun_RunsOnNextBuildAsync()
        {
            using OnityContainer container = new OnityContainer();
            CountingInitializer first = new CountingInitializer();
            container.BindInstance(first);
            await container.BuildAsync();

            LateInitializer late = new LateInitializer();
            container.BindInstance(late);

            Assert.That(late.CallCount, Is.EqualTo(0));

            await container.BuildAsync();

            Assert.That(first.CallCount, Is.EqualTo(1));
            Assert.That(late.CallCount, Is.EqualTo(1));
        }

        [Test]
        public async Task AsyncBuildSteps_StartOnTheContextTheBuildStartedOn()
        {
            Assume.That(
                SynchronizationContext.Current,
                Is.Not.Null,
                "Requires the Editor's main-thread synchronization context.");
            int mainThreadId = Thread.CurrentThread.ManagedThreadId;
            List<int> threads = new List<int>();

            using OnityContainer container = new OnityContainer();
            container.RegisterBuildCallbackAsync(
                async (_, _) => await Task.Run(() => { }).ConfigureAwait(false));
            container.RegisterBuildCallbackAsync(
                (_, _) =>
                {
                    threads.Add(Thread.CurrentThread.ManagedThreadId);
                    return Task.CompletedTask;
                });
            container.BindInstance(new WorkerCompletingInitializer());
            container.BindInstance(new ThreadRecordingInitializer(threads));

            await container.BuildAsync();

            Assert.That(threads, Is.EqualTo(new[] { mainThreadId, mainThreadId }));
        }

        private sealed class SyncAndAsyncService : IOnityInitializable, IOnityAsyncInitializable
        {
            private readonly List<string> m_order;

            public SyncAndAsyncService(List<string> order)
            {
                m_order = order;
            }

            public void Initialize()
            {
                m_order.Add("initialize");
            }

            public ValueTask InitializeAsync(CancellationToken cancellationToken)
            {
                m_order.Add("initialize-async");
                return default;
            }
        }

        private abstract class YieldingInitializer : IOnityAsyncInitializable
        {
            private readonly List<string> m_order;
            private readonly string m_name;

            protected YieldingInitializer(List<string> order, string name)
            {
                m_order = order;
                m_name = name;
            }

            public async ValueTask InitializeAsync(CancellationToken cancellationToken)
            {
                m_order.Add(m_name + "+");
                await Task.Yield();
                m_order.Add(m_name + "-");
            }
        }

        private sealed class YieldingInitializerA : YieldingInitializer
        {
            public YieldingInitializerA(List<string> order)
                : base(order, "A")
            {
            }
        }

        private sealed class YieldingInitializerB : YieldingInitializer
        {
            public YieldingInitializerB(List<string> order)
                : base(order, "B")
            {
            }
        }

        private sealed class YieldingInitializerC : YieldingInitializer
        {
            public YieldingInitializerC(List<string> order)
                : base(order, "C")
            {
            }
        }

        private sealed class BlockingInitializer : IOnityAsyncInitializable
        {
            public bool IsStarted { get; private set; }

            public bool WasCanceled { get; private set; }

            public CancellationToken Token { get; private set; }

            public async ValueTask InitializeAsync(CancellationToken cancellationToken)
            {
                Token = cancellationToken;
                IsStarted = true;

                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    WasCanceled = true;
                    throw;
                }
            }
        }

        private class CountingInitializer : IOnityAsyncInitializable
        {
            public int CallCount { get; private set; }

            public ValueTask InitializeAsync(CancellationToken cancellationToken)
            {
                CallCount++;
                return default;
            }
        }

        private sealed class LateInitializer : CountingInitializer
        {
        }

        private sealed class FailOnceInitializer : IOnityAsyncInitializable
        {
            public int CallCount { get; private set; }

            public ValueTask InitializeAsync(CancellationToken cancellationToken)
            {
                CallCount++;

                if (CallCount == 1)
                {
                    throw new InvalidOperationException("Initialization failure");
                }

                return default;
            }
        }

        private sealed class TransientInitializer : IOnityAsyncInitializable
        {
            public static int CallCount;

            public ValueTask InitializeAsync(CancellationToken cancellationToken)
            {
                CallCount++;
                return default;
            }
        }

        private sealed class WorkerCompletingInitializer : IOnityAsyncInitializable
        {
            public async ValueTask InitializeAsync(CancellationToken cancellationToken)
            {
                await Task.Run(() => { }, cancellationToken).ConfigureAwait(false);
            }
        }

        private sealed class ThreadRecordingInitializer : IOnityAsyncInitializable
        {
            private readonly List<int> m_threads;

            public ThreadRecordingInitializer(List<int> threads)
            {
                m_threads = threads;
            }

            public ValueTask InitializeAsync(CancellationToken cancellationToken)
            {
                m_threads.Add(Thread.CurrentThread.ManagedThreadId);
                return default;
            }
        }
    }
}
