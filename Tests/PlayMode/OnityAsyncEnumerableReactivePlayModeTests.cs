using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Reactive;
using Onity.Unity.Async;
using UnityEngine.TestTools;

namespace Onity.Tests.PlayMode
{
    public sealed class OnityAsyncEnumerableReactivePlayModeTests
    {
        [UnityTest]
        public IEnumerator InternalReverseObserverStaysOnProducerWorker_OrdinaryImportBridgeAwaitReturnsToUnity()
        {
            int main = Thread.CurrentThread.ManagedThreadId;
            SynchronizationContext unity = SynchronizationContext.Current;
            var pending = new OnityTaskCompletionSource<int>();
            var observer = new Sink();
            var subscription = OnityAsyncEnumerable.Return(1).SelectAwait((value, token) => pending.Task)
                .AsObservable().Subscribe(observer);
            Task worker = Task.Run(() => { pending.TrySetResult(42); });
            try
            {
                yield return Wait(() => worker.IsCompleted && observer.Disposals == 1);
                worker.GetAwaiter().GetResult();
                Assert.That(observer.Values, Is.EqualTo(1));
                Assert.That(observer.Value, Is.EqualTo(42));
                Assert.That(observer.Thread, Is.Not.EqualTo(main));
                Assert.That(observer.Completions, Is.EqualTo(1));
                Assert.That(observer.Error, Is.Null);
                using (var subject = new Subject<int>())
                {
                    var iterator = subject.AsOnityAsyncEnumerable(1).GetAsyncEnumerator();
                    var move = iterator.MoveNextAsync();
                    var consumer = ConsumeBridge(move, main, unity);
                    Task publisher = Task.Run(() => subject.OnNext(7));
                    try
                    {
                        yield return Wait(() => publisher.IsCompleted && consumer.IsCompleted);
                        publisher.GetAwaiter().GetResult();
                        Assert.That(consumer.GetAwaiter().GetResult(), Is.True);
                        Assert.That(iterator.Current, Is.EqualTo(7));
                    }
                    finally
                    {
                        Join(publisher);
                        iterator.DisposeAsync().GetAwaiter().GetResult();
                    }
                }
            }
            finally
            {
                pending.TrySetResult(42);
                Join(worker);
                subscription.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator RoundTripFrameSuspensions_RealAwaitForeachBreakThrow_CleansWithoutGlobalErrors()
        {
            Action<Exception> previous = OnityObservableExceptionHandler.Handler;
            Exception unexpected = null;
            OnityObservableExceptionHandler.Handler = exception => unexpected = exception;
            try
            {
                foreach (bool throws in new[] { false, true })
                {
                    var fault = new InvalidOperationException("reactive foreach body");
                    var stream = OnityAsyncEnumerable.Range(1, 3)
                        .SelectAwait((value, token) => Next(value, token)).AsObservable().AsOnityAsyncEnumerable(1);
                    var result = Consume(stream, throws, fault);
                    yield return Wait(() => result.IsCompleted);
                    if (throws)
                    {
                        Exception error = null;
                        try
                        {
                            result.GetAwaiter().GetResult();
                        }
                        catch (Exception exception)
                        {
                            error = exception;
                        }
                        Assert.That(error, Is.SameAs(fault));
                    }
                    else
                    {
                        Assert.That(result.GetAwaiter().GetResult(), Is.EqualTo(10));
                    }
                    yield return null;
                    yield return null;
                    Assert.That(unexpected, Is.Null);
                }
            }
            finally
            {
                OnityObservableExceptionHandler.Handler = previous;
            }
        }

        private static async Task<bool> ConsumeBridge(OnityTask<bool> task, int main, SynchronizationContext unity)
        {
            bool result = await task.AsTask();
            Assert.That(Thread.CurrentThread.ManagedThreadId, Is.EqualTo(main));
            Assert.That(SynchronizationContext.Current, Is.SameAs(unity));
            return result;
        }

        private static async OnityTask<int> Next(int value, CancellationToken token)
        {
            await OnityTask.NextFrame(cancellationToken: token);
            return value * 10;
        }

        private static async OnityTask<int> Consume(IOnityAsyncEnumerable<int> source, bool throws, Exception fault)
        {
            await foreach (int value in source)
            {
                if (throws)
                {
                    throw fault;
                }
                return value;
            }
            throw new InvalidOperationException("Expected a value.");
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

        private sealed class Sink : OnityObserver<int>
        {
            internal int Values;
            internal int Value;
            internal int Thread;
            internal int Completions;
            internal int Disposals;
            internal Exception Error;
            protected override void OnNextCore(int value)
            {
                Values++;
                Value = value;
                Thread = System.Threading.Thread.CurrentThread.ManagedThreadId;
            }
            protected override void OnCompletedCore(OnityResult result) => Completions++;
            protected override void OnErrorCore(Exception error) => Error = error;
            protected override void OnDisposed() => Disposals++;
        }
    }
}
