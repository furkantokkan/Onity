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
        public void Onity002_ResolveAfterBuildOnSameLocal_NoDiagnostic()
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

            OnityAnalyzerVerifier<OnityRegisterAfterBuildAnalyzer>.Verify(source);
        }

        [Test]
        public void Onity002_GenericResolveAfterBuild_NoDiagnostic()
        {
            const string source = @"
class C
{
    void Configure()
    {
        var container = new Container();
        container.Build();
        container.Resolve<int>();
    }
}

class Container
{
    public void Build() { }
    public T Resolve<T>() { return default(T); }
}";

            OnityAnalyzerVerifier<OnityRegisterAfterBuildAnalyzer>.Verify(source);
        }

        [Test]
        public void Onity002_BindInstanceAfterBuild_Reports()
        {
            const string source = @"
class C
{
    void Configure()
    {
        var container = new Container();
        container.Build();
        container.BindInstance(1);
    }
}

class Container
{
    public void Build() { }
    public void BindInstance(object value) { }
}";

            DiagnosticResult expected = OnityAnalyzerVerifier<OnityRegisterAfterBuildAnalyzer>
                .Diagnostic(OnityDiagnostics.k_registerAfterBuildId)
                .WithSpan(8, 9, 8, 34)
                .WithArguments("container", "BindInstance");
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

        [Test]
        public void Onity003_VoidSubscribeWithCancellationToken_NoDiagnostic()
        {
            const string source = @"
using System;
using System.Threading;

class C
{
    void Setup(Source s, CancellationToken token)
    {
        s.Subscribe(_ => { }, token);
    }
}

class Source
{
    public void Subscribe(Action<int> onNext, CancellationToken token) { }
}";

            OnityAnalyzerVerifier<OnitySubscribeWithoutAddToAnalyzer>.Verify(source);
        }

        [Test]
        public void Onity003_VoidSubscribeAwaitWithCancellationToken_NoDiagnostic()
        {
            const string source = @"
using System;
using System.Threading;
using System.Threading.Tasks;

class C
{
    void Setup(Source s, CancellationToken token)
    {
        s.SubscribeAwait(_ => Task.CompletedTask, token);
    }
}

class Source
{
    public void SubscribeAwait(Func<int, Task> onNext, CancellationToken token) { }
}";

            OnityAnalyzerVerifier<OnitySubscribeWithoutAddToAnalyzer>.Verify(source);
        }

        [Test]
        public void Onity003_StreamConsumerOverloads_ReportsOnlyTheDiscardedDisposable()
        {
            // Mirrors the async-stream consumer API: an IDisposable overload plus a
            // void overload whose lifetime is owned by the CancellationToken.
            const string source = @"
using System;
using System.Threading;

class C
{
    void Setup(Stream<int> s, Bag bag, CancellationToken token)
    {
        s.Subscribe(_ => { });
        s.Subscribe(_ => { }, token);
        IDisposable kept = s.Subscribe(_ => { });
        s.Subscribe(_ => { }).AddTo(bag);
        kept.Dispose();
    }
}

class Stream<T> { }

class Bag : IDisposable
{
    public void Dispose() { }
}

static class StreamExtensions
{
    public static IDisposable Subscribe<T>(this Stream<T> source, Action<T> onNext) { return null; }

    public static void Subscribe<T>(this Stream<T> source, Action<T> onNext, CancellationToken cancellationToken) { }

    public static IDisposable AddTo(this IDisposable d, Bag bag) { return d; }
}";

            DiagnosticResult expected = OnityAnalyzerVerifier<OnitySubscribeWithoutAddToAnalyzer>
                .Diagnostic(OnityDiagnostics.k_subscribeWithoutAddToId)
                .WithSpan(9, 9, 9, 30)
                .WithArguments("Subscribe");

            OnityAnalyzerVerifier<OnitySubscribeWithoutAddToAnalyzer>.Verify(source, expected);
        }

        [Test]
        public void Onity003_SubscribeAwaitResultDiscarded_Reports()
        {
            const string source = @"
using System;
using System.Threading.Tasks;

class C
{
    void Setup(Source s)
    {
        s.SubscribeAwait(_ => Task.CompletedTask);
    }
}

class Source
{
    public IDisposable SubscribeAwait(Func<int, Task> onNext) { return null; }
}";

            DiagnosticResult expected = OnityAnalyzerVerifier<OnitySubscribeWithoutAddToAnalyzer>
                .Diagnostic(OnityDiagnostics.k_subscribeWithoutAddToId)
                .WithSpan(9, 9, 9, 50)
                .WithArguments("SubscribeAwait");

            OnityAnalyzerVerifier<OnitySubscribeWithoutAddToAnalyzer>.Verify(source, expected);
        }

        [Test]
        public void Onity003_SubscribeReturningDisposableImplementation_Reports()
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
    public Subscription Subscribe(Action<int> onNext) { return null; }
}

class Subscription : IDisposable
{
    public void Dispose() { }
}";

            DiagnosticResult expected = OnityAnalyzerVerifier<OnitySubscribeWithoutAddToAnalyzer>
                .Diagnostic(OnityDiagnostics.k_subscribeWithoutAddToId)
                .WithSpan(8, 9, 8, 30)
                .WithArguments("Subscribe");

            OnityAnalyzerVerifier<OnitySubscribeWithoutAddToAnalyzer>.Verify(source, expected);
        }

        [Test]
        public void Onity003_SubscribeReturningDisposableInterface_Reports()
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
    public ISubscription Subscribe(Action<int> onNext) { return null; }
}

interface ISubscription : IDisposable { }";

            DiagnosticResult expected = OnityAnalyzerVerifier<OnitySubscribeWithoutAddToAnalyzer>
                .Diagnostic(OnityDiagnostics.k_subscribeWithoutAddToId)
                .WithSpan(8, 9, 8, 30)
                .WithArguments("Subscribe");

            OnityAnalyzerVerifier<OnitySubscribeWithoutAddToAnalyzer>.Verify(source, expected);
        }

        [Test]
        public void Onity003_SubscribeReturningNonDisposable_NoDiagnostic()
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
    public int Subscribe(Action<int> onNext) { return 0; }
}";

            OnityAnalyzerVerifier<OnitySubscribeWithoutAddToAnalyzer>.Verify(source);
        }

        [Test]
        public void Onity003_SubscribeResultDiscardedWithUnderscore_NoDiagnostic()
        {
            // An explicit discard is a deliberate choice and is not an expression statement.
            const string source = @"
using System;

class C
{
    void Setup(Source s)
    {
        _ = s.Subscribe(value => { });
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
