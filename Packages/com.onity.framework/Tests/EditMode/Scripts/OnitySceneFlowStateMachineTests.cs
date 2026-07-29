using NUnit.Framework;
using Onity.Unity.SceneFlow;
using UnityEngine;

namespace Onity.Tests.EditMode
{
    [TestFixture]
    public sealed class OnitySceneFlowStateMachineTests
    {
        private OnitySceneFlowProfile m_profile;

        [SetUp]
        public void SetUp()
        {
            m_profile = ScriptableObject.CreateInstance<OnitySceneFlowProfile>();
            m_profile.SetRouteTransitionsThroughLoadingScene(true);
            m_profile.SetSceneName(OnitySceneFlowStateId.Bootstrap, "BootstrapScene");
            m_profile.SetSceneName(OnitySceneFlowStateId.Loading, "LoadingScene");
            m_profile.SetSceneNames(
                OnitySceneFlowStateId.MainMenuHub,
                new[]
                {
                    "MainMenuScene",
                    "OptionsMenuScene"
                });
            m_profile.SetDefaultSceneName(OnitySceneFlowStateId.MainMenuHub, "MainMenuScene");
            m_profile.SetSceneNames(
                OnitySceneFlowStateId.Hub,
                new[]
                {
                    "HubScene"
                });
            m_profile.SetDefaultSceneName(OnitySceneFlowStateId.Hub, "HubScene");
            m_profile.SetSceneNames(
                OnitySceneFlowStateId.Gameplay,
                new[]
                {
                    "GameplayScene",
                    "BossLevelScene"
                });
            m_profile.SetDefaultSceneName(OnitySceneFlowStateId.Gameplay, "GameplayScene");
        }

        [Test]
        public void StateIds_PreserveExistingValuesAndAppendHub()
        {
            Assert.That((int)OnitySceneFlowStateId.Unknown, Is.EqualTo(0));
            Assert.That((int)OnitySceneFlowStateId.Bootstrap, Is.EqualTo(1));
            Assert.That((int)OnitySceneFlowStateId.Loading, Is.EqualTo(2));
            Assert.That((int)OnitySceneFlowStateId.MainMenuHub, Is.EqualTo(3));
            Assert.That((int)OnitySceneFlowStateId.Gameplay, Is.EqualTo(4));
            Assert.That((int)OnitySceneFlowStateId.Hub, Is.EqualTo(5));
        }

        [TearDown]
        public void TearDown()
        {
            if (m_profile != null)
            {
                Object.DestroyImmediate(m_profile);
                m_profile = null;
            }
        }

        [Test]
        public void BuildTransitionPlan_Gameplay_RoutesThroughLoadingScene()
        {
            OnitySceneFlowStateMachine stateMachine = new OnitySceneFlowStateMachine(m_profile);
            TestEnterData enterData = new TestEnterData(7);

            OnitySceneFlowTransitionPlan transitionPlan =
                stateMachine.BuildTransitionPlan(OnitySceneFlowStateId.Gameplay, enterData);

            Assert.That(transitionPlan.RouteViaLoadingScene, Is.True);
            Assert.That(transitionPlan.EntryStateId, Is.EqualTo(OnitySceneFlowStateId.Loading));
            Assert.That(transitionPlan.TargetStateId, Is.EqualTo(OnitySceneFlowStateId.Gameplay));
            Assert.That(transitionPlan.EntrySceneName, Is.EqualTo("LoadingScene"));
            Assert.That(transitionPlan.TargetSceneName, Is.EqualTo("GameplayScene"));
            Assert.That(transitionPlan.EnterData, Is.SameAs(enterData));
        }

        [Test]
        public void BuildTransitionPlan_Loading_IsAlwaysDirect()
        {
            OnitySceneFlowStateMachine stateMachine = new OnitySceneFlowStateMachine(m_profile);
            OnitySceneFlowTransitionPlan transitionPlan =
                stateMachine.BuildTransitionPlan(OnitySceneFlowStateId.Loading);

            Assert.That(transitionPlan.RouteViaLoadingScene, Is.False);
            Assert.That(transitionPlan.EntryStateId, Is.EqualTo(OnitySceneFlowStateId.Loading));
            Assert.That(transitionPlan.TargetStateId, Is.EqualTo(OnitySceneFlowStateId.Loading));
            Assert.That(transitionPlan.EntrySceneName, Is.EqualTo("LoadingScene"));
            Assert.That(transitionPlan.TargetSceneName, Is.EqualTo("LoadingScene"));
        }

