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
        public async Task SuspensionAfterCompletedAwait_UsesNativeSource()
        {
            TaskCompletionSource<bool> gate = new TaskCompletionSource<bool>();
            OnityTask<LargeResult> task = ReturnAfterSuspensionAsync(gate.Task);

            Assert.That(task.IsCompleted, Is.False);
            Assert.That(GetState(task), Is.Not.InstanceOf<Task<LargeResult>>());

            gate.SetResult(true);

            LargeResult result = await task;
            Assert.That(result.Number, Is.EqualTo(29));
            Assert.That(result.Text, Is.EqualTo("resumed"));
        }

        [Test]
        public async Task SafeAwaiterSuspension_UsesNativeSource()
        {
            SafeAwaitable gate = new SafeAwaitable();
            OnityTask<LargeResult> task = ReturnAfterSafeSuspensionAsync(gate);

            Assert.That(task.IsCompleted, Is.False);
            Assert.That(GetState(task), Is.Not.InstanceOf<Task<LargeResult>>());

            gate.Complete();

            LargeResult result = await task;
            Assert.That(result.Number, Is.EqualTo(41));
            Assert.That(result.Text, Is.EqualTo("safe"));
        }

        [Test]
        public async Task SuspendedTask_AsTaskCompletesWithResult()
        {
            TaskCompletionSource<bool> gate = new TaskCompletionSource<bool>();
            OnityTask<LargeResult> task = ReturnAfterSuspensionAsync(gate.Task);
            Task<LargeResult> converted = task.AsTask();

            Assert.That(converted.IsCompleted, Is.False);

            gate.SetResult(true);

            LargeResult result = await converted;
            Assert.That(result.Number, Is.EqualTo(29));
            Assert.That(result.Text, Is.EqualTo("resumed"));
            Assert.That(converted.IsCompletedSuccessfully, Is.True);
        }

        [Test]
        public void SuspendedTask_FaultAndCancellationKeepTheirStatus()
        {
            TaskCompletionSource<bool> faultGate = new TaskCompletionSource<bool>();
            OnityTask<int> faulted = ThrowAfterSuspensionAsync(
                faultGate.Task, new InvalidOperationException("fault"));
            faultGate.SetResult(true);

            Assert.That(faulted.IsFaulted, Is.True);
            Assert.Throws<InvalidOperationException>(() => faulted.GetAwaiter().GetResult());

            TaskCompletionSource<bool> cancelGate = new TaskCompletionSource<bool>();
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            OnityTask<int> canceled = ThrowAfterSuspensionAsync(
                cancelGate.Task, new OperationCanceledException(cancellation.Token));
            cancelGate.SetResult(true);

            Assert.That(canceled.IsCanceled, Is.True);
            Assert.Catch<OperationCanceledException>(() => canceled.GetAwaiter().GetResult());
        }

        [Test]
        public void AwaiterRegistrationFailure_CompletesTheNativeSource()
        {
            OnityTask<int> task = ReturnAfterThrowingRegistrationAsync(
                new ThrowingSafeAwaitable());

            Assert.That(task.IsFaulted, Is.True);
            Assert.That(GetState(task), Is.Not.InstanceOf<Task<int>>());
            Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult());
        }

        [Test]
        public void InlineContinuation_DrainsAfterStartReturns()
        {
            OnityTask<int> task = ReturnAfterInlineSuspensionAsync(new InlineSafeAwaitable());

            Assert.That(task.IsCompletedSuccessfully, Is.True);
            Assert.That(task.GetAwaiter().GetResult(), Is.EqualTo(43));
        }

        [Test]
        public void InlineContinuationDuringMoveNext_DrainsAfterTheActiveMoveNext()
        {
            SafeAwaitable first = new SafeAwaitable();
            OnityTask<int> task = ReturnAfterGateAndInlineSuspensionAsync(
                first, new InlineSafeAwaitable());

            first.Complete();

            Assert.That(task.IsCompletedSuccessfully, Is.True);
            Assert.That(task.GetAwaiter().GetResult(), Is.EqualTo(47));
        }

        [Test]
        public async Task ConcurrentDuplicateCallbacks_ResumeOnlyOnce()
        {
            UnsafeAwaitable gate = new UnsafeAwaitable();
            int[] resumeCount = new int[1];
            OnityTask<int> task = CountAfterUnsafeGateAsync(gate, resumeCount);
            Action continuation = gate.Continuation;
            using ManualResetEventSlim start = new ManualResetEventSlim(false);

            Task first = Task.Run(() =>
            {
                start.Wait();
                continuation();
            });
            Task second = Task.Run(() =>
            {
                start.Wait();
                continuation();
            });
            start.Set();
            await Task.WhenAll(first, second);

            Assert.That(await task, Is.EqualTo(1));
            Assert.That(resumeCount[0], Is.EqualTo(1));
        }

        [Test]
        public void LateOldContinuation_DoesNotResumeAReusedRunner()
        {
            SafeAwaitable oldGate = new SafeAwaitable();
            OnityTask<int> oldTask = ReturnAfterSafeGateAsync(oldGate);
            object oldRunner = GetState(oldTask);
            oldGate.Complete();
            Assert.That(oldTask.GetAwaiter().GetResult(), Is.EqualTo(7));

            SafeAwaitable newGate = new SafeAwaitable();
            OnityTask<int> newTask = ReturnAfterSafeGateAsync(newGate);
            Assert.That(GetState(newTask), Is.SameAs(oldRunner));

            oldGate.Complete();
            Assert.That(newTask.IsCompleted, Is.False);
            Assert.Throws<InvalidOperationException>(() =>
            {
                _ = oldTask.IsCompleted;
            });

            newGate.Complete();
            Assert.That(newTask.GetAwaiter().GetResult(), Is.EqualTo(7));
        }

        [Test]
        public void LateOldAwaitContinuation_DoesNotSkipTheNextAwait()
        {
            SafeAwaitable first = new SafeAwaitable();
            SafeAwaitable second = new SafeAwaitable();
            OnityTask<int> task = ReturnAfterTwoSafeGatesAsync(first, second);

            first.Complete();
            first.Complete();
            Assert.That(task.IsCompleted, Is.False);

            second.Complete();
            Assert.That(task.GetAwaiter().GetResult(), Is.EqualTo(11));
        }

        [Test]
        public async Task ConsumedCompletion_DoesNotReturnRunnerDuringMoveNext()
        {
            TaskCompletionSource<bool> outerGate = new TaskCompletionSource<bool>();
            TaskCompletionSource<bool> innerGate = new TaskCompletionSource<bool>();
            OnityTask<LargeResult> outer = ReturnAfterSuspensionAsync(outerGate.Task);
            object outerRunner = GetState(outer);
            OnityTask<LargeResult> inner = default;
            Exception callbackError = null;
            bool distinctRunner = false;
            OnityTaskAwaiter<LargeResult> awaiter = outer.GetAwaiter();

            awaiter.OnCompleted(() =>
            {
                try
                {
                    awaiter.GetResult();
                    inner = ReturnAfterSuspensionAsync(innerGate.Task);
                    distinctRunner = ReferenceEquals(outerRunner, GetState(inner)) == false;
                }
                catch (Exception exception)
                {
                    callbackError = exception;
                }
            });

            outerGate.SetResult(true);
            Assert.That(callbackError, Is.Null);
            Assert.That(distinctRunner, Is.True);

            innerGate.SetResult(true);
            Assert.That((await inner).Number, Is.EqualTo(29));
        }

        [Test]
        public void UnsafeAwaiter_FlowsCapturedExecutionContext()
        {
            AsyncLocal<string> context = new AsyncLocal<string>();
            UnsafeAwaitable gate = new UnsafeAwaitable();
            context.Value = "captured";
            OnityTask<string> task = ReadContextAfterUnsafeGateAsync(gate, context);
            context.Value = "caller";

            gate.Complete();

            Assert.That(task.GetAwaiter().GetResult(), Is.EqualTo("captured"));
            Assert.That(context.Value, Is.EqualTo("caller"));
        }

        [Test]
        public void SafeAwaiter_FlowsCapturedExecutionContext()
        {
            AsyncLocal<string> context = new AsyncLocal<string>();
            SafeAwaitable gate = new SafeAwaitable();
            context.Value = "captured";
            OnityTask<string> task = ReadContextAfterSafeGateAsync(gate, context);
            context.Value = "caller";

            gate.Complete();

            Assert.That(task.GetAwaiter().GetResult(), Is.EqualTo("captured"));
            Assert.That(context.Value, Is.EqualTo("caller"));
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

        private static async OnityTask<int> ThrowAfterSuspensionAsync(Task gate, Exception exception)
        {
            await gate;
            throw exception;
        }

        private static async OnityTask<int> ReturnAfterInlineSuspensionAsync(InlineSafeAwaitable gate)
        {
            await gate;
            return 43;
        }

        private static async OnityTask<int> ReturnAfterThrowingRegistrationAsync(
            ThrowingSafeAwaitable gate)
        {
            await gate;
            return 5;
        }

        private static async OnityTask<int> ReturnAfterGateAndInlineSuspensionAsync(
            SafeAwaitable first,
            InlineSafeAwaitable second)
        {
            await first;
            await second;
            return 47;
        }

        private static async OnityTask<int> CountAfterUnsafeGateAsync(
            UnsafeAwaitable gate,
            int[] resumeCount)
        {
            await gate;
            return Interlocked.Increment(ref resumeCount[0]);
        }

        private static async OnityTask<int> ReturnAfterSafeGateAsync(SafeAwaitable gate)
        {
            await gate;
            return 7;
        }

        private static async OnityTask<int> ReturnAfterTwoSafeGatesAsync(
            SafeAwaitable first,
            SafeAwaitable second)
        {
            await first;
            await second;
            return 11;
        }

        private static async OnityTask<string> ReadContextAfterUnsafeGateAsync(
            UnsafeAwaitable gate,
            AsyncLocal<string> context)
        {
            await gate;
            return context.Value;
        }

        private static async OnityTask<string> ReadContextAfterSafeGateAsync(
            SafeAwaitable gate,
            AsyncLocal<string> context)
        {
            await gate;
            return context.Value;
        }

        private sealed class InlineSafeAwaitable : INotifyCompletion
        {
            public bool IsCompleted => false;

            public InlineSafeAwaitable GetAwaiter()
            {
                return this;
            }

            public void OnCompleted(Action continuation)
            {
                continuation();
            }

            public void GetResult()
            {
            }
        }

        private sealed class ThrowingSafeAwaitable : INotifyCompletion
        {
            public bool IsCompleted => false;

            public ThrowingSafeAwaitable GetAwaiter()
            {
                return this;
            }

            public void OnCompleted(Action continuation)
            {
                throw new InvalidOperationException("registration failure");
            }

            public void GetResult()
            {
            }
        }

        private sealed class UnsafeAwaitable : ICriticalNotifyCompletion
        {
            private Action m_continuation;

            public bool IsCompleted => false;

            public Action Continuation => m_continuation;

            public UnsafeAwaitable GetAwaiter()
            {
                return this;
            }

            public void OnCompleted(Action continuation)
            {
                throw new InvalidOperationException("The safe continuation path was used.");
            }

            public void UnsafeOnCompleted(Action continuation)
            {
                m_continuation = continuation;
            }

            public void GetResult()
            {
            }

            public void Complete()
            {
                m_continuation();
            }
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
