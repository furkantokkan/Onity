using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Core;
using Onity.Unity.Async;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.TestTools;

namespace Onity.Tests.PlayMode
{
    public sealed class OnityAsyncEnumerableAdapterPlayModeTests
    {
        [UnityTest]
        public IEnumerator EveryUpdate_IsIdleWithoutReplay_BeforeNodeCompletesSamePass_ReentrantMoveUsesNext()
        {
            var iterator = OnityAsyncEnumerable.EveryUpdate().GetAsyncEnumerator();
            int occurrence = 0;
            int first = 0;
            int second = 0;
            bool afterSawOnlyFirst = false;
            Exception error = null;
            try
            {
                for (int frame = 0; frame < 3; frame++)
                {
                    yield return null;
                    Assert.Throws<InvalidOperationException>(() => { var ignored = iterator.Current; });
                }
                OnityTaskPlayerLoop.Initialize();
                AddNode(typeof(BeforeMarker), () =>
                {
                    occurrence++;
                    if (occurrence != 1)
                    {
                        return;
                    }
                    try
                    {
                        var move = iterator.MoveNextAsync();
                        Observe(move, value =>
                        {
                            if (!value)
                            {
                                error = new InvalidOperationException("Expected a Unit item.");
                                return;
                            }
                            first = occurrence;
                            var nested = iterator.MoveNextAsync();
                            Observe(nested, nestedValue => second = nestedValue ? occurrence : -1,
                                exception => error = exception);
                        }, exception => error = exception);
                    }
                    catch (Exception exception)
                    {
                        error = exception;
                    }
                }, false);
                AddNode(typeof(AfterMarker), () =>
                {
                    if (occurrence == 1)
                    {
                        afterSawOnlyFirst = first == 1 && second == 0;
                    }
                }, true);
                yield return Wait(() => error != null || second != 0);
                Assert.That(error, Is.Null);
                Assert.That(first, Is.EqualTo(1));
                Assert.That(second, Is.EqualTo(2));
                Assert.That(afterSawOnlyFirst, Is.True);
                Assert.That(iterator.Current, Is.EqualTo(Unit.Default));
                yield return null;
                yield return null;
                Assert.That(first, Is.EqualTo(1));
                Assert.That(second, Is.EqualTo(2));
            }
            finally
            {
                RemoveNodes();
                iterator.DisposeAsync();
            }
        }

        [UnityTest]
        public IEnumerator WorkerDispose_PublishesFalseButAwaitsUnityDrain_AndDoesNotCancelCaller()
        {
            using (var caller = new CancellationTokenSource())
            {
                var iterator = OnityAsyncEnumerable.EveryUpdate().GetAsyncEnumerator(caller.Token);
                var move = iterator.MoveNextAsync();
                OnityTask disposal = default;
                bool falseOnWorker = false;
                bool pendingCleanupOnWorker = false;
                Task worker = Task.Run(() =>
                {
                    disposal = iterator.DisposeAsync();
                    falseOnWorker = move.IsCompletedSuccessfully && !move.GetAwaiter().GetResult();
                    pendingCleanupOnWorker = !disposal.IsCompleted;
                });
                try
                {
                    Assert.That(worker.Wait(TimeSpan.FromSeconds(5)), Is.True);
                    worker.GetAwaiter().GetResult();
                    Assert.That(falseOnWorker, Is.True);
                    Assert.That(pendingCleanupOnWorker, Is.True);
                    Assert.That(caller.IsCancellationRequested, Is.False);
                    yield return Wait(() => disposal.IsCompleted);
                    disposal.GetAwaiter().GetResult();
                    Assert.That(iterator.MoveNextAsync().GetAwaiter().GetResult(), Is.False);
                    Assert.DoesNotThrow(() => caller.Cancel());
                }
                finally
                {
                    caller.Cancel();
                    Assert.That(worker.Wait(TimeSpan.FromSeconds(5)), Is.True);
                    iterator.DisposeAsync();
                }
            }
        }

        [UnityTest]
        public IEnumerator CallerWorkerCancellation_IsObservedOnMainWithActualOwnedToken()
        {
            int main = Thread.CurrentThread.ManagedThreadId;
            using (var caller = new CancellationTokenSource())
            {
                var iterator = OnityAsyncEnumerable.EveryUpdate().GetAsyncEnumerator(caller.Token);
                var move = iterator.MoveNextAsync();
                int published = 0;
                int thread = 0;
                Exception error = null;
                move.GetAwaiter().UnsafeOnCompleted(() =>
                {
                    try
                    {
                        move.GetAwaiter().GetResult();
                        error = new InvalidOperationException("Expected cancellation.");
                    }
                    catch (Exception exception)
                    {
                        error = exception;
                    }
                    thread = Thread.CurrentThread.ManagedThreadId;
                    Volatile.Write(ref published, 1);
                });
                Task worker = Task.Run(() => caller.Cancel());
                try
                {
                    Assert.That(worker.Wait(TimeSpan.FromSeconds(5)), Is.True);
                    Assert.That(Volatile.Read(ref published), Is.Zero);
                    yield return Wait(() => Volatile.Read(ref published) == 1);
                    Assert.That(error, Is.TypeOf<OperationCanceledException>());
                    CancellationToken actual = ((OperationCanceledException)error).CancellationToken;
                    Assert.That(actual.CanBeCanceled, Is.True);
                    Assert.That(actual.IsCancellationRequested, Is.True);
                    Assert.That(actual, Is.Not.EqualTo(caller.Token), "EveryUpdate supplies its owned child token.");
                    Assert.That(thread, Is.EqualTo(main));
                    var disposal = iterator.DisposeAsync();
                    yield return Wait(() => disposal.IsCompleted);
                    disposal.GetAwaiter().GetResult();
                    Assert.DoesNotThrow(() => caller.Cancel());
                }
                finally
                {
                    caller.Cancel();
                    Assert.That(worker.Wait(TimeSpan.FromSeconds(5)), Is.True);
                    iterator.DisposeAsync();
                }
            }
        }

