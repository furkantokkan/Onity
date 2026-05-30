using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;

namespace Onity.Analyzers.Tests
{
    /// <summary>
    /// Unit tests for the ONITY005 (invalid <c>[Inject]</c> member) and ONITY006
    /// (manual <c>new</c> of an Onity-managed type) analyzers. Each rule has
    /// positive cases (diagnostic expected at a precise span) and negative cases
    /// (no diagnostic), driven through the Roslyn analyzer test harness via
    /// <see cref="OnityNUnitVerifier"/>.
    /// </summary>
    [TestFixture]
    public sealed class OnityInjectMemberAndNewRulesTests
    {
        // ---------- ONITY005: invalid [Inject] member ----------

        [Test]
        public void Onity005_InjectPropertyWithoutSetter_Reports()
        {
            const string source = @"
using System;

[AttributeUsage(AttributeTargets.Property)]
sealed class InjectAttribute : Attribute { }

class Service
{
    [Inject]
    public int Value { get; }
}";

            DiagnosticResult expected = OnityAnalyzerVerifier<OnityInvalidInjectMemberAnalyzer>
                .Diagnostic(OnityDiagnostics.k_invalidInjectMemberId)
                .WithSpan(10, 16, 10, 21)
                .WithArguments(
                    "property",
                    "Value",
                    "has no setter, so injection throws OnityBindingException at resolve. Add a (private) set accessor, or move [Inject] to a backing field or an Initialize method.");

            OnityAnalyzerVerifier<OnityInvalidInjectMemberAnalyzer>.Verify(source, expected);
        }

        [Test]
        public void Onity005_InjectExpressionBodiedProperty_Reports()
        {
            const string source = @"
using System;

[AttributeUsage(AttributeTargets.Property)]
sealed class InjectAttribute : Attribute { }

class Service
{
    [Inject]
    public int Value => 0;
}";

            DiagnosticResult expected = OnityAnalyzerVerifier<OnityInvalidInjectMemberAnalyzer>
                .Diagnostic(OnityDiagnostics.k_invalidInjectMemberId)
                .WithSpan(10, 16, 10, 21)
                .WithArguments(
                    "property",
                    "Value",
                    "has no setter, so injection throws OnityBindingException at resolve. Add a (private) set accessor, or move [Inject] to a backing field or an Initialize method.");

            OnityAnalyzerVerifier<OnityInvalidInjectMemberAnalyzer>.Verify(source, expected);
        }

        [Test]
        public void Onity005_InjectIndexer_Reports()
        {
            const string source = @"
using System;

[AttributeUsage(AttributeTargets.Property)]
sealed class InjectAttribute : Attribute { }

class Service
{
    [Inject]
    public int this[int index] { get { return 0; } set { } }
}";

            DiagnosticResult expected = OnityAnalyzerVerifier<OnityInvalidInjectMemberAnalyzer>
                .Diagnostic(OnityDiagnostics.k_invalidInjectMemberId)
                .WithSpan(10, 16, 10, 20)
                .WithArguments(
                    "property",
                    "this[]",
                    "is an indexer, so injection throws OnityBindingException at resolve. Inject into a non-indexed property, a field, or a method parameter instead.");

            OnityAnalyzerVerifier<OnityInvalidInjectMemberAnalyzer>.Verify(source, expected);
        }

        [Test]
        public void Onity005_InjectGenericMethod_Reports()
        {
            const string source = @"
using System;

[AttributeUsage(AttributeTargets.Method)]
sealed class InjectAttribute : Attribute { }

class Service
{
    [Inject]
    public void Initialize<T>(T value) { }
}";

            DiagnosticResult expected = OnityAnalyzerVerifier<OnityInvalidInjectMemberAnalyzer>
                .Diagnostic(OnityDiagnostics.k_invalidInjectMemberId)
                .WithSpan(10, 17, 10, 27)
                .WithArguments(
                    "method",
                    "Initialize",
                    "is generic, so injection throws OnityBindingException at resolve. Use a non-generic [Inject] method whose parameter types are concrete and resolvable.");

            OnityAnalyzerVerifier<OnityInvalidInjectMemberAnalyzer>.Verify(source, expected);
        }

        [Test]
        public void Onity005_InjectStaticField_Reports()
        {
            const string source = @"
using System;

[AttributeUsage(AttributeTargets.Field)]
sealed class InjectAttribute : Attribute { }

class Service
{
    [Inject]
    static object s_dependency;
}";

            DiagnosticResult expected = OnityAnalyzerVerifier<OnityInvalidInjectMemberAnalyzer>
                .Diagnostic(OnityDiagnostics.k_invalidInjectMemberId)
                .WithSpan(10, 19, 10, 31)
                .WithArguments(
                    "field",
                    "s_dependency",
                    "is static, so it is never injected; only instance members are scanned. Make it an instance field.");

            OnityAnalyzerVerifier<OnityInvalidInjectMemberAnalyzer>.Verify(source, expected);
        }

