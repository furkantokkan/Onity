using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using NUnit.Framework;
using Onity.Unity.Async;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Covers the single-word completion protocol and the value-type shapes of the PERF-7 redesign:
    /// OnityTask stays free of a static constructor, completed awaits store no reference, a
    /// registration racing the owned completion runs its continuation exactly once, exactly one of two
    /// racing consumers wins, a stale registrant cannot touch a re-rented runner, a fault racing a
    /// runner retirement publishes one consistent outcome, and runners return to their pool at
    /// consumption on every backend.
    /// </summary>
    [TestFixture]
    public sealed class OnityTaskCoreProtocolEditModeTests
    {
        private const int k_raceIterations = 2000;
        private const int k_workerTimeoutMilliseconds = 10000;
        private const BindingFlags k_instance =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private int m_previousRunnerCapacity;

        [SetUp]
        public void SetUp()
        {
            m_previousRunnerCapacity = OnityTask.RunnerPoolCapacity;
            OnityTask.RunnerPoolCapacity = 128;
        }

        [TearDown]
        public void TearDown()
        {
            OnityTask.RunnerPoolCapacity = m_previousRunnerCapacity;
        }

        [Test]
        public void TaskValueTypes_HaveNoStaticConstructor()
        {
            Assert.That(typeof(OnityTask).TypeInitializer, Is.Null,
                "An OnityTask partial added a static field initializer; move it to OnityTaskSettings.");
            Assert.That(typeof(OnityTask<int>).TypeInitializer, Is.Null);
            Assert.That(typeof(OnityTask<string>).TypeInitializer, Is.Null);
        }

        [Test]
        public void CompletedAwaiters_StoreNoState_AndKeepTheInlineResult()
        {
            OnityTaskAwaiter untyped = OnityTask.CompletedTask.GetAwaiter();
            OnityTaskAwaiter<int> typed = OnityTask.FromResult(5).GetAwaiter();

            Assert.That(ReadState(untyped), Is.Null);
            Assert.That(ReadState(typed), Is.Null);
            Assert.That(untyped.IsCompleted, Is.True);
            Assert.That(typed.IsCompleted, Is.True);
            Assert.That(typed.GetResult(), Is.EqualTo(5));
            Assert.DoesNotThrow(() => untyped.GetResult());
        }

        [Test]
        public void PoolCapacities_DefaultAndClamp()
        {
            int previous = OnityTask.SourcePoolCapacity;
            try
            {
                Assert.That(previous, Is.EqualTo(256), "SourcePoolCapacity defaults to 256.");
                OnityTask.SourcePoolCapacity = -5;
                Assert.That(OnityTask.SourcePoolCapacity, Is.Zero);
            }
            finally
            {
                OnityTask.SourcePoolCapacity = previous;
            }

            Assert.That(m_previousRunnerCapacity, Is.EqualTo(128), "RunnerPoolCapacity defaults to 128.");
        }

        [Test]
        public void ConsumedRunner_ReturnsToThePoolImmediately_AndIsRentedAgain()
        {
            Gate firstGate = new Gate();
            OnityTask<int> first = ReturnAfterAsync(firstGate, 1);
            object runner = ReadState(first);
            firstGate.Complete();
            Assert.That(first.GetAwaiter().GetResult(), Is.EqualTo(1));

            Gate nextGate = new Gate();
            OnityTask<int> next = ReturnAfterAsync(nextGate, 2);
            Assert.That(ReadState(next), Is.SameAs(runner), "a consumed runner is pooled at consumption");
            Assert.Throws<InvalidOperationException>(() => _ = first.IsCompleted, "the old token is retired");
            nextGate.Complete();
            Assert.That(next.GetAwaiter().GetResult(), Is.EqualTo(2));
        }

        [Test]
        public void ReentrantConsumption_RerentsTheSameRunnerInsideItsMoveNext_WithoutCorruption()
        {
            Gate firstGate = new Gate();
            Gate nextGate = new Gate();
            OnityTask<int> first = ReturnAfterAsync(firstGate, 53);
            object runner = ReadState(first);
            OnityTask<int> next = default;
            int observed = 0;
            first.GetAwaiter().UnsafeOnCompleted(() =>
            {
                observed = first.GetAwaiter().GetResult();
                next = ReturnAfterAsync(nextGate, 59);
            });

            firstGate.Complete();

            Assert.That(observed, Is.EqualTo(53));
            Assert.That(ReadState(next), Is.SameAs(runner), "the runner is re-rented inside its own MoveNext");
            Assert.That(next.IsCompleted, Is.False);
            nextGate.Complete();
            Assert.That(next.GetAwaiter().GetResult(), Is.EqualTo(59), "the unwinding MoveNext corrupted the rental");
        }

        [Test]
        public void RegistrationRacingOwnedCompletion_RunsTheContinuationExactlyOnce()
        {
            using (var worker = new RaceWorker())
            {
                for (int i = 0; i < k_raceIterations; i++)
                {
                    Gate gate = new Gate();
                    OnityTask task = CompleteAfterAsync(gate);
                    int invocations = 0;
                    worker.Run(() => task.GetAwaiter().UnsafeOnCompleted(() => Interlocked.Increment(ref invocations)),
                        gate.Complete);
                    SpinWait.SpinUntil(() => Volatile.Read(ref invocations) != 0, k_workerTimeoutMilliseconds);
                    Assert.That(Volatile.Read(ref invocations), Is.EqualTo(1), "iteration " + i);
                    task.GetAwaiter().GetResult();
                }
            }
        }

        [Test]
        public void RacingConsumers_ExactlyOneWins()
        {
            using (var worker = new RaceWorker())
            {
                for (int i = 0; i < k_raceIterations; i++)
                {
                    Gate gate = new Gate();
                    OnityTask<int> task = ReturnAfterAsync(gate, i);
                    gate.Complete();
                    int successes = 0;
                    int rejections = 0;
                    Action consume = () =>
                    {
                        try
                        {
                            task.GetAwaiter().GetResult();
                            Interlocked.Increment(ref successes);
                        }
                        catch (InvalidOperationException)
                        {
                            Interlocked.Increment(ref rejections);
                        }
                    };
                    worker.Run(consume, consume);
                    Assert.That(successes, Is.EqualTo(1), "iteration " + i);
                    Assert.That(rejections, Is.EqualTo(1), "iteration " + i);
                }
            }
        }

        [Test]
        public void StaleRegistrant_AfterReuse_IsRejected_AndTheSuccessorCompletesNormally()
        {
            Gate firstGate = new Gate();
            OnityTask first = CompleteAfterAsync(firstGate);
            firstGate.Complete();
            first.GetAwaiter().GetResult();

            Gate nextGate = new Gate();
            OnityTask next = CompleteAfterAsync(nextGate);
            bool staleRan = false;
            Assert.Throws<InvalidOperationException>(
                () => first.GetAwaiter().UnsafeOnCompleted(() => staleRan = true));

            bool nextRan = false;
            next.GetAwaiter().UnsafeOnCompleted(() => nextRan = true);
            nextGate.Complete();
            Assert.That(nextRan, Is.True);
            Assert.That(staleRan, Is.False);
            Assert.DoesNotThrow(() => next.GetAwaiter().GetResult());
        }

        [Test]
        public void FaultRacingRunnerRetirement_PublishesOneConsistentOutcome()
        {
            using (var worker = new RaceWorker())
            {
                for (int i = 0; i < k_raceIterations; i++)
                {
                    Gate gate = new Gate();
                    var fault = new InvalidOperationException("fault " + i);
                    OnityTask task = FaultAfterAsync(gate, fault);
                    object runner = ReadState(task);
                    int version = (int)runner.GetType().GetProperty("Version", k_instance).GetValue(runner);
                    MethodInfo retire = runner.GetType().GetMethod("TrySetRetiredFromRunner", k_instance);
                    worker.Run(gate.Complete, () => retire.Invoke(runner, new object[] { version }));
                    Assert.That(task.IsCompleted, Is.True, "iteration " + i);
                    Exception observed = null;
                    try
                    {
                        task.GetAwaiter().GetResult();
                    }
                    catch (Exception exception)
                    {
                        observed = exception;
                    }

                    Assert.That(observed, Is.Not.Null, "iteration " + i);
                    if (observed is OperationCanceledException)
                    {
                        Assert.That(observed, Is.Not.SameAs(fault));
                    }
                    else
                    {
                        Assert.That(observed, Is.SameAs(fault), "a faulted outcome keeps the thrown instance");
                    }
                }
            }
        }

        [Test]
        public void NullContinuation_IsRejectedBeforeNativeDispatch()
        {
            Gate gate = new Gate();
            OnityTask task = CompleteAfterAsync(gate);
            Assert.Throws<ArgumentNullException>(() => task.GetAwaiter().UnsafeOnCompleted(null));
            Assert.Throws<ArgumentNullException>(() => task.GetAwaiter().OnCompleted(null));
            gate.Complete();
            task.GetAwaiter().GetResult();
        }

        [Test]
        public void TypedTask_UntypedView_SharesTheSourceWithoutConsumingIt()
        {
            Gate gate = new Gate();
            OnityTask<int> typed = ReturnAfterAsync(gate, 7);
            MethodInfo view = typeof(OnityTask<int>).GetMethod("AsUntypedView", k_instance);
            Assert.That(view, Is.Not.Null);
            OnityTask untyped = (OnityTask)view.Invoke(typed, null);
            Assert.That(ReadState(untyped), Is.SameAs(ReadState(typed)), "the view allocates nothing");
            Assert.That(untyped.IsCompleted, Is.False);
            gate.Complete();
            Assert.That(typed.GetAwaiter().GetResult(), Is.EqualTo(7));
            Assert.Throws<InvalidOperationException>(() => untyped.GetAwaiter().GetResult(),
                "the source is consumed once through either handle");
        }

        private static object ReadState(OnityTaskAwaiter awaiter)
        {
            return typeof(OnityTaskAwaiter).GetField("m_state", k_instance).GetValue(awaiter);
        }

        private static object ReadState<T>(OnityTaskAwaiter<T> awaiter)
        {
            return typeof(OnityTaskAwaiter<T>).GetField("m_state", k_instance).GetValue(awaiter);
        }

        private static object ReadState(OnityTask task)
        {
            return typeof(OnityTask).GetField("m_state", k_instance).GetValue(task);
        }

        private static object ReadState<T>(OnityTask<T> task)
        {
            return typeof(OnityTask<T>).GetField("m_state", k_instance).GetValue(task);
        }

        private static async OnityTask CompleteAfterAsync(Gate gate)
        {
            await gate;
        }

        private static async OnityTask<int> ReturnAfterAsync(Gate gate, int value)
        {
            await gate;
            return value;
        }

        private static async OnityTask FaultAfterAsync(Gate gate, Exception fault)
        {
            await gate;
            throw fault;
        }

        /// <summary>Critical awaitable that stores its continuation and runs it on the completing thread.</summary>
        private sealed class Gate : ICriticalNotifyCompletion
        {
            private Action m_continuation;

            public bool IsCompleted => false;

            public Gate GetAwaiter()
            {
                return this;
            }

            public void GetResult()
            {
            }

            public void OnCompleted(Action continuation)
            {
                m_continuation = continuation;
            }

            public void UnsafeOnCompleted(Action continuation)
            {
                m_continuation = continuation;
            }

            public void Complete()
            {
                Action continuation = Interlocked.Exchange(ref m_continuation, null);
                continuation?.Invoke();
            }
        }

        /// <summary>
        /// One long-lived worker thread that runs one action while the calling thread runs another,
        /// both released by the same barrier, so each iteration races the two without creating threads.
        /// </summary>
        private sealed class RaceWorker : IDisposable
        {
            private readonly Thread m_thread;
            private readonly Barrier m_start = new Barrier(2);
            private readonly ManualResetEventSlim m_ready = new ManualResetEventSlim(false);
            private readonly ManualResetEventSlim m_done = new ManualResetEventSlim(false);
            private Action m_action;
            private Exception m_failure;
            private volatile bool m_stopping;

            public RaceWorker()
            {
                m_thread = new Thread(Loop) { IsBackground = true, Name = "Onity protocol race worker" };
                m_thread.Start();
            }

            public void Run(Action workerAction, Action callerAction)
            {
                m_action = workerAction;
                m_failure = null;
                m_done.Reset();
                m_ready.Set();
                m_start.SignalAndWait();
                callerAction();
                Assert.That(m_done.Wait(k_workerTimeoutMilliseconds), Is.True, "the race worker timed out");
                if (m_failure != null)
                {
                    throw new AggregateException(m_failure);
                }
            }

            public void Dispose()
            {
                m_stopping = true;
                m_ready.Set();
                m_thread.Join(k_workerTimeoutMilliseconds);
                m_start.Dispose();
                m_ready.Dispose();
                m_done.Dispose();
            }

            private void Loop()
            {
                while (true)
                {
                    m_ready.Wait();
                    m_ready.Reset();
                    if (m_stopping)
                    {
                        return;
                    }

                    m_start.SignalAndWait();
                    try
                    {
                        m_action();
                    }
                    catch (Exception exception)
                    {
                        m_failure = exception;
                    }

                    m_done.Set();
                }
            }
        }
    }
}