        [Test]
        public void BuildTransitionPlan_Hub_RoutesThroughLoadingScene()
        {
            OnitySceneFlowStateMachine stateMachine = new OnitySceneFlowStateMachine(m_profile);
            OnitySceneFlowTransitionPlan transitionPlan =
                stateMachine.BuildTransitionPlan(OnitySceneFlowStateId.Hub);

            Assert.That(transitionPlan.RouteViaLoadingScene, Is.True);
            Assert.That(transitionPlan.EntryStateId, Is.EqualTo(OnitySceneFlowStateId.Loading));
            Assert.That(transitionPlan.TargetStateId, Is.EqualTo(OnitySceneFlowStateId.Hub));
            Assert.That(transitionPlan.EntrySceneName, Is.EqualTo("LoadingScene"));
            Assert.That(transitionPlan.TargetSceneName, Is.EqualTo("HubScene"));
        }

        [Test]
        public void BuildTransitionPlan_WhenLoadingRouteDisabled_LoadsTargetDirectly()
        {
            m_profile.SetRouteTransitionsThroughLoadingScene(false);
            OnitySceneFlowStateMachine stateMachine = new OnitySceneFlowStateMachine(m_profile);
            OnitySceneFlowTransitionPlan transitionPlan =
                stateMachine.BuildTransitionPlan(OnitySceneFlowStateId.MainMenuHub);

            Assert.That(transitionPlan.RouteViaLoadingScene, Is.False);
            Assert.That(transitionPlan.EntryStateId, Is.EqualTo(OnitySceneFlowStateId.MainMenuHub));
            Assert.That(transitionPlan.TargetStateId, Is.EqualTo(OnitySceneFlowStateId.MainMenuHub));
            Assert.That(transitionPlan.EntrySceneName, Is.EqualTo("MainMenuScene"));
            Assert.That(transitionPlan.TargetSceneName, Is.EqualTo("MainMenuScene"));
        }

        [Test]
        public void BuildTransitionPlan_ByConcreteGroupedScene_RoutesThroughLoadingScene()
        {
            OnitySceneFlowStateMachine stateMachine = new OnitySceneFlowStateMachine(m_profile);
            TestEnterData enterData = new TestEnterData(99);

            OnitySceneFlowTransitionPlan transitionPlan =
                stateMachine.BuildTransitionPlan("BossLevelScene", enterData);

            Assert.That(transitionPlan.RouteViaLoadingScene, Is.True);
            Assert.That(transitionPlan.EntryStateId, Is.EqualTo(OnitySceneFlowStateId.Loading));
            Assert.That(transitionPlan.TargetStateId, Is.EqualTo(OnitySceneFlowStateId.Gameplay));
            Assert.That(transitionPlan.EntrySceneName, Is.EqualTo("LoadingScene"));
            Assert.That(transitionPlan.TargetSceneName, Is.EqualTo("BossLevelScene"));
            Assert.That(transitionPlan.EnterData, Is.SameAs(enterData));
        }

        [Test]
        public void TryGetStateId_ReturnsExpectedState()
        {
            bool resolved = m_profile.TryGetStateId("MainMenuScene", out OnitySceneFlowStateId stateId);

            Assert.That(resolved, Is.True);
            Assert.That(stateId, Is.EqualTo(OnitySceneFlowStateId.MainMenuHub));
        }

        [Test]
        public void TryGetStateId_ReturnsExpectedState_ForAdditionalGroupedScene()
        {
            bool resolved = m_profile.TryGetStateId("OptionsMenuScene", out OnitySceneFlowStateId stateId);

            Assert.That(resolved, Is.True);
            Assert.That(stateId, Is.EqualTo(OnitySceneFlowStateId.MainMenuHub));
        }

        [Test]
        public void TryGetStateId_ReturnsHubWithoutChangingLegacyMenuState()
        {
            bool hubResolved = m_profile.TryGetStateId(
                "HubScene",
                out OnitySceneFlowStateId hubStateId);
            bool menuResolved = m_profile.TryGetStateId(
                "MainMenuScene",
                out OnitySceneFlowStateId menuStateId);

            Assert.That(hubResolved, Is.True);
            Assert.That(hubStateId, Is.EqualTo(OnitySceneFlowStateId.Hub));
            Assert.That(menuResolved, Is.True);
            Assert.That(menuStateId, Is.EqualTo(OnitySceneFlowStateId.MainMenuHub));
        }

        [Test]
        public void SetSceneNames_EmptyGroup_ClearsPreviousDefault()
        {
            m_profile.SetSceneNames(OnitySceneFlowStateId.Hub, System.Array.Empty<string>());

            bool resolved = m_profile.TryGetSceneName(
                OnitySceneFlowStateId.Hub,
                out string sceneName);

            Assert.That(resolved, Is.False);
            Assert.That(sceneName, Is.Empty);
            Assert.That(m_profile.HubSceneNames, Is.Empty);
        }

        private sealed class TestEnterData : IOnitySceneEnterData
        {
            public TestEnterData(int value)
            {
                Value = value;
            }

            public int Value { get; }
        }
    }
}
