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
    /// new cycle alone. Completion without a cancelable source token preserves each consumption
    /// mode's outcome, while real registrations are disposed before continuation delivery and
    /// cannot cancel a later rental.
    /// </summary>
    [TestFixture]
    public sealed class OnityTaskSourceStateEditModeTests
    {
        private const int k_raceIterations = 200;
        private const int k_reuseRaceIterations = 128;
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

        [TestCase(false)]
        [TestCase(true)]
        public void AsTaskRacingNativeConsumption_ImmediateRentalRemainsUsable(bool typed)
        {
            for (int iteration = 0; iteration < k_reuseRaceIterations; iteration++)
            {
                Cycle first = CreateCycle(typed, 11);
                first.Gate.Complete();
                Cycle next = null;
                Exception bridgeError = null;
                Exception nativeError = null;
                Task bridge = null;
                int nativeResult = -1;
                using Barrier barrier = new Barrier(2);
                Task worker = Task.Run(() =>
                {
                    StartRace(barrier);
                    try
                    {
                        bridge = first.AsTask();
                        next = CreateCycle(typed, 22);
                    }
                    catch (Exception exception)
                    {
                        bridgeError = exception;
                    }
                });
                try
                {
                    StartRace(barrier);
                    try
                    {
                        nativeResult = first.GetResult();
                        // Rent before joining the old caller, while it may still be claiming its bridge.
                        next = CreateCycle(typed, 22);
                    }
                    catch (Exception exception)
                    {
                        nativeError = exception;
                    }
                    Join(worker);
                    Assert.That((bridgeError == null) ^ (nativeError == null), Is.True,
                        "Exactly one consumption path must win, iteration " + iteration);
                    AssertExpectedRejection(bridgeError);
                    AssertExpectedRejection(nativeError);
                    if (bridge != null)
                    {
                        Join(bridge);
                        Assert.That(bridge.Status, Is.EqualTo(TaskStatus.RanToCompletion));
                        if (typed)
                        {
                            Assert.That(((Task<int>)bridge).Result, Is.EqualTo(11));
                        }
                    }
                    else
                    {
                        Assert.That(nativeResult, Is.EqualTo(typed ? 11 : 0));
                    }
                    AssertFreshCycle(first, next, typed);
                }
                finally
                {
                    Join(worker);
                    next?.Gate.Complete();
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RepeatedAsTaskRacingRelease_DoesNotReturnOrChangeTheSuccessorBridge(bool typed)
        {
            for (int iteration = 0; iteration < k_reuseRaceIterations; iteration++)
            {
                Cycle first = CreateCycle(typed, 11);
                Task bridge = first.AsTask();
                Task repeated = null;
                Exception error = null;
                Cycle next = null;
                using Barrier barrier = new Barrier(2);
                Task worker = Task.Run(() =>
                {
                    StartRace(barrier);
                    try
                    {
                        repeated = first.AsTask();
                    }
                    catch (Exception exception)
                    {
                        error = exception;
                    }
                });
                try
                {
                    StartRace(barrier);
                    first.Gate.Complete();
                    next = CreateCycle(typed, 22);
                    Task nextBridge = next.AsTask();
                    Join(worker);
                    AssertExpectedRejection(error);
                    if (error == null)
                    {
                        Assert.That(repeated, Is.SameAs(bridge));
                    }
                    Assert.That(next.State, Is.SameAs(first.State));
                    Assert.That(nextBridge.IsCompleted, Is.False);
                    next.Gate.Complete();
                    Join(bridge, nextBridge);
                    if (typed)
                    {
                        Assert.That(((Task<int>)bridge).Result, Is.EqualTo(11));
                        Assert.That(((Task<int>)nextBridge).Result, Is.EqualTo(22));
                    }
                }
                finally
                {
                    first.Gate.Complete();
                    Join(worker);
                    next?.Gate.Complete();
                }
            }
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void RegistrationRacingConsumption_ImmediateRentalSurvivesOldRegistration(
            bool typed, bool preserved)
        {
            for (int iteration = 0; iteration < k_reuseRaceIterations; iteration++)
            {
                Cycle first = CreateCycle(typed, 11);
                first.Gate.Complete();
                Cycle next = null;
                int successfulConsumers = 0;
                int callbacks = 0;
                int result = -1;
                Exception registrationError = null;
                Exception callbackError = null;
                Exception nativeError = null;
                using Barrier barrier = new Barrier(2);
                Task worker = Task.Run(() =>
                {
                    StartRace(barrier);
                    try
                    {
                        if (preserved)
                        {
                            result = first.GetPreservedResult();
                            Interlocked.Increment(ref successfulConsumers);
                            next = CreateCycle(typed, 22);
                        }
                        else
                        {
                            first.Register(() =>
                            {
                                Interlocked.Increment(ref callbacks);
                                try
                                {
                                    result = first.GetResult();
                                    Interlocked.Increment(ref successfulConsumers);
                                    // Reentrant consumption/rental must precede registration returning.
                                    next = CreateCycle(typed, 22);
                                }
                                catch (Exception exception)
                                {
                                    callbackError = exception;
                                }
                            });
                        }
                    }
                    catch (Exception exception)
                    {
                        registrationError = exception;
                    }
                });
                try
                {
                    StartRace(barrier);
                    try
                    {
                        result = first.GetResult();
                        Interlocked.Increment(ref successfulConsumers);
                        next = CreateCycle(typed, 22);
                    }
                    catch (Exception exception)
                    {
                        nativeError = exception;
                    }
                    Join(worker);
                    Assert.That(successfulConsumers, Is.EqualTo(1), "iteration " + iteration);
                    Assert.That(callbacks, Is.InRange(0, 1));
                    AssertExpectedRejection(registrationError);
                    AssertExpectedRejection(callbackError);
                    AssertExpectedRejection(nativeError);
                    Assert.That(result, Is.EqualTo(typed ? 11 : 0));
                    AssertFreshCycle(first, next, typed);
                }
                finally
                {
                    Join(worker);
                    next?.Gate.Complete();
                }
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

        [Test]
        public void NoncancelableCompletion_PreservesOutcomeForEachConsumer(
            [Values(false, true)] bool typed,
            [Values(0, 1, 2)] int outcome,
            [Values(0, 1, 2)] int consumer)
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            Exception failure = CreateCompletionFailure(outcome, cancellation.Token);
            Gate gate = new Gate();
            if (typed)
            {
                OnityTask<int> task = ReturnOutcomeAfterAsync(gate, 11, failure);
                VerifyTypedOutcome(task, gate, failure, consumer);
            }
            else
            {
                OnityTask task = CompleteOutcomeAfterAsync(gate, failure);
                VerifyUntypedOutcome(task, gate, failure, consumer);
            }
        }

        [Test]
        public void RealCancellationRegistration_IsClearedBeforeContinuation_AndCannotCancelReusedRunner(
            [Values(false, true)] bool typed,
            [Values(0, 1, 2)] int outcome)
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            Exception failure = CreateCompletionFailure(outcome, cancellation.Token);
            Cycle first = null;
            Cycle next = null;
            try
            {
                first = CreateRegisteredOutcomeCycle(typed, 11, failure, cancellation.Token);
                // The protocol fields live on a base class of the source bases; search the hierarchy.
                FieldInfo registrationField = FindInstanceField(first.State.GetType(), "m_cancellationRegistration");
                FieldInfo cancellationField = FindInstanceField(first.State.GetType(), "m_cancellationRequested");
                Assert.That(registrationField, Is.Not.Null);
                Assert.That(cancellationField, Is.Not.Null);
                Assert.That((CancellationTokenRegistration)registrationField.GetValue(first.State),
                    Is.Not.EqualTo(default(CancellationTokenRegistration)), "The pending source owns a real registration.");

                int callbacks = 0;
                bool completedInCallback = false;
                CancellationTokenRegistration registrationInCallback = default;
                first.Register(() =>
                {
                    completedInCallback = first.IsCompleted();
                    registrationInCallback = (CancellationTokenRegistration)registrationField.GetValue(first.State);
                    callbacks++;
                });
                if (outcome == 2)
                {
                    cancellation.Cancel();
                    Assert.That((int)cancellationField.GetValue(first.State), Is.EqualTo(1),
                        "The real cancellation callback must have reached the pending source.");
                }

                first.Gate.Complete();
                Assert.That(callbacks, Is.EqualTo(1));
                Assert.That(completedInCallback, Is.True);
                Assert.That(registrationInCallback, Is.EqualTo(default(CancellationTokenRegistration)),
                    "Dispose and clearing must finish before the continuation sees completion.");
                AssertCompletionOutcome(first.GetResult, typed ? 11 : 0, failure, true);

                next = CreateRegisteredOutcomeCycle(typed, 22, null, CancellationToken.None);
                Assert.That(next.State, Is.SameAs(first.State), "The exact completed runner must be rented again.");
                Assert.That(next.IsCompleted(), Is.False);
                Assert.That((int)cancellationField.GetValue(next.State), Is.Zero);

                cancellation.Cancel();
                Assert.That(cancellation.IsCancellationRequested, Is.True);
                Assert.That((int)cancellationField.GetValue(next.State), Is.Zero,
                    "Canceling the old token must not reach the successor source.");
                PropertyInfo version = next.State.GetType().GetProperty("Version", k_instance);
                MethodInfo cancel = next.State.GetType().GetMethod("TrySetCanceledFromRunner", k_instance);
                Assert.That(version, Is.Not.Null);
                Assert.That(cancel, Is.Not.Null);
                Assert.That(cancel.Invoke(next.State, new[] { version.GetValue(next.State) }), Is.EqualTo(false));
                Assert.That(next.IsCompleted(), Is.False);

                next.Gate.Complete();
                Assert.That(next.GetResult(), Is.EqualTo(typed ? 22 : 0));
            }
            finally
            {
                FinishCycle(first);
                FinishCycle(next);
            }
        }

        private static Exception CreateCompletionFailure(int outcome, CancellationToken token)
        {
            if (outcome == 1)
            {
                return new InvalidOperationException("completion failure");
            }
            return outcome == 2 ? new OperationCanceledException(token) : null;
        }

        private static void VerifyTypedOutcome(OnityTask<int> task, Gate gate, Exception failure, int consumer)
        {
            Func<int> getResult;
            Func<bool> isCompleted;
            Func<bool> isFaulted;
            Func<bool> isCanceled;
            if (consumer == 1)
            {
                Task<int> bridge = task.AsTask();
                getResult = () => bridge.GetAwaiter().GetResult();
                isCompleted = () => bridge.IsCompleted;
                isFaulted = () => bridge.IsFaulted;
                isCanceled = () => bridge.IsCanceled;
            }
            else
            {
                if (consumer == 2)
                {
                    task = task.Preserve();
                }
                getResult = () => task.GetAwaiter().GetResult();
                isCompleted = () => task.IsCompleted;
                isFaulted = () => task.IsFaulted;
                isCanceled = () => task.IsCanceled;
            }
            VerifyCompletionOutcome(gate, getResult, isCompleted, isFaulted, isCanceled, 11, failure, consumer);
        }

        private static void VerifyUntypedOutcome(OnityTask task, Gate gate, Exception failure, int consumer)
        {
            Func<int> getResult;
            Func<bool> isCompleted;
            Func<bool> isFaulted;
            Func<bool> isCanceled;
            if (consumer == 1)
            {
                Task bridge = task.AsTask();
                getResult = () => { bridge.GetAwaiter().GetResult(); return 0; };
                isCompleted = () => bridge.IsCompleted;
                isFaulted = () => bridge.IsFaulted;
                isCanceled = () => bridge.IsCanceled;
            }
            else
            {
                if (consumer == 2)
                {
                    task = task.Preserve();
                }
                getResult = () => { task.GetAwaiter().GetResult(); return 0; };
                isCompleted = () => task.IsCompleted;
                isFaulted = () => task.IsFaulted;
                isCanceled = () => task.IsCanceled;
            }
            VerifyCompletionOutcome(gate, getResult, isCompleted, isFaulted, isCanceled, 0, failure, consumer);
        }

        private static void VerifyCompletionOutcome(Gate gate, Func<int> getResult,
            Func<bool> isCompleted, Func<bool> isFaulted, Func<bool> isCanceled,
            int expectedResult, Exception failure, int consumer)
        {
            try
            {
                Assert.That(isCompleted(), Is.False);
                gate.Complete();
                Assert.That(isCompleted(), Is.True);
                Assert.That(isFaulted(), Is.EqualTo(failure != null && !(failure is OperationCanceledException)));
                Assert.That(isCanceled(), Is.EqualTo(failure is OperationCanceledException));
                AssertCompletionOutcome(getResult, expectedResult, failure, consumer == 0);
                if (consumer != 0)
                {
                    AssertCompletionOutcome(getResult, expectedResult, failure, false);
                }
            }
            finally
            {
                gate.Complete();
                ConsumeForCleanup(getResult);
            }
        }

        private static void AssertCompletionOutcome(Func<int> getResult, int expectedResult,
            Exception failure, bool native)
        {
            if (failure is OperationCanceledException canceled)
            {
                OperationCanceledException observed = Assert.Catch<OperationCanceledException>(() => getResult());
                Assert.That(observed.CancellationToken, Is.EqualTo(canceled.CancellationToken));
                if (native)
                {
                    Assert.That(observed, Is.SameAs(failure));
                }
            }
            else if (failure != null)
            {
                Assert.That(Assert.Throws<InvalidOperationException>(() => getResult()), Is.SameAs(failure));
            }
            else
            {
                Assert.That(getResult(), Is.EqualTo(expectedResult));
            }
        }

        private static Cycle CreateRegisteredOutcomeCycle(bool typed, int value, Exception failure,
            CancellationToken token)
        {
            Gate gate = new Gate();
            if (typed)
            {
                OnityTask<int> task = ReturnOutcomeAfterAsync(gate, value, failure);
                object state = GetState(task);
                ResetSource(state, token);
                // Reset advances the version; only the freshly retrieved task may be used afterward.
                task = (OnityTask<int>)state.GetType().GetProperty("Task", k_instance).GetValue(state);
                return new Cycle(gate, state, () => task.AsTask(), () => task.GetAwaiter().GetResult(),
                    callback => task.GetAwaiter().UnsafeOnCompleted(callback),
                    () => task.Preserve().GetAwaiter().GetResult(), () => task.IsCompleted);
            }
            OnityTask plain = CompleteOutcomeAfterAsync(gate, failure);
            object source = GetState(plain);
            ResetSource(source, token);
            plain = (OnityTask)source.GetType().GetProperty("Task", k_instance).GetValue(source);
            return new Cycle(gate, source, () => plain.AsTask(),
                () => { plain.GetAwaiter().GetResult(); return 0; },
                callback => plain.GetAwaiter().UnsafeOnCompleted(callback),
                () => { plain.Preserve().GetAwaiter().GetResult(); return 0; }, () => plain.IsCompleted);
        }

        private static void ResetSource(object source, CancellationToken token)
        {
            MethodInfo reset = source.GetType().BaseType.GetMethod("Reset", k_instance,
                null, new[] { typeof(CancellationToken) }, null);
            Assert.That(reset, Is.Not.Null);
            reset.Invoke(source, new object[] { token });
        }

        private static FieldInfo FindInstanceField(Type type, string name)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(
                    name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    return field;
                }
            }

            return null;
        }

        private static void FinishCycle(Cycle cycle)
        {
            if (cycle == null)
            {
                return;
            }
            cycle.Gate.Complete();
            ConsumeForCleanup(cycle.GetResult);
        }

        private static void ConsumeForCleanup(Func<int> getResult)
        {
            try
            {
                getResult();
            }
            catch (Exception)
            {
                // Consume terminal faults/cancellation, or ignore the token already retired by the test.
            }
        }

        private static async OnityTask CompleteOutcomeAfterAsync(Gate gate, Exception failure)
        {
            await gate;
            if (failure != null)
            {
                throw failure;
            }
        }

        private static async OnityTask<int> ReturnOutcomeAfterAsync(Gate gate, int value, Exception failure)
        {
            await gate;
            if (failure != null)
            {
                throw failure;
            }
            return value;
        }

        private static void StartRace(Barrier barrier)
        {
            if (!barrier.SignalAndWait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("Race participant did not start.");
            }
        }

        private static void Join(params Task[] workers)
        {
            Assert.That(Task.WaitAll(workers, TimeSpan.FromSeconds(5)), Is.True,
                "Owned worker or bridge timed out.");
        }

        private static void AssertExpectedRejection(Exception exception)
        {
            if (exception != null)
            {
                Assert.That(exception, Is.TypeOf<InvalidOperationException>());
            }
        }

        private static void AssertFreshCycle(Cycle first, Cycle next, bool typed)
        {
            Assert.That(next, Is.Not.Null);
            Assert.That(next.State, Is.SameAs(first.State));
            Assert.That(next.IsCompleted(), Is.False);
            next.Gate.Complete();
            Assert.That(next.GetResult(), Is.EqualTo(typed ? 22 : 0));
        }

        private static Cycle CreateCycle(bool typed, int value)
        {
            Gate gate = new Gate();
            if (typed)
            {
                OnityTask<int> task = ReturnAfterAsync(gate, value);
                return new Cycle(gate, GetState(task), () => task.AsTask(),
                    () => task.GetAwaiter().GetResult(),
                    callback => task.GetAwaiter().UnsafeOnCompleted(callback),
                    () => task.Preserve().GetAwaiter().GetResult(), () => task.IsCompleted);
            }
            OnityTask plain = CompleteAfterAsync(gate);
            return new Cycle(gate, GetState(plain), () => plain.AsTask(),
                () => { plain.GetAwaiter().GetResult(); return 0; },
                callback => plain.GetAwaiter().UnsafeOnCompleted(callback),
                () => { plain.Preserve().GetAwaiter().GetResult(); return 0; }, () => plain.IsCompleted);
        }

        private sealed class Cycle
        {
            public Gate Gate { get; }
            public object State { get; }
            public Func<Task> AsTask { get; }
            public Func<int> GetResult { get; }
            public Action<Action> Register { get; }
            public Func<int> GetPreservedResult { get; }
            public Func<bool> IsCompleted { get; }

            public Cycle(Gate gate, object state, Func<Task> asTask, Func<int> getResult,
                Action<Action> register, Func<int> getPreservedResult, Func<bool> isCompleted)
            {
                Gate = gate;
                State = state;
                AsTask = asTask;
                GetResult = getResult;
                Register = register;
                GetPreservedResult = getPreservedResult;
                IsCompleted = isCompleted;
            }
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
