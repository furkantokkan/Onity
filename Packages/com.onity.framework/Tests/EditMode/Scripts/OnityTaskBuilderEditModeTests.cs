using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;

namespace Onity.Tests.EditMode
{
    [TestFixture]
    public sealed class OnityTaskBuilderEditModeTests
    {
        [Test]
        public void SynchronousTypedResult_UsesInlineStorage()
        {
            OnityTask<LargeResult> task = ReturnSynchronousResultAsync();

            Assert.That(task.IsCompletedSuccessfully, Is.True);
            Assert.That(GetState(task), Is.Null);
            Assert.That(task.GetAwaiter().GetResult().Number, Is.EqualTo(13));
            Assert.That(task.GetAwaiter().GetResult().Text, Is.EqualTo("ready"));
        }

        [Test]
        public void SynchronousDefaultStructResult_UsesInlineStorage()
        {
            OnityTask<LargeResult> task = ReturnDefaultResultAsync();

            Assert.That(task.IsCompletedSuccessfully, Is.True);
            Assert.That(GetState(task), Is.Null);
            Assert.That(task.GetAwaiter().GetResult().Number, Is.Zero);
            Assert.That(task.GetAwaiter().GetResult().Text, Is.Null);
        }

        [Test]
        public void SynchronousException_RemainsTaskBacked()
        {
            OnityTask<int> task = ThrowSynchronouslyAsync();

            Assert.That(task.IsFaulted, Is.True);
            Assert.That(GetState(task), Is.InstanceOf<Task<int>>());
            Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult());
        }

        [Test]
        public void SynchronousCancellation_RemainsTaskBacked()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            OnityTask<int> task = CancelSynchronouslyAsync(cancellation.Token);

            Assert.That(task.IsCanceled, Is.True);
            Assert.That(GetState(task), Is.InstanceOf<Task<int>>());
            Assert.Catch<OperationCanceledException>(() => task.GetAwaiter().GetResult());
        }

        [Test]
        public async Task SuspensionAfterCompletedAwait_IsRunnerBacked_AndBridgesThroughAsTask()
        {
            TaskCompletionSource<bool> gate = new TaskCompletionSource<bool>();
            OnityTask<LargeResult> task = ReturnAfterSuspensionAsync(gate.Task);

            Assert.That(task.IsCompleted, Is.False);
            Assert.That(GetState(task), Is.Not.InstanceOf<Task>());

            // The bridge claims the single-consumer task; await the bridge, not the original value.
            Task<LargeResult> converted = task.AsTask();
            gate.SetResult(true);

            LargeResult result = await converted;
            Assert.That(result.Number, Is.EqualTo(29));
            Assert.That(result.Text, Is.EqualTo("resumed"));
        }

        [Test]
        public async Task SafeAwaiterSuspension_IsRunnerBacked_AndAwaitsNatively()
        {
            SafeAwaitable gate = new SafeAwaitable();
            OnityTask<LargeResult> task = ReturnAfterSafeSuspensionAsync(gate);

            Assert.That(task.IsCompleted, Is.False);
            Assert.That(GetState(task), Is.Not.InstanceOf<Task>());

            gate.Complete();

            LargeResult result = await task;
            Assert.That(result.Number, Is.EqualTo(41));
            Assert.That(result.Text, Is.EqualTo("safe"));
        }

        private static object GetState<T>(OnityTask<T> task)
        {
            FieldInfo field = typeof(OnityTask<T>).GetField("m_state",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return field.GetValue(task);
        }

        private static async OnityTask<LargeResult> ReturnSynchronousResultAsync()
        {
            await OnityTask.Completed;
            return new LargeResult(13, "ready");
        }

        private static async OnityTask<LargeResult> ReturnDefaultResultAsync()
        {
            await OnityTask.Completed;
            return default;
        }

        private static async OnityTask<int> ThrowSynchronouslyAsync()
        {
            await OnityTask.Completed;
            throw new InvalidOperationException("builder failure");
        }

        private static async OnityTask<int> CancelSynchronouslyAsync(CancellationToken cancellationToken)
        {
            await OnityTask.Completed;
            throw new OperationCanceledException(cancellationToken);
        }

        private static async OnityTask<LargeResult> ReturnAfterSuspensionAsync(Task gate)
        {
            await OnityTask.Completed;
            await gate;
            return new LargeResult(29, "resumed");
        }

        private static async OnityTask<LargeResult> ReturnAfterSafeSuspensionAsync(SafeAwaitable gate)
        {
            await OnityTask.Completed;
            await gate;
            return new LargeResult(41, "safe");
        }

        private sealed class SafeAwaitable : INotifyCompletion
        {
            private Action m_continuation;

            public bool IsCompleted => false;

            public SafeAwaitable GetAwaiter()
            {
                return this;
            }

            public void OnCompleted(Action continuation)
            {
                m_continuation = continuation;
            }

            public void GetResult()
            {
            }

            public void Complete()
            {
                Action continuation = m_continuation;
                if (continuation == null)
                {
                    throw new InvalidOperationException("No continuation was registered.");
                }

                continuation();
            }
        }

        private readonly struct LargeResult
        {
            public readonly int Number;
            public readonly string Text;

            public LargeResult(int number, string text)
            {
                Number = number;
                Text = text;
            }
        }
    }
}
