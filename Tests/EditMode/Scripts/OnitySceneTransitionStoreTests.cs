using NUnit.Framework;
using Onity.Unity.SceneFlow;

namespace Onity.Tests.EditMode
{
    [TestFixture]
    public sealed class OnitySceneTransitionStoreTests
    {
        [SetUp]
        public void SetUp()
        {
            OnitySceneTransitionStore.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            OnitySceneTransitionStore.Clear();
        }

        [Test]
        public void SetNextTarget_ConsumePending_ReturnsStoredPayloadAndClearsPendingState()
        {
            TestSceneEnterData enterData = new TestSceneEnterData(7);
            OnitySceneTransitionStore.SetNextTarget("Gameplay", enterData);

            bool consumed = OnitySceneTransitionStore.TryConsumePendingOrDefault(
                "Fallback",
                null,
                out OnitySceneTransitionPayload payload);

            Assert.That(consumed, Is.True);
            Assert.That(payload.TargetSceneName, Is.EqualTo("Gameplay"));
            Assert.That(payload.EnterData, Is.SameAs(enterData));
            Assert.That(OnitySceneTransitionStore.PendingTargetScene, Is.Null);
            Assert.That(OnitySceneTransitionStore.PendingEnterData, Is.Null);
        }

        [Test]
        public void SetActiveEnterData_TryConsumeActiveEnterData_ConsumesOnlyOnce()
        {
            TestSceneEnterData enterData = new TestSceneEnterData(42);
            OnitySceneTransitionStore.SetActiveEnterData(enterData);

            bool firstConsume = OnitySceneTransitionStore.TryConsumeActiveEnterData(out TestSceneEnterData firstValue);
            bool secondConsume = OnitySceneTransitionStore.TryConsumeActiveEnterData(out TestSceneEnterData secondValue);

            Assert.That(firstConsume, Is.True);
            Assert.That(firstValue, Is.SameAs(enterData));
            Assert.That(secondConsume, Is.False);
            Assert.That(secondValue, Is.Null);
        }

        private sealed class TestSceneEnterData : IOnitySceneEnterData
        {
            public TestSceneEnterData(int value)
            {
                Value = value;
            }

            public int Value { get; }
        }
    }
}
