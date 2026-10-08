using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.DI;
using Onity.Unity.Async;
using Onity.Unity.Contexts;
using Onity.Unity.Installers;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Onity.Tests.PlayMode
{
    /// <summary>
    /// PlayMode coverage for <see cref="IOnityAsyncInitializable" /> inside a Unity context: readiness
    /// waits for the async initializers, <see cref="OnityContext.WaitReadyAsync" /> resumes on the main
    /// thread, and destroying the context mid-initialization cancels it quietly.
    /// </summary>
    [TestFixture]
    public sealed class OnityAsyncInitializablePlayModeTests
    {
        private const int k_maximumFrames = 30;

        [UnityTest]
        public IEnumerator SceneContext_AsyncInitializer_KeepsContextNotReadyUntilItFinishes()
        {
            GameObject contextObject = new GameObject(nameof(SceneContext_AsyncInitializer_KeepsContextNotReadyUntilItFinishes));
            contextObject.SetActive(false);
            FrameDelayInstaller installer = contextObject.AddComponent<FrameDelayInstaller>();
            SceneContext context = contextObject.AddComponent<SceneContext>();
            SetContextInstallers(context, installer);

            try
            {
                contextObject.SetActive(true);
                FrameDelayInitializer initializer = context.Container.Resolve<FrameDelayInitializer>();

                Assert.That(context.IsReady, Is.False);

                for (int frame = 0; frame < k_maximumFrames && context.IsReady == false; frame++)
                {
                    yield return null;

                    if (initializer.IsCompleted == false)
                    {
                        Assert.That(context.IsReady, Is.False, "Ready before the async initializer finished.");
                    }
                }

                Assert.That(initializer.IsStarted, Is.True);
                Assert.That(initializer.IsCompleted, Is.True);
                Assert.That(context.IsReady, Is.True);
                Assert.That(context.ReadyTask.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            }
            finally
            {
                Object.Destroy(contextObject);
            }
        }

        [UnityTest]
        public IEnumerator WaitReadyAsync_ResumesOnMainThread_AfterWorkerCompletedInitializer()
        {
            int mainThreadId = Thread.CurrentThread.ManagedThreadId;
            GameObject contextObject = new GameObject(nameof(WaitReadyAsync_ResumesOnMainThread_AfterWorkerCompletedInitializer));
            contextObject.SetActive(false);
            WorkerThenRecordingInstaller installer = contextObject.AddComponent<WorkerThenRecordingInstaller>();
            TestContext context = contextObject.AddComponent<TestContext>();
            SetContextInstallers(context, installer);

            try
            {
                contextObject.SetActive(true);
                ReadyProbe probe = new ReadyProbe();
                probe.Run(context).Forget();

                for (int frame = 0; frame < k_maximumFrames && probe.IsFinished == false; frame++)
                {
                    yield return null;
                }

                ThreadRecordingInitializer recorder = context.Container.Resolve<ThreadRecordingInitializer>();

                Assert.That(probe.IsFinished, Is.True);
                Assert.That(probe.WasCanceled, Is.False);
                Assert.That(probe.ThreadId, Is.EqualTo(mainThreadId));
                Assert.That(recorder.ThreadId, Is.EqualTo(mainThreadId));
                Assert.That(context.IsReady, Is.True);
                Assert.That(context.WaitReadyAsync().IsCompletedSuccessfully, Is.True);
            }
            finally
            {
                Object.Destroy(contextObject);
            }
        }

        [UnityTest]
        public IEnumerator DestroyingContextMidInitialization_CancelsInitializerAndWaiters()
        {
            GameObject contextObject = new GameObject(nameof(DestroyingContextMidInitialization_CancelsInitializerAndWaiters));
            contextObject.SetActive(false);
            BlockingInstaller installer = contextObject.AddComponent<BlockingInstaller>();
            TestContext context = contextObject.AddComponent<TestContext>();
            SetContextInstallers(context, installer);

            contextObject.SetActive(true);
            BlockingInitializer initializer = context.Container.Resolve<BlockingInitializer>();

            yield return null;

            Assert.That(initializer.IsStarted, Is.True);
            Assert.That(context.IsReady, Is.False);

            ReadyProbe probe = new ReadyProbe();
            probe.Run(context).Forget();

            Assert.That(probe.IsFinished, Is.False);

            Object.Destroy(contextObject);

            for (int frame = 0;
                frame < k_maximumFrames && (initializer.WasCanceled == false || probe.IsFinished == false);
                frame++)
            {
                yield return null;
            }

            Assert.That(initializer.WasCanceled, Is.True);
            Assert.That(probe.IsFinished, Is.True);
            Assert.That(probe.WasCanceled, Is.True);
            Assert.That(context.IsReady, Is.False);
            Assert.That(context.ReadyTask.IsCanceled, Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        private static void SetContextInstallers(OnityContext context, params MonoInstaller[] installers)
        {
            FieldInfo installersField = typeof(OnityContext).GetField(
                "m_installers",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(installersField, Is.Not.Null);
            installersField.SetValue(context, installers);
        }

        private sealed class TestContext : OnityContext
        {
        }

        private sealed class ReadyProbe
        {
            public bool IsFinished { get; private set; }

            public bool WasCanceled { get; private set; }

            public int ThreadId { get; private set; }

            public async OnityTaskVoid Run(OnityContext context)
            {
                try
                {
                    await context.WaitReadyAsync();
                    ThreadId = Thread.CurrentThread.ManagedThreadId;
                }
                catch (OperationCanceledException)
                {
                    WasCanceled = true;
                }

                IsFinished = true;
            }
        }

        private sealed class FrameDelayInitializer : IOnityAsyncInitializable
        {
            public bool IsStarted { get; private set; }

            public bool IsCompleted { get; private set; }

            public async ValueTask InitializeAsync(CancellationToken cancellationToken)
            {
                IsStarted = true;
                await OnityTask.DelayFrames(2, cancellationToken);
                IsCompleted = true;
            }
        }

        private sealed class FrameDelayInstaller : MonoInstaller
        {
            public override void InstallBindings(OnityContainer container)
            {
                container.Bind<FrameDelayInitializer>().AsSingle();
            }
        }

        private sealed class WorkerCompletingInitializer : IOnityAsyncInitializable
        {
            public async ValueTask InitializeAsync(CancellationToken cancellationToken)
            {
                await Task.Run(() => { }, cancellationToken).ConfigureAwait(false);
            }
        }

        private sealed class ThreadRecordingInitializer : IOnityAsyncInitializable
        {
            public int ThreadId { get; private set; } = -1;

            public ValueTask InitializeAsync(CancellationToken cancellationToken)
            {
                ThreadId = Thread.CurrentThread.ManagedThreadId;
                return default;
            }
        }

        private sealed class WorkerThenRecordingInstaller : MonoInstaller
        {
            public override void InstallBindings(OnityContainer container)
            {
                container.Bind<WorkerCompletingInitializer>().AsSingle();
                container.Bind<ThreadRecordingInitializer>().AsSingle();
            }
        }

        private sealed class BlockingInitializer : IOnityAsyncInitializable
        {
            public bool IsStarted { get; private set; }

            public bool WasCanceled { get; private set; }

            public async ValueTask InitializeAsync(CancellationToken cancellationToken)
            {
                IsStarted = true;

                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    WasCanceled = true;
                    throw;
                }
            }
        }

        private sealed class BlockingInstaller : MonoInstaller
        {
            public override void InstallBindings(OnityContainer container)
            {
                container.Bind<BlockingInitializer>().AsSingle();
            }
        }
    }
}
