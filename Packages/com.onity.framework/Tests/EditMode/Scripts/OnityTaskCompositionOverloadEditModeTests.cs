using System;
using System.Collections.Generic;
using NUnit.Framework;
using Onity.Unity.Async;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Pins how composition calls bind next to the generated tuple overloads (decisions D1 and D2).
    /// Each explicitly typed local fails to compile if a binding changes; the assertions repeat the
    /// static type at run time.
    /// </summary>
    [TestFixture]
    public sealed class OnityTaskCompositionOverloadEditModeTests
    {
        [Test]
        public void WhenAll_TwoSameTypeTypedArguments_BindToTheTupleOverload()
        {
            var combined = OnityTask.WhenAll(OnityTask.FromResult(1), OnityTask.FromResult(2));
            OnityTask<(int, int)> pinned = combined;

            Assert.That(StaticType(combined), Is.EqualTo(typeof(OnityTask<(int, int)>)));
            Assert.That(pinned.GetAwaiter().GetResult(), Is.EqualTo((1, 2)));
        }

        [Test]
        public void WhenAll_FifteenSameTypeTypedArguments_BindToTheTupleOverload()
        {
            OnityTask<int> t = OnityTask.FromResult(7);
            var combined = OnityTask.WhenAll(t, t, t, t, t, t, t, t, t, t, t, t, t, t, t);
            OnityTask<(int, int, int, int, int, int, int, int, int, int, int, int, int, int, int)> pinned = combined;

            Assert.That(StaticType(combined),
                Is.EqualTo(typeof(OnityTask<(int, int, int, int, int, int, int, int, int, int, int, int, int, int, int)>)));
            Assert.That(pinned.GetAwaiter().GetResult().Item15, Is.EqualTo(7));
        }

        [Test]
        public void WhenAll_SixteenTypedArguments_BindToTheParamsArrayOverload()
        {
            OnityTask<int> t = OnityTask.FromResult(3);
            var combined = OnityTask.WhenAll(t, t, t, t, t, t, t, t, t, t, t, t, t, t, t, t);
            OnityTask<int[]> pinned = combined;

            Assert.That(StaticType(combined), Is.EqualTo(typeof(OnityTask<int[]>)));
            Assert.That(pinned.GetAwaiter().GetResult().Length, Is.EqualTo(16));
        }

        [Test]
        public void WhenAll_ExplicitArray_BindsToTheParamsArrayOverload()
        {
            var combined = OnityTask.WhenAll(new[] { OnityTask.FromResult(1), OnityTask.FromResult(2) });
            OnityTask<int[]> pinned = combined;

            Assert.That(StaticType(combined), Is.EqualTo(typeof(OnityTask<int[]>)));
            Assert.That(pinned.GetAwaiter().GetResult(), Is.EqualTo(new[] { 1, 2 }));
        }

        [Test]
        public void WhenAll_TypedSequence_ReturnsAnArray()
        {
            var combined = OnityTask.WhenAll(new List<OnityTask<int>> { OnityTask.FromResult(1) });
            OnityTask<int[]> pinned = combined;

            Assert.That(StaticType(combined), Is.EqualTo(typeof(OnityTask<int[]>)));
            Assert.That(pinned.GetAwaiter().GetResult(), Is.EqualTo(new[] { 1 }));
        }

        [Test]
        public void WhenAll_UntypedPair_BindsToTheUntypedOverload()
        {
            var combined = OnityTask.WhenAll(OnityTask.CompletedTask, OnityTask.CompletedTask);
            OnityTask pinned = combined;

            Assert.That(StaticType(combined), Is.EqualTo(typeof(OnityTask)));
            Assert.That(pinned.IsCompletedSuccessfully, Is.True);
        }

        [Test]
        public void WhenAny_UntypedPair_BindsToTheIndexResult()
        {
            var race = OnityTask.WhenAny(OnityTask.CompletedTask, OnityTask.CompletedTask);
            OnityTask<int> pinned = race;

            Assert.That(StaticType(race), Is.EqualTo(typeof(OnityTask<int>)));
            Assert.That(pinned.GetAwaiter().GetResult(), Is.Zero);
        }

        [Test]
        public void WhenAny_TypedLeftAndUntypedRight_BindToTheLeftRightOverload()
        {
            var race = OnityTask.WhenAny(OnityTask.FromResult(5), OnityTask.CompletedTask);
            OnityTask<(bool hasResultLeft, int result)> pinned = race;

            Assert.That(StaticType(race), Is.EqualTo(typeof(OnityTask<(bool, int)>)));
            Assert.That(pinned.GetAwaiter().GetResult(), Is.EqualTo((true, 5)));
        }

        [Test]
        public void WhenAny_TwoMixedTypedArguments_BindToTheMixedOverload()
        {
            var race = OnityTask.WhenAny(OnityTask.FromResult(1), OnityTask.FromResult("two"));
            OnityTask<(int winArgumentIndex, int result1, string result2)> pinned = race;

            Assert.That(StaticType(race), Is.EqualTo(typeof(OnityTask<(int, int, string)>)));
            Assert.That(pinned.GetAwaiter().GetResult(), Is.EqualTo((0, 1, (string)null)));
        }

        [Test]
        public void WhenAny_TwoSameTypeTypedArguments_BindToTheMixedOverload()
        {
            var race = OnityTask.WhenAny(OnityTask.FromResult(1), OnityTask.FromResult(2));
            OnityTask<(int winArgumentIndex, int result1, int result2)> pinned = race;

            Assert.That(StaticType(race), Is.EqualTo(typeof(OnityTask<(int, int, int)>)));
            Assert.That(pinned.GetAwaiter().GetResult(), Is.EqualTo((0, 1, 0)));
        }

        [Test]
        public void WhenAny_SixteenTypedArguments_BindToTheParamsArrayOverload()
        {
            OnityTask<int> t = OnityTask.FromResult(3);
            var race = OnityTask.WhenAny(t, t, t, t, t, t, t, t, t, t, t, t, t, t, t, t);
            OnityTask<(int winnerIndex, int result)> pinned = race;

            Assert.That(StaticType(race), Is.EqualTo(typeof(OnityTask<(int, int)>)));
            Assert.That(pinned.GetAwaiter().GetResult(), Is.EqualTo((0, 3)));
        }

        [Test]
        public void WhenAny_ExplicitTypedArray_BindsToTheParamsArrayOverload()
        {
            var race = OnityTask.WhenAny(new[] { OnityTask.FromResult(1), OnityTask.FromResult(2) });
            OnityTask<(int winnerIndex, int result)> pinned = race;

            Assert.That(StaticType(race), Is.EqualTo(typeof(OnityTask<(int, int)>)));
            Assert.That(pinned.GetAwaiter().GetResult(), Is.EqualTo((0, 1)));
        }

        private static Type StaticType<T>(T value)
        {
            return typeof(T);
        }
    }
}
