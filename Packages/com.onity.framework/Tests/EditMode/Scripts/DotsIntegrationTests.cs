using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

#if ONITY_ENTITIES
using Onity.DOTS;
using Unity.Collections;
using Unity.Entities;
#endif

namespace Onity.Tests.EditMode
{
    [TestFixture]
    public sealed class DotsIntegrationTests
    {
#if ONITY_ENTITIES
        private World m_previousDefaultWorld;
        private World m_testWorld;

        [SetUp]
        public void SetUp()
        {
            m_previousDefaultWorld = World.DefaultGameObjectInjectionWorld;
            m_testWorld = new World("Onity.DotsIntegrationTests");
            World.DefaultGameObjectInjectionWorld = m_testWorld;

            SystemHandle bootstrapSystem = m_testWorld.GetOrCreateSystem<OnityDotsIntEventBootstrapSystem>();
            bootstrapSystem.Update(m_testWorld.Unmanaged);
        }

        [TearDown]
        public void TearDown()
        {
            World.DefaultGameObjectInjectionWorld = m_previousDefaultWorld;

            if (m_testWorld != null && m_testWorld.IsCreated)
            {
                m_testWorld.Dispose();
            }

            m_testWorld = null;
        }

        [Test]
        public void Bridge_PublishAndAccumulate_UpdatesAccumulatorAndClearsQueue()
        {
            SystemHandle accumulateSystem = m_testWorld.GetOrCreateSystem<OnityDotsIntEventAccumulateSystem>();

            bool publishedThree = OnityDotsIntEventBridge.TryPublish(3);
            bool publishedSeven = OnityDotsIntEventBridge.TryPublish(7);

            accumulateSystem.Update(m_testWorld.Unmanaged);

            bool hasValue = OnityDotsIntEventBridge.TryGetAccumulatedValue(out int value);
            Entity queueEntity = m_testWorld.EntityManager
                .CreateEntityQuery(ComponentType.ReadOnly<OnityDotsIntEventQueueTag>())
                .GetSingletonEntity();

            DynamicBuffer<OnityDotsIntEvent> queue = m_testWorld.EntityManager.GetBuffer<OnityDotsIntEvent>(queueEntity);

            Assert.That(publishedThree, Is.True);
            Assert.That(publishedSeven, Is.True);
            Assert.That(hasValue, Is.True);
            Assert.That(value, Is.EqualTo(10));
            Assert.That(queue.Length, Is.EqualTo(0));
        }

        [Test]
        public void Bridge_ResetAccumulator_SetsValueToZero()
        {
            SystemHandle accumulateSystem = m_testWorld.GetOrCreateSystem<OnityDotsIntEventAccumulateSystem>();
            OnityDotsIntEventBridge.TryPublish(5);
            accumulateSystem.Update(m_testWorld.Unmanaged);

            bool reset = OnityDotsIntEventBridge.TryResetAccumulator();
            bool hasValue = OnityDotsIntEventBridge.TryGetAccumulatedValue(out int value);

            Assert.That(reset, Is.True);
            Assert.That(hasValue, Is.True);
            Assert.That(value, Is.EqualTo(0));
        }

        [Test]
        public void Bridge_WithoutDefaultWorld_ReturnsFalse()
        {
            World.DefaultGameObjectInjectionWorld = null;

            bool canPublish = OnityDotsIntEventBridge.TryPublish(1);
            bool canRead = OnityDotsIntEventBridge.TryGetAccumulatedValue(out int value);
            bool canReset = OnityDotsIntEventBridge.TryResetAccumulator();

            World.DefaultGameObjectInjectionWorld = m_testWorld;

            Assert.That(canPublish, Is.False);
            Assert.That(canRead, Is.False);
            Assert.That(value, Is.EqualTo(0));
            Assert.That(canReset, Is.False);
        }

        [Test]
        public void Bridge_PublishZero_ReturnsFalse()
        {
            bool published = OnityDotsIntEventBridge.TryPublish(0);
            Assert.That(published, Is.False);
        }

        [Test]
        public void DotsSystems_HaveBurstCompileAttribute()
        {
            Assert.That(
                HasAttributeNamed(typeof(OnityDotsIntEventBootstrapSystem), "Unity.Burst.BurstCompileAttribute"),
                Is.True);

            Assert.That(
                HasAttributeNamed(typeof(OnityDotsIntEventAccumulateSystem), "Unity.Burst.BurstCompileAttribute"),
                Is.True);
        }

