using System;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;
using Unity.Collections;
using Unity.Jobs;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Onity.Tests.EditMode
{
    [TestFixture]
    public sealed class OnityJobHandleEditModeTests
    {
        private static readonly object s_domainSentinel = new object();

        [UnityTearDown]
        public IEnumerator ExitPlayModeAfterFailure()
        {
            if (Application.isPlaying)
            {
                yield return new ExitPlayMode();
            }
        }

        [UnityTearDown]
        public IEnumerator RestoreEditMode()
        {
            if (Application.isPlaying)
            {
                yield return new ExitPlayMode();
            }
        }

        [Test]
        public void DefaultAndPrecompletedHandles_CompleteInline()
        {
            OnityTask empty = default(JobHandle).AsOnityTask();
            Assert.That(empty.IsCompletedSuccessfully, Is.True);
            empty.GetAwaiter().GetResult();
            using PendingWork work = new PendingWork();
            work.Handle.Complete();
            OnityTask completed = work.Handle.AsOnityTask();
            Assert.That(completed.IsCompletedSuccessfully, Is.True);
            completed.GetAwaiter().GetResult();
            Assert.That(work.Read(), Is.EqualTo(work.Expected));
        }

        [UnityTest]
        public IEnumerator PendingHandle_PublicationAllowsSafeDataAccessOnMainThread()
        {
            int mainThread = Thread.CurrentThread.ManagedThreadId;
            using PendingWork work = PendingWork.Accept();
            Exception error = null;
            int observed = 0;
            int thread = 0;
            bool completed = false;
            work.Task.GetAwaiter().UnsafeOnCompleted(() =>
            {
                try
                {
                    work.Task.GetAwaiter().GetResult();
                    observed = work.Read();
                    thread = Thread.CurrentThread.ManagedThreadId;
                }
                catch (Exception exception)
                {
                    error = exception;
                }
                finally
                {
                    completed = true;
                }
            });
            yield return WaitFor(() => completed);
            Assert.That(error, Is.Null);
            Assert.That(observed, Is.EqualTo(work.Expected));
            Assert.That(thread, Is.EqualTo(mainThread));
        }

        [UnityTest]
        public IEnumerator CombinedAndDependentDisposalHandles_SettleTheirDependencies()
        {
            using PendingWork first = new PendingWork();
            using PendingWork second = new PendingWork();
            JobHandle combined = JobHandle.CombineDependencies(first.Handle, second.Handle);
            Task bridge = combined.AsOnityTask().AsTask();
            yield return WaitFor(() => bridge.IsCompleted);
            bridge.GetAwaiter().GetResult();
            Assert.That(first.Read(), Is.EqualTo(first.Expected));
            Assert.That(second.Read(), Is.EqualTo(second.Expected));

            NativeArray<int> output = new NativeArray<int>(1, Allocator.Persistent);
            JobHandle compute = default;
            JobHandle disposal = default;
            AssemblyReloadEvents.AssemblyReloadCallback cleanup = () =>
            {
                try
                {
                    disposal.Complete();
                }
                finally
                {
                    compute.Complete();
                    if (output.IsCreated)
                    {
                        output.Dispose();
                    }
                }
            };
            AssemblyReloadEvents.beforeAssemblyReload += cleanup;
            try
            {
                compute = new ComputeJob { Output = output, Iterations = 200000 }.Schedule();
                disposal = output.Dispose(compute);
                Task disposed = disposal.AsOnityTask().AsTask();
                yield return WaitFor(() => disposed.IsCompleted);
                disposed.GetAwaiter().GetResult();
                Assert.That(disposal.IsCompleted, Is.True);
            }
            finally
            {
                AssemblyReloadEvents.beforeAssemblyReload -= cleanup;
                cleanup();
            }
        }

        [UnityTest]
        public IEnumerator WorkerCall_IsRejectedWithoutTakingCompletionOwnership()
        {
            using PendingWork work = new PendingWork();
            Task<Exception> rejection = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    work.Handle.AsOnityTask();
                    return null;
                }
                catch (Exception exception)
                {
                    return exception;
                }
            });
            yield return WaitFor(() => rejection.IsCompleted);
            Assert.That(rejection.GetAwaiter().GetResult(), Is.TypeOf<InvalidOperationException>());
            work.Handle.Complete();
            Assert.That(work.Read(), Is.EqualTo(work.Expected));
        }

        [UnityTest]
        public IEnumerator BridgePreserveAndWorkerConsumption_KeepSingleConsumerAndPoolRules()
        {
            using PendingWork bridged = PendingWork.Accept();
            OnityTask original = bridged.Task;
            Task bridge = original.AsTask();
            Assert.That(original.AsTask(), Is.SameAs(bridge));
            Assert.Throws<InvalidOperationException>(() => original.GetAwaiter().OnCompleted(() => { }));
            Assert.Throws<InvalidOperationException>(() => original.GetAwaiter().GetResult());
            yield return WaitFor(() => bridge.IsCompleted);
            bridge.GetAwaiter().GetResult();

            using PendingWork preserved = PendingWork.Accept();
            OnityTask shared = preserved.Task.Preserve();
            int subscribers = 0;
            shared.GetAwaiter().OnCompleted(() => subscribers++);
            shared.GetAwaiter().OnCompleted(() => subscribers++);
            yield return WaitFor(() => subscribers == 2);
            shared.GetAwaiter().GetResult();
            shared.GetAwaiter().GetResult();

            using PendingWork native = PendingWork.Accept();
            OnityTask stale = native.Task;
            yield return WaitFor(() => stale.IsCompleted);
            Task consumer = System.Threading.Tasks.Task.Run(() => stale.GetAwaiter().GetResult());
            yield return WaitFor(() => consumer.IsCompleted);
            consumer.GetAwaiter().GetResult();
            Assert.Throws<InvalidOperationException>(() => _ = stale.IsCompleted);
            using PendingWork next = PendingWork.Accept();
            Task nextBridge = next.Task.AsTask();
            yield return WaitFor(() => nextBridge.IsCompleted);
            nextBridge.GetAwaiter().GetResult();
            Assert.That(next.Read(), Is.EqualTo(next.Expected));
            Assert.Throws<InvalidOperationException>(() => stale.GetAwaiter().GetResult());
        }

        [Test]
        public void RunnerRetirement_CompletesUnawaitedWorkBeforeCancellation()
        {
            using PendingWork work = PendingWork.Accept();
            UnityEngine.Object.DestroyImmediate(FindRunner());
            Assert.That(work.Read(), Is.EqualTo(work.Expected));
            Assert.That(work.Task.IsCanceled, Is.True);
            Assert.Catch<OperationCanceledException>(() => work.Task.GetAwaiter().GetResult());
            Assert.DoesNotThrow(() => default(JobHandle).AsOnityTask().GetAwaiter().GetResult());
        }

        [Test]
        public void PumpContinuation_CanRetireRemainingEntriesAndRejectReentrantRegistration()
        {
            using PendingWork remaining = PendingWork.Accept();
            using PendingWork trigger = PendingWork.Accept();
            Exception callbackError = null;
            Exception rejection = null;
            int calls = 0;
            trigger.Task.GetAwaiter().OnCompleted(() =>
            {
                try
                {
                    trigger.Task.GetAwaiter().GetResult();
                    UnityEngine.Object.DestroyImmediate(FindRunner());
                    calls++;
                }
                catch (Exception exception)
                {
                    callbackError = exception;
                }
            });
            remaining.Task.GetAwaiter().OnCompleted(() =>
            {
                try
                {
                    remaining.Task.GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                    try
                    {
                        default(JobHandle).AsOnityTask();
                    }
                    catch (Exception exception)
                    {
                        rejection = exception;
                    }
                }
                catch (Exception exception)
                {
                    callbackError = exception;
                }
            });
            trigger.Handle.Complete();
            Registry.GetMethod("Pump", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            Assert.That(callbackError, Is.Null);
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(rejection, Is.TypeOf<InvalidOperationException>());
            Assert.That(remaining.Read(), Is.EqualTo(remaining.Expected));
            Assert.DoesNotThrow(() => default(JobHandle).AsOnityTask());
        }

        [UnityTest]
        public IEnumerator ActualReloadDisabledPlayEditPlay_AcceptsAwakeAndReopensSessions()
        {
            if (!EditorSettings.enterPlayModeOptionsEnabled
                || (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableDomainReload) == 0
                || (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableSceneReload) == 0)
            {
                Assert.Ignore("Requires the verification host's existing disabled domain and scene reload settings.");
            }
            object domainSentinel = s_domainSentinel;
            Type probeType = Type.GetType(
                "Onity.Tests.PlayMode.OnityJobHandleAwakeProbe, Onity.Tests.PlayMode", true);
            Component first = null;
            Component second = null;
            try
            {
                first = new GameObject("Onity Awake Job Probe First").AddComponent(probeType);
                yield return new EnterPlayMode(false);
                Assert.That(ReferenceEquals(domainSentinel, s_domainSentinel), Is.True, "Domain reloaded on first entry.");
                yield return WaitFor(() => ReadProbe<bool>(first, "Completed"));
                CheckAwakeProbe(first);
                using (PendingWork exiting = PendingWork.Accept())
                {
                    yield return new ExitPlayMode();
                    Assert.That(ReferenceEquals(domainSentinel, s_domainSentinel), Is.True, "Domain reloaded on first exit.");
                    DestroyProbe(first);
                    first = null;
                    Assert.That(exiting.Task.IsCompleted, Is.True,
                        "Play exit abandoned an accepted completion obligation.");
                    Assert.That(exiting.Read(), Is.EqualTo(exiting.Expected));
                    try
                    {
                        exiting.Task.GetAwaiter().GetResult();
                    }
                    catch (OperationCanceledException)
                    {
                        // Exit may follow a final Update that already published successful completion.
                    }
                }
                Assert.That(Application.isPlaying, Is.False);
                using (PendingWork edit = PendingWork.Accept())
                {
                    Task editBridge = edit.Task.AsTask();
                    yield return WaitFor(() => editBridge.IsCompleted);
                    editBridge.GetAwaiter().GetResult();
                    Assert.That(edit.Read(), Is.EqualTo(edit.Expected));
                }
                second = new GameObject("Onity Awake Job Probe Second").AddComponent(probeType);
                yield return new EnterPlayMode(false);
                Assert.That(ReferenceEquals(domainSentinel, s_domainSentinel), Is.True, "Domain reloaded on second entry.");
                yield return WaitFor(() => ReadProbe<bool>(second, "Completed"));
                CheckAwakeProbe(second);
                yield return new ExitPlayMode();
                Assert.That(ReferenceEquals(domainSentinel, s_domainSentinel), Is.True, "Domain reloaded on second exit.");
                Assert.DoesNotThrow(() => default(JobHandle).AsOnityTask());
            }
            finally
            {
                DestroyProbe(first);
                DestroyProbe(second);
            }
        }

        private static T ReadProbe<T>(Component probe, string field)
        {
            Assert.That(probe != null, Is.True, "Awake probe disappeared across a reload-disabled transition.");
            return (T)probe.GetType().GetField(field).GetValue(probe);
        }

        private static void CheckAwakeProbe(Component probe)
        {
            Assert.That(ReadProbe<string>(probe, "Error"), Is.Null);
            Assert.That(ReadProbe<bool>(probe, "RegisteredInAwake"), Is.True);
            Assert.That(ReadProbe<bool>(probe, "AwakeBeforeEnteredPlay"), Is.True);
            Assert.That(ReadProbe<int>(probe, "Result"), Is.EqualTo(ReadProbe<int>(probe, "Expected")));
        }

        private static void DestroyProbe(Component probe)
        {
            if (probe != null && probe.gameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(probe.gameObject);
            }
        }

        private static Type Registry => typeof(OnityTask).Assembly.GetType(
            "Onity.Unity.Async.OnityJobHandleRegistry", true);

        private static GameObject FindRunner()
        {
            foreach (GameObject candidate in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (candidate.name == "OnityTaskRunner")
                {
                    return candidate;
                }
            }
            Assert.Fail("Accepted pending work did not create its runner.");
            return null;
        }

        private static IEnumerator WaitFor(Func<bool> predicate)
        {
            Stopwatch timer = Stopwatch.StartNew();
            while (!predicate() && timer.Elapsed.TotalSeconds < 10)
            {
                yield return null;
            }
            Assert.That(predicate(), Is.True, "Job adapter operation timed out.");
        }

        public sealed class PendingWork : IDisposable
        {
            private NativeArray<int> m_output;
            public JobHandle Handle;
            public OnityTask Task;
            public int Expected;

            public PendingWork(int iterations = 200000)
            {
                m_output = new NativeArray<int>(1, Allocator.Persistent);
                AssemblyReloadEvents.beforeAssemblyReload += Dispose;
                Expected = Calculate(iterations);
                Handle = new ComputeJob { Output = m_output, Iterations = iterations }.Schedule();
            }

            public static PendingWork Accept()
            {
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    PendingWork work = new PendingWork(200000 << (attempt * 2));
                    try
                    {
                        work.Task = work.Handle.AsOnityTask();
                    }
                    catch
                    {
                        work.Dispose();
                        throw;
                    }
                    if (!work.Task.IsCompleted)
                    {
                        return work;
                    }
                    work.Task.GetAwaiter().GetResult();
                    work.Dispose();
                }
                throw new InvalidOperationException("Could not observe a pending finite job after four attempts.");
            }

            public int Read() => m_output[0];

            public void Dispose()
            {
                AssemblyReloadEvents.beforeAssemblyReload -= Dispose;
                Handle.Complete();
                if (m_output.IsCreated)
                {
                    m_output.Dispose();
                }
            }
        }

        private struct ComputeJob : IJob
        {
            public NativeArray<int> Output;
            public int Iterations;
            public void Execute() => Output[0] = Calculate(Iterations);
        }

        private static int Calculate(int iterations)
        {
            int value = 17;
            for (int i = 0; i < iterations; i++)
            {
                value = unchecked(value * 1664525 + 1013904223);
            }
            return value;
        }
    }
}
