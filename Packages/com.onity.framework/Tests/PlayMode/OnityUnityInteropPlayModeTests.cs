// AsyncInstantiateOperation exists from Unity 2022.3.20 and in the Unity 6 line.
#if (UNITY_2022_3 && !(UNITY_2022_3_0 || UNITY_2022_3_1 || UNITY_2022_3_2 || UNITY_2022_3_3 || UNITY_2022_3_4 || UNITY_2022_3_5 || UNITY_2022_3_6 || UNITY_2022_3_7 || UNITY_2022_3_8 || UNITY_2022_3_9 || UNITY_2022_3_10 || UNITY_2022_3_11 || UNITY_2022_3_12 || UNITY_2022_3_13 || UNITY_2022_3_14 || UNITY_2022_3_15 || UNITY_2022_3_16 || UNITY_2022_3_17 || UNITY_2022_3_18 || UNITY_2022_3_19)) || UNITY_2023_3_OR_NEWER
#define ONITY_ASYNC_INSTANTIATE_TESTS
#endif

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using Onity.Unity.Async;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Onity.Tests.PlayMode
{
    /// <summary>
    /// Covers the Unity interop leftovers: the <see cref="IProgress{T}"/> overload of the
    /// async-operation adapter, awaiting a job handle, the instantiate-operation adapters and, in
    /// Unity 2023.1 and newer, the <c>Awaitable</c> adapters.
    /// </summary>
    [TestFixture]
    public sealed class OnityUnityInteropPlayModeTests
    {
        private const int k_timeoutSeconds = 10;

        private struct WriteJob : IJob
        {
            public NativeArray<int> Output;
            public int Value;

            public void Execute()
            {
                Output[0] = Value;
            }
        }

        private static IEnumerator WaitFor(Func<bool> predicate)
        {
            Stopwatch timer = Stopwatch.StartNew();
            while (!predicate() && timer.Elapsed.TotalSeconds < k_timeoutSeconds)
            {
                yield return null;
            }

            Assert.That(predicate(), Is.True, "The Unity operation adapter timed out.");
        }

        [UnityTest]
        public IEnumerator AsOnityTask_ProgressOverload_ReportsProgressAndCompletesWithTheOperation()
        {
            List<float> reports = new List<float>();
            IProgress<float> progress = OnityProgress.Create(reports.Add);
            ResourceRequest request = Resources.LoadAsync<TextAsset>("onity-interop-missing-asset");

            OnityTask<ResourceRequest> task = request.AsOnityTask(progress);
            yield return WaitFor(() => task.IsCompleted);

            Assert.That(task.GetAwaiter().GetResult(), Is.SameAs(request));
            Assert.That(reports.Count, Is.GreaterThan(0));
            Assert.That(reports[reports.Count - 1], Is.EqualTo(1f));
        }

        [UnityTest]
        public IEnumerator AsOnityTask_ProgressOverload_AlreadyDoneOperation_ReportsOneOnce()
        {
            ResourceRequest request = Resources.LoadAsync<TextAsset>("onity-interop-missing-asset");
            yield return WaitFor(() => request.isDone);
            List<float> reports = new List<float>();

            OnityTask<ResourceRequest> task = request.AsOnityTask(OnityProgress.Create(reports.Add));

            Assert.That(task.IsCompletedSuccessfully, Is.True);
            Assert.That(reports, Is.EqualTo(new[] { 1f }));
        }

        [UnityTest]
        public IEnumerator AsOnityTask_ProgressOverload_NullProgress_BehavesAsTheCallbackOverload()
        {
            ResourceRequest request = Resources.LoadAsync<TextAsset>("onity-interop-missing-asset");

            OnityTask<ResourceRequest> viaProgress = request.AsOnityTask((IProgress<float>)null);
            yield return WaitFor(() => viaProgress.IsCompleted);

            Assert.That(viaProgress.GetAwaiter().GetResult(), Is.SameAs(request));
            Assert.That(request.AsOnityTask().IsCompletedSuccessfully, Is.True,
                "The overload without progress must keep binding to the callback overload.");
        }

        [Test]
        public void AsOnityTask_ProgressOverload_PreCanceledToken_ReturnsACanceledTask()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            ResourceRequest request = Resources.LoadAsync<TextAsset>("onity-interop-missing-asset");

            OnityTask<ResourceRequest> task = request.AsOnityTask(OnityProgress.Create(_ => { }), cancellation.Token);

            Assert.That(task.IsCanceled, Is.True);
        }

        [Test]
        public void AsOnityTask_ProgressOverload_NullOperation_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => ((ResourceRequest)null).AsOnityTask(OnityProgress.Create(_ => { })));
        }

        [UnityTest]
        public IEnumerator JobHandle_GetAwaiter_CompletesTheJobBeforeResuming()
        {
            OnityTask<int> task = RunJobAsync(42);

            yield return WaitFor(() => task.IsCompleted);

            Assert.That(task.GetAwaiter().GetResult(), Is.EqualTo(42));
        }

        private static async OnityTask<int> RunJobAsync(int value)
        {
            NativeArray<int> output = new NativeArray<int>(1, Allocator.Persistent);
            try
            {
                JobHandle handle = new WriteJob { Output = output, Value = value }.Schedule();
                await handle;
                return output[0];
            }
            finally
            {
                output.Dispose();
            }
        }

        [Test]
        public void JobHandle_GetAwaiter_DefaultHandleCompletesInline()
        {
            Assert.That(default(JobHandle).GetAwaiter().IsCompleted, Is.True);
        }

