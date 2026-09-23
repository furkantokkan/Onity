using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;

namespace Onity.Tests.EditMode
{
    [TestFixture]
    public sealed class OnityTaskPreserveEditModeTests
    {
        [Test]
        public void Preserve_InlineTaskBackedAndShareableTasks_KeepTheirState()
        {
            OnityTask completed = OnityTask.Completed.Preserve();
            OnityTask<int> result = OnityTask.FromResult(7).Preserve();
            Task<int> typedBacking = Task.FromResult(11);
            Task untypedBacking = Task.CompletedTask;
            OnityTask untypedTask = OnityTask.FromTask(untypedBacking);
            OnityTask<int> genericTask = OnityTask<int>.FromTask(typedBacking);
            OnityTaskCompletionSource untypedSource = new OnityTaskCompletionSource();
            OnityTaskCompletionSource<int> typedSource = new OnityTaskCompletionSource<int>();

            Assert.That(GetState(completed), Is.Null);
            Assert.That(GetState(result), Is.Null);
            Assert.That(result.GetAwaiter().GetResult(), Is.EqualTo(7));
            Assert.That(GetState(untypedTask.Preserve()), Is.SameAs(untypedBacking));
            Assert.That(GetState(genericTask.Preserve()), Is.SameAs(typedBacking));
            Assert.That(GetState(untypedSource.Task.Preserve()), Is.SameAs(untypedSource));
            Assert.That(GetState(typedSource.Task.Preserve()), Is.SameAs(typedSource));
            Assert.That(untypedSource.TrySetResult(), Is.True);
            Assert.That(typedSource.TrySetResult(13), Is.True);
        }

        [Test]
        public async Task Preserve_UntypedNativeTask_SupportsFourPendingAndLateConsumers()
        {
            (OnityTask original, object source, MethodInfo tick) = NewStandaloneFrameTask();
            OnityTask shared = original.Preserve();
            Task first = Observe(shared);
            Task second = Observe(shared);
            Task third = Observe(shared);
            Task fourth = Observe(shared);
            Task bridge = shared.AsTask();

            Assert.That(first.IsCompleted, Is.False);
            Assert.That(bridge.IsCompleted, Is.False);
            Assert.That(shared.AsTask(), Is.SameAs(bridge));
            Assert.That(GetState(shared.Preserve()), Is.SameAs(GetState(shared)));
            Assert.Throws<InvalidOperationException>(() => original.Preserve());
            Assert.Throws<InvalidOperationException>(() => original.GetAwaiter().OnCompleted(() => { }));

            Assert.That(tick.Invoke(source, new object[] { 0f, 0f }), Is.EqualTo(true));
            await Task.WhenAll(first, second, third, fourth, bridge);
            await Observe(shared);
            Assert.DoesNotThrow(() => shared.GetAwaiter().GetResult());
            Assert.Throws<InvalidOperationException>(() => original.GetAwaiter().GetResult());
        }

        [Test]
        public async Task Preserve_TypedNativeTask_SupportsFourPendingAndLateConsumers()
        {
            TaskCompletionSource<bool> firstInput = NewCompletionSource();
            TaskCompletionSource<bool> secondInput = NewCompletionSource();
            OnityTask<int> original = OnityTask.WhenAny(
                OnityTask.FromTask(firstInput.Task),
                OnityTask.FromTask(secondInput.Task));
            OnityTask<int> shared = original.Preserve();
            Task<int> first = Observe(shared);
            Task<int> second = Observe(shared);
            Task<int> third = Observe(shared);
            Task<int> fourth = Observe(shared);
            Task<int> bridge = shared.AsTask();

            Assert.That(first.IsCompleted, Is.False);
            Assert.That(bridge.IsCompleted, Is.False);
            Assert.That(shared.AsTask(), Is.SameAs(bridge));
            Assert.That(GetState(shared.Preserve()), Is.SameAs(GetState(shared)));
            Assert.Throws<InvalidOperationException>(() => original.Preserve());
            Assert.Throws<InvalidOperationException>(() => original.GetAwaiter().OnCompleted(() => { }));

            secondInput.SetResult(true);
            int[] results = await Task.WhenAll(first, second, third, fourth, bridge);
            Assert.That(results, Is.All.EqualTo(1));
            Assert.That(await Observe(shared), Is.EqualTo(1));
            Assert.Throws<InvalidOperationException>(() => original.GetAwaiter().GetResult());
            firstInput.SetResult(true);
        }

