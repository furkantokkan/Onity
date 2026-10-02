using System;
using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;
using UnityEngine;
using UnityEngine.TestTools;

namespace Onity.Tests.PlayMode
{
    public sealed class OnityTaskRunnerRetirementPlayModeTests
    {
        private static readonly FieldInfo s_instance = typeof(OnityTask).Assembly
            .GetType("Onity.Unity.Async.OnityTaskRunner", true)
            .GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic);

        [UnityTest]
        public IEnumerator Destroy_PublishesTokenlessCancellationForEveryLegacyQueueAndFamily()
        {
            int main = Thread.CurrentThread.ManagedThreadId;
            OnityTask[] waits = CreateWaits(default);
            Observation[] results = Observe(waits);
            GameObject owner = CurrentRunner();
            try
            {
                foreach (var result in results)
                {
                    Assert.That(result.Completed, Is.False);
                }
                UnityEngine.Object.DestroyImmediate(owner);
                foreach (var result in results)
                {
                    AssertCanceled(result, default, main);
                }
                yield return null;
            }
            finally
            {
                DestroyCurrent();
            }
        }

        [UnityTest]
        public IEnumerator ForcedRetirement_PreservesUnrequestedTokensWithoutCancelingTheirOwners()
        {
            using (var first = new CancellationTokenSource())
            using (var second = new CancellationTokenSource())
            {
                OnityTask[] waits = CreateWaits(first.Token);
                var separate = new Observation(OnityTask.Delay(1000f, second.Token));
                Observation[] results = Observe(waits);
                try
                {
                    UnityEngine.Object.DestroyImmediate(CurrentRunner());
                    foreach (var result in results)
                    {
                        AssertCanceled(result, first.Token, Thread.CurrentThread.ManagedThreadId);
                    }
                    AssertCanceled(separate, second.Token, Thread.CurrentThread.ManagedThreadId);
                    Assert.That(first.IsCancellationRequested, Is.False);
                    Assert.That(second.IsCancellationRequested, Is.False);
                    yield return null;
                }
                finally
                {
                    DestroyCurrent();
                }
            }
        }

        [UnityTest]
        public IEnumerator ActivePredicate_DestroyThenReturnOrThrow_CancelsOnlyAfterCallbackUnwinds()
        {
            for (int mode = 0; mode < 2; mode++)
            {
                int calls = 0;
                bool inside = false;
                bool completedInside = false;
                Observation active = null;
                Observation replacement = null;
                GameObject owner = null;
                int selectedMode = mode;
                OnityTask task = OnityTask.WaitUntil(() =>
                {
                    if (++calls == 1)
                    {
                        return false;
                    }
                    inside = true;
                    UnityEngine.Object.DestroyImmediate(owner);
                    completedInside = active.Completed;
                    replacement = new Observation(OnityTask.NextFrame());
                    inside = false;
                    if (selectedMode == 1)
                    {
                        throw new InvalidOperationException("Retired predicate fault must not replace cancellation.");
                    }
                    return true;
                });
                active = new Observation(task, () => completedInside |= inside);
                // Default frame waits run on the PlayerLoop scheduler; use another legacy family.
                var other = new Observation(OnityTask.WaitUntil(() => false));
                owner = CurrentRunner();
                try
                {
                    yield return Poll(() => active.Completed && replacement != null && replacement.Completed);
                    Assert.That(completedInside, Is.False);
                    AssertCanceled(active, default, Thread.CurrentThread.ManagedThreadId);
                    AssertCanceled(other, default, Thread.CurrentThread.ManagedThreadId);
                    Assert.That(replacement.Error, Is.Null);
                }
                finally
                {
                    DestroyCurrent();
                }
            }
        }

        [UnityTest]
        public IEnumerator ThrowingRetirementConsumer_DoesNotAbandonDetachedQueues()
        {
            OnityTask throwing = OnityTask.WaitUntil(() => false);
            Observation[] others = Observe(CreateWaits(default));
            throwing.GetAwaiter().UnsafeOnCompleted(() =>
            {
                try
                {
                    throwing.GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                }
                throw new InvalidOperationException("Expected retirement consumer failure.");
            });
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: Expected retirement consumer failure\\."));
            try
            {
                UnityEngine.Object.DestroyImmediate(CurrentRunner());
                foreach (var result in others)
                {
                    AssertCanceled(result, default, Thread.CurrentThread.ManagedThreadId);
                }
                yield return null;
            }
            finally
            {
                DestroyCurrent();
            }
        }