#if ONITY_ASYNC_INSTANTIATE_TESTS
        [UnityTest]
        public IEnumerator AsInstancesOnityTask_Typed_ReturnsTheInstantiatedObjects()
        {
            GameObject template = new GameObject("onity-instantiate-template");
            GameObject[] instances = null;
            try
            {
                List<float> reports = new List<float>();
                AsyncInstantiateOperation<GameObject> operation = Object.InstantiateAsync(template, 2);

                OnityTask<GameObject[]> task = operation.AsInstancesOnityTask(OnityProgress.Create(reports.Add));
                yield return WaitFor(() => task.IsCompleted);
                instances = task.GetAwaiter().GetResult();

                Assert.That(instances.Length, Is.EqualTo(2));
                Assert.That(instances[0], Is.Not.Null);
                Assert.That(reports[reports.Count - 1], Is.EqualTo(1f));
            }
            finally
            {
                Object.Destroy(template);
                if (instances != null)
                {
                    for (int i = 0; i < instances.Length; i++)
                    {
                        Object.Destroy(instances[i]);
                    }
                }
            }
        }

        [UnityTest]
        public IEnumerator AsInstancesOnityTask_NonGeneric_ReturnsTheInstantiatedObjects()
        {
            GameObject template = new GameObject("onity-instantiate-template");
            Object[] instances = null;
            try
            {
                AsyncInstantiateOperation<GameObject> typed = Object.InstantiateAsync(template);
                AsyncInstantiateOperation operation = typed;

                OnityTask<Object[]> task = operation.AsInstancesOnityTask();
                yield return WaitFor(() => task.IsCompleted);
                instances = task.GetAwaiter().GetResult();

                Assert.That(instances.Length, Is.EqualTo(1));
            }
            finally
            {
                Object.Destroy(template);
                if (instances != null)
                {
                    for (int i = 0; i < instances.Length; i++)
                    {
                        Object.Destroy(instances[i]);
                    }
                }
            }
        }

        [UnityTest]
        public IEnumerator AsInstancesOnityTask_CanceledToken_StopsWaitingWithoutCancelingTheOperation()
        {
            GameObject template = new GameObject("onity-instantiate-template");
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            AsyncInstantiateOperation<GameObject> operation = Object.InstantiateAsync(template);

            OnityTask<GameObject[]> task = operation.AsInstancesOnityTask(null, cancellation.Token);
            cancellation.Cancel();
            yield return WaitFor(() => task.IsCompleted);
            bool canceled = task.IsCanceled;
            yield return WaitFor(() => operation.isDone);
            GameObject[] results = operation.Result;
            for (int i = 0; i < results.Length; i++)
            {
                Object.Destroy(results[i]);
            }

            Object.Destroy(template);

            Assert.That(canceled, Is.True);
            Assert.That(results.Length, Is.EqualTo(1), "The instantiation must keep running after the wait was canceled.");
        }

        [Test]
        public void AsInstancesOnityTask_NullOperation_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => ((AsyncInstantiateOperation<GameObject>)null).AsInstancesOnityTask());
            Assert.Throws<ArgumentNullException>(() => ((AsyncInstantiateOperation)null).AsInstancesOnityTask());
        }
#endif

#if UNITY_2023_1_OR_NEWER
        [UnityTest]
        public IEnumerator Awaitable_AsOnityTask_CompletesWhenTheAwaitableDoes()
        {
            int startFrame = Time.frameCount;

            OnityTask task = Awaitable.NextFrameAsync().AsOnityTask();
            yield return WaitFor(() => task.IsCompleted);

            Assert.That(Time.frameCount, Is.GreaterThan(startFrame));
            task.GetAwaiter().GetResult();
        }

        [UnityTest]
        public IEnumerator Awaitable_AsOnityTask_Typed_ReturnsTheResult()
        {
            OnityTask<int> task = ComputeAwaitableAsync().AsOnityTask();

            yield return WaitFor(() => task.IsCompleted);

            Assert.That(task.GetAwaiter().GetResult(), Is.EqualTo(5));
        }

        private static async Awaitable<int> ComputeAwaitableAsync()
        {
            await Awaitable.NextFrameAsync();
            return 5;
        }
#endif
    }
}