        [Test]
        public void Onity005_InjectStaticMethod_Reports()
        {
            const string source = @"
using System;

[AttributeUsage(AttributeTargets.Method)]
sealed class InjectAttribute : Attribute { }

class Service
{
    [Inject]
    static void Initialize(object value) { }
}";

            DiagnosticResult expected = OnityAnalyzerVerifier<OnityInvalidInjectMemberAnalyzer>
                .Diagnostic(OnityDiagnostics.k_invalidInjectMemberId)
                .WithSpan(10, 17, 10, 27)
                .WithArguments(
                    "method",
                    "Initialize",
                    "is static, so it is never invoked for injection; only instance members are scanned. Make it an instance method.");

            OnityAnalyzerVerifier<OnityInvalidInjectMemberAnalyzer>.Verify(source, expected);
        }

        [Test]
        public void Onity005_ValidInjectMembers_NoDiagnostic()
        {
            const string source = @"
using System;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
sealed class InjectAttribute : Attribute { }

class Service
{
    [Inject]
    object m_field;

    [Inject]
    public object Property { get; private set; }

    [Inject]
    public object PublicProperty { get; set; }

    [Inject]
    public void Initialize(object value) { }
}";

            OnityAnalyzerVerifier<OnityInvalidInjectMemberAnalyzer>.Verify(source);
        }

        [Test]
        public void Onity005_NonInjectInvalidMembers_NoDiagnostic()
        {
            const string source = @"
using System;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
sealed class InjectAttribute : Attribute { }

class Service
{
    static object s_field;

    public int Value { get; }

    public void Generic<T>(T value) { }
}";

            OnityAnalyzerVerifier<OnityInvalidInjectMemberAnalyzer>.Verify(source);
        }

        // ---------- ONITY006: new on an Onity-managed type ----------

        [Test]
        public void Onity006_NewOnBoundType_Reports()
        {
            const string source = @"
class Service { }

class Installer
{
    void Configure(Container c)
    {
        c.Bind<Service>();
        var s = new Service();
    }
}

class Container
{
    public void Bind<T>() { }
}";

            DiagnosticResult expected = OnityAnalyzerVerifier<OnityNewOnInjectableAnalyzer>
                .Diagnostic(OnityDiagnostics.k_newOnInjectableId)
                .WithSpan(9, 17, 9, 30)
                .WithArguments("Service");

            OnityAnalyzerVerifier<OnityNewOnInjectableAnalyzer>.Verify(source, expected);
        }

        [Test]
        public void Onity006_NewOnResolvedType_Reports()
        {
            const string source = @"
class Service { }

class Consumer
{
    void Use(Container c)
    {
        var resolved = c.Resolve<Service>();
        var manual = new Service();
    }
}

class Container
{
    public T Resolve<T>() { return default; }
}";

            DiagnosticResult expected = OnityAnalyzerVerifier<OnityNewOnInjectableAnalyzer>
                .Diagnostic(OnityDiagnostics.k_newOnInjectableId)
                .WithSpan(9, 22, 9, 35)
                .WithArguments("Service");

            OnityAnalyzerVerifier<OnityNewOnInjectableAnalyzer>.Verify(source, expected);
        }

        [Test]
        public void Onity006_NewWithoutBindOrResolve_NoDiagnostic()
        {
            const string source = @"
class Service { }

class Consumer
{
    void Use()
    {
        var s = new Service();
    }
}";

            OnityAnalyzerVerifier<OnityNewOnInjectableAnalyzer>.Verify(source);
        }

        [Test]
        public void Onity006_BindWithoutNew_NoDiagnostic()
        {
            const string source = @"
class Service { }

class Installer
{
    void Configure(Container c)
    {
        c.Bind<Service>();
    }
}

class Container
{
    public void Bind<T>() { }
}";

            OnityAnalyzerVerifier<OnityNewOnInjectableAnalyzer>.Verify(source);
        }

        [Test]
        public void Onity006_NewOfDifferentType_NoDiagnostic()
        {
            const string source = @"
class Service { }
class Other { }

class Installer
{
    void Configure(Container c)
    {
        c.Bind<Service>();
        var o = new Other();
    }
}

class Container
{
    public void Bind<T>() { }
}";

            OnityAnalyzerVerifier<OnityNewOnInjectableAnalyzer>.Verify(source);
        }
    }
}
