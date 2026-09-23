using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;

namespace Onity.Tests.EditMode
{
    [TestFixture]
    public sealed class OnityWhenAnyEditModeTests
    {
        [Test]
        public void WhenAny_BothCompleted_FirstWinsTie()
        {
            OnityTask<int> race = OnityTask.WhenAny(OnityTask.Completed, OnityTask.Completed);

            Assert.That(race.IsCompletedSuccessfully, Is.True);
            Assert.That(race.GetAwaiter().GetResult(), Is.EqualTo(0));
        }

        [Test]
        public async Task WhenAny_SecondCompletedWhileFirstPending_ReturnsOne()
        {
            TaskCompletionSource<bool> first = NewCompletionSource();
            OnityTask<int> race = OnityTask.WhenAny(
                OnityTask.FromTask(first.Task),
                OnityTask.Completed);

            Assert.That(await race.AsTask(), Is.EqualTo(1));
            first.SetResult(true);
        }

        [Test]
        public async Task WhenAny_SecondCompletesAfterRegistration_ReturnsOne()
        {
            TaskCompletionSource<bool> first = NewCompletionSource();
            TaskCompletionSource<bool> second = NewCompletionSource();
            OnityTask<int> race = OnityTask.WhenAny(
                OnityTask.FromTask(first.Task),
                OnityTask.FromTask(second.Task));
            Task<int> resultTask = race.AsTask();

            Assert.That(resultTask.IsCompleted, Is.False);
            second.SetResult(true);
            Assert.That(await resultTask, Is.EqualTo(1));
            first.SetResult(true);
        }

        [Test]
        public async Task WhenAny_FirstFaults_PropagatesOriginalFailure()
        {
            TaskCompletionSource<bool> first = NewCompletionSource();
            TaskCompletionSource<bool> second = NewCompletionSource();
            Exception failure = new InvalidOperationException("first failed");
            OnityTask<int> race = OnityTask.WhenAny(
                OnityTask.FromTask(first.Task),
                OnityTask.FromTask(second.Task));
            Task<int> resultTask = race.AsTask();

            first.SetException(failure);
            try
            {
                await resultTask;
                Assert.Fail("Expected the first failure.");
            }
            catch (InvalidOperationException caught)
            {
                Assert.That(caught, Is.SameAs(failure));
            }

            second.SetResult(true);
        }

        [Test]
        public void WhenAny_FaultedOperationCanceledException_RemainsFaulted()
        {
            OperationCanceledException failure = new OperationCanceledException("faulted input");
            OnityTask<int> race = OnityTask.WhenAny(
                OnityTask.FromException(failure),
                OnityTask.Completed);

            Assert.That(race.IsFaulted, Is.True);
            Assert.That(race.IsCanceled, Is.False);
            OperationCanceledException caught = Assert.Catch<OperationCanceledException>(
                () => race.GetAwaiter().GetResult());
            Assert.That(caught, Is.SameAs(failure));
        }

        [Test]
        public void WhenAny_FirstCanceled_PreservesCancellationToken()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            OnityTask<int> race = OnityTask.WhenAny(
                OnityTask.FromCanceled(cancellation.Token),
                OnityTask.Completed);

            Assert.That(race.IsCanceled, Is.True);
            OperationCanceledException caught = Assert.Catch<OperationCanceledException>(
                () => race.GetAwaiter().GetResult());
            Assert.That(caught.CancellationToken, Is.EqualTo(cancellation.Token));

            OnityTask<int> bridged = OnityTask.WhenAny(
                OnityTask.FromCanceled(cancellation.Token),
                OnityTask.Completed);
            Assert.That(bridged.AsTask().IsCanceled, Is.True);
        }

        [Test]
        public void WhenAny_LateNativeLoser_IsConsumedAfterWinner()
        {
            (OnityTask loser, object source, MethodInfo tick) = NewStandaloneFrameTask();
            OnityTask<int> race = OnityTask.WhenAny(OnityTask.Completed, loser);

            Assert.That(race.GetAwaiter().GetResult(), Is.EqualTo(0));
            Assert.That(loser.IsCompleted, Is.False);
            Assert.That(tick.Invoke(source, new object[] { 0f, 0f }), Is.EqualTo(true));
            Assert.Throws<InvalidOperationException>(() => loser.GetAwaiter().GetResult());
        }

        [Test]
        public void WhenAny_SameNativeInputTwice_RejectsDoubleConsumption()
        {
            (OnityTask task, object source, MethodInfo tick) = NewStandaloneFrameTask();

            Assert.Throws<ArgumentException>(() => OnityTask.WhenAny(task, task));
            tick.Invoke(source, new object[] { 0f, 0f });
            Assert.DoesNotThrow(() => task.GetAwaiter().GetResult());
        }

        [Test]
        public async Task WhenAny_ConcurrentCompletions_ReturnsOneWinner()
        {
            for (int i = 0; i < 64; i++)
            {
                TaskCompletionSource<bool> first = NewCompletionSource();
                TaskCompletionSource<bool> second = NewCompletionSource();
                OnityTask<int> race = await Task.Run(() => OnityTask.WhenAny(
                    OnityTask.FromTask(first.Task),
                    OnityTask.FromTask(second.Task)));
                Task<int> resultTask = race.AsTask();

                await Task.WhenAll(
                    Task.Run(() => first.SetResult(true)),
                    Task.Run(() => second.SetResult(true)));

                Assert.That(await resultTask, Is.InRange(0, 1));
            }
        }

