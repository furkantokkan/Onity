using NUnit.Framework;
using Onity.DI;
using Onity.Factory;

namespace Onity.Tests.EditMode
{
    // Ported from Zenject's "Factories" unit-test category
    // (Assets/ThirdParty/Zenject/OptionalExtras/UnitTests/Editor/Factories).
    //
    // Onity factory model divergence from Zenject:
    //   Zenject builds factories via a fluent chain (BindFactory<...>().To<C>().FromX(...)).
    //   Onity has no fluent factory body: BindFactory<TValue,TFactory>() simply registers a
    //   concrete TFactory : IFactory<...> as a singleton, and the factory class itself decides
    //   how to produce the value (typically by taking IResolver in its constructor and resolving
    //   or constructing the value). Each ported test therefore replaces Zenject's .From*/.To*
    //   chain with an explicit Onity IFactory implementation that preserves the original intent.
    [TestFixture]
    public sealed class OnityZenjectFactoryParityTests
    {
        [SetUp]
        public void SetUp()
        {
            OnityContainer.DiagnosticsCollectionEnabled = false;
        }

        [TearDown]
        public void TearDown()
        {
            OnityContainer.DiagnosticsCollectionEnabled = false;
        }

        // --- Zenject TestIFactory ---

        // TestIFactory.Test1: BindIFactory<Foo>; factory.Create() returns non-null.
        [Test]
        public void IFactory_NoParam_CreateReturnsValue()
        {
            using OnityContainer container = new OnityContainer();
            container.BindFactory<Foo, FooFactory>();

            IFactory<Foo> factory = container.Resolve<IFactory<Foo>>();

            Assert.That(factory.Create(), Is.Not.Null);
        }

        // TestIFactory.Test2Error: factory producing a value whose dependency cannot be resolved
        // throws on Create(). Onity throws OnityResolveException for the unresolvable string.
        [Test]
        public void IFactory_CreateWithUnresolvableDependency_Throws()
        {
            using OnityContainer container = new OnityContainer();
            container.BindFactory<FooTwo, FooTwoResolveFactory>();

            IFactory<FooTwo> factory = container.Resolve<IFactory<FooTwo>>();

            Assert.That(() => factory.Create(), Throws.TypeOf<OnityResolveException>());
        }

        // TestIFactory.Test2: BindIFactory<string, FooTwo>; Create("asdf").Value == "asdf".
        [Test]
        public void IFactory_OneParam_PassesParameterToValue()
        {
            using OnityContainer container = new OnityContainer();
            container.BindFactory<string, FooTwo, FooTwoFactory>();

            IFactory<string, FooTwo> factory = container.Resolve<IFactory<string, FooTwo>>();

            Assert.That(factory.Create("asdf").Value, Is.EqualTo("asdf"));
        }

        // --- Zenject TestFactory / TestFactoryFrom0 (self) ---

        // TestFactory.TestToSelf and TestFactoryFrom0.TestSelf1/TestSelf2/TestSelf3:
        // factory bound to itself produces a non-null value.
        [Test]
        public void Factory_BoundToSelf_CreateReturnsValue()
        {
            using OnityContainer container = new OnityContainer();
            container.BindFactory<Foo, FooFactory>();

            FooFactory factory = container.Resolve<FooFactory>();

            Assert.That(factory.Create(), Is.Not.Null);
        }

        // TestFactoryFrom0.TestFactoryScopeDefault: the factory itself is a singleton, so
        // resolving it twice returns the same factory instance.
        [Test]
        public void Factory_DefaultScope_ResolvesSameFactoryInstance()
        {
            using OnityContainer container = new OnityContainer();
            container.BindFactory<Foo, FooFactory>();

            FooFactory first = container.Resolve<FooFactory>();
            FooFactory second = container.Resolve<FooFactory>();

            // Divergence: Onity BindFactory always binds the factory AsSingle (no AsTransient option).
            Assert.That(first, Is.SameAs(second));
        }

