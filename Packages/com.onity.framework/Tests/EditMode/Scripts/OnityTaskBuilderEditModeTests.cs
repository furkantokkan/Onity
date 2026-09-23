using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;
using UnityEngine;
using UnityEngine.TestTools;

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
        public void NativeAwait_CompletedSourceKeepsTheInlineResult()
        {
            OnityTask gate = OnityTask.NextFrame();
            CompleteNativeSource(gate);

            OnityTask<int> task = ReturnAfterNativeGateAsync(gate);

            Assert.That(GetState(task), Is.Null);
            Assert.That(task.GetAwaiter().GetResult(), Is.EqualTo(67));
        }

        [Test]
        public void NativeAwait_StoresAValueContinuationUntilCompletion()
        {
            OnityTask gate = OnityTask.NextFrame();
            object source = GetNativeState(gate);
            OnityTask<int> task = ReturnAfterNativeGateAsync(gate);

            Assert.That(task.IsCompleted, Is.False);
            Assert.That(GetSourceField(source, "m_continuation"), Is.Null);
            Assert.That(GetRecordRunner(GetNativeContinuation(source)), Is.SameAs(GetState(task)));

            CompleteNativeSource(gate);

            Assert.That(task.GetAwaiter().GetResult(), Is.EqualTo(67));
            Assert.That(GetRecordRunner(GetNativeContinuation(source)), Is.Null);
        }

        [Test]
        public async Task NativeAwait_CompletionRacingRegistrationResumesOnce()
        {
            for (int i = 0; i < 32; i++)
            {
                OnityTask gate = OnityTask.NextFrame();
                object source = GetNativeState(gate);
                DetachNativeSource(source);
                using ManualResetEventSlim start = new ManualResetEventSlim(false);
                Task completion = Task.Run(() =>
                {
                    start.Wait();
                    TickNativeSource(source);
                });

                start.Set();
                OnityTask<int> task = ReturnAfterNativeGateAsync(gate);
                await completion;
                Assert.That(await task, Is.EqualTo(67));
            }
        }

        [Test]
        public void NativeAwait_StaleAndDuplicateRecordsCannotResumeAReusedRunner()
        {
            OnityTask oldGate = OnityTask.NextFrame();
            object oldSource = GetNativeState(oldGate);
            OnityTask<int> oldTask = ReturnAfterNativeGateAsync(oldGate);
            object oldRunner = GetState(oldTask);
            object oldRecord = GetNativeContinuation(oldSource);
            CompleteNativeSource(oldGate);
            Assert.That(oldTask.GetAwaiter().GetResult(), Is.EqualTo(67));

            OnityTask newGate = OnityTask.NextFrame();
            OnityTask<int> newTask = ReturnAfterNativeGateAsync(newGate);
            Assert.That(GetState(newTask), Is.SameAs(oldRunner));

            InvokeNativeContinuation(oldRecord);
            InvokeNativeContinuation(oldRecord);
            Assert.That(newTask.IsCompleted, Is.False);

            CompleteNativeSource(newGate);
            Assert.That(newTask.GetAwaiter().GetResult(), Is.EqualTo(67));
        }

        [Test]
        public async Task NativeThenExternalAwait_PreservesBothContinuations()
        {
            OnityTask nativeGate = OnityTask.NextFrame();
            TaskCompletionSource<bool> externalGate = new TaskCompletionSource<bool>();
            OnityTask<int> task = ReturnAfterNativeAndExternalGatesAsync(
                nativeGate, externalGate.Task);

            CompleteNativeSource(nativeGate);
            Assert.That(task.IsCompleted, Is.False);

            externalGate.SetResult(true);
            Assert.That(await task, Is.EqualTo(71));
        }

        [Test]
        public async Task NativeAwait_AsTaskFaultAndCancellationKeepTheirStatus()
        {
            OnityTask successGate = OnityTask.NextFrame();
            Task<int> converted = ReturnAfterNativeGateAsync(successGate).AsTask();
            CompleteNativeSource(successGate);
            Assert.That(await converted, Is.EqualTo(67));

            int predicateCalls = 0;
            OnityTask faultGate = OnityTask.WaitUntil(() =>
            {
                if (predicateCalls++ == 0)
                {
                    return false;
                }

                throw new InvalidOperationException("native source failure");
            });
            OnityTask<int> faulted = ReturnAfterNativeGateAsync(faultGate);
            CompleteNativeSource(faultGate);
            Assert.That(faulted.IsFaulted, Is.True);
            Assert.Throws<InvalidOperationException>(() => faulted.GetAwaiter().GetResult());

            using CancellationTokenSource cancellation = new CancellationTokenSource();
            OnityTask cancelGate = OnityTask.NextFrame(cancellation.Token);
            OnityTask<int> canceled = ReturnAfterNativeGateAsync(cancelGate);
            cancellation.Cancel();
            CancelNativeSource(cancelGate);
            Assert.That(canceled.IsCanceled, Is.True);
            Assert.Catch<OperationCanceledException>(() => canceled.GetAwaiter().GetResult());
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

        [Test]
        public async Task ConfigureAwaitFalse_DoesNotRestoreTheUnitySynchronizationContext()
        {
            SynchronizationContext unityContext = SynchronizationContext.Current;
            Assert.That(unityContext, Is.Not.Null);
            int unityThreadId = Thread.CurrentThread.ManagedThreadId;
            AsyncLocal<string> ambient = new AsyncLocal<string>();
            ambient.Value = "captured";
            TaskCompletionSource<bool> gate = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            OnityTask<ContextSnapshot> task = ReadContextAfterGateAsync(
                gate.Task, ambient, true);
            ambient.Value = "caller";

            gate.SetResult(true);
            ContextSnapshot snapshot = await task;

            Assert.That(snapshot.FirstContext, Is.Null);
            Assert.That(snapshot.AfterYieldContext, Is.Null);
            Assert.That(snapshot.FirstThreadId, Is.Not.EqualTo(unityThreadId));
            Assert.That(snapshot.AfterYieldThreadId, Is.Not.EqualTo(unityThreadId));
            Assert.That(snapshot.AmbientValue, Is.EqualTo("captured"));
            Assert.That(snapshot.AmbientValueAfterYield, Is.EqualTo("captured"));
            Assert.That(ambient.Value, Is.EqualTo("caller"));
        }

        [Test]
        public async Task DefaultAwait_ResumesOnTheUnitySynchronizationContext()
        {
            SynchronizationContext unityContext = SynchronizationContext.Current;
            Assert.That(unityContext, Is.Not.Null);
            int unityThreadId = Thread.CurrentThread.ManagedThreadId;
            AsyncLocal<string> ambient = new AsyncLocal<string>();
            ambient.Value = "captured";
            TaskCompletionSource<bool> gate = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            OnityTask<ContextSnapshot> task = ReadContextAfterGateAsync(
                gate.Task, ambient, false);
            ambient.Value = "caller";

            gate.SetResult(true);
            ContextSnapshot snapshot = await task;

            Assert.That(snapshot.FirstContext, Is.Not.Null);
            Assert.That(snapshot.AfterYieldContext, Is.Not.Null);
            Assert.That(snapshot.FirstContext.GetType(), Is.EqualTo(unityContext.GetType()));
            Assert.That(snapshot.AfterYieldContext.GetType(), Is.EqualTo(unityContext.GetType()));
            Assert.That(snapshot.FirstThreadId, Is.EqualTo(unityThreadId));
            Assert.That(snapshot.AfterYieldThreadId, Is.EqualTo(unityThreadId));
            Assert.That(snapshot.AmbientValue, Is.EqualTo("captured"));
            Assert.That(snapshot.AmbientValueAfterYield, Is.EqualTo("captured"));
            Assert.That(ambient.Value, Is.EqualTo("caller"));
        }

        [Test]
        public async Task NativeOnityAwait_ResumesOnTheUnitySynchronizationContext()
        {
            SynchronizationContext unityContext = SynchronizationContext.Current;
            Assert.That(unityContext, Is.Not.Null);
            int unityThreadId = Thread.CurrentThread.ManagedThreadId;
            AsyncLocal<string> ambient = new AsyncLocal<string>();
            ambient.Value = "captured";
            TaskCompletionSource<bool> firstGate = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<bool> secondGate = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            OnityTask<ContextSnapshot> task = ReadContextAfterNativeGateAsync(
                firstGate.Task, secondGate.Task, ambient);
            ambient.Value = "caller";

            firstGate.SetResult(true);
            ContextSnapshot snapshot = await task;
            secondGate.SetResult(true);

            Assert.That(snapshot.FirstContext, Is.Not.Null);
            Assert.That(snapshot.AfterYieldContext, Is.Not.Null);
            Assert.That(snapshot.FirstContext.GetType(), Is.EqualTo(unityContext.GetType()));
            Assert.That(snapshot.AfterYieldContext.GetType(), Is.EqualTo(unityContext.GetType()));
            Assert.That(snapshot.FirstThreadId, Is.EqualTo(unityThreadId));
            Assert.That(snapshot.AfterYieldThreadId, Is.EqualTo(unityThreadId));
            Assert.That(snapshot.AmbientValue, Is.EqualTo("captured"));
            Assert.That(snapshot.AmbientValueAfterYield, Is.EqualTo("captured"));
            Assert.That(ambient.Value, Is.EqualTo("caller"));
        }

        [Test]
        public async Task CrossThreadCallbackQueuedDuringMoveNext_ResumesOnThreadPool()
        {
            int unityThreadId = Thread.CurrentThread.ManagedThreadId;
            AsyncLocal<string> ambient = new AsyncLocal<string>();
            ambient.Value = "captured";
            SafeAwaitable firstGate = new SafeAwaitable();
            OnityTask<ContextSnapshot> task = ReadContextAfterCrossThreadGateAsync(
                firstGate, new CrossThreadSafeAwaitable(), ambient);
            ambient.Value = "caller";

            firstGate.Complete();
            ContextSnapshot snapshot = await task;

            Assert.That(snapshot.FirstContext, Is.Null);
            Assert.That(snapshot.AfterYieldContext, Is.Null);
            Assert.That(snapshot.FirstThreadId, Is.Not.EqualTo(unityThreadId));
            Assert.That(snapshot.AfterYieldThreadId, Is.Not.EqualTo(unityThreadId));
            Assert.That(snapshot.AmbientValue, Is.EqualTo("captured"));
            Assert.That(snapshot.AmbientValueAfterYield, Is.EqualTo("captured"));
            Assert.That(ambient.Value, Is.EqualTo("caller"));
        }

        [Test]
        public async Task SuppressedFlow_QueuedResumeDoesNotInheritCallerAsyncLocal()
        {
            AsyncLocal<string> ambient = new AsyncLocal<string>();
            ambient.Value = "caller";
            SafeAwaitable firstGate = new SafeAwaitable();
            OnityTask<string> task = ReadAfterSuppressedCrossThreadGateAsync(
                firstGate, new SuppressedCrossThreadSafeAwaitable(), ambient);

            firstGate.Complete();

            Assert.That(await task, Is.Null);
            Assert.That(ambient.Value, Is.EqualTo("caller"));
        }

        [Test]
        public async Task PostThenThrow_DoesNotFaultAReusedRunner()
        {
            SafeAwaitable firstGate = new SafeAwaitable();
            SafeAwaitable replacementGate = new SafeAwaitable();
            OnityTask<int> replacement = default;
            ThrowAfterPostingContext context = new ThrowAfterPostingContext(() =>
            {
                replacement = ReturnAfterPostContextGateAsync(
                    replacementGate, new PostContextSafeAwaitable(null));
            });
            OnityTask<int> original = ReturnAfterPostContextGateAsync(
                firstGate, new PostContextSafeAwaitable(context));
            object originalRunner = GetState(original);
            Task<int> originalTask = original.AsTask();
            LogAssert.Expect(LogType.Exception, new Regex("post after callback"));

            firstGate.Complete();

            Assert.That(await originalTask, Is.EqualTo(3));
            Assert.That(GetState(replacement), Is.SameAs(originalRunner));
            Assert.That(replacement.IsCompleted, Is.False);

            replacementGate.Complete();
            Assert.That(await replacement, Is.EqualTo(3));
        }

        private static object GetState<T>(OnityTask<T> task)
        {
            FieldInfo field = typeof(OnityTask<T>).GetField("m_state",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return field.GetValue(task);
        }

        private static object GetNativeState(OnityTask task)
        {
            FieldInfo field = typeof(OnityTask).GetField("m_state",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return field.GetValue(task);
        }

        private static object GetSourceField(object source, string fieldName)
        {
            FieldInfo field = source.GetType().BaseType.GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return field.GetValue(source);
        }

        private static object GetNativeContinuation(object source)
        {
            return GetSourceField(source, "m_nativeContinuation");
        }

        private static object GetRecordRunner(object record)
        {
            FieldInfo field = record.GetType().GetField("Runner",
                BindingFlags.Instance | BindingFlags.Public);
            Assert.That(field, Is.Not.Null);
            return field.GetValue(record);
        }

        private static void InvokeNativeContinuation(object record)
        {
            MethodInfo method = record.GetType().GetMethod("Invoke",
                BindingFlags.Instance | BindingFlags.Public);
            Assert.That(method, Is.Not.Null);
            method.Invoke(record, null);
        }

        private static void DetachNativeSource(object source)
        {
            Type runnerType = typeof(OnityTask).Assembly.GetType(
                "Onity.Unity.Async.OnityTaskRunner");
            Assert.That(runnerType, Is.Not.Null);
            FieldInfo instanceField = runnerType.GetField("s_instance",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(instanceField, Is.Not.Null);
            object runner = instanceField.GetValue(null);
            Assert.That(runner, Is.Not.Null);
            FieldInfo sourcesField = runnerType.GetField("m_updateSources",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(sourcesField, Is.Not.Null);
            IList sources = (IList)sourcesField.GetValue(runner);
            Assert.That(sources.Contains(source), Is.True);
            sources.Remove(source);
        }

        private static void TickNativeSource(object source)
        {
            MethodInfo tick = source.GetType().GetMethod("Tick",
                BindingFlags.Instance | BindingFlags.Public);
            Assert.That(tick, Is.Not.Null);
            Assert.That(tick.Invoke(source, new object[] { 0f, 0f }), Is.EqualTo(true));
        }

        private static void CompleteNativeSource(OnityTask task)
        {
            object source = GetNativeState(task);
            DetachNativeSource(source);
            TickNativeSource(source);
        }

        private static void CancelNativeSource(OnityTask task)
        {
            object source = GetNativeState(task);
            DetachNativeSource(source);
            PropertyInfo version = source.GetType().GetProperty("Version",
                BindingFlags.Instance | BindingFlags.Public);
            MethodInfo cancel = source.GetType().GetMethod("TrySetCanceledFromRunner",
                BindingFlags.Instance | BindingFlags.Public);
            Assert.That(version, Is.Not.Null);
            Assert.That(cancel, Is.Not.Null);
            Assert.That(cancel.Invoke(source, new[] { version.GetValue(source) }), Is.EqualTo(true));
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

        private static async OnityTask<int> ReturnAfterNativeGateAsync(OnityTask gate)
        {
            await gate;
            return 67;
        }

        private static async OnityTask<int> ReturnAfterNativeAndExternalGatesAsync(
            OnityTask nativeGate,
            Task externalGate)
        {
            await nativeGate;
            await externalGate;
            return 71;
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

        private static async OnityTask<ContextSnapshot> ReadContextAfterGateAsync(
            Task gate,
            AsyncLocal<string> ambient,
            bool configureFalse)
        {
            if (configureFalse)
            {
                await gate.ConfigureAwait(false);
            }
            else
            {
                await gate;
            }

            SynchronizationContext firstContext = SynchronizationContext.Current;
            int firstThreadId = Thread.CurrentThread.ManagedThreadId;
            string ambientValue = ambient.Value;
            await Task.Yield();
            return new ContextSnapshot(
                firstContext,
                SynchronizationContext.Current,
                firstThreadId,
                Thread.CurrentThread.ManagedThreadId,
                ambientValue,
                ambient.Value);
        }

        private static async OnityTask<ContextSnapshot> ReadContextAfterNativeGateAsync(
            Task firstGate,
            Task secondGate,
            AsyncLocal<string> ambient)
        {
            await OnityTask.WhenAny(
                OnityTask.FromTask(firstGate),
                OnityTask.FromTask(secondGate));

            SynchronizationContext firstContext = SynchronizationContext.Current;
            int firstThreadId = Thread.CurrentThread.ManagedThreadId;
            string ambientValue = ambient.Value;
            await Task.Yield();
            return new ContextSnapshot(
                firstContext,
                SynchronizationContext.Current,
                firstThreadId,
                Thread.CurrentThread.ManagedThreadId,
                ambientValue,
                ambient.Value);
        }

        private static async OnityTask<ContextSnapshot> ReadContextAfterCrossThreadGateAsync(
            SafeAwaitable firstGate,
            CrossThreadSafeAwaitable secondGate,
            AsyncLocal<string> ambient)
        {
            await firstGate;
            await secondGate;

            SynchronizationContext firstContext = SynchronizationContext.Current;
            int firstThreadId = Thread.CurrentThread.ManagedThreadId;
            string ambientValue = ambient.Value;
            await Task.Yield();
            return new ContextSnapshot(
                firstContext,
                SynchronizationContext.Current,
                firstThreadId,
                Thread.CurrentThread.ManagedThreadId,
                ambientValue,
                ambient.Value);
        }

        private static async OnityTask<string> ReadAfterSuppressedCrossThreadGateAsync(
            SafeAwaitable firstGate,
            SuppressedCrossThreadSafeAwaitable secondGate,
            AsyncLocal<string> ambient)
        {
            await firstGate;
            ExecutionContext.SuppressFlow();
            await secondGate;
            return ambient.Value;
        }

        private static async OnityTask<int> ReturnAfterPostContextGateAsync(
            SafeAwaitable firstGate,
            PostContextSafeAwaitable secondGate)
        {
            await firstGate;
            await secondGate;
            return 3;
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

        private sealed class CrossThreadSafeAwaitable : INotifyCompletion
        {
            public bool IsCompleted => false;

            public CrossThreadSafeAwaitable GetAwaiter()
            {
                return this;
            }

            public void OnCompleted(Action continuation)
            {
                Task.Run(() =>
                {
                    SynchronizationContext.SetSynchronizationContext(null);
                    continuation();
                }).GetAwaiter().GetResult();
            }

            public void GetResult()
            {
            }
        }

        private sealed class SuppressedCrossThreadSafeAwaitable : INotifyCompletion
        {
            public bool IsCompleted => false;

            public SuppressedCrossThreadSafeAwaitable GetAwaiter()
            {
                return this;
            }

            public void OnCompleted(Action continuation)
            {
                ExecutionContext.RestoreFlow();
                using (ExecutionContext.SuppressFlow())
                {
                    Task.Run(() =>
                    {
                        SynchronizationContext.SetSynchronizationContext(null);
                        continuation();
                    }).GetAwaiter().GetResult();
                }
            }

            public void GetResult()
            {
            }
        }

        private sealed class PostContextSafeAwaitable : INotifyCompletion
        {
            private readonly SynchronizationContext m_context;

            public PostContextSafeAwaitable(SynchronizationContext context)
            {
                m_context = context;
            }

            public bool IsCompleted => false;

            public PostContextSafeAwaitable GetAwaiter()
            {
                return this;
            }

            public void OnCompleted(Action continuation)
            {
                if (m_context == null)
                {
                    continuation();
                    return;
                }

                Task.Run(() =>
                {
                    SynchronizationContext.SetSynchronizationContext(m_context);
                    continuation();
                }).GetAwaiter().GetResult();
            }

            public void GetResult()
            {
            }
        }

        private sealed class ThrowAfterPostingContext : SynchronizationContext
        {
            private readonly Action m_afterCallback;

            public ThrowAfterPostingContext(Action afterCallback)
            {
                m_afterCallback = afterCallback;
            }

            public override void Post(SendOrPostCallback callback, object state)
            {
                Task.Run(() =>
                {
                    SetSynchronizationContext(this);
                    callback(state);
                }).GetAwaiter().GetResult();

                m_afterCallback();
                throw new InvalidOperationException("post after callback");
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

        private readonly struct ContextSnapshot
        {
            public readonly SynchronizationContext FirstContext;
            public readonly SynchronizationContext AfterYieldContext;
            public readonly int FirstThreadId;
            public readonly int AfterYieldThreadId;
            public readonly string AmbientValue;
            public readonly string AmbientValueAfterYield;

            public ContextSnapshot(
                SynchronizationContext firstContext,
                SynchronizationContext afterYieldContext,
                int firstThreadId,
                int afterYieldThreadId,
                string ambientValue,
                string ambientValueAfterYield)
            {
                FirstContext = firstContext;
                AfterYieldContext = afterYieldContext;
                FirstThreadId = firstThreadId;
                AfterYieldThreadId = afterYieldThreadId;
                AmbientValue = ambientValue;
                AmbientValueAfterYield = ambientValueAfterYield;
            }
        }
    }
}
