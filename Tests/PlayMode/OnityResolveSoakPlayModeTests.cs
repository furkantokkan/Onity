using System;
using System.Collections;
using NUnit.Framework;
using Onity.DI;
using Onity.Reactive;
using UnityEngine.TestTools;

namespace Onity.Tests.PlayMode
{
    /// <summary>
    /// Coarse PlayMode soak test exercising the container and reactive primitives under churn:
    /// over many frames it repeatedly resolves a transient from a built <see cref="OnityContainer" />
    /// and creates then disposes <see cref="Subject{T}" /> subscriptions, asserting no exceptions
    /// and stable behavior.
    /// </summary>
    [TestFixture]
    public sealed class OnityResolveSoakPlayModeTests
    {
        private const int k_frameCount = 120;
        private const int k_resolvesPerFrame = 8;

        /// <summary>
        /// Resolves transients and churns reactive subscriptions every frame for ~120 frames,
        /// verifying stable resolution and subscription delivery without exceptions.
        /// </summary>
        /// <returns>Frame-yielding enumerator for the PlayMode runner.</returns>
        [UnityTest]
        public IEnumerator ResolveAndSubscribe_UnderChurn_StaysStable()
        {
            bool previousDiagnostics = OnityContainer.DiagnosticsCollectionEnabled;
            OnityContainer.DiagnosticsCollectionEnabled = false;

            OnityContainer container = new OnityContainer();
            Subject<int> subject = new Subject<int>();

            // Reused across the whole soak so per-frame work avoids extra delegate/closure allocation.
            int observedValue = 0;
            int observedCount = 0;
            Observer<int> observer = value =>
            {
                observedValue = value;
                observedCount++;
            };

            try
            {
                container.Bind<ISoakService>().To<SoakService>().AsTransient();
                container.Build();

                ISoakService warmup = container.Resolve<ISoakService>();
                Assert.That(warmup, Is.Not.Null, "Transient resolve returned null before the soak loop.");

                ISoakService previousInstance = warmup;

                for (int frame = 0; frame < k_frameCount; frame++)
                {
                    for (int resolveIndex = 0; resolveIndex < k_resolvesPerFrame; resolveIndex++)
                    {
                        ISoakService current = container.Resolve<ISoakService>();

                        Assert.That(current, Is.Not.Null, "Transient resolve returned null during the soak loop.");
                        Assert.That(
                            current,
                            Is.Not.SameAs(previousInstance),
                            "Transient binding must return a fresh instance on each resolve.");

                        previousInstance = current;
                    }

                    // Create a short-lived subscription, push a value through it, then tear it down.
                    // Alternating between explicit Dispose and CompositeDisposable.Dispose exercises
                    // both teardown paths under churn.
                    int countBeforePush = observedCount;
                    int expectedValue = frame;

                    if ((frame & 1) == 0)
                    {
                        IDisposable subscription = subject.Subscribe(observer);
                        subject.OnNext(expectedValue);
                        subscription.Dispose();
                    }
                    else
                    {
                        CompositeDisposable disposables = new CompositeDisposable();
                        subject.Subscribe(observer).AddTo(disposables);
                        subject.OnNext(expectedValue);
                        disposables.Dispose();
                    }

                    Assert.That(
                        observedCount,
                        Is.EqualTo(countBeforePush + 1),
                        "Active subscription must observe exactly one pushed value per frame.");
                    Assert.That(
                        observedValue,
                        Is.EqualTo(expectedValue),
                        "Subscription observed an unexpected value during the soak loop.");

                    // After teardown the subject must hold no live subscribers, so a further push
                    // delivers nothing. This proves subscriptions are released under churn.
                    int countAfterDispose = observedCount;
                    subject.OnNext(-1);

                    Assert.That(
                        observedCount,
                        Is.EqualTo(countAfterDispose),
                        "Disposed subscription must not receive further values.");

                    yield return null;
                }

                Assert.That(
                    observedCount,
                    Is.EqualTo(k_frameCount),
                    "Each frame should deliver exactly one observed value across the soak.");
            }
            finally
            {
                subject.Dispose();
                container.Dispose();
                OnityContainer.DiagnosticsCollectionEnabled = previousDiagnostics;
            }
        }

        /// <summary>
        /// Contract resolved transiently during the soak loop.
        /// </summary>
        private interface ISoakService
        {
        }

        /// <summary>
        /// Trivial transient implementation; a new instance is expected per resolve.
        /// </summary>
        private sealed class SoakService : ISoakService
        {
        }
    }
}