        [UnityTest]
        public IEnumerator OrdinaryAwaitOfExportedValueTask_RetainsUnityContextCapture()
        {
            int main = Thread.CurrentThread.ManagedThreadId;
            SynchronizationContext context = SynchronizationContext.Current;
            Assert.That(context, Is.Not.Null);
            var source = new PendingNative();
            var iterator = source.AsAsyncEnumerable().GetAsyncEnumerator();
            var consumed = ConsumeNormally(iterator.MoveNextAsync());
            Task worker = Task.Run(() => { source.Move.TrySetResult(true); });
            try
            {
                yield return Wait(() => worker.IsCompleted && consumed.IsCompleted);
                var result = consumed.GetAwaiter().GetResult();
                Assert.That(result.value, Is.True);
                Assert.That(result.thread, Is.EqualTo(main));
                Assert.That(result.context, Is.SameAs(context));
                Assert.That(iterator.Current, Is.EqualTo(71));
                var disposal = iterator.DisposeAsync();
                yield return Wait(() => disposal.IsCompleted);
                disposal.GetAwaiter().GetResult();
                Assert.That(source.Disposals, Is.EqualTo(1));
            }
            finally
            {
                source.Move.TrySetResult(false);
                Assert.That(worker.Wait(TimeSpan.FromSeconds(5)), Is.True);
                iterator.DisposeAsync();
            }
        }

        private static async Task<(bool value, int thread, SynchronizationContext context)> ConsumeNormally(ValueTask<bool> task)
        {
            bool value = await task;
            return (value, Thread.CurrentThread.ManagedThreadId, SynchronizationContext.Current);
        }

        private static void Observe(OnityTask<bool> task, Action<bool> success, Action<Exception> failure)
        {
            task.GetAwaiter().UnsafeOnCompleted(() =>
            {
                try
                {
                    success(task.GetAwaiter().GetResult());
                }
                catch (Exception exception)
                {
                    failure(exception);
                }
            });
        }

        private static IEnumerator Wait(Func<bool> predicate)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + 5;
            while (!predicate())
            {
                Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(deadline), "Adapter outcome timed out.");
                yield return null;
            }
        }

        private static void AddNode(Type marker, PlayerLoopSystem.UpdateFunction callback, bool after)
        {
            PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
            Assert.That(Insert(ref loop, new PlayerLoopSystem { type = marker, updateDelegate = callback }, after), Is.True);
            PlayerLoop.SetPlayerLoop(loop);
        }

        private static bool Insert(ref PlayerLoopSystem loop, PlayerLoopSystem node, bool after)
        {
            if (loop.subSystemList == null)
            {
                return false;
            }
            for (int index = 0; index < loop.subSystemList.Length; index++)
            {
                var child = loop.subSystemList[index];
                if (child.type?.FullName == "Onity.Unity.Async.OnityTaskPlayerLoop+UpdateMarker")
                {
                    var children = new List<PlayerLoopSystem>(loop.subSystemList);
                    children.Insert(index + (after ? 1 : 0), node);
                    loop.subSystemList = children.ToArray();
                    return true;
                }
                if (Insert(ref child, node, after))
                {
                    loop.subSystemList[index] = child;
                    return true;
                }
            }
            return false;
        }

        private static void RemoveNodes()
        {
            PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
            Remove(ref loop);
            PlayerLoop.SetPlayerLoop(loop);
        }

        private static void Remove(ref PlayerLoopSystem loop)
        {
            if (loop.subSystemList == null)
            {
                return;
            }
            var children = new List<PlayerLoopSystem>();
            foreach (PlayerLoopSystem original in loop.subSystemList)
            {
                if (original.type == typeof(BeforeMarker) || original.type == typeof(AfterMarker))
                {
                    continue;
                }
                var child = original;
                Remove(ref child);
                children.Add(child);
            }
            loop.subSystemList = children.ToArray();
        }

        private sealed class BeforeMarker
        {
        }
        private sealed class AfterMarker
        {
        }

        private sealed class PendingNative : IOnityAsyncEnumerable<int>, IOnityAsyncEnumerator<int>
        {
            internal readonly OnityTaskCompletionSource<bool> Move = new OnityTaskCompletionSource<bool>();
            internal int Disposals;
            public int Current => 71;
            public IOnityAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken cancellationToken = default) => this;
            public OnityTask<bool> MoveNextAsync() => Move.Task;
            public OnityTask DisposeAsync()
            {
                Disposals++;
                return default;
            }
        }
    }
}
