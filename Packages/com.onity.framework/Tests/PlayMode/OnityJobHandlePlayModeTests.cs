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
using UnityEngine;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Onity.Tests.PlayMode
{
    [TestFixture]
    public sealed class OnityJobHandlePlayModeTests
    {
        [UnityTest]
        public IEnumerator PendingJob_CompletesBeforePublicationAndAllowsCallbackRegistration()
        {
            int mainThread = Thread.CurrentThread.ManagedThreadId;
            using PendingWork first = PendingWork.Accept();
            PendingWork next = null;
            Exception error = null;
            bool completed = false;
            int firstFrame = -1;
            int nextFrame = -1;
            int observedThread = 0;
            try
            {
                first.Task.GetAwaiter().UnsafeOnCompleted(() =>
                {
                    try
                    {
                        first.Task.GetAwaiter().GetResult();
                        if (first.Read() != first.Expected)
                        {
                            throw new InvalidOperationException("First job data mismatch.");
                        }
                        firstFrame = Time.frameCount;
                        next = PendingWork.Accept();
                        next.Task.GetAwaiter().UnsafeOnCompleted(() =>
                        {
                            try
                            {
                                next.Task.GetAwaiter().GetResult();
                                if (next.Read() != next.Expected)
                                {
                                    throw new InvalidOperationException("Reentrant job data mismatch.");
                                }
                                observedThread = Thread.CurrentThread.ManagedThreadId;
                                nextFrame = Time.frameCount;
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
                    }
                    catch (Exception exception)
                    {
                        error = exception;
                        completed = true;
                    }
                });
                yield return WaitFor(() => completed);
                Assert.That(error, Is.Null);
                Assert.That(observedThread, Is.EqualTo(mainThread));
                Assert.That(nextFrame, Is.GreaterThan(firstFrame),
                    "A callback's new pending registration was polled in the current Update batch.");
            }
            finally
            {
                next?.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator RunnerDestroy_SettlesUnawaitedAndObservedJobs_ThenAcceptsFreshWork()
        {
            using PendingWork ignored = PendingWork.Accept();
            using PendingWork observed = PendingWork.Accept();
            Exception failure = null;
            int callbacks = 0;
            observed.Task.GetAwaiter().OnCompleted(() =>
            {
                try
                {
                    observed.Task.GetAwaiter().GetResult();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
                finally
                {
                    callbacks++;
                }
            });
            UnityEngine.Object.DestroyImmediate(FindRunner());
            Assert.That(callbacks, Is.EqualTo(1));
            Assert.That(failure, Is.TypeOf<OperationCanceledException>());
            Assert.That(ignored.Task.IsCanceled, Is.True);
            Assert.That(ignored.Read(), Is.EqualTo(ignored.Expected));
            Assert.That(observed.Read(), Is.EqualTo(observed.Expected));
            Assert.Catch<OperationCanceledException>(() => ignored.Task.GetAwaiter().GetResult());
            using PendingWork fresh = PendingWork.Accept();
            Task bridge = fresh.Task.AsTask();
            yield return WaitFor(() => bridge.IsCompleted);
            bridge.GetAwaiter().GetResult();
            Assert.That(fresh.Read(), Is.EqualTo(fresh.Expected));
        }

        [UnityTest]
        public IEnumerator RetirementCallback_ReplacesFrameRunner_AndKeepsMainDispatcherAlive()
        {
            int mainThread = Thread.CurrentThread.ManagedThreadId;
            using PendingWork work = PendingWork.Accept();
            Task frame = null;
            Exception callbackError = null;
            Exception rejection = null;
            work.Task.GetAwaiter().OnCompleted(() =>
            {
                try
                {
                    work.Task.GetAwaiter().GetResult();
                    callbackError = new InvalidOperationException("Retired work succeeded instead of canceling.");
                }
                catch (OperationCanceledException)
                {
                    try
                    {
                        // Ordinary frame work may create a replacement during job retirement.
                        frame = OnityTask.NextFrame().AsTask();
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
                }
                catch (Exception exception)
                {
                    callbackError = exception;
                }
            });
            GameObject oldRunner = FindRunner();
            UnityEngine.Object.DestroyImmediate(oldRunner);
            Assert.That(callbackError, Is.Null);
            Assert.That(rejection, Is.TypeOf<InvalidOperationException>());
            Assert.That(frame, Is.Not.Null);
            GameObject replacement = FindRunner();
            Assert.That(replacement, Is.Not.SameAs(oldRunner));
            Type dispatcher = typeof(OnityTask).Assembly.GetType(
                "Onity.Unity.Async.OnityTaskMainThreadDispatcher", true);
            Assert.That((int)dispatcher.GetField("s_runnerMissing", BindingFlags.Static | BindingFlags.NonPublic)
                .GetValue(null), Is.Zero, "Old OnDestroy marked the replacement runner missing.");
            Task<int> switched = System.Threading.Tasks.Task.Run(async () =>
            {
                await OnityTask.SwitchToMainThread();
                return Thread.CurrentThread.ManagedThreadId;
            });
            yield return WaitFor(() => frame.IsCompleted && switched.IsCompleted);
            frame.GetAwaiter().GetResult();
            Assert.That(switched.GetAwaiter().GetResult(), Is.EqualTo(mainThread));
            Assert.That(work.Read(), Is.EqualTo(work.Expected));
        }

        [UnityTest]
        public IEnumerator CombinedDependenciesAndWorkerConsumption_PreserveNativeSafety()
        {
            using PendingWork first = new PendingWork();
            using PendingWork second = new PendingWork();
            OnityTask combined = JobHandle.CombineDependencies(first.Handle, second.Handle).AsOnityTask();
            yield return WaitFor(() => combined.IsCompleted);
            Task consumer = System.Threading.Tasks.Task.Run(() => combined.GetAwaiter().GetResult());
            yield return WaitFor(() => consumer.IsCompleted);
            consumer.GetAwaiter().GetResult();
            Assert.That(first.Read(), Is.EqualTo(first.Expected));
            Assert.That(second.Read(), Is.EqualTo(second.Expected));
        }

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

        private sealed class PendingWork : IDisposable
        {
            private NativeArray<int> m_output;
            public JobHandle Handle;
            public OnityTask Task;
            public int Expected;

            public PendingWork(int iterations = 200000)
            {
                m_output = new NativeArray<int>(1, Allocator.Persistent);
#if UNITY_EDITOR
                AssemblyReloadEvents.beforeAssemblyReload += Dispose;
#endif
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
#if UNITY_EDITOR
                AssemblyReloadEvents.beforeAssemblyReload -= Dispose;
#endif
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
