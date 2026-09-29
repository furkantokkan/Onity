using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Covers the packed-state guarantees of the pooled source bases behind suspended async
    /// OnityTask methods: a stale token is rejected atomically once the source was reused, a
    /// registration that races the completion delivers the continuation exactly once and only
    /// after the status is visible, AsTask racing the completion finishes the bridge and releases
    /// the runner exactly once, and a runner cancellation carrying a retired version leaves the
    /// new cycle alone.
    /// </summary>
    [TestFixture]
    public sealed class OnityTaskSourceStateEditModeTests
    {
        private const int k_raceIterations = 200;
        private const BindingFlags k_instance =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private int m_previousPoolCapacity;

        [SetUp]
        public void SaveSettings()
        {
            m_previousPoolCapacity = OnityTask.RunnerPoolCapacity;
            OnityTask.RunnerPoolCapacity = 128;
        }

        [TearDown]
        public void RestoreSettings()
        {
            OnityTask.RunnerPoolCapacity = m_previousPoolCapacity;
        }

        [Test]
        public void StaleTokenAfterReuse_IsRejectedByEveryEntryPoint_AndLeavesTheNewCycleAlone()
        {
            Gate firstGate = new Gate();
            OnityTask first = CompleteAfterAsync(firstGate);
            OnityTaskAwaiter staleAwaiter = first.GetAwaiter();
            object runner = GetState(first);
            firstGate.Complete();
            staleAwaiter.GetResult();

            Gate secondGate = new Gate();
            OnityTask second = CompleteAfterAsync(secondGate);
            Assume.That(GetState(second), Is.SameAs(runner), "the pool hands the released runner back");

            Assert.Throws<InvalidOperationException>(() => _ = staleAwaiter.IsCompleted);
            Assert.Throws<InvalidOperationException>(() => staleAwaiter.GetResult());
            Assert.Throws<InvalidOperationException>(() => staleAwaiter.UnsafeOnCompleted(() => { }));
            Assert.Throws<InvalidOperationException>(() => first.AsTask());
            Assert.Throws<InvalidOperationException>(() => first.Preserve());
            Assert.That(second.IsCompleted, Is.False);

            secondGate.Complete();
            Assert.That(second.IsCompletedSuccessfully, Is.True);
            Assert.DoesNotThrow(() => second.GetAwaiter().GetResult());
        }

        [Test]
        public void StaleTokenAfterReuse_Typed_IsRejectedByEveryEntryPoint_AndLeavesTheNewCycleAlone()
        {
            Gate firstGate = new Gate();
            OnityTask<int> first = ReturnAfterAsync(firstGate, 1);
            OnityTaskAwaiter<int> staleAwaiter = first.GetAwaiter();
            object runner = GetState(first);
            firstGate.Complete();
            Assert.That(staleAwaiter.GetResult(), Is.EqualTo(1));

            Gate secondGate = new Gate();
            OnityTask<int> second = ReturnAfterAsync(secondGate, 2);
            Assume.That(GetState(second), Is.SameAs(runner), "the pool hands the released runner back");

            Assert.Throws<InvalidOperationException>(() => _ = staleAwaiter.IsCompleted);
            Assert.Throws<InvalidOperationException>(() => staleAwaiter.GetResult());
            Assert.Throws<InvalidOperationException>(() => staleAwaiter.UnsafeOnCompleted(() => { }));
            Assert.Throws<InvalidOperationException>(() => first.AsTask());
            Assert.Throws<InvalidOperationException>(() => first.Preserve());
            Assert.That(second.IsCompleted, Is.False);

            secondGate.Complete();
            Assert.That(second.GetAwaiter().GetResult(), Is.EqualTo(2));
        }

        [Test]
        public void ContinuationRegisteredAfterCompletion_RunsSynchronously_AndSeesTheStatus()
        {
            Gate gate = new Gate();
            OnityTask task = CompleteAfterAsync(gate);
            OnityTaskAwaiter awaiter = task.GetAwaiter();
            gate.Complete();

            bool ran = false;
            bool completedWhenInvoked = false;
            awaiter.UnsafeOnCompleted(() =>
            {
                completedWhenInvoked = awaiter.IsCompleted;
                ran = true;
            });

            Assert.That(ran, Is.True);
            Assert.That(completedWhenInvoked, Is.True);
            Assert.DoesNotThrow(() => awaiter.GetResult());
        }

        [Test]
        public void RegistrationRacingCompletion_DeliversTheContinuationExactlyOnce_AfterTheStatus()
        {
            for (int iteration = 0; iteration < k_raceIterations; iteration++)
            {
                Gate gate = new Gate();
                OnityTask task = CompleteAfterAsync(gate);
                OnityTaskAwaiter awaiter = task.GetAwaiter();
                int invocations = 0;
                bool completedWhenInvoked = false;
                using Barrier barrier = new Barrier(2);
                Task completer = Task.Run(() =>
                {
                    barrier.SignalAndWait();
                    gate.Complete();
                });

                barrier.SignalAndWait();
                awaiter.UnsafeOnCompleted(() =>
                {
                    completedWhenInvoked = awaiter.IsCompleted;
                    Interlocked.Increment(ref invocations);
                });
                completer.GetAwaiter().GetResult();

                Assert.That(invocations, Is.EqualTo(1), "iteration " + iteration);
                Assert.That(completedWhenInvoked, Is.True, "iteration " + iteration);
                Assert.DoesNotThrow(() => awaiter.GetResult());
            }
        }

        [Test]
        public void RegistrationRacingCompletion_Typed_DeliversTheContinuationExactlyOnce_AfterTheStatus()
        {
            for (int iteration = 0; iteration < k_raceIterations; iteration++)
            {
                Gate gate = new Gate();
                OnityTask<int> task = ReturnAfterAsync(gate, iteration);
                OnityTaskAwaiter<int> awaiter = task.GetAwaiter();
                int invocations = 0;
                bool completedWhenInvoked = false;
                using Barrier barrier = new Barrier(2);
                Task completer = Task.Run(() =>
                {
                    barrier.SignalAndWait();
                    gate.Complete();
                });

                barrier.SignalAndWait();
                awaiter.UnsafeOnCompleted(() =>
                {
                    completedWhenInvoked = awaiter.IsCompleted;
                    Interlocked.Increment(ref invocations);
                });
                completer.GetAwaiter().GetResult();

                Assert.That(invocations, Is.EqualTo(1), "iteration " + iteration);
                Assert.That(completedWhenInvoked, Is.True, "iteration " + iteration);
                Assert.That(awaiter.GetResult(), Is.EqualTo(iteration));
            }
        }

        [Test]
        public void AsTaskRacingCompletion_CompletesTheBridgeOnce_AndReturnsTheRunner()
        {
            for (int iteration = 0; iteration < k_raceIterations; iteration++)
            {
                Gate gate = new Gate();
                OnityTask task = CompleteAfterAsync(gate);
                object runner = GetState(task);
                using Barrier barrier = new Barrier(2);
                Task<Task> bridging = Task.Factory.StartNew(() =>
                {
                    barrier.SignalAndWait();
                    return task.AsTask();
                });

                barrier.SignalAndWait();
                gate.Complete();
                Task bridge = bridging.GetAwaiter().GetResult();

                Assert.That(bridge.Wait(TimeSpan.FromSeconds(5)), Is.True, "iteration " + iteration);
                Assert.That(bridge.Status, Is.EqualTo(TaskStatus.RanToCompletion), "iteration " + iteration);

                // Both parties have returned, so the single release has happened on one of them.
                Gate nextGate = new Gate();
                OnityTask next = CompleteAfterAsync(nextGate);
                Assert.That(GetState(next), Is.SameAs(runner), "iteration " + iteration);
                nextGate.Complete();
                next.GetAwaiter().GetResult();
            }
        }

        [Test]
        public void AsTaskRacingCompletion_Typed_CompletesTheBridgeOnce_AndReturnsTheRunner()
        {
            for (int iteration = 0; iteration < k_raceIterations; iteration++)
            {
                Gate gate = new Gate();
                OnityTask<int> task = ReturnAfterAsync(gate, iteration);
                object runner = GetState(task);
                using Barrier barrier = new Barrier(2);
                Task<Task<int>> bridging = Task.Factory.StartNew(() =>
                {
                    barrier.SignalAndWait();
                    return task.AsTask();
                });

                barrier.SignalAndWait();
                gate.Complete();
                Task<int> bridge = bridging.GetAwaiter().GetResult();

                Assert.That(bridge.Wait(TimeSpan.FromSeconds(5)), Is.True, "iteration " + iteration);
                Assert.That(bridge.Result, Is.EqualTo(iteration), "iteration " + iteration);

                Gate nextGate = new Gate();
                OnityTask<int> next = ReturnAfterAsync(nextGate, -1);
                Assert.That(GetState(next), Is.SameAs(runner), "iteration " + iteration);
                nextGate.Complete();
                Assert.That(next.GetAwaiter().GetResult(), Is.EqualTo(-1));
            }
        }

        [Test]
        public void RunnerCancellationWithARetiredVersion_IsRejected_AndLeavesTheNewCycleAlone()
        {
            Gate firstGate = new Gate();
            OnityTask first = CompleteAfterAsync(firstGate);
            object runner = GetState(first);
            PropertyInfo version = runner.GetType().GetProperty("Version", k_instance);
            MethodInfo retire = runner.GetType().GetMethod("TrySetRetiredFromRunner", k_instance);
            MethodInfo cancel = runner.GetType().GetMethod("TrySetCanceledFromRunner", k_instance);
            Assert.That(version, Is.Not.Null);
            Assert.That(retire, Is.Not.Null);
            Assert.That(cancel, Is.Not.Null);
            int retiredToken = (int)version.GetValue(runner);
            firstGate.Complete();
            first.GetAwaiter().GetResult();

            Gate secondGate = new Gate();
            OnityTask second = CompleteAfterAsync(secondGate);
            Assume.That(GetState(second), Is.SameAs(runner), "the pool hands the released runner back");

            Assert.That(retire.Invoke(runner, new object[] { retiredToken }), Is.EqualTo(false));
            Assert.That(cancel.Invoke(runner, new object[] { retiredToken }), Is.EqualTo(false));
            Assert.That(second.IsCompleted, Is.False);

            int currentToken = (int)version.GetValue(runner);
            Assert.That(retire.Invoke(runner, new object[] { currentToken }), Is.EqualTo(true));
            Assert.That(second.IsCanceled, Is.True);
            Assert.Throws<OperationCanceledException>(() => second.GetAwaiter().GetResult());
        }

        private static object GetState(OnityTask task)
        {
            FieldInfo field = typeof(OnityTask).GetField("m_state", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return field.GetValue(task);
        }

        private static object GetState<T>(OnityTask<T> task)
        {
            FieldInfo field = typeof(OnityTask<T>).GetField("m_state", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return field.GetValue(task);
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

        /// <summary>
        /// Awaitable that never completes on its own; <see cref="Complete"/> resumes the one
        /// registered continuation on the calling thread.
        /// </summary>
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
    }
}
