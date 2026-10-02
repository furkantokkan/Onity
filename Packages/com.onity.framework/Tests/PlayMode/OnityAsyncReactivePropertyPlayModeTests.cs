using System;
using System.Collections;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Onity.Tests.PlayMode
{
    public sealed class OnityAsyncReactivePropertyPlayModeTests
    {
        private static IOnityAsyncEnumerable<int> Frames() =>
            OnityAsyncEnumerable.EveryUpdate().Select(_ => Time.frameCount);

        [UnityTest]
        public IEnumerator BindTo_Behaviour_AppliesValuesAcrossFrames_UntilTheBehaviourIsDestroyed()
        {
            var property = new OnityAsyncReactiveProperty<int>(1);
            var owner = new GameObject("OnityAsyncBindingProbe");
            OnityAsyncBindingProbe probe = owner.AddComponent<OnityAsyncBindingProbe>();
            property.BindTo(probe, (target, value) => target.Values.Add(value));
            Assert.That(probe.Values, Is.EqualTo(new[] { 1 }));
            yield return null;
            property.Value = 2;
            yield return null;
            property.Value = 3;
            Assert.That(probe.Values, Is.EqualTo(new[] { 1, 2, 3 }));

            Object.Destroy(owner);
            yield return null;
            property.Value = 4;
            yield return null;
            Assert.That(probe.Values, Is.EqualTo(new[] { 1, 2, 3 }), "the destroy token ended the binding");
            property.Dispose();
        }

        [UnityTest]
        public IEnumerator Publish_FrameStream_IsSharedByEverySubscriber()
        {
            IOnityConnectableAsyncEnumerable<int> published = Frames().Take(3).Publish();
            Task<int[]> first = published.ToArrayAsync().AsTask();
            Task<int[]> second = published.ToArrayAsync().AsTask();
            IDisposable connection = published.Connect();
            yield return Wait(() => first.IsCompleted && second.IsCompleted);
            Assert.That(first.Result.Length, Is.EqualTo(3));
            Assert.That(second.Result, Is.EqualTo(first.Result));
            connection.Dispose();
        }

        [UnityTest]
        public IEnumerator Queue_SlowConsumer_StillSeesEveryFrame()
        {
            Task<int[]> frames = ReadSlowly(Frames().Queue(), 4).AsTask();
            yield return Wait(() => frames.IsCompleted);
            int[] result = frames.Result;
            for (int i = 1; i < result.Length; i++)
            {
                Assert.That(result[i] - result[i - 1], Is.EqualTo(1), "the queue pump pulled every frame");
            }
        }

        [UnityTest]
        public IEnumerator ReadOnlyProperty_FromAMergedStream_TracksTheLatestValue()
        {
            var first = new OnityAsyncReactiveProperty<int>(1);
            var second = new OnityAsyncReactiveProperty<int>(10);
            OnityReadOnlyAsyncReactiveProperty<int> latest = first.WithoutCurrent()
                .Merge(second.WithoutCurrent())
                .ToReadOnlyAsyncReactiveProperty(0, default);
            yield return null;
            first.Value = 2;
            Assert.That(latest.Value, Is.EqualTo(2));
            yield return null;
            second.Value = 20;
            Assert.That(latest.Value, Is.EqualTo(20));
            latest.Dispose();
            first.Dispose();
            second.Dispose();
        }

        private static async OnityTask<int[]> ReadSlowly(IOnityAsyncEnumerable<int> source, int count)
        {
            IOnityAsyncEnumerator<int> enumerator = source.GetAsyncEnumerator();
            var result = new int[count];
            try
            {
                for (int i = 0; i < count; i++)
                {
                    await enumerator.MoveNextAsync();
                    result[i] = enumerator.Current;
                    await OnityTask.DelayFrames(2);
                }
            }
            finally
            {
                await enumerator.DisposeAsync();
            }
            return result;
        }

        private static IEnumerator Wait(Func<bool> predicate)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + 5;
            while (!predicate())
            {
                Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(deadline), "The stream timed out.");
                yield return null;
            }
        }
    }
}
