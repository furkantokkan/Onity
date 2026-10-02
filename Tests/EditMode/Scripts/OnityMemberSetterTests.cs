using System.Collections.Generic;
using NUnit.Framework;
using Onity.DI;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Verifies that compiled member setters/invokers preserve identical field,
    /// property, method, base-class, and ordering injection behavior.
    /// </summary>
    [TestFixture]
    public sealed class OnityMemberSetterTests
    {
        [Test]
        public void Inject_FieldPropertyAndMethod_AllPopulatedWithSameInstance()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<IMemberDependency>().To<MemberDependency>().AsSingle();

            MemberInjectionTarget target = new MemberInjectionTarget();
            container.Inject(target);

            Assert.That(target.FieldDependency, Is.Not.Null);
            Assert.That(target.PropertyDependency, Is.Not.Null);
            Assert.That(target.MethodDependency, Is.Not.Null);
            Assert.That(target.PropertyDependency, Is.SameAs(target.FieldDependency));
            Assert.That(target.MethodDependency, Is.SameAs(target.FieldDependency));
        }

        [Test]
        public void Inject_PrivateSetterProperty_IsPopulated()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<IMemberDependency>().To<MemberDependency>().AsSingle();

            PrivateSetterTarget target = new PrivateSetterTarget();
            container.Inject(target);

            Assert.That(target.Dependency, Is.Not.Null);
        }

        [Test]
        public void Inject_BaseClassMembers_ArePopulated()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<IMemberDependency>().To<MemberDependency>().AsSingle();

            DerivedInjectionTarget target = new DerivedInjectionTarget();
            container.Inject(target);

            Assert.That(target.BaseFieldDependency, Is.Not.Null, "Base field must be injected.");
            Assert.That(target.BasePropertyDependency, Is.Not.Null, "Base property must be injected.");
            Assert.That(target.BaseMethodDependency, Is.Not.Null, "Base method must be invoked.");
            Assert.That(target.DerivedFieldDependency, Is.Not.Null, "Derived field must be injected.");
            Assert.That(target.DerivedMethodDependency, Is.Not.Null, "Derived method must be invoked.");
        }

        [Test]
        public void Inject_FieldAndProperty_RunBeforeMethod()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<IMemberDependency>().To<MemberDependency>().AsSingle();

            OrderingTarget target = new OrderingTarget();
            container.Inject(target);

            // The [Inject] method reads field + property; if members ran after the
            // method, the method would have observed nulls and recorded false.
            Assert.That(target.FieldWasSetBeforeMethod, Is.True);
            Assert.That(target.PropertyWasSetBeforeMethod, Is.True);
        }

        [Test]
        public void Inject_BaseMembers_RunBeforeDerivedMethod()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<IMemberDependency>().To<MemberDependency>().AsSingle();

            BaseBeforeDerivedTarget target = new BaseBeforeDerivedTarget();
            container.Inject(target);

            // Base members must be populated before the derived [Inject] method
            // runs, matching reflection-based hierarchy ordering.
            Assert.That(target.SawBaseFieldFromDerivedMethod, Is.True);
        }

        [Test]
        public void Inject_ParameterlessMethod_IsInvoked()
        {
            using OnityContainer container = new OnityContainer();

            ParameterlessMethodTarget target = new ParameterlessMethodTarget();
            container.Inject(target);

            Assert.That(target.WasInvoked, Is.True);
        }

        private interface IMemberDependency
        {
        }

        private sealed class MemberDependency : IMemberDependency
        {
        }

        private sealed class MemberInjectionTarget
        {
            [Inject]
            private IMemberDependency m_fieldDependency = null;

            [Inject]
            public IMemberDependency PropertyDependency { get; set; }

            public IMemberDependency FieldDependency => m_fieldDependency;

            public IMemberDependency MethodDependency { get; private set; }

            [Inject]
            private void Initialize(IMemberDependency dependency)
            {
                MethodDependency = dependency;
            }
        }

        private sealed class PrivateSetterTarget
        {
            [Inject]
            public IMemberDependency Dependency { get; private set; }
        }

        private class BaseInjectionTarget
        {
            [Inject]
            protected IMemberDependency m_baseField = null;

            [Inject]
            public IMemberDependency BasePropertyDependency { get; private set; }

            public IMemberDependency BaseFieldDependency => m_baseField;

            public IMemberDependency BaseMethodDependency { get; private set; }

            [Inject]
            private void InitializeBase(IMemberDependency dependency)
            {
                BaseMethodDependency = dependency;
            }
        }

        private sealed class DerivedInjectionTarget : BaseInjectionTarget
        {
            [Inject]
            private IMemberDependency m_derivedField = null;

            public IMemberDependency DerivedFieldDependency => m_derivedField;

            public IMemberDependency DerivedMethodDependency { get; private set; }

            [Inject]
            private void InitializeDerived(IMemberDependency dependency)
            {
                DerivedMethodDependency = dependency;
            }
        }

        private sealed class OrderingTarget
        {
            [Inject]
            private IMemberDependency m_field = null;

            [Inject]
            public IMemberDependency Property { get; set; }

            public bool FieldWasSetBeforeMethod { get; private set; }

            public bool PropertyWasSetBeforeMethod { get; private set; }

            [Inject]
            private void Initialize(IMemberDependency dependency)
            {
                FieldWasSetBeforeMethod = m_field != null;
                PropertyWasSetBeforeMethod = Property != null;
            }
        }

        private class BaseFieldHolder
        {
            [Inject]
            protected IMemberDependency m_baseField = null;
        }

        private sealed class BaseBeforeDerivedTarget : BaseFieldHolder
        {
            public bool SawBaseFieldFromDerivedMethod { get; private set; }

            [Inject]
            private void Initialize(IMemberDependency dependency)
            {
                SawBaseFieldFromDerivedMethod = m_baseField != null;
            }
        }

        private sealed class ParameterlessMethodTarget
        {
            public bool WasInvoked { get; private set; }

            [Inject]
            private void Initialize()
            {
                WasInvoked = true;
            }
        }
    }
}