        // TestFactoryFrom0.TestConcrete: a factory typed to produce IFoo creates a Foo.
        [Test]
        public void Factory_AbstractContract_CreatesConcreteValue()
        {
            using OnityContainer container = new OnityContainer();
            container.BindFactory<IFoo, IFooFactory>();

            IFooFactory factory = container.Resolve<IFooFactory>();
            IFoo created = factory.Create();

            Assert.That(created, Is.Not.Null);
            Assert.That(created, Is.InstanceOf<Foo>());
        }

        // --- Zenject TestFactoryFrom1 / TestIFactory.Test2 (one parameter) ---

        // TestFactoryFrom1.TestSelf: one-parameter factory forwards the parameter to the value.
        [Test]
        public void Factory_OneParam_BoundToSelf_PassesParameter()
        {
            using OnityContainer container = new OnityContainer();
            container.BindFactory<string, ParamFoo, ParamFooFactory>();

            ParamFooFactory factory = container.Resolve<ParamFooFactory>();

            Assert.That(factory.Create("asdf").Value, Is.EqualTo("asdf"));
        }

        // TestFactoryFrom1.TestConcrete: one-parameter factory typed to IParamFoo creates a ParamFoo
        // and forwards the parameter.
        [Test]
        public void Factory_OneParam_AbstractContract_CreatesConcreteValue()
        {
            using OnityContainer container = new OnityContainer();
            container.BindFactory<string, IParamFoo, IParamFooFactory>();

            IParamFooFactory factory = container.Resolve<IParamFooFactory>();
            IParamFoo created = factory.Create("asdf");

            Assert.That(created, Is.InstanceOf<ParamFoo>());
            Assert.That(created.Value, Is.EqualTo("asdf"));
        }

        // --- Zenject TestFactoryFromInstance0 ---

        // TestFactoryFromInstance0.TestSelf: a factory that always returns a pre-built instance.
        [Test]
        public void Factory_FromInstance_ReturnsSameInstance()
        {
            using OnityContainer container = new OnityContainer();
            Foo foo = new Foo();
            container.BindInstance(foo);
            container.BindFactory<Foo, FooResolveFactory>();

            FooResolveFactory factory = container.Resolve<FooResolveFactory>();

            // Divergence: Zenject's .FromInstance(foo) becomes an instance binding the factory resolves.
            Assert.That(factory.Create(), Is.SameAs(foo));
        }

        // TestFactoryFromInstance0.TestConcrete: instance-backed factory typed to IFoo.
        [Test]
        public void Factory_FromInstance_AbstractContract_ReturnsSameInstance()
        {
            using OnityContainer container = new OnityContainer();
            Foo foo = new Foo();
            container.BindInstance<IFoo>(foo);
            container.BindFactory<IFoo, IFooResolveFactory>();

            IFooResolveFactory factory = container.Resolve<IFooResolveFactory>();

            Assert.That(factory.Create(), Is.SameAs(foo));
        }

        // --- Zenject TestFactoryFromResolve0 ---

        // TestFactoryFromResolve0.TestSelf: factory pulls the value out of the container via resolve.
        [Test]
        public void Factory_FromResolve_ReturnsBoundInstance()
        {
            using OnityContainer container = new OnityContainer();
            Foo foo = new Foo();
            container.BindInstance(foo);
            container.BindFactory<Foo, FooResolveFactory>();

            FooResolveFactory factory = container.Resolve<FooResolveFactory>();

            Assert.That(factory.Create(), Is.SameAs(foo));
        }

        // --- Zenject TestFactoryFromMethod0 / TestFactoryFromMethod1 ---

        // TestFactoryFromMethod0.TestSelf: factory whose Create body builds the value directly.
        [Test]
        public void Factory_FromMethod_BuildsValue()
        {
            using OnityContainer container = new OnityContainer();
            container.BindFactory<Foo, FooNewFactory>();

            FooNewFactory factory = container.Resolve<FooNewFactory>();
            Foo first = factory.Create();
            Foo second = factory.Create();

            Assert.That(first, Is.Not.Null);
            Assert.That(first, Is.Not.SameAs(second));
        }

