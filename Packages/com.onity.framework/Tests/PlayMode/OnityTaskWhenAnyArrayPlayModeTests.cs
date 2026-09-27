using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;
using UnityEngine.TestTools;

namespace Onity.Tests.PlayMode
{
    public sealed class OnityTaskWhenAnyArrayPlayModeTests
    {
        [UnityTest]
        public IEnumerator NativeArrayWinner_ResumesTypedAndUntypedOnProducerWorker()
        {
            int main = Thread.CurrentThread.ManagedThreadId;
            var first = new OnityTaskCompletionSource<int>();
            var winner = new OnityTaskCompletionSource<int>();
            var last = new OnityTaskCompletionSource<int>();
            var plainFirst = new OnityTaskCompletionSource();
            var plainWinner = new OnityTaskCompletionSource();
            var plainLast = new OnityTaskCompletionSource();
            var typed = ObserveNative(OnityTask.WhenAny(new[] { first.Task, winner.Task, last.Task }));
            var plain = ObserveNative(OnityTask.WhenAny(new[] { plainFirst.Task, plainWinner.Task, plainLast.Task }));
            Task<int> worker = Task.Run(() =>
            {
                winner.TrySetResult(42);
                plainWinner.TrySetResult();
                return Thread.CurrentThread.ManagedThreadId;
            });
            try
            {
                yield return Wait(() => worker.IsCompleted && typed.IsCompleted && plain.IsCompleted);
                Assert.That(worker.IsFaulted, Is.False);
                Assert.That(worker.Result, Is.Not.EqualTo(main));
                Assert.That(typed.GetAwaiter().GetResult(), Is.EqualTo((1, 42, worker.Result)));
                Assert.That(plain.GetAwaiter().GetResult(), Is.EqualTo((1, worker.Result)));
            }
            finally
            {
                first.TrySetResult(0);
                winner.TrySetResult(0);
                last.TrySetResult(0);
                plainFirst.TrySetResult();
                plainWinner.TrySetResult();
                plainLast.TrySetResult();
                Assert.That(worker.Wait(TimeSpan.FromSeconds(5)), Is.True);
            }
        }

        [UnityTest]
        public IEnumerator ExplicitArrayBridgeAwait_ReturnsToUnityContext()
        {
            int main = Thread.CurrentThread.ManagedThreadId;
            SynchronizationContext context = SynchronizationContext.Current;
            Assert.That(context, Is.Not.Null);
            var sources = new[] { new OnityTaskCompletionSource<int>(), new OnityTaskCompletionSource<int>(),
                new OnityTaskCompletionSource<int>() };
            var race = OnityTask.WhenAny(new[] { sources[0].Task, sources[1].Task, sources[2].Task });
            Task<(int index, int value, int thread, SynchronizationContext context)> observed = ObserveBridge(race.AsTask());
            Task worker = Task.Run(() => sources[2].TrySetResult(77));
            try
            {
                yield return Wait(() => worker.IsCompleted && observed.IsCompleted);
                var result = observed.GetAwaiter().GetResult();
                Assert.That(result.index, Is.EqualTo(2));
                Assert.That(result.value, Is.EqualTo(77));
                Assert.That(result.thread, Is.EqualTo(main));
                Assert.That(result.context, Is.SameAs(context));
            }
            finally
            {
                foreach (var source in sources)
                {
                    source.TrySetResult(0);
                }
                Assert.That(worker.Wait(TimeSpan.FromSeconds(5)), Is.True);
            }
        }

        private static async Task<(int index, int value, int thread)> ObserveNative(
            OnityTask<(int winnerIndex, int result)> race)
        {
            var result = await race;
            return (result.winnerIndex, result.result, Thread.CurrentThread.ManagedThreadId);
        }

        private static async Task<(int index, int thread)> ObserveNative(OnityTask<int> race)
        {
            int index = await race;
            return (index, Thread.CurrentThread.ManagedThreadId);
        }

        private static async Task<(int index, int value, int thread, SynchronizationContext context)> ObserveBridge(
            Task<(int winnerIndex, int result)> race)
        {
            var result = await race;
            return (result.winnerIndex, result.result, Thread.CurrentThread.ManagedThreadId, SynchronizationContext.Current);
        }

        private static IEnumerator Wait(Func<bool> completed)
        {
            for (int frame = 0; frame < 240 && !completed(); frame++)
            {
                yield return null;
            }
            Assert.That(completed(), Is.True, "Array WhenAny PlayMode operation timed out.");
        }
    }
}