        [Test]
        public void Preserve_InvokesPendingConsumerOutsideSourceLock()
        {
            (OnityTask original, object source, MethodInfo tick) = NewStandaloneFrameTask();
            OnityTask shared = original.Preserve();
            object retainedSource = GetState(shared);
            bool called = false;
            bool lockHeld = true;

            shared.GetAwaiter().OnCompleted(() =>
            {
                called = true;
                lockHeld = Monitor.IsEntered(retainedSource);
            });

            Assert.That(tick.Invoke(source, new object[] { 0f, 0f }), Is.EqualTo(true));
            Assert.That(called, Is.True);
            Assert.That(lockHeld, Is.False);
            Assert.DoesNotThrow(() => shared.GetAwaiter().GetResult());
        }

        [Test]
        public void Preserve_AlreadyCompletedNativeTask_KeepsItsResult()
        {
            (OnityTask original, object source, MethodInfo tick) = NewStandaloneFrameTask();
            Assert.That(tick.Invoke(source, new object[] { 0f, 0f }), Is.EqualTo(true));

            OnityTask shared = original.Preserve();

            Assert.That(shared.IsCompleted, Is.True);
            Assert.DoesNotThrow(() => shared.GetAwaiter().GetResult());
            Assert.DoesNotThrow(() => shared.GetAwaiter().GetResult());
            Assert.Throws<InvalidOperationException>(() => original.GetAwaiter().GetResult());
        }

        [Test]
        public void Preserve_UntypedOriginalCannotTakeResultBeforeAdapterCompletion()
        {
            var frame = NewStandaloneFrameTask();
            OnityTask original = frame.task;
            object source = frame.source;
            OnityTask shared = original.Preserve();

            Assert.Throws<InvalidOperationException>(() => original.GetAwaiter().GetResult());

            PublishNativeSuccessWithoutCallback(source);
            Assert.Throws<InvalidOperationException>(() => original.GetAwaiter().GetResult());

            CompletePreservedAdapter(shared);
            Assert.DoesNotThrow(() => shared.GetAwaiter().GetResult());
        }

        [Test]
        public void Preserve_TypedOriginalCannotTakeResultBeforeAdapterCompletion()
        {
            TaskCompletionSource<bool> firstInput = NewCompletionSource();
            TaskCompletionSource<bool> secondInput = NewCompletionSource();
            OnityTask<int> original = OnityTask.WhenAny(
                OnityTask.FromTask(firstInput.Task),
                OnityTask.FromTask(secondInput.Task));
            object source = GetState(original);
            OnityTask<int> shared = original.Preserve();

            Assert.Throws<InvalidOperationException>(() => original.GetAwaiter().GetResult());

            PublishNativeSuccessWithoutCallback(source);
            Assert.Throws<InvalidOperationException>(() => original.GetAwaiter().GetResult());

            CompletePreservedAdapter(shared);
            Assert.That(shared.GetAwaiter().GetResult(), Is.EqualTo(0));
            firstInput.SetResult(true);
            secondInput.SetResult(true);
        }

        [Test]
        public async Task Preserve_ConcurrentNativeCompletion_DoesNotLoseResult()
        {
            for (int i = 0; i < 64; i++)
            {
                TaskCompletionSource<bool> firstInput = NewCompletionSource();
                TaskCompletionSource<bool> secondInput = NewCompletionSource();
                OnityTask<int> original = OnityTask.WhenAny(
                    OnityTask.FromTask(firstInput.Task),
                    OnityTask.FromTask(secondInput.Task));

                using ManualResetEventSlim start = new ManualResetEventSlim(false);
                Task producer = Task.Run(() =>
                {
                    start.Wait();
                    firstInput.SetResult(true);
                });
                start.Set();
                OnityTask<int> shared = original.Preserve();

                Assert.That(await Observe(shared), Is.EqualTo(0));
                await producer;
                secondInput.SetResult(true);
                Assert.That(shared.GetAwaiter().GetResult(), Is.EqualTo(0));
            }
        }