        // TestFactoryFromMethod1.TestSelf: one-parameter factory whose Create body builds the value
        // from the parameter directly.
        [Test]
        public void Factory_FromMethod_OneParam_BuildsValueFromParameter()
        {
            using OnityContainer container = new OnityContainer();
            container.BindFactory<string, ParamFoo, ParamFooNewFactory>();

            ParamFooNewFactory factory = container.Resolve<ParamFooNewFactory>();

            Assert.That(factory.Create("asdf").Value, Is.EqualTo("asdf"));
        }

        // --- Zenject TestFactoryFromGetter0 ---

        // TestFactoryFromGetter0.TestSelf: factory returns a property read off another resolved service.
        [Test]
        public void Factory_FromGetter_ReturnsValueFromResolvedService()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<Holder>().To<Holder>().AsSingle();
            container.BindFactory<Bar, BarGetterFactory>();

            BarGetterFactory factory = container.Resolve<BarGetterFactory>();
            Bar created = factory.Create();

            Assert.That(created, Is.Not.Null);
            Assert.That(created, Is.SameAs(container.Resolve<Holder>().Bar));
        }

        // --- Zenject TestFactoryFromFactory0 / TestFactoryFromFactory1 ---

        // TestFactoryFromFactory0.TestSelf: a factory backed by another (custom) IFactory.
        [Test]
        public void Factory_FromFactory_DelegatesToInnerFactory()
        {
            using OnityContainer container = new OnityContainer();
            container.BindFactory<Foo, DelegatingFooFactory>();

            DelegatingFooFactory factory = container.Resolve<DelegatingFooFactory>();
            Foo created = factory.Create();

            Assert.That(created, Is.Not.Null);
            Assert.That(created, Is.SameAs(DelegatingFooFactory.SharedFoo));
        }

        // TestFactoryFromFactory1.TestSelf: one-parameter factory backed by an inner factory.
        [Test]
        public void Factory_FromFactory_OneParam_DelegatesToInnerFactory()
        {
            using OnityContainer container = new OnityContainer();
            container.BindFactory<string, ParamFoo, DelegatingParamFooFactory>();

            DelegatingParamFooFactory factory = container.Resolve<DelegatingParamFooFactory>();

            Assert.That(factory.Create("asdf").Value, Is.EqualTo("asdf"));
        }

        // --- Helper types (all private nested) ---

        private interface IFoo
        {
        }

        private sealed class Foo : IFoo
        {
        }

        private sealed class FooTwo
        {
            public FooTwo(string value)
            {
                Value = value;
            }

            public string Value { get; }
        }

        private interface IParamFoo
        {
            string Value { get; }
        }

        private sealed class ParamFoo : IParamFoo
        {
            public ParamFoo(string value)
            {
                Value = value;
            }

            public string Value { get; }
        }

        private sealed class Bar
        {
        }

        private sealed class Holder
        {
            public Holder()
            {
                Bar = new Bar();
            }

            public Bar Bar { get; }
        }

        // No-parameter factory that asks the container for a new Foo each call.
        private sealed class FooFactory : IFactory<Foo>
        {
            private readonly IResolver m_resolver;

            public FooFactory(IResolver resolver)
            {
                m_resolver = resolver;
            }

            public Foo Create()
            {
                return m_resolver.Resolve<Foo>();
            }
        }

        // Factory typed to the IFoo contract; resolves the concrete Foo.
        private sealed class IFooFactory : IFactory<IFoo>
        {
            private readonly IResolver m_resolver;

            public IFooFactory(IResolver resolver)
            {
                m_resolver = resolver;
            }

            public IFoo Create()
            {
                return m_resolver.Resolve<Foo>();
            }
        }

