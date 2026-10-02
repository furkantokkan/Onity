using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Covers the intrusive compare-and-swap gate pools behind the PlayerLoop, EndOfFrame and JobHandle
    /// task sources, the array WhenAny sources and the typed WhenAll coordinator: a released instance
    /// is rented again with a new version, a stale task value is rejected before and after the reuse,
    /// the retention cap holds, cancellation registrations do not leak into a later rental, and
    /// concurrent rent and release never hands one instance to two owners. The internal sources are
    /// driven through reflection because the production types are not visible to this assembly.
    /// </summary>
    [TestFixture]
    public sealed class OnityTaskSourcePoolEditModeTests
    {
        private const BindingFlags k_all =
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        // Explicit timing, end-of-frame and JobHandle sources share OnityTask.SourcePoolCapacity.
        private static int SourceCap => OnityTask.SourcePoolCapacity;
        private const int k_whenAnySmallCap = 256;
        private const int k_whenAnyMediumCap = 128;
        private const int k_whenAllCap = 256;

        private static readonly Assembly s_runtime = typeof(OnityTask).Assembly;

        [TestCase("PlayerLoop")]
        [TestCase("EndOfFrame")]
        [TestCase("JobHandle")]
        public void ReleasedSource_IsRentedAgain_WithANewVersionAndAClearedPoolLink(string kind)
        {
            object first = Rent(kind);
            int token = VersionOf(first);
            Assert.That(StatusOf(first, token), Is.EqualTo("Pending"));
            Publish(first, false, null);
            Assert.That(StatusOf(first, token), Is.EqualTo("Succeeded"));
            GetResultOf(first, token);

            object second = Rent(kind);
            Assert.That(second, Is.SameAs(first), "the pool hands the released source back");
            int nextToken = VersionOf(second);
            Assert.That(nextToken, Is.Not.EqualTo(token));
            Assert.That(StatusOf(second, nextToken), Is.EqualTo("Pending"));
            Assert.That(second.GetType().GetField("m_nextPooled", k_all).GetValue(second), Is.Null,
                "a rented source must not keep its pool link");

            Publish(second, false, null);
            GetResultOf(second, nextToken);
        }

        [TestCase("PlayerLoop")]
        [TestCase("EndOfFrame")]
        [TestCase("JobHandle")]
        public void StaleToken_AfterNativeRelease_IsRejectedBeforeAndAfterReuse(string kind)
        {
            object first = Rent(kind);
            int token = VersionOf(first);
            Publish(first, false, null);
            GetResultOf(first, token);

            // The consumption itself retired the version, before the source can be pooled again.
            AssertStale(first, token);

            object second = Rent(kind);
            Assert.That(second, Is.SameAs(first));
            int nextToken = VersionOf(second);
            AssertStale(second, token);
            Assert.That(StatusOf(second, nextToken), Is.EqualTo("Pending"),
                "a stale call must leave the new cycle alone");

            Publish(second, false, null);
            GetResultOf(second, nextToken);
        }

        [TestCase("PlayerLoop", false)]
        [TestCase("PlayerLoop", true)]
        [TestCase("EndOfFrame", false)]
        [TestCase("EndOfFrame", true)]
        [TestCase("JobHandle", false)]
        [TestCase("JobHandle", true)]
        public void StaleToken_AfterBridgeRelease_IsRejectedBeforeAndAfterReuse(string kind, bool publishFirst)
        {
            object first = Rent(kind);
            int token = VersionOf(first);
            Task bridge;
            if (publishFirst)
            {
                Publish(first, false, null);
                bridge = AsTaskOf(first, token);
            }
            else
            {
                bridge = AsTaskOf(first, token);
                Assert.That(bridge.IsCompleted, Is.False);
                Publish(first, false, null);
            }

            Assert.That(bridge.IsCompleted, Is.True);
            Assert.That(bridge.IsFaulted || bridge.IsCanceled, Is.False);

            // The bridge release claimed and retired the version in one step.
            AssertStale(first, token);

            object second = Rent(kind);
            Assert.That(second, Is.SameAs(first));
            AssertStale(second, token);
            int nextToken = VersionOf(second);
            Assert.That(StatusOf(second, nextToken), Is.EqualTo("Pending"));
            Publish(second, false, null);
            GetResultOf(second, nextToken);
        }

        [TestCase("PlayerLoop", false)]
        [TestCase("PlayerLoop", true)]
        [TestCase("EndOfFrame", false)]
        [TestCase("EndOfFrame", true)]
        [TestCase("JobHandle", false)]
        [TestCase("JobHandle", true)]
        public void FaultedAndCanceledSources_AreReleasedAndRentedAgain(string kind, bool canceled)
        {
            object first = Rent(kind);
            int token = VersionOf(first);
            InvalidOperationException fault = new InvalidOperationException("pool fault");
            Publish(first, canceled, canceled ? null : fault);
            if (canceled)
            {
                Assert.Throws<OperationCanceledException>(() => GetResultOf(first, token));
            }
            else
            {
                Assert.That(Assert.Throws<InvalidOperationException>(() => GetResultOf(first, token)),
                    Is.SameAs(fault));
            }

            AssertStale(first, token);
            object second = Rent(kind);
            Assert.That(second, Is.SameAs(first));
            int nextToken = VersionOf(second);
            Assert.That(StatusOf(second, nextToken), Is.EqualTo("Pending"));
            Publish(second, false, null);
            GetResultOf(second, nextToken);
        }

        [TestCase("PlayerLoop")]
        [TestCase("EndOfFrame")]
        public void CancellationRegistration_IsFlaggedWhileRented_AndNeverLeaksIntoTheNextRental(string kind)
        {
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                object first = Rent(kind, cancellation.Token);
                int token = VersionOf(first);
                Assert.That(IsCancellationFlagged(first), Is.False);
                cancellation.Cancel();
                Assert.That(IsCancellationFlagged(first), Is.True,
                    "the registration must exist by the time Rent returns");
                Publish(first, true, null);
                Assert.Throws<OperationCanceledException>(() => GetResultOf(first, token));

                object second = Rent(kind);
                Assert.That(second, Is.SameAs(first));
                Assert.That(IsCancellationFlagged(second), Is.False, "the flag is cleared for the new rental");
                int nextToken = VersionOf(second);
                Publish(second, false, null);
                GetResultOf(second, nextToken);
            }
        }

        [TestCase("PlayerLoop")]
        [TestCase("EndOfFrame")]
        public void PrecanceledToken_FlagsTheRentalBeforeRentReturns(string kind)
        {
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                object source = Rent(kind, cancellation.Token);
                int token = VersionOf(source);
                Assert.That(IsCancellationFlagged(source), Is.True);
                Publish(source, true, null);
                Assert.Throws<OperationCanceledException>(() => GetResultOf(source, token));
            }
        }

        [TestCase("PlayerLoop")]
        [TestCase("EndOfFrame")]
        [TestCase("JobHandle")]
        public void SourcePool_RetainsAtMostItsCap_AndDropsTheRest(string kind)
        {
            const int extra = 3;
            List<object> drained = new List<object>(SourceCap + 1);
            for (int i = 0; i <= SourceCap; i++)
            {
                drained.Add(Rent(kind));
            }

            // The pool is empty now: it can hold at most the cap.
            HashSet<object> released = new HashSet<object>();
            List<object> sources = new List<object>(SourceCap + extra);
            for (int i = 0; i < SourceCap + extra; i++)
            {
                object source = Rent(kind);
                Assert.That(released.Add(source), Is.True, "an empty pool must allocate distinct sources");
                sources.Add(source);
            }

            foreach (object source in sources)
            {
                int token = VersionOf(source);
                Publish(source, false, null);
                GetResultOf(source, token);
            }

            int reused = 0;
            List<object> rented = new List<object>(SourceCap + extra);
            for (int i = 0; i < SourceCap + extra; i++)
            {
                object source = Rent(kind);
                rented.Add(source);
                if (released.Contains(source))
                {
                    reused++;
                }
            }

            Assert.That(reused, Is.EqualTo(SourceCap));
            foreach (object source in rented)
            {
                int token = VersionOf(source);
                Publish(source, false, null);
                GetResultOf(source, token);
            }

            foreach (object source in drained)
            {
                int token = VersionOf(source);
                Publish(source, false, null);
                GetResultOf(source, token);
            }
        }

        [TestCase("PlayerLoop")]
        [TestCase("EndOfFrame")]
        [TestCase("JobHandle")]
        public void ConcurrentRentAndRelease_NeverHandsOneInstanceToTwoOwners(string kind)
        {
            const int workers = 4;
            const int iterations = 3000;
            ConcurrentDictionary<object, int> owners = new ConcurrentDictionary<object, int>();
            ConcurrentQueue<string> failures = new ConcurrentQueue<string>();
            Task[] tasks = new Task[workers];
            for (int worker = 0; worker < workers; worker++)
            {
                int id = worker + 1;
                tasks[worker] = Task.Run(() =>
                {
                    try
                    {
                        for (int i = 0; i < iterations; i++)
                        {
                            object source = Rent(kind);
                            if (!owners.TryAdd(source, id))
                            {
                                failures.Enqueue("source rented twice at iteration " + i);
                                return;
                            }

                            int token = VersionOf(source);
                            Publish(source, false, null);
                            owners.TryRemove(source, out _);
                            GetResultOf(source, token);
                        }
                    }
                    catch (Exception exception)
                    {
                        failures.Enqueue(exception.ToString());
                    }
                });
            }

            Assert.That(Task.WaitAll(tasks, TimeSpan.FromSeconds(60)), Is.True);
            Assert.That(failures, Is.Empty);
        }

        [TestCase(false, 2)]
        [TestCase(false, 20)]
        [TestCase(true, 2)]
        [TestCase(true, 20)]
        public void WhenAny_ReleasedSource_IsRentedAgain_AndStaleValuesThrow(bool typed, int inputCount)
        {
            Race first = StartRace(typed, inputCount);
            first.CompleteAll();
            first.Consume();
            first.AssertStale();

            Race second = StartRace(typed, inputCount);
            try
            {
                Assert.That(second.State, Is.SameAs(first.State), "the pool hands the released source back");
                first.AssertStale();
                Assert.That(second.IsCompleted(), Is.False, "a stale call must leave the new race alone");
                second.CompleteAll();
                second.Consume();
            }
            finally
            {
                second.CompleteAll();
            }
        }

        [TestCase(false, 2, k_whenAnySmallCap)]
        [TestCase(true, 2, k_whenAnySmallCap)]
        [TestCase(false, 20, k_whenAnyMediumCap)]
        [TestCase(true, 20, k_whenAnyMediumCap)]
        public void WhenAny_PoolRetainsAtMostItsCap_PerBucket(bool typed, int inputCount, int cap)
        {
            int total = cap + 4;
            List<Race> drained = new List<Race>(total);
            List<Race> released = new List<Race>(total);
            List<Race> reused = new List<Race>(total);
            try
            {
                for (int i = 0; i < total; i++)
                {
                    drained.Add(StartRace(typed, inputCount));
                }

                HashSet<object> releasedStates = new HashSet<object>();
                for (int i = 0; i < total; i++)
                {
                    Race race = StartRace(typed, inputCount);
                    Assert.That(releasedStates.Add(race.State), Is.True);
                    released.Add(race);
                }

                foreach (Race race in released)
                {
                    race.CompleteAll();
                    race.Consume();
                }

                int reusedCount = 0;
                for (int i = 0; i < total; i++)
                {
                    Race race = StartRace(typed, inputCount);
                    reused.Add(race);
                    if (releasedStates.Contains(race.State))
                    {
                        reusedCount++;
                    }
                }

                Assert.That(reusedCount, Is.EqualTo(cap));
            }
            finally
            {
                foreach (Race race in drained)
                {
                    race.CompleteAll();
                }

                foreach (Race race in reused)
                {
                    race.CompleteAll();
                }
            }
        }

        [Test]
        public void WhenAllCoordinator_ReleasedInstance_IsRentedAgain_WithoutRetainedState()
        {
            OnityTaskCompletionSource<int>[] firstSources = CreateSources(3);
            object first = RentCoordinator(firstSources, out Task<int[]> firstOutput);
            firstSources[2].TrySetResult(30);
            firstSources[0].TrySetResult(10);
            Assert.That(firstOutput.IsCompleted, Is.False);
            firstSources[1].TrySetResult(20);
            Assert.That(firstOutput.GetAwaiter().GetResult(), Is.EqualTo(new[] { 10, 20, 30 }));

            OnityTaskCompletionSource<int>[] secondSources = CreateSources(2);
            object second = RentCoordinator(secondSources, out Task<int[]> secondOutput);
            Assert.That(second, Is.SameAs(first), "the pool hands the released coordinator back");
            Assert.That(second.GetType().GetField("m_nextPooled", k_all).GetValue(second), Is.Null);
            Assert.That(secondOutput.IsCompleted, Is.False);
            secondSources[0].TrySetResult(1);
            secondSources[1].TrySetResult(2);
            Assert.That(secondOutput.GetAwaiter().GetResult(), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(firstOutput.GetAwaiter().GetResult(), Is.EqualTo(new[] { 10, 20, 30 }),
                "a retained output must survive the reuse");
        }

        [Test]
        public void WhenAllCoordinator_PoolRetainsAtMostItsCap()
        {
            const int extra = 4;
            int total = k_whenAllCap + extra;
            List<OnityTaskCompletionSource<int>[]> drainedSources = new List<OnityTaskCompletionSource<int>[]>(total);
            List<OnityTaskCompletionSource<int>[]> reusedSources = new List<OnityTaskCompletionSource<int>[]>(total);
            try
            {
                for (int i = 0; i < total; i++)
                {
                    OnityTaskCompletionSource<int>[] sources = CreateSources(1);
                    drainedSources.Add(sources);
                    RentCoordinator(sources, out _);
                }

                // Rent them all before completing any, so each one is a distinct coordinator.
                HashSet<object> released = new HashSet<object>();
                List<OnityTaskCompletionSource<int>[]> releasedSources =
                    new List<OnityTaskCompletionSource<int>[]>(total);
                for (int i = 0; i < total; i++)
                {
                    OnityTaskCompletionSource<int>[] sources = CreateSources(1);
                    Assert.That(released.Add(RentCoordinator(sources, out _)), Is.True);
                    releasedSources.Add(sources);
                }

                for (int i = 0; i < total; i++)
                {
                    releasedSources[i][0].TrySetResult(i);
                }

                int reusedCount = 0;
                for (int i = 0; i < total; i++)
                {
                    OnityTaskCompletionSource<int>[] sources = CreateSources(1);
                    reusedSources.Add(sources);
                    if (released.Contains(RentCoordinator(sources, out _)))
                    {
                        reusedCount++;
                    }
                }

                Assert.That(reusedCount, Is.EqualTo(k_whenAllCap));
            }
            finally
            {
                foreach (OnityTaskCompletionSource<int>[] sources in drainedSources)
                {
                    sources[0].TrySetResult(0);
                }

                foreach (OnityTaskCompletionSource<int>[] sources in reusedSources)
                {
                    sources[0].TrySetResult(0);
                }
            }
        }

        private static Type SourceType(string kind)
        {
            return s_runtime.GetType("Onity.Unity.Async.Onity" + kind + "TaskSource", true);
        }

        private static object Rent(string kind, CancellationToken token = default)
        {
            Type type = SourceType(kind);
            MethodInfo rent = type.GetMethod("Rent", k_all);
            Assert.That(rent, Is.Not.Null);
            object[] arguments;
            switch (kind)
            {
                case "PlayerLoop":
                    // The last argument is cancelImmediately: these tests cover the flag-only registration.
                    arguments = new object[] { 1UL, 2u, 1, token, false };
                    break;
                case "EndOfFrame":
                    arguments = new object[] { 1UL, token, false };
                    break;
                default:
                    arguments = new object[0];
                    break;
            }

            return Call(rent, null, arguments);
        }

        private static void Publish(object source, bool canceled, Exception failure)
        {
            // PlayerLoop and EndOfFrame sources publish the cycle their queue entry names (PERF-7); the
            // JobHandle source keeps the unversioned form.
            MethodInfo versioned = source.GetType().GetMethod(
                "Publish", k_all, null, new[] { typeof(int), typeof(bool), typeof(Exception) }, null);
            if (versioned != null)
            {
                Call(versioned, source, VersionOf(source), canceled, failure);
                return;
            }

            MethodInfo publish = source.GetType().GetMethod(
                "Publish", k_all, null, new[] { typeof(bool), typeof(Exception) }, null);
            Assert.That(publish, Is.Not.Null);
            Call(publish, source, canceled, failure);
        }

        private static int VersionOf(object source)
        {
            return (int)source.GetType().GetProperty("Version", k_all).GetValue(source);
        }

        private static bool IsCancellationFlagged(object source)
        {
            return (bool)source.GetType().GetProperty("IsCancellationFlagged", k_all).GetValue(source);
        }

        private static string StatusOf(object source, int token)
        {
            MethodInfo method = source.GetType().GetMethod("GetStatus", k_all, null, new[] { typeof(int) }, null);
            return Call(method, source, token).ToString();
        }

        private static void GetResultOf(object source, int token)
        {
            MethodInfo method = source.GetType().GetMethod("GetResult", k_all, null, new[] { typeof(int) }, null);
            Call(method, source, token);
        }

        private static Task AsTaskOf(object source, int token)
        {
            MethodInfo method = source.GetType().GetMethod("AsTask", k_all, null, new[] { typeof(int) }, null);
            return (Task)Call(method, source, token);
        }

        private static void OnCompletedOf(object source, Action continuation, int token)
        {
            MethodInfo method = source.GetType().GetMethod(
                "OnCompleted", k_all, null, new[] { typeof(Action), typeof(int) }, null);
            Call(method, source, continuation, token);
        }

        private static void AssertStale(object source, int token)
        {
            Assert.Throws<InvalidOperationException>(() => StatusOf(source, token));
            Assert.Throws<InvalidOperationException>(() => GetResultOf(source, token));
            Assert.Throws<InvalidOperationException>(() => AsTaskOf(source, token));
            Assert.Throws<InvalidOperationException>(() => OnCompletedOf(source, () => { }, token));
        }

        private static object Call(MethodInfo method, object target, params object[] arguments)
        {
            try
            {
                return method.Invoke(target, arguments);
            }
            catch (TargetInvocationException exception)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
        }

        private static OnityTaskCompletionSource<int>[] CreateSources(int count)
        {
            OnityTaskCompletionSource<int>[] sources = new OnityTaskCompletionSource<int>[count];
            for (int i = 0; i < count; i++)
            {
                sources[i] = new OnityTaskCompletionSource<int>();
            }

            return sources;
        }

        private static object RentCoordinator(OnityTaskCompletionSource<int>[] sources, out Task<int[]> output)
        {
            Type type = s_runtime.GetType("Onity.Unity.Async.OnityWhenAllTypedCoordinator`1", true)
                .MakeGenericType(typeof(int));
            OnityTask<int>[] inputs = new OnityTask<int>[sources.Length];
            for (int i = 0; i < sources.Length; i++)
            {
                inputs[i] = sources[i].Task;
            }

            object[] arguments = { inputs, null };
            bool rented = (bool)Call(type.GetMethod("TryRent", k_all), null, arguments);
            Assert.That(rented, Is.True, "pending completion-source inputs must be coordinator eligible");
            object coordinator = arguments[1];
            output = (Task<int[]>)Call(type.GetMethod("Start", k_all), coordinator);
            return coordinator;
        }

        private static Race StartRace(bool typed, int inputCount)
        {
            OnityTaskCompletionSource<int>[] sources = CreateSources(inputCount);
            if (typed)
            {
                OnityTask<int>[] inputs = new OnityTask<int>[inputCount];
                for (int i = 0; i < inputCount; i++)
                {
                    inputs[i] = sources[i].Task;
                }

                OnityTask<(int winnerIndex, int result)> race = OnityTask.WhenAny(inputs);
                return new Race
                {
                    State = StateOf(race),
                    CompleteAll = () => CompleteAll(sources),
                    Consume = () => race.GetAwaiter().GetResult(),
                    IsCompleted = () => race.IsCompleted,
                    AssertStale = () =>
                    {
                        Assert.Throws<InvalidOperationException>(() => race.GetAwaiter().GetResult());
                        Assert.Throws<InvalidOperationException>(() => race.AsTask());
                    }
                };
            }

            OnityTaskCompletionSource[] plainSources = new OnityTaskCompletionSource[inputCount];
            OnityTask[] plainInputs = new OnityTask[inputCount];
            for (int i = 0; i < inputCount; i++)
            {
                plainSources[i] = new OnityTaskCompletionSource();
                plainInputs[i] = plainSources[i].Task;
            }

            OnityTask<int> plainRace = OnityTask.WhenAny(plainInputs);
            return new Race
            {
                State = StateOf(plainRace),
                CompleteAll = () =>
                {
                    for (int i = 0; i < plainSources.Length; i++)
                    {
                        plainSources[i].TrySetResult();
                    }
                },
                Consume = () => plainRace.GetAwaiter().GetResult(),
                IsCompleted = () => plainRace.IsCompleted,
                AssertStale = () =>
                {
                    Assert.Throws<InvalidOperationException>(() => plainRace.GetAwaiter().GetResult());
                    Assert.Throws<InvalidOperationException>(() => plainRace.AsTask());
                }
            };
        }

        private static void CompleteAll(OnityTaskCompletionSource<int>[] sources)
        {
            for (int i = 0; i < sources.Length; i++)
            {
                sources[i].TrySetResult(i);
            }
        }

        private static object StateOf<T>(OnityTask<T> task)
        {
            return typeof(OnityTask<T>).GetField("m_state", k_all).GetValue(task);
        }

        private sealed class Race
        {
            public object State;
            public Action CompleteAll;
            public Action Consume;
            public Func<bool> IsCompleted;
            public Action AssertStale;
        }
    }
}
