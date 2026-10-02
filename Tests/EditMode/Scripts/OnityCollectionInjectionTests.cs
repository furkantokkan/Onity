using System.Collections.Generic;
using NUnit.Framework;
using Onity.DI;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Verifies collection injection: binding a contract more than once and resolving
    /// <c>IEnumerable&lt;T&gt;</c> / <c>IReadOnlyList&lt;T&gt;</c> / <c>IList&lt;T&gt;</c> /
    /// <c>T[]</c> / <c>List&lt;T&gt;</c> returns every registration (registration order,
    /// ancestors first across a scope hierarchy), while a single <c>Resolve&lt;T&gt;()</c>
    /// keeps last-wins for back-compat. This is the VContainer/Zenject "register many,
    /// inject all" feature Onity previously lacked.
    /// </summary>
    [TestFixture]
    public sealed class OnityCollectionInjectionTests
    {
        [SetUp]
        public void SetUp()
        {
            OnityContainer.DiagnosticsCollectionEnabled = false;
        }

        [Test]
        public void ResolveIEnumerable_ReturnsAllRegistrationsInRegistrationOrder()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<IService>().To<ServiceA>().AsSingle();
            container.Bind<IService>().To<ServiceB>().AsSingle();
            container.Bind<IService>().To<ServiceC>().AsSingle();

            List<IService> all = new List<IService>(container.Resolve<IEnumerable<IService>>());

            Assert.That(all.Count, Is.EqualTo(3));
            Assert.That(all[0], Is.TypeOf<ServiceA>());
            Assert.That(all[1], Is.TypeOf<ServiceB>());
            Assert.That(all[2], Is.TypeOf<ServiceC>());
        }

        [Test]
        public void SingleResolve_AfterMultipleBinds_ReturnsLastWins()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<IService>().To<ServiceA>().AsSingle();
            container.Bind<IService>().To<ServiceB>().AsSingle();

            IService single = container.Resolve<IService>();

            Assert.That(single, Is.TypeOf<ServiceB>());
        }

        [Test]
        public void ResolveReadOnlyListArrayAndList_AllReturnEveryRegistration()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<IService>().To<ServiceA>().AsSingle();
            container.Bind<IService>().To<ServiceB>().AsSingle();

            IReadOnlyList<IService> readOnlyList = container.Resolve<IReadOnlyList<IService>>();
            IService[] array = container.Resolve<IService[]>();
            List<IService> list = container.Resolve<List<IService>>();

            Assert.That(readOnlyList.Count, Is.EqualTo(2));
            Assert.That(array.Length, Is.EqualTo(2));
            Assert.That(list, Is.InstanceOf<List<IService>>());
            Assert.That(list.Count, Is.EqualTo(2));
        }

        [Test]
        public void ConstructorInjection_IEnumerable_InjectsAllRegistrations()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<IService>().To<ServiceA>().AsSingle();
            container.Bind<IService>().To<ServiceB>().AsSingle();
            container.Bind<EnumerableConsumer>().AsTransient();

            EnumerableConsumer consumer = container.Resolve<EnumerableConsumer>();

            Assert.That(consumer.Services.Count, Is.EqualTo(2));
        }

        [Test]
        public void ConstructorInjection_ReadOnlyList_InjectsAllRegistrations()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<IService>().To<ServiceA>().AsSingle();
            container.Bind<IService>().To<ServiceB>().AsSingle();
            container.Bind<IService>().To<ServiceC>().AsSingle();
            container.Bind<ReadOnlyListConsumer>().AsTransient();

            ReadOnlyListConsumer consumer = container.Resolve<ReadOnlyListConsumer>();

            Assert.That(consumer.Count, Is.EqualTo(3));
        }

        [Test]
        public void Collection_SingletonElement_SharesInstanceWithSingleResolve()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<IService>().To<ServiceA>().AsSingle();

            IService single = container.Resolve<IService>();
            IService[] array = container.Resolve<IService[]>();

            Assert.That(array.Length, Is.EqualTo(1));
            Assert.That(array[0], Is.SameAs(single));
        }

        [Test]
        public void Collection_TransientElement_ProducesFreshInstancesEachResolve()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<IService>().To<ServiceA>().AsTransient();

            IService[] first = container.Resolve<IService[]>();
            IService[] second = container.Resolve<IService[]>();

            Assert.That(first[0], Is.Not.SameAs(second[0]));
        }

        [Test]
        public void ChildContainer_Collection_IncludesAncestorBindingsAncestorsFirst()
        {
            using OnityContainer parent = new OnityContainer();
            parent.Bind<IService>().To<ServiceA>().AsSingle();

            using OnityContainer child = new OnityContainer(parent);
            child.Bind<IService>().To<ServiceB>().AsSingle();

            IService[] all = child.Resolve<IService[]>();

            Assert.That(all.Length, Is.EqualTo(2));
            Assert.That(all[0], Is.TypeOf<ServiceA>());
            Assert.That(all[1], Is.TypeOf<ServiceB>());
        }

        [Test]
        public void Collection_MultiContractBinding_ContributesOncePerElementType()
        {
            using OnityContainer container = new OnityContainer();
            container.BindInterfacesAndSelfTo<MultiContractService>().AsSingle();

            IService[] services = container.Resolve<IService[]>();

            Assert.That(services.Length, Is.EqualTo(1));
            Assert.That(services[0], Is.TypeOf<MultiContractService>());
        }

        [Test]
        public void ResolveCollection_NoElementBindings_IsUnresolvable()
        {
            using OnityContainer container = new OnityContainer();

            Assert.That(
                () => container.Resolve<IEnumerable<IService>>(),
                Throws.TypeOf<OnityResolveException>());
            Assert.That(container.CanResolve(typeof(IEnumerable<IService>)), Is.False);
        }

        [Test]
        public void CanResolve_CollectionWithBoundElement_IsTrue()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<IService>().To<ServiceA>().AsSingle();

            Assert.That(container.CanResolve(typeof(IEnumerable<IService>)), Is.True);
            Assert.That(container.CanResolve(typeof(IReadOnlyList<IService>)), Is.True);
            Assert.That(container.CanResolve(typeof(IService[])), Is.True);
        }

        private interface IService
        {
        }

        private interface IOtherContract
        {
        }

        private sealed class ServiceA : IService
        {
        }

        private sealed class ServiceB : IService
        {
        }

        private sealed class ServiceC : IService
        {
        }

        private sealed class MultiContractService : IService, IOtherContract
        {
        }

        private sealed class EnumerableConsumer
        {
            public EnumerableConsumer(IEnumerable<IService> services)
            {
                Services = new List<IService>(services);
            }

            public List<IService> Services { get; }
        }

        private sealed class ReadOnlyListConsumer
        {
            public ReadOnlyListConsumer(IReadOnlyList<IService> services)
            {
                Count = services.Count;
            }

            public int Count { get; }
        }
    }
}