        // Factory that resolves FooTwo (whose string dependency is unbound) -> Create throws.
        private sealed class FooTwoResolveFactory : IFactory<FooTwo>
        {
            private readonly IResolver m_resolver;

            public FooTwoResolveFactory(IResolver resolver)
            {
                m_resolver = resolver;
            }

            public FooTwo Create()
            {
                return m_resolver.Resolve<FooTwo>();
            }
        }

        // One-parameter factory that forwards the parameter into the value.
        private sealed class FooTwoFactory : IFactory<string, FooTwo>
        {
            public FooTwo Create(string param)
            {
                return new FooTwo(param);
            }
        }

        // One-parameter factory that forwards the parameter into a concrete ParamFoo.
        private sealed class ParamFooFactory : IFactory<string, ParamFoo>
        {
            public ParamFoo Create(string param)
            {
                return new ParamFoo(param);
            }
        }

        // One-parameter factory typed to IParamFoo, builds a concrete ParamFoo.
        private sealed class IParamFooFactory : IFactory<string, IParamFoo>
        {
            public IParamFoo Create(string param)
            {
                return new ParamFoo(param);
            }
        }

        // Factory that returns a container-resolved Foo instance (FromInstance/FromResolve intent).
        private sealed class FooResolveFactory : IFactory<Foo>
        {
            private readonly IResolver m_resolver;

            public FooResolveFactory(IResolver resolver)
            {
                m_resolver = resolver;
            }

            public Foo Create()
            {
                return m_resolver.Resolve<Foo>();
            }
        }

        // IFoo-typed factory that returns a container-resolved IFoo instance.
        private sealed class IFooResolveFactory : IFactory<IFoo>
        {
            private readonly IResolver m_resolver;

            public IFooResolveFactory(IResolver resolver)
            {
                m_resolver = resolver;
            }

            public IFoo Create()
            {
                return m_resolver.Resolve<IFoo>();
            }
        }

        // Factory whose body builds the value directly (FromMethod intent).
        private sealed class FooNewFactory : IFactory<Foo>
        {
            public Foo Create()
            {
                return new Foo();
            }
        }

        // One-parameter factory whose body builds the value directly (FromMethod intent).
        private sealed class ParamFooNewFactory : IFactory<string, ParamFoo>
        {
            public ParamFoo Create(string param)
            {
                return new ParamFoo(param);
            }
        }

        // Factory that returns a property read off another resolved service (FromGetter intent).
        private sealed class BarGetterFactory : IFactory<Bar>
        {
            private readonly IResolver m_resolver;

            public BarGetterFactory(IResolver resolver)
            {
                m_resolver = resolver;
            }

            public Bar Create()
            {
                return m_resolver.Resolve<Holder>().Bar;
            }
        }

        // Outer factory backed by an inner custom IFactory (FromIFactory intent).
        private sealed class DelegatingFooFactory : IFactory<Foo>
        {
            public static readonly Foo SharedFoo = new Foo();

            private readonly IFactory<Foo> m_inner;

            public DelegatingFooFactory()
            {
                m_inner = new InnerFooFactory();
            }

            public Foo Create()
            {
                return m_inner.Create();
            }

            private sealed class InnerFooFactory : IFactory<Foo>
            {
                public Foo Create()
                {
                    return SharedFoo;
                }
            }
        }

        // One-parameter outer factory backed by an inner custom IFactory (FromIFactory intent).
        private sealed class DelegatingParamFooFactory : IFactory<string, ParamFoo>
        {
            private readonly IFactory<string, ParamFoo> m_inner;

            public DelegatingParamFooFactory()
            {
                m_inner = new InnerParamFooFactory();
            }

            public ParamFoo Create(string param)
            {
                return m_inner.Create(param);
            }

            private sealed class InnerParamFooFactory : IFactory<string, ParamFoo>
            {
                public ParamFoo Create(string param)
                {
                    return new ParamFoo(param);
                }
            }
        }
    }
}