        [Test]
        public async Task AsTask_ConcurrentCompletionAndSecondBridge_PreservesResult()
        {
            for (int i = 0; i < 128; i++)
            {
                TaskCompletionSource<bool> first = NewCompletionSource();
                TaskCompletionSource<bool> second = NewCompletionSource();
                OnityTask<int> race = OnityTask.WhenAny(
                    OnityTask.FromTask(first.Task),
                    OnityTask.FromTask(second.Task));
                Task<int> bridge = race.AsTask();

                await RaceBridgeWithCompletion(
                    () => second.SetResult(true),
                    () => race.AsTask(),
                    bridge);

                Assert.That(await bridge, Is.EqualTo(1));
                first.SetResult(true);
            }
        }

        [Test]
        public async Task AsTask_ConcurrentFaultAndSecondBridge_PreservesException()
        {
            for (int i = 0; i < 64; i++)
            {
                TaskCompletionSource<bool> first = NewCompletionSource();
                TaskCompletionSource<bool> second = NewCompletionSource();
                InvalidOperationException failure = new InvalidOperationException("winner failed");
                OnityTask<int> race = OnityTask.WhenAny(
                    OnityTask.FromTask(first.Task),
                    OnityTask.FromTask(second.Task));
                Task<int> bridge = race.AsTask();

                await RaceBridgeWithCompletion(
                    () => second.SetException(failure),
                    () => race.AsTask(),
                    bridge);

                InvalidOperationException caught = Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await bridge);
                Assert.That(caught, Is.SameAs(failure));
                first.SetResult(true);
            }
        }

        [Test]
        public async Task AsTask_ConcurrentCancellationAndSecondBridge_PreservesToken()
        {
            for (int i = 0; i < 64; i++)
            {
                using CancellationTokenSource cancellation = new CancellationTokenSource();
                cancellation.Cancel();
                TaskCompletionSource<bool> first = NewCompletionSource();
                TaskCompletionSource<bool> second = NewCompletionSource();
                OnityTask<int> race = OnityTask.WhenAny(
                    OnityTask.FromTask(first.Task),
                    OnityTask.FromTask(second.Task));
                Task<int> bridge = race.AsTask();

                await RaceBridgeWithCompletion(
                    () => second.TrySetCanceled(cancellation.Token),
                    () => race.AsTask(),
                    bridge);

                OperationCanceledException caught = Assert.CatchAsync<OperationCanceledException>(
                    async () => await bridge);
                Assert.That(caught.CancellationToken, Is.EqualTo(cancellation.Token));
                first.SetResult(true);
            }
        }

        [Test]
        public async Task AsTask_UntypedConcurrentCompletionAndSecondBridge_Completes()
        {
            for (int i = 0; i < 128; i++)
            {
                (OnityTask task, object source, MethodInfo tick) = NewStandaloneFrameTask();
                Task bridge = task.AsTask();

                await RaceBridgeWithCompletion(
                    () => tick.Invoke(source, new object[] { 0f, 0f }),
                    () => task.AsTask(),
                    bridge);

                Assert.That(bridge.IsCompletedSuccessfully, Is.True);
            }
        }

        [Test]
        public void WhenAny_ResultSource_AllowsOnlyOneNativeConsumer()
        {
            OnityTask<int> race = OnityTask.WhenAny(OnityTask.Completed, OnityTask.Completed);
            OnityTaskAwaiter<int> awaiter = race.GetAwaiter();

            Assert.That(awaiter.GetResult(), Is.EqualTo(0));
            Assert.Throws<InvalidOperationException>(() => awaiter.GetResult());
        }

        private static TaskCompletionSource<bool> NewCompletionSource()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private static async Task RaceBridgeWithCompletion(
            Action complete,
            Action materialize,
            Task bridge)
        {
            using ManualResetEventSlim start = new ManualResetEventSlim();
            using CancellationTokenSource timeout = new CancellationTokenSource();
            Task deadline = Task.Delay(TimeSpan.FromSeconds(5), timeout.Token);
            Task completing = Task.Run(() =>
            {
                start.Wait();
                complete();
            });
            Task materializing = Task.Run(() =>
            {
                start.Wait();
                try
                {
                    materialize();
                }
                catch (InvalidOperationException)
                {
                    // A bridge requested after the pooled source is released is invalid.
                }
            });

            start.Set();
            Task actors = Task.WhenAll(completing, materializing);
            Assert.That(await Task.WhenAny(actors, deadline), Is.SameAs(actors));
            await actors;
            Assert.That(await Task.WhenAny(bridge, deadline), Is.SameAs(bridge));
            timeout.Cancel();
        }

        private static (OnityTask task, object source, MethodInfo tick) NewStandaloneFrameTask()
        {
            Type sourceType = typeof(OnityTask).Assembly.GetType(
                "Onity.Unity.Async.OnityFrameTaskSource",
                true);
            object source = Activator.CreateInstance(sourceType, true);
            MethodInfo reset = sourceType.BaseType.GetMethod(
                "Reset",
                BindingFlags.Instance | BindingFlags.NonPublic);
            reset.Invoke(source, new object[] { CancellationToken.None });

            ConstructorInfo taskConstructor = null;
            ConstructorInfo[] constructors = typeof(OnityTask).GetConstructors(
                BindingFlags.Instance | BindingFlags.NonPublic);
            for (int i = 0; i < constructors.Length; i++)
            {
                ParameterInfo[] parameters = constructors[i].GetParameters();
                if (parameters.Length == 1 && parameters[0].ParameterType.IsAssignableFrom(sourceType))
                {
                    taskConstructor = constructors[i];
                    break;
                }
            }

            Assert.That(taskConstructor, Is.Not.Null);
            MethodInfo tick = sourceType.GetMethod("Tick", BindingFlags.Instance | BindingFlags.Public);
            return ((OnityTask)taskConstructor.Invoke(new[] { source }), source, tick);
        }
    }
}
