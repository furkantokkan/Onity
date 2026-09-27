using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;
using UnityEngine.TestTools;

namespace Onity.Tests.PlayMode
{
    public sealed class OnityAsyncEnumerableAwaitPlayModeTests
    {
        [UnityTest]
        public IEnumerator InternalObservation_PublishesOnWorker_OrdinaryBridgeAwaitReturnsToUnity()
        {
            int main = Thread.CurrentThread.ManagedThreadId;
            SynchronizationContext unity = SynchronizationContext.Current;
            var pending = new OnityTaskCompletionSource<int>();
            var iterator = OnityAsyncEnumerable.Return(1).SelectAwait((value, token) => pending.Task).GetAsyncEnumerator();
            var move = iterator.MoveNextAsync();
            int observedThread = 0;
            Exception error = null;
            move.GetAwaiter().UnsafeOnCompleted(() =>
            {
                try
                {
                    if (!move.GetAwaiter().GetResult() || iterator.Current != 42)
                    {
                        throw new InvalidOperationException("Worker selector result changed.");
                    }
                    observedThread = Thread.CurrentThread.ManagedThreadId;
                }
                catch (Exception exception)
                {
                    error = exception;
                }
            });
            Task worker = Task.Run(() => { pending.TrySetResult(42); });
            try
            {
                yield return Wait(() => worker.IsCompleted && (observedThread != 0 || error != null));
                worker.GetAwaiter().GetResult();
                Assert.That(error, Is.Null);
                Assert.That(observedThread, Is.Not.EqualTo(main));
                iterator.DisposeAsync().GetAwaiter().GetResult();
                var predicate = new TaskCompletionSource<bool>();
                var filtered = OnityAsyncEnumerable.Return(7)
                    .WhereAwait((value, token) => OnityTask<bool>.FromTask(predicate.Task)).GetAsyncEnumerator();
                var next = filtered.MoveNextAsync();
                var consumer = ConsumeBridge(next, main, unity);
                Task completion = Task.Run(() => { predicate.TrySetResult(true); });
                try
                {
                    yield return Wait(() => consumer.IsCompleted && completion.IsCompleted);
                    completion.GetAwaiter().GetResult();
                    Assert.That(consumer.GetAwaiter().GetResult(), Is.True);
                    Assert.That(filtered.Current, Is.EqualTo(7));
                }
                finally
                {
                    predicate.TrySetResult(true);
                    Join(completion);
                    filtered.DisposeAsync().GetAwaiter().GetResult();
                }
            }
            finally
            {
                pending.TrySetResult(42);
                Join(worker);
                iterator.DisposeAsync().GetAwaiter().GetResult();
            }
        }

        [UnityTest]
        public IEnumerator RealAwaitForeachBreakThrow_AndForEach_WithFrameSuspendingDelegates()
        {
            foreach (bool throwBody in new[] { false, true })
            {
                var fault = new InvalidOperationException("await operator foreach body");
                int calls = 0;
                var stream = OnityAsyncEnumerable.Range(1, 3).SelectAwait((value, token) =>
                {
                    calls++;
                    return NextValue(value);
                });
                var result = Consume(stream, throwBody, fault);
                yield return Wait(() => result.IsCompleted);
                if (throwBody)
                {
                    Assert.That(Catch(() => result.GetAwaiter().GetResult()), Is.SameAs(fault));
                }
                else
                {
                    Assert.That(result.GetAwaiter().GetResult(), Is.EqualTo(10));
                }
                Assert.That(calls, Is.EqualTo(1));
            }
            int total = 0;
            int actions = 0;
            var terminal = OnityAsyncEnumerable.Range(1, 3).ForEachAsync((value, token) =>
            {
                actions++;
                return NextAction(value, item => total += item);
            });
            yield return Wait(() => terminal.IsCompleted);
            terminal.GetAwaiter().GetResult();
            Assert.That(actions, Is.EqualTo(3));
            Assert.That(total, Is.EqualTo(6));
        }

        private static async Task<bool> ConsumeBridge(OnityTask<bool> move, int main, SynchronizationContext unity)
        {
            bool result = await move.AsTask();
            Assert.That(Thread.CurrentThread.ManagedThreadId, Is.EqualTo(main));
            Assert.That(SynchronizationContext.Current, Is.SameAs(unity));
            return result;
        }

        private static async OnityTask<int> NextValue(int value)
        {
            await OnityTask.NextFrame();
            return value * 10;
        }

        private static async OnityTask NextAction(int value, Action<int> add)
        {
            await OnityTask.NextFrame();
            add(value);
        }

        private static async OnityTask<int> Consume(IOnityAsyncEnumerable<int> source, bool throwBody, Exception fault)
        {
            await foreach (int value in source)
            {
                if (throwBody)
                {
                    throw fault;
                }
                return value;
            }
            throw new InvalidOperationException("Expected an item.");
        }

        private static IEnumerator Wait(Func<bool> done)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!done())
            {
                Assert.That(watch.Elapsed.TotalSeconds, Is.LessThan(5));
                yield return null;
            }
        }

        private static void Join(Task worker)
        {
            Assert.That(SpinWait.SpinUntil(() => worker.IsCompleted, TimeSpan.FromSeconds(5)), Is.True);
            worker.GetAwaiter().GetResult();
        }

        private static Exception Catch(Action action)
        {
            try
            {
                action();
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }
    }
}