        [Test]
        public void DotsAsync_CanceledToken_ReturnsCanceledTask()
        {
            using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            cancellationTokenSource.Cancel();

            Task<int> task =
                OnityDotsIntEventAsync.WaitForAccumulatorAtLeastAsync(1, cancellationTokenSource.Token);

            Assert.That(task.IsCanceled, Is.True);
        }

        [Test]
        public void PoolEntityUtils_AddAndDestroy_TogglesEnableableState()
        {
            Entity entity = m_testWorld.EntityManager.CreateEntity();
            EntityManager entityManager = m_testWorld.EntityManager;

            OnityDotsPoolEntityUtils.AddPoolComponents(ref entityManager, entity);

            Assert.That(entityManager.HasComponent<OnityPoolableTag>(entity), Is.True);
            Assert.That(entityManager.HasComponent<OnityPooledEventTag>(entity), Is.True);
            Assert.That(entityManager.IsComponentEnabled<OnityPooledEventTag>(entity), Is.False);

            OnityDotsPoolEntityUtils.DestroyEntity(ref entityManager, entity);

            Assert.That(entityManager.IsComponentEnabled<OnityPooledEventTag>(entity), Is.True);
        }

        [Test]
        public void PoolEntityUtils_CommandBufferPath_TogglesEnableableState()
        {
            Entity entity = m_testWorld.EntityManager.CreateEntity();
            EntityManager entityManager = m_testWorld.EntityManager;

            EntityCommandBuffer commandBuffer = new EntityCommandBuffer(Allocator.Temp);

            try
            {
                OnityDotsPoolEntityUtils.AddPoolComponents(ref commandBuffer, entity);
                commandBuffer.Playback(entityManager);
            }
            finally
            {
                commandBuffer.Dispose();
            }

            Assert.That(entityManager.HasComponent<OnityPoolableTag>(entity), Is.True);
            Assert.That(entityManager.HasComponent<OnityPooledEventTag>(entity), Is.True);
            Assert.That(entityManager.IsComponentEnabled<OnityPooledEventTag>(entity), Is.False);

            EntityCommandBuffer destroyBuffer = new EntityCommandBuffer(Allocator.Temp);

            try
            {
                OnityDotsPoolEntityUtils.DestroyEntity(ref destroyBuffer, entity);
                destroyBuffer.Playback(entityManager);
            }
            finally
            {
                destroyBuffer.Dispose();
            }

            Assert.That(entityManager.IsComponentEnabled<OnityPooledEventTag>(entity), Is.True);
        }

        [Test]
        public void OnitySystemGroups_CanBeCreated()
        {
            OnityInitGroup initGroup = m_testWorld.GetOrCreateSystemManaged<OnityInitGroup>();
            OnityLateInitGroup lateInitGroup = m_testWorld.GetOrCreateSystemManaged<OnityLateInitGroup>();
            OnitySimulationGroup simulationGroup = m_testWorld.GetOrCreateSystemManaged<OnitySimulationGroup>();
            OnityBeginSimulationGroup beginSimulationGroup = m_testWorld.GetOrCreateSystemManaged<OnityBeginSimulationGroup>();
            OnityFixedStepGroup fixedStepGroup = m_testWorld.GetOrCreateSystemManaged<OnityFixedStepGroup>();
            OnityLateSimulationGroup lateSimulationGroup = m_testWorld.GetOrCreateSystemManaged<OnityLateSimulationGroup>();
            OnityBeginPresentationGroup beginPresentationGroup = m_testWorld.GetOrCreateSystemManaged<OnityBeginPresentationGroup>();

            Assert.That(initGroup, Is.Not.Null);
            Assert.That(lateInitGroup, Is.Not.Null);
            Assert.That(simulationGroup, Is.Not.Null);
            Assert.That(beginSimulationGroup, Is.Not.Null);
            Assert.That(fixedStepGroup, Is.Not.Null);
            Assert.That(lateSimulationGroup, Is.Not.Null);
            Assert.That(beginPresentationGroup, Is.Not.Null);
        }

        private static bool HasAttributeNamed(MemberInfo memberInfo, string fullName)
        {
            if (memberInfo == null)
            {
                return false;
            }

            var attributes = memberInfo.GetCustomAttributesData();

            for (int i = 0; i < attributes.Count; i++)
            {
                if (attributes[i].AttributeType.FullName == fullName)
                {
                    return true;
                }
            }

            return false;
        }
#else
        [Test]
        public void DotsPackageMissing_SkipsDotsIntegrationSuite()
        {
            Assert.Ignore("ONITY_ENTITIES define is not active.");
        }
#endif
    }
}
