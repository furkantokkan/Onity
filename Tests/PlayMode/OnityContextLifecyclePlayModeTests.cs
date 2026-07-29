using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Onity.DI;
using Onity.Unity.Contexts;
using Onity.Unity.Installers;
using UnityEngine;
using UnityEngine.TestTools;

namespace Onity.Tests.PlayMode
{
    /// <summary>
    /// PlayMode coverage proving an <see cref="OnityContext" /> drives container lifecycle
    /// entry points: a bound singleton's <see cref="IOnityInitializable.Initialize" /> runs once
    /// at build and its <see cref="IOnityTickable.Tick" /> is pumped each frame from the context's
    /// <c>Update</c>.
    /// </summary>
    [TestFixture]
    public sealed class OnityContextLifecyclePlayModeTests
    {
        /// <summary>
        /// Builds a context with a lifecycle service installer, runs several frames, and asserts
        /// the service initialized exactly once and ticked across multiple frames.
        /// </summary>
        /// <returns>Frame-yielding enumerator for the PlayMode runner.</returns>
        [UnityTest]
        public IEnumerator ContextPump_RunsInitializeOnceAndTicksEachFrame()
        {
            bool previousDiagnostics = OnityContainer.DiagnosticsCollectionEnabled;
            OnityContainer.DiagnosticsCollectionEnabled = false;

            GameObject contextObject = new GameObject(nameof(OnityContextLifecyclePlayModeTests));

            try
            {
                // Build the object inactive so adding components does not trigger Awake before
                // the installer is wired into the context's serialized installer list.
                contextObject.SetActive(false);

                LifecycleInstaller installer = contextObject.AddComponent<LifecycleInstaller>();
                LifecycleContext context = contextObject.AddComponent<LifecycleContext>();

                SetContextInstallers(context, installer);

                // Activation runs OnityContext.Awake: container is created, bindings installed,
                // Build runs and fires Initialize on the bound lifecycle service.
                contextObject.SetActive(true);

                Assert.That(context.Container, Is.Not.Null, "Context did not create a container on Awake.");

                LifecycleService service = context.Container.Resolve<LifecycleService>();
                Assert.That(service, Is.Not.Null, "Lifecycle service was not bound by the installer.");
                Assert.That(
                    service.InitializeCount,
                    Is.EqualTo(1),
                    "Initialize must run exactly once at container build.");

                int initialTicks = service.TickCount;

                const int framesToRun = 5;

                for (int frame = 0; frame < framesToRun; frame++)
                {
                    yield return null;
                }

                Assert.That(
                    service.InitializeCount,
                    Is.EqualTo(1),
                    "Initialize must not run again after the first build.");
                Assert.That(
                    service.TickCount,
                    Is.GreaterThanOrEqualTo(initialTicks + framesToRun),
                    "Tick must increment at least once per frame via the context Update pump.");
            }
            finally
            {
                Object.Destroy(contextObject);
                OnityContainer.DiagnosticsCollectionEnabled = previousDiagnostics;
            }
        }

        private static void SetContextInstallers(OnityContext context, params MonoInstaller[] installers)
        {
            FieldInfo installersField = typeof(OnityContext).GetField(
                "m_installers",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(
                installersField,
                Is.Not.Null,
                "Expected OnityContext to hold installers in the private field 'm_installers'.");

            installersField.SetValue(context, installers);
        }

        /// <summary>
        /// Minimal concrete context used only by this PlayMode fixture.
        /// </summary>
        private sealed class LifecycleContext : OnityContext
        {
        }

        /// <summary>
        /// Installer that binds the lifecycle service as a single instance so the container
        /// collects it for both initialization and tick pumping.
        /// </summary>
        private sealed class LifecycleInstaller : MonoInstaller
        {
            /// <inheritdoc />
            public override void InstallBindings(OnityContainer container)
            {
                container.BindInterfacesAndSelfTo<LifecycleService>().AsSingle();
            }
        }

        /// <summary>
        /// Test service implementing both lifecycle entry points and counting invocations.
        /// </summary>
        private sealed class LifecycleService : IOnityInitializable, IOnityTickable
        {
            /// <summary>
            /// Number of times <see cref="Initialize" /> has run.
            /// </summary>
            public int InitializeCount { get; private set; }

            /// <summary>
            /// Number of times <see cref="Tick" /> has run.
            /// </summary>
            public int TickCount { get; private set; }

            /// <inheritdoc />
            public void Initialize()
            {
                InitializeCount++;
            }

            /// <inheritdoc />
            public void Tick()
            {
                TickCount++;
            }
        }
    }
}
