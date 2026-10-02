using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Threading;
using NUnit.Framework;
using Onity.Unity.Async;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace Onity.Tests.PlayMode
{
    [TestFixture]
    public sealed class OnityUnityOperationExtensionsPlayModeTests
    {
        [UnityTest]
        public IEnumerator ResourceRequest_Typed_CompletesAndProgressReachesOne()
        {
            float last = -1f;
            ResourceRequest request = Resources.LoadAsync<Texture2D>("onity-par-b6-missing");

            OnityTask<Texture2D> task = request.AsAssetOnityTask<Texture2D>(
                OnityProgress.Create(value => last = value));
            yield return WaitFor(() => task.IsCompleted);

            Assert.That(task.GetAwaiter().GetResult(), Is.Null);
            Assert.That(last, Is.EqualTo(1f));
        }

        [UnityTest]
        public IEnumerator ResourceRequest_CanceledWhilePending_Cancels()
        {
            using var cts = new CancellationTokenSource();
            ResourceRequest request = Resources.LoadAsync<Texture2D>("onity-par-b6-missing");

            OnityTask<Texture2D> task = request.AsAssetOnityTask<Texture2D>(
                null,
                cts.Token);
            cts.Cancel();
            yield return WaitFor(() => task.IsCompleted);

            Assert.Catch<OperationCanceledException>(() => task.GetAwaiter().GetResult());
        }

        [UnityTest]
        public IEnumerator ResourceRequest_AlreadyDone_ReportsOneAndCompletesSynchronously()
        {
            ResourceRequest request = Resources.LoadAsync<Texture2D>("onity-par-b6-missing");
            yield return request;
            float last = -1f;

            OnityTask<UnityEngine.Object> task = request.AsAssetOnityTask(
                OnityProgress.Create(value => last = value));

            Assert.That(task.IsCompleted, Is.True);
            Assert.That(last, Is.EqualTo(1f));
            Assert.That(task.GetAwaiter().GetResult(), Is.Null);
        }

        [UnityTest]
        public IEnumerator AssetBundleCreateRequest_InvalidData_CompletesWithNullBundle()
        {
            // Unity may log the decompression error on a different frame than request completion;
            // the log is not the subject of this test, only the null payload is.
            LogAssert.ignoreFailingMessages = true;
            try
            {
                AssetBundleCreateRequest request = AssetBundle.LoadFromMemoryAsync(new byte[] { 1, 2, 3, 4 });
                float last = -1f;

                OnityTask<AssetBundle> task = request.AsAssetBundleOnityTask(
                    OnityProgress.Create(value => last = value));
                yield return WaitFor(() => task.IsCompleted);

                Assert.That(request.isDone, Is.True);
                Assert.That(task.GetAwaiter().GetResult(), Is.Null);
                Assert.That(last, Is.EqualTo(1f));
                yield return null;
                yield return null;
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }
        }

        [UnityTest]
        public IEnumerator WebRequest_MissingFile_FaultsWithWebRequestException()
        {
            string url = new Uri(Path.Combine(
                Application.temporaryCachePath,
                "onity-par-b6-missing-" + Guid.NewGuid().ToString("N") + ".bin")).AbsoluteUri;
            using UnityWebRequest request = UnityWebRequest.Get(url);
            float last = -1f;

            OnityTask<UnityWebRequest> task = request.SendWebRequest().AsWebRequestOnityTask(
                OnityProgress.Create(value => last = value));
            yield return WaitFor(() => task.IsCompleted);

            var exception = Assert.Throws<OnityUnityWebRequestException>(
                () => task.GetAwaiter().GetResult());
            Assert.That(exception.Result, Is.Not.EqualTo(UnityWebRequest.Result.Success));
            Assert.That(exception.Url, Is.EqualTo(request.url));
            Assert.That(last, Is.EqualTo(1f));
        }

        [UnityTest]
        public IEnumerator WebRequest_ExistingFile_ReturnsRequest()
        {
            string path = Path.Combine(
                Application.temporaryCachePath,
                "onity-par-b6-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(path, "payload");
            try
            {
                using UnityWebRequest request = UnityWebRequest.Get(new Uri(path).AbsoluteUri);

                OnityTask<UnityWebRequest> task = request.SendWebRequest().AsWebRequestOnityTask();
                yield return WaitFor(() => task.IsCompleted);

                UnityWebRequest result = task.GetAwaiter().GetResult();
                Assert.That(result, Is.SameAs(request));
                Assert.That(result.downloadHandler.text, Is.EqualTo("payload"));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [UnityTest]
        public IEnumerator WebRequest_CanceledWhilePending_AbortsAndCancels()
        {
            using var cts = new CancellationTokenSource();
            using UnityWebRequest request = UnityWebRequest.Get("http://127.0.0.1:9/onity-par-b6");
            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            if (operation.isDone)
            {
                Assert.Inconclusive("The loopback request completed before it could be canceled.");
            }

            OnityTask<UnityWebRequest> task = operation.AsWebRequestOnityTask(null, cts.Token);
            cts.Cancel();
            yield return WaitFor(() => task.IsCompleted);

            Assert.Catch<OperationCanceledException>(() => task.GetAwaiter().GetResult());
        }

        [UnityTest]
        public IEnumerator AsyncGpuReadback_CompletesWhenSupported()
        {
            if (SystemInfo.supportsAsyncGPUReadback == false)
            {
                Assert.Ignore("AsyncGPUReadback is not supported in this environment.");
            }

            var texture = new RenderTexture(4, 4, 0);
            try
            {
                // A render texture is created lazily, and a readback of an uncreated one fails.
                texture.Create();
                AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(texture);

                OnityTask<AsyncGPUReadbackRequest> task = request.AsOnityTask();
                yield return WaitFor(() => task.IsCompleted);

                if (request.hasError)
                {
                    // The environment cannot service the readback, for example the Editor in batch
                    // mode or a device without a GPU. The adapter must still report that failure; the
                    // success path cannot be verified here.
                    Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult());
                    Assert.Inconclusive(
                        "AsyncGPUReadback failed in this environment, so only the error mapping was verified.");
                }

                Assert.That(task.GetAwaiter().GetResult().done, Is.True);
            }
            finally
            {
                UnityEngine.Object.Destroy(texture);
            }
        }

        private static IEnumerator WaitFor(Func<bool> predicate)
        {
            Stopwatch timer = Stopwatch.StartNew();
            while (predicate() == false && timer.Elapsed.TotalSeconds < 10)
            {
                yield return null;
            }

            Assert.That(predicate(), Is.True, "Unity operation task timed out.");
        }
    }
}