        [UnityTest]
        public IEnumerator ActiveCompletion_ConsumeDestroyAndRerentSameSource_OldVersionCannotCancelReplacement()
        {
            // Legacy delay sources: default frame waits are stateless and no longer pooled per wait.
            OnityTask first = OnityTask.Delay(0.001f);
            object originalSource = typeof(OnityTask).GetField("m_state", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(first);
            GameObject owner = CurrentRunner();
            Observation replacement = null;
            object rentedSource = null;
            Exception error = null;
            first.GetAwaiter().UnsafeOnCompleted(() =>
            {
                try
                {
                    first.GetAwaiter().GetResult();
                    UnityEngine.Object.DestroyImmediate(owner);
                    OnityTask next = OnityTask.Delay(0.001f);
                    rentedSource = typeof(OnityTask).GetField("m_state", BindingFlags.Instance | BindingFlags.NonPublic)
                        .GetValue(next);
                    replacement = new Observation(next);
                }
                catch (Exception exception)
                {
                    error = exception;
                }
            });
            try
            {
                yield return Poll(() => error != null || (replacement != null && replacement.Completed));
                Assert.That(error, Is.Null);
                Assert.That(rentedSource, Is.SameAs(originalSource), "The saved queue entry must refer to a reused source.");
                Assert.That(replacement.Error, Is.Null);
            }
            finally
            {
                DestroyCurrent();
            }
        }

        [UnityTest]
        public IEnumerator RetirementReentry_CreatesReplacementAndKeepsDispatcherAndInjectedWaitAlive()
        {
            OnityTask old = OnityTask.WaitUntil(() => false);
            var injected = new Observation(OnityTask.DelayFrames(3, OnityPlayerLoopTiming.Update, default));
            Observation replacement = null;
            Exception error = null;
            old.GetAwaiter().UnsafeOnCompleted(() =>
            {
                try
                {
                    try
                    {
                        old.GetAwaiter().GetResult();
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    replacement = new Observation(OnityTask.NextFrame());
                }
                catch (Exception exception)
                {
                    error = exception;
                }
            });
            GameObject owner = CurrentRunner();
            Task<int> worker = null;
            try
            {
                UnityEngine.Object.DestroyImmediate(owner);
                Assert.That(CurrentRunner(), Is.Not.SameAs(owner));
                worker = Task.Run(async () =>
                {
                    await OnityTask.SwitchToMainThread();
                    return Thread.CurrentThread.ManagedThreadId;
                });
                yield return Poll(() => worker.IsCompleted && injected.Completed && replacement != null && replacement.Completed);
                Assert.That(error, Is.Null);
                Assert.That(worker.GetAwaiter().GetResult(), Is.EqualTo(Thread.CurrentThread.ManagedThreadId));
                Assert.That(injected.Error, Is.Null);
                Assert.That(replacement.Error, Is.Null);
            }
            finally
            {
                DestroyCurrent();
            }
        }

        [UnityTest]
        public IEnumerator DeferredOldOnDestroy_DoesNotRetireReplacementWait()
        {
            var old = new Observation(OnityTask.WaitUntil(() => false));
            GameObject owner = CurrentRunner();
            // Close through the existing managed retirement method, then defer this old object's OnDestroy.
            Component runner = (Component)s_instance.GetValue(null);
            runner.GetType().GetMethod("Retire", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(runner, null);
            // A three-frame predicate wait still runs on the legacy runner and creates the replacement;
            // DelayFrames(3) runs on Onity's PlayerLoop nodes in Play since PERF-7.
            int targetFrame = Time.frameCount + 3;
            var replacement = new Observation(OnityTask.WaitUntil(() => Time.frameCount >= targetFrame));
            GameObject newOwner = CurrentRunner();
            UnityEngine.Object.Destroy(owner);
            try
            {
                yield return Poll(() => replacement.Completed);
                AssertCanceled(old, default, Thread.CurrentThread.ManagedThreadId);
                Assert.That(newOwner != null, Is.True);
                Assert.That(CurrentRunner(), Is.SameAs(newOwner));
                Assert.That(replacement.Error, Is.Null);
            }
            finally
            {
                DestroyCurrent();
                if (owner != null)
                {
                    UnityEngine.Object.DestroyImmediate(owner);
                }
            }
        }

        [UnityTest]
        public IEnumerator PausedFixed_WorkerCancellationRacingDestroy_CompletesOnceOnMain()
        {
            float previous = Time.timeScale;
            using (var cancellation = new CancellationTokenSource())
            {
                Task worker = null;
                try
                {
                    Time.timeScale = 0f;
                    var result = new Observation(OnityTask.NextFixedFrame(cancellation.Token));
                    GameObject owner = CurrentRunner();
                    worker = Task.Run(() => cancellation.Cancel());
                    UnityEngine.Object.DestroyImmediate(owner);
                    Assert.That(worker.Wait(TimeSpan.FromSeconds(5)), Is.True);
                    yield return null;
                    AssertCanceled(result, cancellation.Token, Thread.CurrentThread.ManagedThreadId);
                    Assert.That(result.Count, Is.EqualTo(1));
                }
                finally
                {
                    cancellation.Cancel();
                    if (worker != null)
                    {
                        Assert.That(worker.Wait(TimeSpan.FromSeconds(5)), Is.True);
                    }
                    DestroyCurrent();
                    Time.timeScale = previous;
                }
            }
        }

        [UnityTest]
        public IEnumerator RealAsyncOperation_RetiresObservationOnly_AndProgressRetirementDefersActivePublication()
        {
            for (int mode = 0; mode < 3; mode++)
            {
                AsyncOperation operation = Resources.UnloadUnusedAssets();
                Assert.That(operation.isDone, Is.False, "A pending real operation is required.");
                bool inProgress = false;
                bool publishedInside = false;
                int progressCalls = 0;
                bool complete = false;
                bool operationCompletedAtProgress = false;
                Exception outcome = null;
                GameObject owner = null;
                int selectedMode = mode;
                OnityTask<AsyncOperation> wait = operation.AsOnityTask(onProgress: value =>
                {
                    progressCalls++;
                    if (selectedMode == 2 && progressCalls == 1)
                    {
                        // Once the operation is done, let the regular progress call return;
                        // retire from the final 1f progress callback before result publication.
                        return;
                    }
                    if (selectedMode != 0)
                    {
                        operationCompletedAtProgress = operation.isDone;
                        inProgress = true;
                        UnityEngine.Object.DestroyImmediate(owner);
                        publishedInside |= complete;
                        inProgress = false;
                        if (selectedMode == 1)
                        {
                            throw new InvalidOperationException("Retired progress fault must not replace cancellation.");
                        }
                    }
                });
                owner = CurrentRunner();
                var runner = (Behaviour)s_instance.GetValue(null);
                wait.GetAwaiter().UnsafeOnCompleted(() =>
                {
                    publishedInside |= inProgress;
                    try
                    {
                        wait.GetAwaiter().GetResult();
                    }
                    catch (Exception exception)
                    {
                        outcome = exception;
                    }
                    complete = true;
                });
                try
                {
                    if (mode == 0)
                    {
                        UnityEngine.Object.DestroyImmediate(owner);
                    }
                    else if (mode == 1)
                    {
                        Assert.That(operation.isDone, Is.False, "The first progress callback must observe a pending operation.");
                        runner.GetType().GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)
                            .Invoke(runner, null);
                        Assert.That(operationCompletedAtProgress, Is.False);
                    }
                    else
                    {
                        runner.enabled = false;
                        yield return Poll(() => operation.isDone);
                        Assert.That(complete, Is.False, "Disabled observation runner should not poll the operation.");
                        runner.enabled = true;
                    }
                    yield return Poll(() => complete && operation.isDone);
                    Assert.That(outcome, Is.TypeOf<OperationCanceledException>());
                    Assert.That(publishedInside, Is.False);
                    Assert.That(progressCalls, Is.EqualTo(mode));
                    if (mode == 2)
                    {
                        Assert.That(operationCompletedAtProgress, Is.True);
                    }
                }
                finally
                {
                    if (runner != null)
                    {
                        runner.enabled = true;
                    }
                    DestroyCurrent();
                }
            }
        }

        private static OnityTask[] CreateWaits(CancellationToken token)
        {
            // In Play the default frame waits run on the PlayerLoop scheduler and are retired by
            // session exit, not by the legacy runner; they are covered by the PlayerLoop tests.
            return new[]
            {
                OnityTask.Delay(1000f, token), OnityTask.WaitUntil(() => false, token),
                OnityTask.WaitWhile(() => true, token)
            };
        }

        private static Observation[] Observe(OnityTask[] tasks)
        {
            var results = new Observation[tasks.Length];
            for (int i = 0; i < tasks.Length; i++)
            {
                results[i] = new Observation(tasks[i]);
            }
            return results;
        }

        private static void AssertCanceled(Observation result, CancellationToken token, int thread)
        {
            Assert.That(result.Completed, Is.True);
            Assert.That(result.Error, Is.TypeOf<OperationCanceledException>());
            Assert.That(((OperationCanceledException)result.Error).CancellationToken, Is.EqualTo(token));
            Assert.That(result.Thread, Is.EqualTo(thread));
        }

        private static GameObject CurrentRunner()
        {
            var runner = (Component)s_instance.GetValue(null);
            return runner != null ? runner.gameObject : null;
        }

        private static void DestroyCurrent()
        {
            GameObject owner = CurrentRunner();
            if (owner != null)
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        private static IEnumerator Poll(Func<bool> complete)
        {
            double deadline = UnityEngine.Time.realtimeSinceStartupAsDouble + 5d;
            while (!complete() && UnityEngine.Time.realtimeSinceStartupAsDouble < deadline)
            {
                yield return null;
            }
            Assert.That(complete(), Is.True, "Retirement check timed out after 5 seconds.");
        }

        private sealed class Observation
        {
            internal bool Completed;
            internal int Count;
            internal int Thread;
            internal Exception Error;

            internal Observation(OnityTask task, Action after = null)
            {
                task.GetAwaiter().UnsafeOnCompleted(() =>
                {
                    Count++;
                    Thread = System.Threading.Thread.CurrentThread.ManagedThreadId;
                    try
                    {
                        task.GetAwaiter().GetResult();
                    }
                    catch (Exception exception)
                    {
                        Error = exception;
                    }
                    Completed = true;
                    after?.Invoke();
                });
            }
        }
    }
}