        [Test]
        public void Preserve_NativeFaultedOperationCanceledException_RemainsFaulted()
        {
            OperationCanceledException failure = new OperationCanceledException("faulted input");
            OnityTask<int> original = OnityTask.WhenAny(
                OnityTask.FromException(failure),
                OnityTask.Completed);
            OnityTask<int> shared = original.Preserve();

            Assert.That(shared.IsFaulted, Is.True);
            Assert.That(shared.IsCanceled, Is.False);
            Assert.That(Assert.Catch<OperationCanceledException>(
                () => shared.GetAwaiter().GetResult()), Is.SameAs(failure));
            Assert.That(shared.AsTask().IsFaulted, Is.True);
        }

        [Test]
        public void Preserve_UntypedNativeFaultedOperationCanceledException_RemainsFaulted()
        {
            var frame = NewStandaloneFrameTask();
            OnityTask original = frame.task;
            object source = frame.source;
            OnityTask shared = original.Preserve();
            OperationCanceledException failure = new OperationCanceledException("native fault");
            MethodInfo fail = source.GetType().BaseType.GetMethod(
                "TrySetException",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(fail.Invoke(source, new object[] { failure }), Is.EqualTo(true));
            Assert.That(shared.IsFaulted, Is.True);
            Assert.That(shared.IsCanceled, Is.False);
            Assert.That(Assert.Catch<OperationCanceledException>(
                () => shared.GetAwaiter().GetResult()), Is.SameAs(failure));
            Assert.That(shared.AsTask().IsFaulted, Is.True);
        }

        [Test]
        public void Preserve_NativeCancellation_PreservesTokenAndTaskBridge()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            var frame = NewStandaloneFrameTask(cancellation.Token);
            OnityTask original = frame.task;
            object source = frame.source;
            OnityTask shared = original.Preserve();
            Task bridge = shared.AsTask();
            MethodInfo cancel = source.GetType().BaseType.GetMethod(
                "TrySetCanceledFromRunner",
                BindingFlags.Instance | BindingFlags.Public);
            int token = (int)source.GetType().BaseType.GetProperty("Version").GetValue(source);

            cancellation.Cancel();
            Assert.That(cancel.Invoke(source, new object[] { token }), Is.EqualTo(true));
            Assert.That(shared.IsCanceled, Is.True);
            Assert.That(shared.IsFaulted, Is.False);
            Assert.That(Assert.Catch<OperationCanceledException>(
                () => shared.GetAwaiter().GetResult()).CancellationToken,
                Is.EqualTo(cancellation.Token));
            Assert.That(bridge.IsCanceled, Is.True);
        }

        [Test]
        public void Preserve_OriginalAlreadyMaterializedAsTask_IsRejected()
        {
            (OnityTask original, object source, MethodInfo tick) = NewStandaloneFrameTask();
            Task bridge = original.AsTask();

            Assert.Throws<InvalidOperationException>(() => original.Preserve());
            Assert.That(tick.Invoke(source, new object[] { 0f, 0f }), Is.EqualTo(true));
            Assert.DoesNotThrow(() => bridge.GetAwaiter().GetResult());
        }

        [Test]
        public void Preserve_OriginalAlreadyAwaited_IsRejected()
        {
            (OnityTask original, object source, MethodInfo tick) = NewStandaloneFrameTask();
            bool called = false;
            original.GetAwaiter().OnCompleted(() => called = true);

            Assert.Throws<InvalidOperationException>(() => original.Preserve());
            Assert.That(tick.Invoke(source, new object[] { 0f, 0f }), Is.EqualTo(true));
            Assert.That(called, Is.True);
            Assert.DoesNotThrow(() => original.GetAwaiter().GetResult());
        }

        [Test]
        public void Preserve_SharedNativeTask_CanBePassedToWhenAnyTwice()
        {
            (OnityTask original, object source, MethodInfo tick) = NewStandaloneFrameTask();
            OnityTask shared = original.Preserve();
            OnityTask<int> race = OnityTask.WhenAny(shared, shared);

            Assert.That(race.IsCompleted, Is.False);
            Assert.That(tick.Invoke(source, new object[] { 0f, 0f }), Is.EqualTo(true));
            Assert.That(race.GetAwaiter().GetResult(), Is.EqualTo(0));
            Assert.DoesNotThrow(() => shared.GetAwaiter().GetResult());
        }

