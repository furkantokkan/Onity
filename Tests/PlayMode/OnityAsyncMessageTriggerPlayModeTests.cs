using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;
using Onity.Unity.Async.Triggers;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Onity.Tests.PlayMode
{
    /// <summary>
    /// Real Unity messages delivered to the generated message triggers. Messages that need a rendering
    /// device, input, an Animator or focus changes are covered by the Edit mode tests, which raise the
    /// private message methods directly. The tests assume the physics and physics2d modules, as the
    /// Onity test project has them installed. The 3D physics tests put their objects on layers that the
    /// project's layer collision matrix lets interact, because that matrix may switch the Default layer off.
    /// </summary>
    public sealed class OnityAsyncMessageTriggerPlayModeTests
    {
        private readonly List<GameObject> m_objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < m_objects.Count; i++)
            {
                if (m_objects[i] != null)
                {
                    Object.Destroy(m_objects[i]);
                }
            }

            m_objects.Clear();
        }

        [UnityTest]
        public IEnumerator Update_ReusableHandler_ResumesAtMostOncePerFrame()
        {
            GameObject owner = NewObject("message-update");
            IOnityAsyncUpdateHandler handler = owner.GetAsyncUpdateTrigger().GetUpdateAsyncHandler();
            List<int> frames = new List<int>();

            for (int i = 0; i < 3; i++)
            {
                OnityTaskAwaiter awaiter = handler.UpdateAsync().GetAwaiter();
                awaiter.OnCompleted(() =>
                {
                    awaiter.GetResult();
                    frames.Add(Time.frameCount);
                });

                for (int guard = 0; guard < 5 && frames.Count <= i; guard++)
                {
                    yield return null;
                }
            }

            ((IDisposable)handler).Dispose();

            Assert.That(frames.Count, Is.EqualTo(3));
            Assert.That(frames[1], Is.GreaterThan(frames[0]));
            Assert.That(frames[2], Is.GreaterThan(frames[1]));
        }

        [UnityTest]
        public IEnumerator LateUpdate_ResumesTheWaiter()
        {
            GameObject owner = NewObject("message-late-update");
            OnityTask wait = owner.GetAsyncLateUpdateTrigger().LateUpdateAsync();

            for (int guard = 0; guard < 5 && !wait.IsCompleted; guard++)
            {
                yield return null;
            }

            Assert.That(wait.IsCompletedSuccessfully, Is.True);
            wait.GetAwaiter().GetResult();
        }

        [UnityTest]
        public IEnumerator FixedUpdate_ResumesTheWaiter()
        {
            GameObject owner = NewObject("message-fixed-update");
            OnityTask wait = owner.GetAsyncFixedUpdateTrigger().FixedUpdateAsync();

            yield return WaitFixedUntil(() => wait.IsCompleted, 10);

            Assert.That(wait.IsCompletedSuccessfully, Is.True);
            wait.GetAwaiter().GetResult();
        }

        [UnityTest]
        public IEnumerator TransformMessages_ResumeTheirWaiters()
        {
            GameObject parent = NewObject("message-transform-parent");
            GameObject child = NewObject("message-transform-child");
            OnityTask before = child.GetAsyncBeforeTransformParentChangedTrigger()
                .OnBeforeTransformParentChangedAsync();
            OnityTask after = child.GetAsyncTransformParentChangedTrigger().OnTransformParentChangedAsync();
            OnityTask children = parent.GetAsyncTransformChildrenChangedTrigger()
                .OnTransformChildrenChangedAsync();
            yield return null;

            Assert.That(before.IsCompleted, Is.False);
            Assert.That(after.IsCompleted, Is.False);
            Assert.That(children.IsCompleted, Is.False);

            child.transform.SetParent(parent.transform);
            for (int guard = 0; guard < 3 && !(before.IsCompleted && after.IsCompleted && children.IsCompleted);
                guard++)
            {
                yield return null;
            }

            Assert.That(before.IsCompletedSuccessfully, Is.True);
            Assert.That(after.IsCompletedSuccessfully, Is.True);
            Assert.That(children.IsCompletedSuccessfully, Is.True);
        }

        [UnityTest]
        public IEnumerator TriggerEnterAndExit_ReportTheOtherCollider()
        {
            (int zoneLayer, int bodyLayer) = FindCollidingLayers();
            Vector3 far = new Vector3(20f, 0f, 0f);
            GameObject zone = NewObject("message-trigger-zone");
            zone.layer = zoneLayer;
            zone.AddComponent<BoxCollider>().isTrigger = true;
            GameObject body = NewObject("message-trigger-body");
            body.layer = bodyLayer;
            BoxCollider bodyCollider = body.AddComponent<BoxCollider>();
            body.AddComponent<Rigidbody>().isKinematic = true;
            body.transform.position = far;
            OnityAsyncTriggerEnterTrigger enter = zone.GetAsyncTriggerEnterTrigger();
            OnityAsyncTriggerExitTrigger exit = zone.GetAsyncTriggerExitTrigger();
            yield return new WaitForFixedUpdate();

            OnityTask<Collider> enterWait = enter.OnTriggerEnterAsync();
            body.transform.position = Vector3.zero;
            yield return WaitFixedUntil(() => enterWait.IsCompleted, 20);

            Assert.That(enterWait.IsCompletedSuccessfully, Is.True);
            Assert.That(enterWait.GetAwaiter().GetResult(), Is.EqualTo(bodyCollider));

            OnityTask<Collider> exitWait = exit.OnTriggerExitAsync();
            body.transform.position = far;
            yield return WaitFixedUntil(() => exitWait.IsCompleted, 20);

            Assert.That(exitWait.IsCompletedSuccessfully, Is.True);
            Assert.That(exitWait.GetAwaiter().GetResult(), Is.EqualTo(bodyCollider));
        }

        [UnityTest]
        public IEnumerator CollisionEnter_ReportsTheContactedCollider()
        {
            (int groundLayer, int ballLayer) = FindCollidingLayers();
            GameObject ground = NewObject("message-collision-ground");
            ground.layer = groundLayer;
            BoxCollider groundCollider = ground.AddComponent<BoxCollider>();
            groundCollider.size = new Vector3(20f, 1f, 20f);
            GameObject ball = NewObject("message-collision-ball");
            ball.layer = ballLayer;
            ball.AddComponent<SphereCollider>();
            ball.AddComponent<Rigidbody>();
            ball.transform.position = new Vector3(0f, 2f, 0f);

            // While Physics.reuseCollisionCallbacks is on (the project's setting), Unity reuses one Collision
            // instance and flips it to deliver the message to the other collider, so Collision.collider read
            // after the callback returns is the ball's own collider. The continuation runs inline inside
            // OnCollisionEnter, so the collider is read there.
            Collider contacted = null;
            OnityTaskAwaiter<Collision> awaiter = ball.GetAsyncCollisionEnterTrigger().OnCollisionEnterAsync()
                .GetAwaiter();
            awaiter.OnCompleted(() => contacted = awaiter.GetResult().collider);

            yield return WaitFixedUntil(() => contacted != null, 200);

            Assert.That(contacted != null, Is.True, "OnCollisionEnter did not arrive");
            Assert.That(contacted, Is.EqualTo(groundCollider));
        }

        [UnityTest]
        public IEnumerator TriggerEnter2DAndExit2D_ReportTheOtherCollider()
        {
            Vector3 far = new Vector3(20f, 0f, 0f);
            GameObject zone = NewObject("message-trigger2d-zone");
            zone.AddComponent<BoxCollider2D>().isTrigger = true;
            GameObject body = NewObject("message-trigger2d-body");
            BoxCollider2D bodyCollider = body.AddComponent<BoxCollider2D>();
            body.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            body.transform.position = far;
            OnityAsyncTriggerEnter2DTrigger enter = zone.GetAsyncTriggerEnter2DTrigger();
            OnityAsyncTriggerExit2DTrigger exit = zone.GetAsyncTriggerExit2DTrigger();
            yield return new WaitForFixedUpdate();

            OnityTask<Collider2D> enterWait = enter.OnTriggerEnter2DAsync();
            body.transform.position = Vector3.zero;
            yield return WaitFixedUntil(() => enterWait.IsCompleted, 20);

            Assert.That(enterWait.IsCompletedSuccessfully, Is.True);
            Assert.That(enterWait.GetAwaiter().GetResult(), Is.EqualTo(bodyCollider));

            OnityTask<Collider2D> exitWait = exit.OnTriggerExit2DAsync();
            body.transform.position = far;
            yield return WaitFixedUntil(() => exitWait.IsCompleted, 20);

            Assert.That(exitWait.IsCompletedSuccessfully, Is.True);
            Assert.That(exitWait.GetAwaiter().GetResult(), Is.EqualTo(bodyCollider));
        }

        [UnityTest]
        public IEnumerator CollisionEnter2D_ReportsTheContactedCollider()
        {
            GameObject ground = NewObject("message-collision2d-ground");
            BoxCollider2D groundCollider = ground.AddComponent<BoxCollider2D>();
            groundCollider.size = new Vector2(20f, 1f);
            GameObject ball = NewObject("message-collision2d-ball");
            ball.AddComponent<CircleCollider2D>();
            ball.AddComponent<Rigidbody2D>();
            ball.transform.position = new Vector3(0f, 2f, 0f);

            // Physics2D.reuseCollisionCallbacks may be on in other projects: read the collider inside the
            // continuation, which runs inline inside OnCollisionEnter2D (see the 3D test).
            Collider2D contacted = null;
            OnityTaskAwaiter<Collision2D> awaiter = ball.GetAsyncCollisionEnter2DTrigger()
                .OnCollisionEnter2DAsync().GetAwaiter();
            awaiter.OnCompleted(() => contacted = awaiter.GetResult().collider);

            yield return WaitFixedUntil(() => contacted != null, 200);

            Assert.That(contacted != null, Is.True, "OnCollisionEnter2D did not arrive");
            Assert.That(contacted, Is.EqualTo(groundCollider));
        }

        [UnityTest]
        public IEnumerator AudioFilterRead_RaisedFromAnotherThread_ResumesWaiters_AndAContinuationMayWaitAgain()
        {
            GameObject owner = NewObject("message-audio-thread");
            OnityAsyncAudioFilterReadTrigger trigger = owner.GetAsyncAudioFilterReadTrigger();
            yield return null;

            float[] data = new float[4];
            int resumed = 0;
            OnityTask<(float[] data, int channels)> second = default;
            OnityTaskAwaiter<(float[] data, int channels)> awaiter = trigger.OnAudioFilterReadAsync().GetAwaiter();
            awaiter.OnCompleted(() =>
            {
                awaiter.GetResult();
                resumed++;
                second = trigger.OnAudioFilterReadAsync();
            });

            RaiseAudioFilterRead(trigger, data, 2);

            Assert.That(resumed, Is.EqualTo(1));
            Assert.That(second.IsCompleted, Is.False, "a wait started by the continuation misses the current raise");

            RaiseAudioFilterRead(trigger, data, 2);

            Assert.That(second.IsCompletedSuccessfully, Is.True);
            (float[] data, int channels) result = second.GetAwaiter().GetResult();
            Assert.That(result.data, Is.SameAs(data));
            Assert.That(result.channels, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator AudioFilterRead_WaitersChangedWhileAnotherThreadRaises_StayConsistent()
        {
            GameObject owner = NewObject("message-audio-stress");
            OnityAsyncAudioFilterReadTrigger trigger = owner.GetAsyncAudioFilterReadTrigger();
            yield return null;

            float[] data = new float[4];
            MethodInfo message = FindAudioFilterReadMessage();
            using (CancellationTokenSource stop = new CancellationTokenSource())
            {
                Task raiser = Task.Run(() =>
                {
                    object[] arguments = { data, 2 };
                    while (!stop.IsCancellationRequested)
                    {
                        message.Invoke(trigger, arguments);
                    }
                });

                for (int i = 0; i < 2000; i++)
                {
                    IOnityAsyncOnAudioFilterReadHandler handler = trigger.GetOnAudioFilterReadAsyncHandler();
                    handler.OnAudioFilterReadAsync();
                    ((IDisposable)handler).Dispose();
                }

                stop.Cancel();
                raiser.GetAwaiter().GetResult();
            }

            // The list is still intact: a fresh wait resumes on the next raise.
            OnityTask<(float[] data, int channels)> last = trigger.OnAudioFilterReadAsync();
            RaiseAudioFilterRead(trigger, data, 2);

            Assert.That(last.IsCompletedSuccessfully, Is.True);
            last.GetAwaiter().GetResult();
            LogAssert.NoUnexpectedReceived();
        }

        // The 3D layer collision matrix decides whether contacts and trigger events happen at all, and a
        // project may switch the Default layer off (the Onity test project does: its matrix has no layer
        // below 6 interacting with anything). Pick a pair the project lets interact instead of changing the
        // matrix. The 2D matrix of that project is fully enabled, so the 2D tests need no such pair.
        private static (int first, int second) FindCollidingLayers()
        {
            for (int first = 0; first < 32; first++)
            {
                for (int second = first; second < 32; second++)
                {
                    if (!Physics.GetIgnoreLayerCollision(first, second))
                    {
                        return (first, second);
                    }
                }
            }

            Assert.Inconclusive("The project's 3D layer collision matrix lets no pair of layers interact.");
            return (0, 0);
        }

        private static MethodInfo FindAudioFilterReadMessage()
        {
            return typeof(OnityAsyncAudioFilterReadTrigger).GetMethod(
                "OnAudioFilterRead",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        }

        // Unity raises OnAudioFilterRead on the audio thread; the tests raise it from a pool thread.
        private static void RaiseAudioFilterRead(OnityAsyncAudioFilterReadTrigger trigger, float[] data, int channels)
        {
            MethodInfo message = FindAudioFilterReadMessage();
            Task.Run(() =>
            {
                message.Invoke(trigger, new object[] { data, channels });
            }).GetAwaiter().GetResult();
        }

        private GameObject NewObject(string name)
        {
            GameObject gameObject = new GameObject(name);
            m_objects.Add(gameObject);
            return gameObject;
        }

        private static IEnumerator WaitFixedUntil(Func<bool> condition, int maxSteps)
        {
            for (int step = 0; step < maxSteps && !condition(); step++)
            {
                yield return new WaitForFixedUpdate();
            }
        }
    }
}
