using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;
using UnityEngine.TestTools;

namespace Onity.Tests.PlayMode
{
    public sealed class OnityTaskCancellationPlayModeTests
    {
        [UnityTest]
        public IEnumerator NativeAttachmentAndSuppression_PublishOnCancelingOrProducerWorker()
        {
            int main = Thread.CurrentThread.ManagedThreadId;
            using (var external = new CancellationTokenSource())
            using (var producerCancellation = new CancellationTokenSource())
            {
                producerCancellation.Cancel();
                var typed = new OnityTaskCompletionSource<int>();
                var plain = new OnityTaskCompletionSource();
                var typedSuppression = new OnityTaskCompletionSource<int>();
                var plainSuppression = new OnityTaskCompletionSource();
                var typedObserved = ObserveNative(typed.Task.AttachExternalCancellation(external.Token));
                var plainObserved = ObserveNative(plain.Task.AttachExternalCancellation(external.Token));
                var typedSuppressed = ObserveNative(typedSuppression.Task.SuppressCancellationThrow());
                var plainSuppressed = ObserveNative(plainSuppression.Task.SuppressCancellationThrow());
                Task<int> worker = Task.Run(() =>
                {
                    external.Cancel();
                    typed.TrySetCanceled(producerCancellation.Token);
                    plain.TrySetCanceled(producerCancellation.Token);
                    typedSuppression.TrySetCanceled(producerCancellation.Token);
                    plainSuppression.TrySetCanceled(producerCancellation.Token);
                    return Thread.CurrentThread.ManagedThreadId;
                });
                try
                {
                    yield return Wait(() => worker.IsCompleted && typedObserved.IsCompleted && plainObserved.IsCompleted
                        && typedSuppressed.IsCompleted && plainSuppressed.IsCompleted);
                    int producer = worker.GetAwaiter().GetResult();
                    Assert.That(producer, Is.Not.EqualTo(main));
                    var typedResult = typedObserved.GetAwaiter().GetResult();
                    var plainResult = plainObserved.GetAwaiter().GetResult();
                    Assert.That(typedResult.error, Is.TypeOf<OperationCanceledException>());
                    Assert.That(plainResult.error, Is.TypeOf<OperationCanceledException>());
                    Assert.That(((OperationCanceledException)typedResult.error).CancellationToken, Is.EqualTo(external.Token));
                    Assert.That(((OperationCanceledException)plainResult.error).CancellationToken, Is.EqualTo(external.Token));
                    Assert.That(typedResult.thread, Is.EqualTo(producer));
                    Assert.That(plainResult.thread, Is.EqualTo(producer));
                    var typedSuppressionResult = typedSuppressed.GetAwaiter().GetResult();
                    var plainSuppressionResult = plainSuppressed.GetAwaiter().GetResult();
                    Assert.That(typedSuppressionResult.error, Is.Null);
                    Assert.That(plainSuppressionResult.error, Is.Null);
                    Assert.That(typedSuppressionResult.value, Is.EqualTo((true, 0)));
                    Assert.That(plainSuppressionResult.value, Is.True);
                    Assert.That(typedSuppressionResult.thread, Is.EqualTo(producer));
                    Assert.That(plainSuppressionResult.thread, Is.EqualTo(producer));
                }
                finally
                {
                    typed.TrySetResult(0);
                    plain.TrySetResult();
                    typedSuppression.TrySetResult(0);
                    plainSuppression.TrySetResult();
                    Assert.That(worker.Wait(TimeSpan.FromSeconds(5)), Is.True);
                }
            }
        }

        [UnityTest]
        public IEnumerator ExplicitCancellationAndSuppressionBridges_ReturnToUnityContext()
        {
            int main = Thread.CurrentThread.ManagedThreadId;
            SynchronizationContext context = SynchronizationContext.Current;
            Assert.That(context, Is.Not.Null);
            using (var cancellation = new CancellationTokenSource())
            {
                var typed = new OnityTaskCompletionSource<int>();
                var plain = new OnityTaskCompletionSource();
                var attached = ObserveBridge(typed.Task.AttachExternalCancellation(cancellation.Token).AsTask());
                var suppressed = ObserveBridge(plain.Task.SuppressCancellationThrow().AsTask());
                Task worker = Task.Run(() =>
                {
                    cancellation.Cancel();
                    plain.TrySetCanceled();
                });
                try
                {
                    yield return Wait(() => worker.IsCompleted && attached.IsCompleted && suppressed.IsCompleted);
                    var attachedResult = attached.GetAwaiter().GetResult();
                    var suppressedResult = suppressed.GetAwaiter().GetResult();
                    Assert.That(attachedResult.error, Is.InstanceOf<OperationCanceledException>());
                    Assert.That(((OperationCanceledException)attachedResult.error).CancellationToken,
                        Is.EqualTo(cancellation.Token));
                    Assert.That(suppressedResult.error, Is.Null);
                    Assert.That(suppressedResult.value, Is.True);
                    Assert.That(attachedResult.thread, Is.EqualTo(main));
                    Assert.That(suppressedResult.thread, Is.EqualTo(main));
                    Assert.That(attachedResult.context, Is.SameAs(context));
                    Assert.That(suppressedResult.context, Is.SameAs(context));
                }
                finally
                {
                    typed.TrySetResult(0);
                    plain.TrySetResult();
                    Assert.That(worker.Wait(TimeSpan.FromSeconds(5)), Is.True);
                }
            }
        }

        private static async Task<(T value, Exception error, int thread)> ObserveNative<T>(OnityTask<T> task)
        {
            try
            {
                T value = await task;
                return (value, null, Thread.CurrentThread.ManagedThreadId);
            }
            catch (Exception exception)
            {
                return (default, exception, Thread.CurrentThread.ManagedThreadId);
            }
        }

        private static async Task<(Exception error, int thread)> ObserveNative(OnityTask task)
        {
            try
            {
                await task;
                return (null, Thread.CurrentThread.ManagedThreadId);
            }
            catch (Exception exception)
            {
                return (exception, Thread.CurrentThread.ManagedThreadId);
            }
        }

        private static async Task<(T value, Exception error, int thread, SynchronizationContext context)> ObserveBridge<T>(Task<T> task)
        {
            try
            {
                T value = await task;
                return (value, null, Thread.CurrentThread.ManagedThreadId, SynchronizationContext.Current);
            }
            catch (Exception exception)
            {
                return (default, exception, Thread.CurrentThread.ManagedThreadId, SynchronizationContext.Current);
            }
        }

        private static IEnumerator Wait(Func<bool> completed)
        {
            for (int frame = 0; frame < 240 && !completed(); frame++)
            {
                yield return null;
            }
            Assert.That(completed(), Is.True, "Cancellation PlayMode operation timed out.");
        }
    }
}
