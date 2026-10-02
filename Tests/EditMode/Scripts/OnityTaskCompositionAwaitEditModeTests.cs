using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;
using static Onity.Tests.EditMode.OnityTaskCompositionTestSupport;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Pins the <see cref="OnityTaskCompositionAwaitExtensions"/> shorthands: awaiting an array,
    /// sequence or tuple of Onity tasks is awaiting <c>OnityTask.WhenAll</c> over them.
    /// </summary>
    [TestFixture]
    public sealed class OnityTaskCompositionAwaitEditModeTests
    {
        [Test]
        public async Task AwaitTypedArray_ReturnsResultsInInputOrder()
        {
            OnityTask<int>[] tasks = { OnityTask.FromResult(1), OnityTask.FromResult(2), OnityTask.FromResult(3) };

            int[] results = await tasks;

            Assert.That(results, Is.EqualTo(new[] { 1, 2, 3 }));
        }

        [Test]
        public async Task AwaitTypedSequence_ReturnsResultsInSequenceOrder()
        {
            List<OnityTask<string>> tasks = new List<OnityTask<string>>
            {
                OnityTask.FromResult("a"),
                OnityTask.FromResult("b")
            };

            string[] results = await tasks;

            Assert.That(results, Is.EqualTo(new[] { "a", "b" }));
        }

        [Test]
        public async Task AwaitUntypedArray_CompletesAfterEveryInput()
        {
            OnityTaskCompletionSource[] sources =
            {
                new OnityTaskCompletionSource(),
                new OnityTaskCompletionSource(),
                new OnityTaskCompletionSource()
            };
            Task awaited = AwaitArray(new[] { sources[0].Task, sources[1].Task, sources[2].Task });

            sources[0].TrySetResult();
            sources[2].TrySetResult();
            Assert.That(awaited.IsCompleted, Is.False);
            sources[1].TrySetResult();

            await awaited;
            Assert.That(awaited.IsCompletedSuccessfully, Is.True);
        }

        [Test]
        public async Task AwaitUntypedSequence_CompletesAfterEveryInput_AndPropagatesTheFault()
        {
            OnityTaskCompletionSource first = new OnityTaskCompletionSource();
            OnityTaskCompletionSource second = new OnityTaskCompletionSource();
            InvalidOperationException failure = new InvalidOperationException("sequence input failed");
            Task awaited = AwaitSequence(new List<OnityTask> { first.Task, second.Task });

            second.TrySetException(failure);
            Assert.That(awaited.IsCompleted, Is.False);
            first.TrySetResult();

            Exception observed = null;
            try
            {
                await awaited;
            }
            catch (Exception exception)
            {
                observed = exception;
            }

            Assert.That(observed, Is.SameAs(failure));
        }

        [Test]
        public async Task AwaitTypedArray_PendingInputs_ResumeAfterTheLastInput()
        {
            OnityTaskCompletionSource<int>[] sources = Sources<int>(2);
            Task<int[]> awaited = AwaitTypedArray(new[] { sources[0].Task, sources[1].Task });

            sources[1].TrySetResult(20);
            Assert.That(awaited.IsCompleted, Is.False);
            sources[0].TrySetResult(10);

            Assert.That(await awaited, Is.EqualTo(new[] { 10, 20 }));
        }

        [Test]
        public async Task AwaitTypedTuple_ReturnsResultsInTupleOrder()
        {
            (int number, string text) = await (OnityTask.FromResult(1), OnityTask.FromResult("a"));

            Assert.That(number, Is.EqualTo(1));
            Assert.That(text, Is.EqualTo("a"));
        }

        [Test]
        public async Task AwaitTypedTuple_PendingInputs_ResumeAfterTheLastInput()
        {
            OnityTaskCompletionSource<int> first = new OnityTaskCompletionSource<int>();
            OnityTaskCompletionSource<string> second = new OnityTaskCompletionSource<string>();
            Task<(int, string)> awaited = AwaitTypedPair(first.Task, second.Task);

            second.TrySetResult("two");
            Assert.That(awaited.IsCompleted, Is.False);
            first.TrySetResult(1);

            Assert.That(await awaited, Is.EqualTo((1, "two")));
        }

        [Test]
        public async Task AwaitFifteenElementTypedTuple_ReturnsEveryResult()
        {
            OnityTask<int> t = OnityTask.FromResult(4);

            var results = await (t, t, t, t, t, t, t, OnityTask.FromResult("eighth"), t, t, t, t, t, t,
                OnityTask.FromResult(15L));

            Assert.That(results.Item8, Is.EqualTo("eighth"));
            Assert.That(results.Item14, Is.EqualTo(4));
            Assert.That(results.Item15, Is.EqualTo(15L));
        }

        [Test]
        public async Task AwaitUntypedTuples_CompleteAfterEveryInput()
        {
            OnityTaskCompletionSource[] sources =
            {
                new OnityTaskCompletionSource(),
                new OnityTaskCompletionSource(),
                new OnityTaskCompletionSource(),
                new OnityTaskCompletionSource(),
                new OnityTaskCompletionSource()
            };
            Task pair = AwaitUntypedPair(sources[0].Task, sources[1].Task);
            Task triple = AwaitUntypedTriple(sources[2].Task, sources[3].Task, sources[4].Task);

            sources[1].TrySetResult();
            sources[2].TrySetResult();
            sources[3].TrySetResult();
            Assert.That(pair.IsCompleted, Is.False);
            Assert.That(triple.IsCompleted, Is.False);
            sources[0].TrySetResult();
            sources[4].TrySetResult();

            await pair;
            await triple;
            Assert.That(pair.IsCompletedSuccessfully && triple.IsCompletedSuccessfully, Is.True);
        }

        [Test]
        public void AwaitNullCollections_ThrowArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => ((OnityTask[])null).GetAwaiter());
            Assert.Throws<ArgumentNullException>(() => ((IEnumerable<OnityTask>)null).GetAwaiter());
            Assert.Throws<ArgumentNullException>(() => ((OnityTask<int>[])null).GetAwaiter());
            Assert.Throws<ArgumentNullException>(() => ((IEnumerable<OnityTask<int>>)null).GetAwaiter());
        }

        private static async Task AwaitArray(OnityTask[] tasks)
        {
            await tasks;
        }

        private static async Task AwaitSequence(IEnumerable<OnityTask> tasks)
        {
            await tasks;
        }

        private static async Task<int[]> AwaitTypedArray(OnityTask<int>[] tasks)
        {
            return await tasks;
        }

        private static async Task<(int, string)> AwaitTypedPair(OnityTask<int> first, OnityTask<string> second)
        {
            return await (first, second);
        }

        private static async Task AwaitUntypedPair(OnityTask first, OnityTask second)
        {
            await (first, second);
        }

        private static async Task AwaitUntypedTriple(OnityTask first, OnityTask second, OnityTask third)
        {
            await (first, second, third);
        }
    }
}
