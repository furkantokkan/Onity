using System;
using NUnit.Framework;
using Onity.Unity.SceneFlow;

namespace Onity.Tests.EditMode
{
    [TestFixture]
    public sealed class OnitySceneRevealPolicyTests
    {
        [Test]
        public void ShouldReveal_WaitsTheSettleFramesAfterReadiness()
        {
            OnitySceneRevealPolicy policy = new OnitySceneRevealPolicy(2, 10f);

            Assert.That(policy.ShouldReveal(false, 0, 0f), Is.False, "not ready");
            Assert.That(policy.ShouldReveal(true, 0, 0.1f), Is.False, "ready on this frame");
            Assert.That(policy.ShouldReveal(true, 1, 0.2f), Is.False, "one frame settled");
            Assert.That(policy.ShouldReveal(true, 2, 0.3f), Is.True, "two frames settled");
        }

        [Test]
        public void ShouldReveal_ZeroSettleFrames_RevealsOnTheReadyFrame()
        {
            OnitySceneRevealPolicy policy = new OnitySceneRevealPolicy(0, 10f);

            Assert.That(policy.ShouldReveal(false, 0, 0f), Is.False);
            Assert.That(policy.ShouldReveal(true, 0, 0f), Is.True);
        }

        [Test]
        public void ShouldReveal_SceneNeverReady_RevealsOnceTheMaximumWaitElapses()
        {
            OnitySceneRevealPolicy policy = new OnitySceneRevealPolicy(1, 3f);

            Assert.That(policy.ShouldReveal(false, 0, 2.99f), Is.False);
            Assert.That(policy.ShouldReveal(false, 0, 3f), Is.True, "a scene that never finishes cannot keep the cover");
        }

        [Test]
        public void ShouldReveal_InfiniteMaximumWait_WaitsForReadiness()
        {
            OnitySceneRevealPolicy policy = new OnitySceneRevealPolicy(1, float.PositiveInfinity);

            Assert.That(policy.ShouldReveal(false, 0, 100000f), Is.False);
            Assert.That(policy.ShouldReveal(true, 1, 100000f), Is.True);
        }

        [Test]
        public void DefaultPolicy_UsesTheDocumentedDefaults()
        {
            OnitySceneRevealPolicy policy = new OnitySceneRevealPolicy();

            Assert.That(policy.SettleFrames, Is.EqualTo(OnitySceneRevealPolicy.DefaultSettleFrames));
            Assert.That(policy.MaxWaitSeconds, Is.EqualTo(OnitySceneRevealPolicy.DefaultMaxWaitSeconds));
            Assert.That(new OnityActiveSceneReadiness().Policy.SettleFrames,
                Is.EqualTo(OnitySceneRevealPolicy.DefaultSettleFrames));
            Assert.That(new OnityActiveSceneReadiness(null).Policy, Is.Not.Null);
        }

        [Test]
        public void Constructor_InvalidValues_Throw()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new OnitySceneRevealPolicy(-1, 1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new OnitySceneRevealPolicy(0, 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new OnitySceneRevealPolicy(0, float.NaN));
        }
    }
}
