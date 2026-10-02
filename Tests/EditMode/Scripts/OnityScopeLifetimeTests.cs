using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.DI;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Covers the DI scope lifetime token: lazy creation, cancel-first disposal, parent linking,
    /// <see cref="IOnityScopeLifetime" /> injection, <c>AddTo</c>, and async build cancellation.
    /// </summary>
    [TestFixture]
    public sealed class OnityScopeLifetimeTests
    {
        [Test]
        public void LifetimeToken_StaysLiveUntilDispose_ThenIsCanceledOnce()
        {
            OnityContainer container = new OnityContainer();
            CancellationToken token = container.LifetimeToken;
            int cancelCount = 0;
            token.Register(() => cancelCount++);

            container.Build();
            _ = container.Resolve<DisposableProbe>();

            Assert.That(token.CanBeCanceled, Is.True);
            Assert.That(token.IsCancellationRequested, Is.False);
            Assert.That(container.LifetimeToken, Is.EqualTo(token));

            container.Dispose();
            container.Dispose();

            Assert.That(token.IsCancellationRequested, Is.True);
            Assert.That(cancelCount, Is.EqualTo(1));
            Assert.That(container.IsDisposed, Is.True);
            Assert.That(container.LifetimeToken.IsCancellationRequested, Is.True);
        }

        [Test]
        public void Dispose_CancelsTokenBeforeDisposingSingletonsAndScopedInstances()
        {
            OnityContainer container = new OnityContainer();
            container.Bind<DisposableProbe>().AsSingle();
            container.Bind<ScopedDisposableProbe>().AsScoped();
            container.Build();

            DisposableProbe singleton = container.Resolve<DisposableProbe>();
            ScopedDisposableProbe scoped = container.Resolve<ScopedDisposableProbe>();
            singleton.Token = container.LifetimeToken;
            scoped.Token = container.LifetimeToken;

            container.Dispose();

            Assert.That(singleton.DisposeCount, Is.EqualTo(1));
            Assert.That(singleton.WasTokenCanceledAtDispose, Is.True);
            Assert.That(scoped.DisposeCount, Is.EqualTo(1));
            Assert.That(scoped.WasTokenCanceledAtDispose, Is.True);
        }

        [Test]
        public async Task LifetimeToken_NeverRequested_AllocatesNoSource()
        {
            FieldInfo sourceField = typeof(OnityContainer).GetField(
                "m_lifetimeSource",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(sourceField, Is.Not.Null, "Expected the lazily created lifetime source field.");

            OnityContainer container = new OnityContainer();
            container.Bind<DisposableProbe>().AsSingle();
            container.RegisterBuildCallback(_ => { });
            await container.BuildAsync();
            _ = container.Resolve<DisposableProbe>();

            Assert.That(sourceField.GetValue(container), Is.Null);

            container.Dispose();

            Assert.That(sourceField.GetValue(container), Is.Null);
            Assert.That(container.LifetimeToken.IsCancellationRequested, Is.True);
            Assert.That(sourceField.GetValue(container), Is.Null);
        }

        [Test]
        public void ChildLifetimeToken_IsCanceledWhenParentIsDisposed()
        {
            OnityContainer parent = new OnityContainer();
            using OnityContainer child = new OnityContainer(parent);
            CancellationToken childToken = child.LifetimeToken;

            Assert.That(childToken.IsCancellationRequested, Is.False);

            parent.Dispose();

            Assert.That(childToken.IsCancellationRequested, Is.True);
            Assert.That(child.IsDisposed, Is.False);
        }

        [Test]
        public void ChildDispose_DoesNotCancelParentToken()
        {
            using OnityContainer parent = new OnityContainer();
            CancellationToken parentToken = parent.LifetimeToken;
            OnityContainer child = new OnityContainer(parent);
            CancellationToken childToken = child.LifetimeToken;

            child.Dispose();

            Assert.That(childToken.IsCancellationRequested, Is.True);
            Assert.That(parentToken.IsCancellationRequested, Is.False);
        }

        [Test]
        public void ChildOfDisposedParent_StartsWithCanceledToken()
        {
            OnityContainer parent = new OnityContainer();
            CancellationToken parentToken = parent.LifetimeToken;
            parent.Dispose();

            using OnityContainer child = new OnityContainer(parent);

            Assert.That(parentToken.IsCancellationRequested, Is.True);
            Assert.That(child.LifetimeToken.IsCancellationRequested, Is.True);
        }

        [Test]
        public void Container_IsItsOwnScopeLifetime()
        {
            OnityContainer container = new OnityContainer();
            IOnityScopeLifetime lifetime = container;

            Assert.That(lifetime.Token, Is.EqualTo(container.LifetimeToken));
            Assert.That(lifetime.IsDisposed, Is.False);

            container.Dispose();

            Assert.That(lifetime.IsDisposed, Is.True);
            Assert.That(lifetime.Token.IsCancellationRequested, Is.True);
        }

        [Test]
        public void BoundScopeLifetime_InjectsTheOwningScope()
        {
            using OnityContainer parent = new OnityContainer();
            parent.BindInstance<IOnityScopeLifetime>(parent);
            parent.Bind<ParentOwnedService>().AsSingle();
            parent.Build();

            using OnityContainer child = new OnityContainer(parent);
            child.BindInstance<IOnityScopeLifetime>(child);
            child.Bind<ChildOwnedService>().AsSingle();
            child.Build();

            ParentOwnedService parentService = child.Resolve<ParentOwnedService>();
            ChildOwnedService childService = child.Resolve<ChildOwnedService>();

            Assert.That(parentService.Lifetime, Is.SameAs(parent));
            Assert.That(childService.Lifetime, Is.SameAs(child));
        }

        [Test]
        public void AddTo_DisposesWhenScopeEnds()
        {
            OnityContainer container = new OnityContainer();
            DisposableProbe probe = new DisposableProbe();

            DisposableProbe returned = probe.AddTo(container);

            Assert.That(returned, Is.SameAs(probe));
            Assert.That(probe.DisposeCount, Is.EqualTo(0));

            container.Dispose();

            Assert.That(probe.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public void AddTo_DisposesBeforeOwnedSingletons()
        {
            List<string> order = new List<string>(2);
            OnityContainer container = new OnityContainer();
            container.BindInstance(order);
            container.Bind<OrderedDisposable>().AsSingle();
            container.Build();
            _ = container.Resolve<OrderedDisposable>();

            new ActionDisposable(() => order.Add("addTo")).AddTo(container);
            container.Dispose();

            Assert.That(order, Is.EqualTo(new[] { "addTo", "singleton" }));
        }

        [Test]
        public void AddTo_DisposedScope_DisposesImmediately()
        {
            OnityContainer container = new OnityContainer();
            container.Dispose();
            DisposableProbe probe = new DisposableProbe();

            probe.AddTo(container);

            Assert.That(probe.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public void AddTo_NullArguments_Throw()
        {
            using OnityContainer container = new OnityContainer();

            Assert.That(() => ((DisposableProbe)null).AddTo(container), Throws.ArgumentNullException);
            Assert.That(() => new DisposableProbe().AddTo(null), Throws.ArgumentNullException);
        }

        [Test]
        public async Task BuildAsync_WithoutCallerToken_PassesLifetimeToken()
        {
            using OnityContainer container = new OnityContainer();
            CancellationToken observed = default;
            container.RegisterBuildCallbackAsync(
                (_, cancellationToken) =>
                {
                    observed = cancellationToken;
                    return Task.CompletedTask;
                });

            await container.BuildAsync();

            Assert.That(observed, Is.EqualTo(container.LifetimeToken));
        }

        [Test]
        public async Task BuildAsync_DisposeDuringCallback_CancelsCallbackTokenAndBuild()
        {
            OnityContainer container = new OnityContainer();
            CancellationToken observed = default;
            container.RegisterBuildCallbackAsync(
                async (_, cancellationToken) =>
                {
                    observed = cancellationToken;
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                });

            Task build = container.BuildAsync();

            Assert.That(observed.IsCancellationRequested, Is.False);

            container.Dispose();

            Assert.That(observed.IsCancellationRequested, Is.True);

            try
            {
                await build;
                Assert.Fail("Expected the build to end canceled.");
            }
            catch (OperationCanceledException)
            {
            }

            Assert.That(build.IsCanceled, Is.True);
        }

        [Test]
        public async Task BuildAsync_CallerToken_IsLinkedWithLifetime()
        {
            using OnityContainer container = new OnityContainer();
            using CancellationTokenSource caller = new CancellationTokenSource();
            CancellationToken observed = default;
            container.RegisterBuildCallbackAsync(
                async (_, cancellationToken) =>
                {
                    observed = cancellationToken;
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                });

            Task build = container.BuildAsync(caller.Token);

            Assert.That(observed, Is.Not.EqualTo(caller.Token));
            Assert.That(observed, Is.Not.EqualTo(container.LifetimeToken));

            caller.Cancel();

            Assert.That(observed.IsCancellationRequested, Is.True);
            Assert.That(container.LifetimeToken.IsCancellationRequested, Is.False);

            try
            {
                await build;
                Assert.Fail("Expected the build to end canceled.");
            }
            catch (OperationCanceledException exception)
            {
                Assert.That(exception.CancellationToken, Is.EqualTo(caller.Token));
            }
        }

        [Test]
        public void BuildAsync_LinkedCallerToken_IsCanceledByDispose()
        {
            OnityContainer container = new OnityContainer();
            using CancellationTokenSource caller = new CancellationTokenSource();
            CancellationToken observed = default;
            container.RegisterBuildCallbackAsync(
                (_, cancellationToken) =>
                {
                    observed = cancellationToken;
                    return Task.CompletedTask;
                });

            Task build = container.BuildAsync(caller.Token);

            Assert.That(build.IsCompleted, Is.True);
            Assert.That(observed.IsCancellationRequested, Is.False);

            container.Dispose();

            Assert.That(observed.IsCancellationRequested, Is.True);
            Assert.That(caller.IsCancellationRequested, Is.False);
        }

        [Test]
        public async Task BuildAsync_DisposeWithLinkedCallerToken_ReportsLifetimeToken()
        {
            OnityContainer container = new OnityContainer();
            using CancellationTokenSource caller = new CancellationTokenSource();
            CancellationToken lifetimeToken = container.LifetimeToken;
            container.RegisterBuildCallbackAsync(
                async (_, cancellationToken) => await Task.Delay(Timeout.Infinite, cancellationToken));

            Task build = container.BuildAsync(caller.Token);
            container.Dispose();

            try
            {
                await build;
                Assert.Fail("Expected the build to end canceled.");
            }
            catch (OperationCanceledException exception)
            {
                Assert.That(exception.CancellationToken, Is.EqualTo(lifetimeToken));
            }
        }

        private class DisposableProbe : IDisposable
        {
            public CancellationToken Token { get; set; }

            public int DisposeCount { get; private set; }

            public bool WasTokenCanceledAtDispose { get; private set; }

            public void Dispose()
            {
                DisposeCount++;
                WasTokenCanceledAtDispose = Token.IsCancellationRequested;
            }
        }

        private sealed class ScopedDisposableProbe : DisposableProbe
        {
        }

        private sealed class OrderedDisposable : IDisposable
        {
            private readonly List<string> m_order;

            public OrderedDisposable(List<string> order)
            {
                m_order = order;
            }

            public void Dispose()
            {
                m_order.Add("singleton");
            }
        }

        private sealed class ActionDisposable : IDisposable
        {
            private readonly Action m_action;

            public ActionDisposable(Action action)
            {
                m_action = action;
            }

            public void Dispose()
            {
                m_action();
            }
        }

        private sealed class ParentOwnedService
        {
            public ParentOwnedService(IOnityScopeLifetime lifetime)
            {
                Lifetime = lifetime;
            }

            public IOnityScopeLifetime Lifetime { get; }
        }

        private sealed class ChildOwnedService
        {
            public ChildOwnedService(IOnityScopeLifetime lifetime)
            {
                Lifetime = lifetime;
            }

            public IOnityScopeLifetime Lifetime { get; }
        }
    }
}