        [Test]
        public void Preserve_PooledSourceReuse_DoesNotChangePreservedResult()
        {
            (OnityTask original, object source, MethodInfo tick) = NewStandaloneFrameTask();
            OnityTask shared = original.Preserve();
            Assert.That(tick.Invoke(source, new object[] { 0f, 0f }), Is.EqualTo(true));

            FieldInfo poolField = source.GetType().GetField(
                "s_pool",
                BindingFlags.Static | BindingFlags.NonPublic);
            object pool = poolField.GetValue(null);
            object reusedSource = pool.GetType().GetMethod("Pop").Invoke(pool, null);
            Assert.That(reusedSource, Is.SameAs(source));
            ResetSource(reusedSource, CancellationToken.None);
            OnityTask next = NewTaskFromSource(reusedSource);

            Assert.That(next.IsCompleted, Is.False);
            Assert.DoesNotThrow(() => shared.GetAwaiter().GetResult());
            Assert.Throws<InvalidOperationException>(() => original.GetAwaiter().GetResult());
            Assert.That(tick.Invoke(reusedSource, new object[] { 0f, 0f }), Is.EqualTo(true));
            Assert.DoesNotThrow(() => next.GetAwaiter().GetResult());
            Assert.DoesNotThrow(() => shared.GetAwaiter().GetResult());
        }

        private static TaskCompletionSource<bool> NewCompletionSource()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private static async Task Observe(OnityTask task)
        {
            await task;
        }

        private static async Task<int> Observe(OnityTask<int> task)
        {
            return await task;
        }

        private static object GetState(OnityTask task)
        {
            return typeof(OnityTask).GetField(
                "m_state",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(task);
        }

        private static object GetState<T>(OnityTask<T> task)
        {
            return typeof(OnityTask<T>).GetField(
                "m_state",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(task);
        }

        private static void PublishNativeSuccessWithoutCallback(object source)
        {
            // Reproduce the interval after terminal status is visible but before
            // the preserved adapter receives its completion callback.
            source.GetType().BaseType.GetField(
                "m_status",
                BindingFlags.Instance | BindingFlags.NonPublic).SetValue(source, 1);
        }

        private static void CompletePreservedAdapter(OnityTask task)
        {
            CompletePreservedAdapter(GetState(task));
        }

        private static void CompletePreservedAdapter<T>(OnityTask<T> task)
        {
            CompletePreservedAdapter(GetState(task));
        }

        private static void CompletePreservedAdapter(object adapter)
        {
            adapter.GetType().GetMethod(
                "Complete",
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(adapter, null);
        }

        private static (OnityTask task, object source, MethodInfo tick) NewStandaloneFrameTask(
            CancellationToken cancellationToken = default)
        {
            Type sourceType = typeof(OnityTask).Assembly.GetType(
                "Onity.Unity.Async.OnityFrameTaskSource",
                true);
            object source = Activator.CreateInstance(sourceType, true);
            ResetSource(source, cancellationToken);
            MethodInfo tick = sourceType.GetMethod("Tick", BindingFlags.Instance | BindingFlags.Public);
            return (NewTaskFromSource(source), source, tick);
        }

        private static void ResetSource(object source, CancellationToken cancellationToken)
        {
            MethodInfo reset = source.GetType().BaseType.GetMethod(
                "Reset",
                BindingFlags.Instance | BindingFlags.NonPublic);
            reset.Invoke(source, new object[] { cancellationToken });
        }

        private static OnityTask NewTaskFromSource(object source)
        {
            ConstructorInfo[] constructors = typeof(OnityTask).GetConstructors(
                BindingFlags.Instance | BindingFlags.NonPublic);
            for (int i = 0; i < constructors.Length; i++)
            {
                ParameterInfo[] parameters = constructors[i].GetParameters();
                if (parameters.Length == 1
                    && parameters[0].ParameterType.IsAssignableFrom(source.GetType()))
                {
                    return (OnityTask)constructors[i].Invoke(new[] { source });
                }
            }

            Assert.Fail("The native OnityTask constructor was not found.");
            return default;
        }
    }
}
