using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.DI;
using Onity.Unity.Async;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Covers <see cref="OnityAsyncCompositionExtensions.BindAsyncReactiveProperty{T}" />: one shared
    /// instance for every contract, owned and disposed by the container scope.
    /// </summary>
    [TestFixture]
    public sealed class OnityAsyncCompositionEditModeTests
    {
        [Test]
        public void BindAsyncReactiveProperty_ResolvesEveryContractAsSameInstance()
        {
            using OnityContainer container = new OnityContainer();
            OnityAsyncReactiveProperty<int> returned = container.BindAsyncReactiveProperty(5);

            OnityAsyncReactiveProperty<int> concrete = container.Resolve<OnityAsyncReactiveProperty<int>>();
            IOnityAsyncReactiveProperty<int> writable = container.Resolve<IOnityAsyncReactiveProperty<int>>();
            IOnityReadOnlyAsyncReactiveProperty<int> readOnly =
                container.Resolve<IOnityReadOnlyAsyncReactiveProperty<int>>();

            Assert.That(concrete, Is.SameAs(returned));
            Assert.That(writable, Is.SameAs(returned));
            Assert.That(readOnly, Is.SameAs(returned));
            Assert.That(readOnly.Value, Is.EqualTo(5));
        }

        [Test]
        public async Task ContainerDispose_DisposesPropertyAndCancelsPendingWait()
        {
            OnityContainer container = new OnityContainer();
            OnityAsyncReactiveProperty<int> property = container.BindAsyncReactiveProperty(0);
            OnityTask<int> wait = property.WaitAsync();

            Assert.That(wait.IsCompleted, Is.False);

            container.Dispose();

            Assert.That(wait.IsCanceled, Is.True);

            try
            {
                await wait;
                Assert.Fail("Expected the pending wait to be canceled.");
            }
            catch (OperationCanceledException)
            {
            }
        }

        [Test]
        public async Task ContainerDispose_EndsPendingEnumeratorMove()
        {
            OnityContainer container = new OnityContainer();
            OnityAsyncReactiveProperty<int> property = container.BindAsyncReactiveProperty(0);
            IOnityAsyncEnumerator<int> enumerator = property.WithoutCurrent().GetAsyncEnumerator();
            OnityTask<bool> move = enumerator.MoveNextAsync();

            Assert.That(move.IsCompleted, Is.False);

            container.Dispose();

            Assert.That(await move, Is.False);
            await enumerator.DisposeAsync();
        }
    }
}
