using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Onity.Unity.Async;
using UnityEngine;

namespace Onity.Tests.EditMode
{
    [TestFixture]
    public sealed class OnityUnityOperationExtensionsEditModeTests
    {
        [Test]
        public void OnityProgress_Create_ForwardsValuesInline()
        {
            var values = new List<float>();
            IProgress<float> progress = OnityProgress.Create(values.Add);

            progress.Report(0.25f);
            progress.Report(1f);

            Assert.That(values, Is.EqualTo(new[] { 0.25f, 1f }));
        }

        [Test]
        public void OnityProgress_Create_NullCallback_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => OnityProgress.Create(null));
        }

        [Test]
        public void NullOperations_ThrowArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                OnityUnityOperationExtensions.AsAssetOnityTask<Texture2D>((ResourceRequest)null));
            Assert.Throws<ArgumentNullException>(() =>
                OnityUnityOperationExtensions.AsAssetOnityTask((AssetBundleRequest)null));
            Assert.Throws<ArgumentNullException>(() =>
                OnityUnityOperationExtensions.AwaitForAllAssets(null));
            Assert.Throws<ArgumentNullException>(() =>
                OnityUnityOperationExtensions.AsAssetBundleOnityTask(null));
            Assert.Throws<ArgumentNullException>(() =>
                OnityUnityOperationExtensions.AsWebRequestOnityTask(null));
        }

        [Test]
        public void ResourceRequest_CanceledBeforeStart_ReturnsCanceledTaskWithoutProgress()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            int reports = 0;
            ResourceRequest request = Resources.LoadAsync<Texture2D>("onity-par-b6-missing");

            OnityTask<Texture2D> task = request.AsAssetOnityTask<Texture2D>(
                OnityProgress.Create(_ => reports++),
                cts.Token);

            Assert.That(reports, Is.EqualTo(0));
            Assert.Catch<OperationCanceledException>(() => task.GetAwaiter().GetResult());
        }
    }
}
