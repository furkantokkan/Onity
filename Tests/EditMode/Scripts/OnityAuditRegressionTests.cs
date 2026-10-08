using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Core;
using Onity.DI;
using Onity.Editor.Validation;
using Onity.Pooling;
using Onity.Reactive;
using Onity.Unity.Contexts;
using Onity.Unity.Reactive;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Onity.Tests.EditMode
{
    [TestFixture]
    public sealed class OnityAuditRegressionTests
    {
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void Container_DisposalFromLocalTick_StopsTheCurrentPass(int pump)
        {
            using OnityContainer container = new OnityContainer();
            TickProbe<First> first = new TickProbe<First> { Callback = container.Dispose };
            TickProbe<Second> second = new TickProbe<Second>();
            container.BindInstance(first);
            container.BindInstance(second);
            container.Build();

            Assert.DoesNotThrow(() => Pump(container, pump));
            Pump(container, pump);

            Assert.That(first.TickCount, Is.EqualTo(1));
            Assert.That(second.TickCount, Is.Zero);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void Container_DisposalFromForwardedTick_StopsLaterChildren(int pump)
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<TickProbe<First>>().FromSubContainerResolve(
                child => child.Bind<TickProbe<First>>().AsSingle());
            container.Bind<TickProbe<Second>>().FromSubContainerResolve(
                child => child.Bind<TickProbe<Second>>().AsSingle());
            container.Build();
            TickProbe<First> first = container.Resolve<TickProbe<First>>();
            TickProbe<Second> second = container.Resolve<TickProbe<Second>>();
            first.Callback = container.Dispose;

            Assert.DoesNotThrow(() => Pump(container, pump));
            Assert.That(first.TickCount, Is.EqualTo(1));
            Assert.That(second.TickCount, Is.Zero);
        }

        [Test]
        public void Container_DisposalCancellationCannotReenterAnyTickPump()
        {
            using OnityContainer container = new OnityContainer();
            TickProbe<First> probe = new TickProbe<First>();
            container.BindInstance(probe);
            container.Build();
            using CancellationTokenRegistration registration = container.LifetimeToken.Register(
                () =>
                {
                    container.Tick();
                    container.FixedTick();
                    container.LateTick();
                });

            container.Dispose();

            Assert.That(probe.TickCount, Is.Zero);
        }

        [TestCase("before", false)]
        [TestCase("callback", false)]
        [TestCase("initializer", false)]
        [TestCase("after", false)]
        [TestCase("before", true)]
        [TestCase("callback", true)]
        [TestCase("initializer", true)]
        [TestCase("after", true)]
        public async Task InheritedScopedLifecycle_IsOwnedAndInitializedByResolvingChild(string stage, bool asyncBuild)
        {
            using OnityContainer parent = new OnityContainer();
            parent.Bind<ScopedLifecycle>().AsScoped();
            parent.Build();
            ScopedLifecycle parentService = parent.Resolve<ScopedLifecycle>();
            using OnityContainer child = new OnityContainer(parent);
            ScopedLifecycle service = null;
            Action resolve = () => service = child.Resolve<ScopedLifecycle>();

            if (stage == "before")
            {
                resolve();
                Assert.That(service.InitializeCount, Is.Zero);
            }
            else if (stage == "callback")
            {
                child.RegisterBuildCallback(_ => resolve());
            }
            else if (stage == "initializer")
            {
                child.BindInstance(new InitializationCallback(resolve));
            }

            if (asyncBuild)
            {
                await child.BuildAsync();
            }
            else
            {
                child.Build();
            }

            if (stage == "after")
            {
                resolve();
            }

            Assert.That(service, Is.Not.Null);
            Assert.That(service, Is.Not.SameAs(parentService));
            Assert.That(service.InitializeCount, Is.EqualTo(1));
            Assert.That(service.AsyncInitializeCount, Is.EqualTo(asyncBuild && stage != "after" ? 1 : 0));
            await child.BuildAsync();
            await child.BuildAsync();
            Assert.That(service.InitializeCount, Is.EqualTo(1));
            Assert.That(service.AsyncInitializeCount, Is.EqualTo(1));

            child.Tick();
            child.FixedTick();
            child.LateTick();
            Assert.That(service.TickCount, Is.EqualTo(3));
            Assert.That(parentService.TickCount, Is.Zero);
            child.Dispose();
            Assert.That(service.DisposeCount, Is.EqualTo(1));
            Assert.That(parentService.DisposeCount, Is.Zero);
        }

        [Test]
        public async Task InheritedScopedLifecycle_ResolvedDuringPendingAsyncBuild_JoinsItsQueue()
        {
            using OnityContainer parent = new OnityContainer();
            parent.Bind<ScopedLifecycle>().AsScoped();
            parent.Build();
            using OnityContainer child = new OnityContainer(parent);
            AsyncInitializationGate gate = new AsyncInitializationGate();
            child.BindInstance(gate);
            Task build = child.BuildAsync();
            Assert.That(build.IsCompleted, Is.False);

            ScopedLifecycle service = child.Resolve<ScopedLifecycle>();
            Assert.That(service.InitializeCount, Is.EqualTo(1));
            Assert.That(service.AsyncInitializeCount, Is.Zero);
            gate.Complete();
            await build;

            Assert.That(service.AsyncInitializeCount, Is.EqualTo(1));
            await child.BuildAsync();
            Assert.That(service.AsyncInitializeCount, Is.EqualTo(1));
        }

        [Test]
        public void Container_TickPumpsAllocateNothingAfterWarmup()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<TickProbe<First>>().AsSingle();
            container.Bind<TickProbe<Second>>().FromSubContainerResolve(
                child => child.Bind<TickProbe<Second>>().AsSingle());
            container.Build();
            container.Resolve<TickProbe<Second>>();
            for (int i = 0; i < 64; i++)
            {
                PumpAll(container);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 256; i++)
            {
                PumpAll(container);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void InheritedScopedLifecycle_LateInitializerDisposesOwner_ResolveRejectsDisposedResult()
        {
            using OnityContainer parent = new OnityContainer();
            parent.BindInstance<Action>(() => { });
            parent.Bind<DisposingInitializer>().AsScoped();
            parent.Build();
            using OnityContainer child = new OnityContainer(parent);
            child.BindInstance<Action>(child.Dispose);
            child.Build();

            Assert.Throws<OnityResolveException>(() => child.Resolve<DisposingInitializer>());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Pool_ThrowingDestroyHook_DrainsEveryRetainedItem(bool dispose)
        {
            List<object> destroyed = new List<object>();
            bool shouldThrow = true;
            using OnityObjectPool<object> pool = new OnityObjectPool<object>(
                () => new object(),
                actionOnDestroy: item =>
                {
                    destroyed.Add(item);
                    if (shouldThrow)
                    {
                        throw new InvalidOperationException("Destroy failure");
                    }
                },
                initialSize: 3);

            AggregateException failure = Assert.Throws<AggregateException>(() =>
            {
                if (dispose)
                {
                    pool.Dispose();
                }
                else
                {
                    pool.Clear();
                }
            });

            Assert.That(failure.InnerExceptions.Count, Is.EqualTo(3));
            Assert.That(new HashSet<object>(destroyed).Count, Is.EqualTo(3));
            Assert.That(pool.GetDiagnosticsSnapshot().CountInactive, Is.Zero);
            Assert.That(pool.CountAll, Is.Zero);
            Assert.That(pool.CountActive, Is.Zero);
            Assert.That(pool.CountInactive, Is.Zero);
            shouldThrow = false;
            if (dispose == false)
            {
                object next = pool.Get();
                pool.Release(next);
                pool.Clear();
                Assert.That(destroyed.Count, Is.EqualTo(4));
                Assert.That(pool.CountAll, Is.Zero);
                Assert.That(pool.CountActive, Is.Zero);
                Assert.That(pool.CountInactive, Is.Zero);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Pool_ClearRetainsItemsCreatedReentrantlyByDestroyHook(bool fixedSizeTryGet)
        {
            OnityObjectPool<object> pool = null;
            object replacement = null;
            int destroyCount = 0;
            pool = new OnityObjectPool<object>(
                () => new object(),
                actionOnDestroy: _ =>
                {
                    if (++destroyCount == 1)
                    {
                        pool.Clear();
                        Assert.That(pool.CountAll, Is.Zero);
                        Assert.That(pool.CountActive, Is.Zero);
                        Assert.That(pool.CountInactive, Is.Zero);
                        if (fixedSizeTryGet)
                        {
                            Assert.That(pool.TryGet(out replacement), Is.True,
                                "Clearing retained items must free fixed capacity before destroy callbacks run.");
                        }
                        else
                        {
                            replacement = pool.Get();
                        }

                        Assert.That(pool.CountAll, Is.EqualTo(1));
                        Assert.That(pool.CountActive, Is.EqualTo(1));
                        Assert.That(pool.CountInactive, Is.Zero);
                        pool.Release(replacement);
                    }
                },
                collectionCheck: true,
                maxSize: fixedSizeTryGet ? 2 : 1024,
                initialSize: 2,
                fixedSize: fixedSizeTryGet);
            using (pool)
            {
                pool.Clear();
                Assert.That(destroyCount, Is.EqualTo(2));
                Assert.That(pool.GetDiagnosticsSnapshot().CountInactive, Is.EqualTo(1));
                Assert.That(pool.GetDiagnosticsSnapshot().CountAll, Is.EqualTo(1));
                Assert.That(pool.CountAll, Is.EqualTo(1));
                Assert.That(pool.CountActive, Is.Zero);
                Assert.That(pool.CountInactive, Is.EqualTo(1));
                object retained = pool.Get();
                Assert.That(retained, Is.SameAs(replacement));
                Assert.That(pool.CountAll, Is.EqualTo(1));
                Assert.That(pool.CountActive, Is.EqualTo(1));
                Assert.That(pool.CountInactive, Is.Zero);
                pool.Release(retained);
            }

            Assert.That(destroyCount, Is.EqualTo(3));
            Assert.That(pool.CountAll, Is.Zero);
            Assert.That(pool.CountActive, Is.Zero);
            Assert.That(pool.CountInactive, Is.Zero);
        }

        [Test]
        public void Composite_ClearDetachesItsBatchBeforeReentrantClearAndAdd()
        {
            using CompositeDisposable bag = new CompositeDisposable();
            int firstDisposed = 0;
            int addedDisposed = 0;
            bag.Add(new DisposableAction(() => firstDisposed++));
            bag.Add(new DisposableAction(() =>
            {
                bag.Clear();
                bag.Add(new DisposableAction(() => addedDisposed++));
            }));

            Assert.DoesNotThrow(bag.Clear);
            Assert.That(firstDisposed, Is.EqualTo(1));
            Assert.That(addedDisposed, Is.Zero);
            Assert.That(bag.Count, Is.EqualTo(1));
            bag.Clear();
            Assert.That(addedDisposed, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Composite_ThrowingCleanup_DrainsTheBatchAndRetainsValidState(bool dispose)
        {
            using CompositeDisposable bag = new CompositeDisposable();
            int received = 0;
            using Subject<int> source = new Subject<int>();
            bag.Add(source.Subscribe(_ => received++));
            bag.Add(new DisposableAction(() => throw new InvalidOperationException("Cleanup failure")));
            Assert.Throws<AggregateException>(() =>
            {
                if (dispose)
                {
                    bag.Dispose();
                }
                else
                {
                    bag.Clear();
                }
            });

            source.OnNext(1);
            Assert.That(received, Is.Zero);
            Assert.That(bag.Count, Is.Zero);
            int nextDisposed = 0;
            bag.Add(new DisposableAction(() => nextDisposed++));
            Assert.That(nextDisposed, Is.EqualTo(dispose ? 1 : 0));
            bag.Dispose();
            Assert.That(nextDisposed, Is.EqualTo(1));
        }

        [TestCase("merge")]
        [TestCase("combine")]
        [TestCase("sample")]
        public void MultiSource_SubscribeFailureRollsBackEarlierSubscriptions(string operation)
        {
            SubscriptionProbe first = new SubscriptionProbe();
            InvalidOperationException expected = new InvalidOperationException("Subscribe failure");
            IOnityObservable<int> failing = new OnityObservable<int>(_ => throw expected);
            IOnityObservable<int> combined = Combine(first.Stream, failing, operation);

            Exception actual = Assert.Throws<InvalidOperationException>(() => combined.Subscribe(_ => { }));
            Assert.That(actual, Is.SameAs(expected));
            Assert.That(first.ActiveCount, Is.Zero);
            Assert.That(first.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public void Merge_ThirdSourceFailureRollsBackBothEarlierSubscriptions()
        {
            SubscriptionProbe first = new SubscriptionProbe();
            SubscriptionProbe second = new SubscriptionProbe();
            IOnityObservable<int> failing = new OnityObservable<int>(_ => throw new InvalidOperationException());
            Assert.Throws<InvalidOperationException>(() => first.Stream.Merge(second.Stream, failing).Subscribe(_ => { }));
            Assert.That(first.ActiveCount + second.ActiveCount, Is.Zero);
            Assert.That(first.DisposeCount, Is.EqualTo(1));
            Assert.That(second.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public void CombineLatest_ThrowingInitialSelectorRollsBackFirstSource()
        {
            SubscriptionProbe first = new SubscriptionProbe(1);
            using ReactiveProperty<int> second = new ReactiveProperty<int>(2);
            IOnityObservable<int> combined = first.Stream.CombineLatest<int, int, int>(
                second, (_, __) => throw new InvalidOperationException("Selector failure"));
            Assert.Throws<InvalidOperationException>(() => combined.Subscribe(_ => { }));
            Assert.That(first.ActiveCount, Is.Zero);
            Assert.That(first.DisposeCount, Is.EqualTo(1));
        }

        [TestCase("merge")]
        [TestCase("combine")]
        [TestCase("sample")]
        public void MultiSource_DisposalFailureStillReleasesEverySource(string operation)
        {
            SubscriptionProbe first = new SubscriptionProbe { ThrowOnDispose = true };
            SubscriptionProbe second = new SubscriptionProbe { ThrowOnDispose = true };
            IDisposable subscription = Combine(first.Stream, second.Stream, operation).Subscribe(_ => { });
            AggregateException failure = Assert.Throws<AggregateException>(subscription.Dispose);
            Assert.That(failure.InnerExceptions.Count, Is.EqualTo(2));
            Assert.That(first.ActiveCount + second.ActiveCount, Is.Zero);
            Assert.DoesNotThrow(subscription.Dispose);
        }

        [TestCase("cancellation")]
        [TestCase("token")]
        [TestCase("task")]
        public void TakeUntil_SignalBetweenConstructionAndSubscription_DoesNotSubscribe(string operation)
        {
            SubscriptionProbe source = new SubscriptionProbe(42);
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            TaskCompletionSource<bool> stop = new TaskCompletionSource<bool>();
            IOnityObservable<int> stream;
            if (operation == "task")
            {
                stream = source.Stream.TakeUntil(stop.Task);
                stop.SetResult(true);
            }
            else if (operation == "token")
            {
                stream = source.Stream.TakeUntil(cancellation.Token);
                cancellation.Cancel();
            }
            else
            {
                stream = source.Stream.TakeUntilCancellation(cancellation.Token);
                cancellation.Cancel();
            }

            int received = 0;
            using IDisposable subscription = stream.Subscribe(_ => received++);
            Assert.That(received, Is.Zero);
            Assert.That(source.SubscribeCount, Is.Zero);
        }

        [Test]
        public void TimedBuffer_ThrowingObserverReportsAndContinuesWithNextWindow()
        {
            SubscriptionProbe source = new SubscriptionProbe();
            ManualTimeProvider time = new ManualTimeProvider();
            List<int> received = new List<int>();
            List<Exception> errors = new List<Exception>();
            Action<Exception> previousHandler = OnityObservableExceptionHandler.Handler;
            IDisposable subscription = null;
            try
            {
                OnityObservableExceptionHandler.Handler = errors.Add;
                subscription = source.Stream.Buffer(TimeSpan.FromSeconds(1), time).Subscribe(values =>
                {
                    received.Add(values[0]);
                    if (received.Count == 1)
                    {
                        throw new InvalidOperationException("Observer failure");
                    }
                });
                source.Emit(1);
                time.Advance();
                source.Emit(2);
                time.Advance();

                Assert.That(received, Is.EqualTo(new[] { 1, 2 }));
                Assert.That(errors.Count, Is.EqualTo(1));
                Assert.That(source.ActiveCount, Is.EqualTo(1));
                subscription.Dispose();
                Assert.That(source.ActiveCount, Is.Zero);
                Assert.That(time.LastToken.IsCancellationRequested, Is.True);
            }
            finally
            {
                subscription?.Dispose();
                OnityObservableExceptionHandler.Handler = previousHandler;
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TimedBuffer_ProviderFailureReleasesUpstream(bool synchronousFailure)
        {
            SubscriptionProbe source = new SubscriptionProbe();
            ManualTimeProvider time = new ManualTimeProvider { ThrowImmediately = synchronousFailure };
            List<Exception> errors = new List<Exception>();
            Action<Exception> previousHandler = OnityObservableExceptionHandler.Handler;
            IDisposable subscription = null;
            try
            {
                OnityObservableExceptionHandler.Handler = errors.Add;
                subscription = source.Stream.Buffer(TimeSpan.FromSeconds(1), time).Subscribe(_ => { });
                if (synchronousFailure == false)
                {
                    time.Fail();
                }

                Assert.That(errors.Count, Is.EqualTo(1));
                Assert.That(source.ActiveCount, Is.Zero);
                Assert.That(source.DisposeCount, Is.EqualTo(1));
                Assert.DoesNotThrow(subscription.Dispose);
            }
            finally
            {
                subscription?.Dispose();
                OnityObservableExceptionHandler.Handler = previousHandler;
            }
        }

        [TestCase("stop")]
        [TestCase("pause")]
        [TestCase("dispose")]
        public void IntervalTimer_CallbackEndsCurrentCatchup(string operation)
        {
            using OnityIntervalTimer timer = new OnityIntervalTimer(1, false, false);
            int received = 0;
            timer.IntervalElapsed += _ =>
            {
                received++;
                if (operation == "stop")
                {
                    timer.Stop();
                }
                else if (operation == "pause")
                {
                    timer.Pause();
                }
                else
                {
                    timer.Dispose();
                }
            };
            timer.Start();
            Assert.DoesNotThrow(() => timer.Tick(5));
            Assert.That(received, Is.EqualTo(1));
            Assert.That(timer.TickCount, Is.EqualTo(1));
            Assert.That(timer.IsRunning, Is.False);
        }

        [TestCase("earlier")]
        [TestCase("all")]
        [TestCase("new")]
        public void TimerRunner_CallbackMutationUpdatesEachSurvivingTimerOnce(string operation)
        {
            using TimerRunnerScope runner = new TimerRunnerScope();
            using OnityIntervalTimer earlier = new OnityIntervalTimer(1);
            using OnityIntervalTimer trigger = new OnityIntervalTimer(1);
            using OnityIntervalTimer added = new OnityIntervalTimer(1);
            int earlierCount = 0;
            int triggerCount = 0;
            int addedCount = 0;
            earlier.IntervalElapsed += _ => earlierCount++;
            added.IntervalElapsed += _ => addedCount++;
            trigger.IntervalElapsed += _ =>
            {
                if (++triggerCount != 1)
                {
                    return;
                }

                earlier.Stop();
                if (operation == "all")
                {
                    trigger.Stop();
                }
                else if (operation == "new")
                {
                    added.Start();
                }
            };
            earlier.Start();
            trigger.Start();

            Assert.DoesNotThrow(() => runner.Update(1, 1));
            Assert.That(triggerCount, Is.EqualTo(1));
            Assert.That(earlierCount, Is.Zero);
            Assert.That(addedCount, Is.Zero);
            runner.Update(1, 1);
            Assert.That(triggerCount, Is.EqualTo(operation == "all" ? 1 : 2));
            Assert.That(addedCount, Is.EqualTo(operation == "new" ? 1 : 0));
        }

        [Test]
        public void TimerRunner_UnchangedPassAllocatesNothingAfterWarmup()
        {
            using TimerRunnerScope runner = new TimerRunnerScope();
            using OnityIntervalTimer first = new OnityIntervalTimer(1);
            using OnityIntervalTimer second = new OnityIntervalTimer(1);
            first.Start();
            second.Start();
            for (int i = 0; i < 64; i++)
            {
                runner.Update(0.1f, 0.1f);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 256; i++)
            {
                runner.Update(0.1f, 0.1f);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void SceneValidation_GathersContextsFromEveryRootIncludingInactiveChildren()
        {
            Scene original = SceneManager.GetActiveScene();
            bool wasDirty = original.isDirty;
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                List<OnityContext> expected = new List<OnityContext>();
                for (int i = 0; i < 3; i++)
                {
                    GameObject root = new GameObject("Audit context root");
                    root.SetActive(false);
                    SceneManager.MoveGameObjectToScene(root, preview);
                    if (i < 2)
                    {
                        expected.Add(root.AddComponent<SceneContext>());
                        GameObject child = new GameObject("Nested context");
                        child.SetActive(false);
                        child.transform.SetParent(root.transform);
                        expected.Add(child.AddComponent<GameObjectContext>());
                    }
                }

                MethodInfo gather = typeof(OnitySceneValidationMenu).GetMethod(
                    "GatherContexts", BindingFlags.NonPublic | BindingFlags.Static);
                Assert.That(gather, Is.Not.Null);
                List<OnityContext> actual = (List<OnityContext>)gather.Invoke(null, new object[] { preview });
                Assert.That(actual, Is.EquivalentTo(expected));
                Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(original));
                Assert.That(original.isDirty, Is.EqualTo(wasDirty));
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        private static IOnityObservable<int> Combine(
            IOnityObservable<int> first, IOnityObservable<int> second, string operation)
        {
            if (operation == "merge")
            {
                return first.Merge(second);
            }

            return operation == "combine" ? first.CombineLatest(second, (a, b) => a + b) : first.Sample(second);
        }

        private static void Pump(OnityContainer container, int pump)
        {
            if (pump == 0)
            {
                container.Tick();
            }
            else if (pump == 1)
            {
                container.FixedTick();
            }
            else
            {
                container.LateTick();
            }
        }

        private static void PumpAll(OnityContainer container)
        {
            container.Tick();
            container.FixedTick();
            container.LateTick();
        }

        private sealed class First { }
        private sealed class Second { }

        private sealed class TickProbe<T> : IOnityTickable, IOnityFixedTickable, IOnityLateTickable
        {
            public Action Callback;
            public int TickCount;

            public void Tick()
            {
                TickCount++;
                Callback?.Invoke();
            }

            public void FixedTick() => Tick();
            public void LateTick() => Tick();
        }

        private sealed class ScopedLifecycle : IOnityInitializable, IOnityAsyncInitializable,
            IOnityTickable, IOnityFixedTickable, IOnityLateTickable, IDisposable
        {
            public int InitializeCount;
            public int AsyncInitializeCount;
            public int TickCount;
            public int DisposeCount;

            public void Initialize() => InitializeCount++;
            public ValueTask InitializeAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AsyncInitializeCount++;
                return default;
            }

            public void Tick() => TickCount++;
            public void FixedTick() => TickCount++;
            public void LateTick() => TickCount++;
            public void Dispose() => DisposeCount++;
        }

        private sealed class InitializationCallback : IOnityInitializable
        {
            private readonly Action m_callback;
            public InitializationCallback(Action callback) => m_callback = callback;
            public void Initialize() => m_callback();
        }

        private sealed class DisposingInitializer : IOnityInitializable
        {
            private readonly Action m_disposeOwner;
            public DisposingInitializer(Action disposeOwner) => m_disposeOwner = disposeOwner;
            public void Initialize() => m_disposeOwner();
        }

        private sealed class TimerRunnerScope : IDisposable
        {
            private readonly FieldInfo m_subscriptionField;
            private readonly object m_previousSubscription;
            public Action<float, float> Update { get; }

            public TimerRunnerScope()
            {
                Type runner = typeof(OnityTimer).Assembly.GetType("Onity.Unity.Reactive.OnityTimerRunner", true);
                FieldInfo timersField = runner.GetField("s_timers", BindingFlags.Static | BindingFlags.NonPublic);
                List<OnityTimer> timers = (List<OnityTimer>)timersField.GetValue(null);
                Assert.That(timers.Count, Is.Zero, "The test must not advance another owner's active timers.");
                MethodInfo update = runner.GetMethod("UpdateTimers", BindingFlags.Static | BindingFlags.NonPublic,
                    null, new[] { typeof(float), typeof(float) }, null);
                Assert.That(update, Is.Not.Null);
                Update = (Action<float, float>)Delegate.CreateDelegate(typeof(Action<float, float>), update);
                m_subscriptionField = runner.GetField("s_updateSubscription", BindingFlags.Static | BindingFlags.NonPublic);
                m_previousSubscription = m_subscriptionField.GetValue(null);
                // The manual pump owns these timers; avoid creating a hidden Unity frame-pump GameObject.
                m_subscriptionField.SetValue(null, DisposableAction.Empty);
            }

            public void Dispose()
            {
                m_subscriptionField.SetValue(null, m_previousSubscription);
            }
        }

        private sealed class AsyncInitializationGate : IOnityAsyncInitializable
        {
            private readonly TaskCompletionSource<bool> m_completion = new TaskCompletionSource<bool>();
            public ValueTask InitializeAsync(CancellationToken cancellationToken) => new ValueTask(m_completion.Task);
            public void Complete() => m_completion.SetResult(true);
        }

        private sealed class SubscriptionProbe
        {
            private Observer<int> m_observer;
            public int SubscribeCount;
            public int ActiveCount;
            public int DisposeCount;
            public bool ThrowOnDispose;
            public IOnityObservable<int> Stream { get; }

            public SubscriptionProbe(int? initialValue = null)
            {
                Stream = new OnityObservable<int>(observer =>
                {
                    SubscribeCount++;
                    m_observer = observer;
                    if (initialValue.HasValue)
                    {
                        observer(initialValue.Value);
                    }

                    ActiveCount++;
                    return new DisposableAction(() =>
                    {
                        m_observer = null;
                        ActiveCount--;
                        DisposeCount++;
                        if (ThrowOnDispose)
                        {
                            throw new InvalidOperationException("Subscription cleanup failure");
                        }
                    });
                });
            }

            public void Emit(int value) => m_observer?.Invoke(value);
        }

        private sealed class ManualTimeProvider : OnityTimeProvider
        {
            private TaskCompletionSource<bool> m_pending;
            public CancellationToken LastToken;
            public bool ThrowImmediately;

            public override Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
            {
                LastToken = cancellationToken;
                if (ThrowImmediately)
                {
                    throw new InvalidOperationException("Provider failure");
                }

                m_pending = new TaskCompletionSource<bool>();
                return WaitAsync(m_pending, cancellationToken);
            }

            public void Advance()
            {
                TaskCompletionSource<bool> pending = m_pending;
                m_pending = null;
                pending.SetResult(true);
            }

            public void Fail() => m_pending.SetException(new InvalidOperationException("Provider failure"));

            private static async Task WaitAsync(TaskCompletionSource<bool> pending, CancellationToken token)
            {
                using CancellationTokenRegistration registration = token.Register(() => pending.TrySetCanceled(token));
                await pending.Task;
            }
        }
    }
}
