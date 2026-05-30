using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;

namespace Onity.Analyzers.Tests
{
    /// <summary>
    /// Unit tests for the ONITY002, ONITY003, and ONITY004 analyzers. Each rule has
    /// at least one positive case (diagnostic expected at a precise span) and one
    /// negative case (no diagnostic), driven through the Roslyn analyzer test
    /// harness via <see cref="OnityNUnitVerifier"/>.
    /// </summary>
    [TestFixture]
    public sealed class OnityAnalyzerRulesTests
    {
        // ---------- ONITY002: register after Build() ----------

        [Test]
        public void Onity002_BindAfterBuildOnSameLocal_Reports()
        {
            const string source = @"
class C
{
    void Configure()
    {
        var container = new Container();
        container.Build();
        container.Bind();
    }
}

class Container
{
    public void Build() { }
    public void Bind() { }
    public void BindInstance(object o) { }
    public void Resolve() { }
}";

            DiagnosticResult expected = OnityAnalyzerVerifier<OnityRegisterAfterBuildAnalyzer>
                .Diagnostic(OnityDiagnostics.k_registerAfterBuildId)
                .WithSpan(8, 9, 8, 25)
                .WithArguments("container", "Bind");

            OnityAnalyzerVerifier<OnityRegisterAfterBuildAnalyzer>.Verify(source, expected);
        }

        [Test]
        public void Onity002_ResolveAfterBuildOnSameLocal_Reports()
        {
            const string source = @"
class C
{
    void Configure()
    {
        var container = new Container();
        container.Build();
        container.Resolve();
    }
}

class Container
{
    public void Build() { }
    public void Bind() { }
    public void Resolve() { }
}";

            DiagnosticResult expected = OnityAnalyzerVerifier<OnityRegisterAfterBuildAnalyzer>
                .Diagnostic(OnityDiagnostics.k_registerAfterBuildId)
                .WithSpan(8, 9, 8, 28)
                .WithArguments("container", "Resolve");

            OnityAnalyzerVerifier<OnityRegisterAfterBuildAnalyzer>.Verify(source, expected);
        }

        [Test]
        public void Onity002_BindBeforeBuild_NoDiagnostic()
        {
            const string source = @"
class C
{
    void Configure()
    {
        var container = new Container();
        container.Bind();
        container.Build();
    }
}

class Container
{
    public void Build() { }
    public void Bind() { }
}";

            OnityAnalyzerVerifier<OnityRegisterAfterBuildAnalyzer>.Verify(source);
        }

        [Test]
        public void Onity002_BuildAndBindOnDifferentLocals_NoDiagnostic()
        {
            const string source = @"
class C
{
    void Configure()
    {
        var a = new Container();
        var b = new Container();
        a.Build();
        b.Bind();
    }
}

class Container
{
    public void Build() { }
    public void Bind() { }
}";

            OnityAnalyzerVerifier<OnityRegisterAfterBuildAnalyzer>.Verify(source);
        }

        // ---------- ONITY003: Subscribe without AddTo ----------

        [Test]
        public void Onity003_SubscribeResultDiscarded_Reports()
        {
            const string source = @"
using System;

class C
{
    void Setup(Source s)
    {
        s.Subscribe(_ => { });
    }
}

class Source
{
    public IDisposable Subscribe(Action<int> onNext) { return null; }
}";

            DiagnosticResult expected = OnityAnalyzerVerifier<OnitySubscribeWithoutAddToAnalyzer>
                .Diagnostic(OnityDiagnostics.k_subscribeWithoutAddToId)
                .WithSpan(8, 9, 8, 30);

            OnityAnalyzerVerifier<OnitySubscribeWithoutAddToAnalyzer>.Verify(source, expected);
        }

        [Test]
        public void Onity003_SubscribeChainedToAddTo_NoDiagnostic()
        {
            const string source = @"
using System;

class C
{
    void Setup(Source s, Bag bag)
    {
        s.Subscribe(_ => { }).AddTo(bag);
    }
}

class Source
{
    public IDisposable Subscribe(Action<int> onNext) { return new Bag(); }
}

class Bag : IDisposable
{
    public void Dispose() { }
}

static class Ext
{
    public static IDisposable AddTo(this IDisposable d, Bag bag) { return d; }
}";

            OnityAnalyzerVerifier<OnitySubscribeWithoutAddToAnalyzer>.Verify(source);
        }

        [Test]
        public void Onity003_SubscribeResultAssigned_NoDiagnostic()
        {
            const string source = @"
using System;

class C
{
    void Setup(Source s)
    {
        IDisposable handle = s.Subscribe(_ => { });
        handle.Dispose();
    }
}

class Source
{
    public IDisposable Subscribe(Action<int> onNext) { return null; }
}";

            OnityAnalyzerVerifier<OnitySubscribeWithoutAddToAnalyzer>.Verify(source);
        }

        [Test]
        public void Onity003_SubscribeResultReturned_NoDiagnostic()
        {
            const string source = @"
using System;

class C
{
    IDisposable Setup(Source s)
    {
        return s.Subscribe(_ => { });
    }
}

class Source
{
    public IDisposable Subscribe(Action<int> onNext) { return null; }
}";

            OnityAnalyzerVerifier<OnitySubscribeWithoutAddToAnalyzer>.Verify(source);
        }

        // ---------- ONITY004: multiple [Inject] constructors ----------

        [Test]
        public void Onity004_TwoInjectConstructors_Reports()
        {
            const string source = @"
using System;

[AttributeUsage(AttributeTargets.Constructor)]
sealed class InjectAttribute : Attribute { }

class Service
{
    [Inject]
    public Service() { }

    [Inject]
    public Service(int value) { }
}";

            DiagnosticResult expected = OnityAnalyzerVerifier<OnityMultipleInjectConstructorsAnalyzer>
                .Diagnostic(OnityDiagnostics.k_multipleInjectConstructorsId)
                .WithSpan(7, 7, 7, 14)
                .WithArguments("Service", 2);

            OnityAnalyzerVerifier<OnityMultipleInjectConstructorsAnalyzer>.Verify(source, expected);
        }

        [Test]
        public void Onity004_SingleInjectConstructor_NoDiagnostic()
        {
            const string source = @"
using System;

[AttributeUsage(AttributeTargets.Constructor)]
sealed class InjectAttribute : Attribute { }

class Service
{
    [Inject]
    public Service() { }

    public Service(int value) { }
}";

            OnityAnalyzerVerifier<OnityMultipleInjectConstructorsAnalyzer>.Verify(source);
        }

        [Test]
        public void Onity004_TwoConstructorsOnlyOneInjected_NoDiagnostic()
        {
            const string source = @"
using System;

[AttributeUsage(AttributeTargets.Constructor)]
sealed class InjectAttribute : Attribute { }

class Service
{
    public Service() { }

    [Inject]
    public Service(int value) { }
}";

            OnityAnalyzerVerifier<OnityMultipleInjectConstructorsAnalyzer>.Verify(source);
        }
    }
}
