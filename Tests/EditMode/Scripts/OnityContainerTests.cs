using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.DI;
using Onity.Factory;
using Onity.Unity.Installers;
using UnityEngine;

namespace Onity.Tests.EditMode
{
    [TestFixture]
    public sealed class OnityContainerTests
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

        [Test]
        public void SingletonBinding_ResolveReturnsSameInstance()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<ITestService>().To<TestService>().AsSingle();

            ITestService first = container.Resolve<ITestService>();
            ITestService second = container.Resolve<ITestService>();

            Assert.That(first, Is.SameAs(second));
        }

        [Test]
        public void TransientBinding_ResolveReturnsDifferentInstances()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<ITestService>().To<TestService>().AsTransient();

            ITestService first = container.Resolve<ITestService>();
            ITestService second = container.Resolve<ITestService>();

            Assert.That(first, Is.Not.SameAs(second));
        }

        [Test]
        public void BindInterfacesAndSelfTo_BindsBothInterfaceAndConcrete()
        {
            using OnityContainer container = new OnityContainer();
            container.BindInterfacesAndSelfTo<TestService>().AsSingle();

            TestService concrete = container.Resolve<TestService>();
            ITestService contract = container.Resolve<ITestService>();

            Assert.That(concrete, Is.SameAs(contract));
        }

        [Test]
        public void BindInterfacesTo_BindsOnlyInterfaces()
        {
            using OnityContainer container = new OnityContainer();
            container.BindInterfacesTo<InterfaceOnlyService>().AsSingle();

            ITestService primary = container.Resolve<ITestService>();
            IAdditionalService additional = container.Resolve<IAdditionalService>();
            bool canResolveConcrete = container.TryResolve(out InterfaceOnlyService concrete);

            Assert.That(primary, Is.Not.Null);
            Assert.That(additional, Is.Not.Null);
            Assert.That(primary, Is.SameAs(additional));
            Assert.That(canResolveConcrete, Is.True);
            Assert.That(concrete, Is.Not.Null);
            Assert.That(primary, Is.Not.SameAs(concrete));
        }

        [Test]
        public void BindInterfacesTo_TypeWithoutInterfaces_Throws()
        {
            using OnityContainer container = new OnityContainer();

            Assert.That(
                () => container.BindInterfacesTo<NoInterfaceService>(),
                Throws.TypeOf<OnityBindingException>());
        }

        [Test]
        public void TryResolve_UnboundInterface_ReturnsFalse()
        {
            using OnityContainer container = new OnityContainer();
            bool isResolved = container.TryResolve(out ITestService service);

            Assert.That(isResolved, Is.False);
            Assert.That(service, Is.Null);
        }

        [Test]
        public void Inject_PopulatesFieldPropertyAndMethodMembers()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<ITestService>().To<TestService>().AsSingle();

            InjectionTarget target = new InjectionTarget();
            container.Inject(target);

            Assert.That(target.FieldService, Is.Not.Null);
            Assert.That(target.PropertyService, Is.Not.Null);
            Assert.That(target.MethodService, Is.Not.Null);
            Assert.That(target.FieldService, Is.SameAs(target.PropertyService));
            Assert.That(target.PropertyService, Is.SameAs(target.MethodService));
        }

        [Test]
        public void ChildContainer_ResolvesMissingServiceFromParent()
        {
            using OnityContainer parent = new OnityContainer();
            parent.Bind<ITestService>().To<TestService>().AsSingle();

            using OnityContainer child = new OnityContainer(parent);
            ITestService resolved = child.Resolve<ITestService>();
            ITestService parentResolved = parent.Resolve<ITestService>();

            Assert.That(resolved, Is.SameAs(parentResolved));
        }

        [Test]
        public void PushBindingSource_RegistersSourceForExplicitBinding()
        {
            using OnityContainer container = new OnityContainer();

            using (container.PushBindingSource("SceneInstaller/TestService"))
            {
                container.Bind<ITestService>().To<TestService>().AsSingle();
            }

            bool found = container.TryGetBindingSource(typeof(ITestService), out OnityBindingSourceInfo sourceInfo);

            Assert.That(found, Is.True);
            Assert.That(sourceInfo.ContractType, Is.EqualTo(typeof(ITestService)));
            Assert.That(sourceInfo.ImplementationType, Is.EqualTo(typeof(TestService)));
            Assert.That(sourceInfo.IsImplicitRegistration, Is.False);
            Assert.That(sourceInfo.ScopeDepth, Is.EqualTo(0));
            Assert.That(sourceInfo.SourceName, Is.EqualTo("SceneInstaller/TestService"));
        }

        [Test]
        public void TryGetBindingSource_ChildScopeReportsParentDepth()
        {
            using OnityContainer parent = new OnityContainer();

            using (parent.PushBindingSource("ProjectInstaller/SharedService"))
            {
                parent.Bind<ITestService>().To<TestService>().AsSingle();
            }

            using OnityContainer child = new OnityContainer(parent);
            bool found = child.TryGetBindingSource(typeof(ITestService), out OnityBindingSourceInfo sourceInfo);

            Assert.That(found, Is.True);
            Assert.That(sourceInfo.ScopeDepth, Is.EqualTo(1));
            Assert.That(sourceInfo.SourceName, Is.EqualTo("ProjectInstaller/SharedService"));
        }

        [Test]
        public void CanResolve_ReportsResolvableAndMissingTypesWithoutInstantiation()
        {
            using OnityContainer container = new OnityContainer();
            bool missingInterface = container.CanResolve(typeof(ITestService));
            bool concreteFallback = container.CanResolve(typeof(NoInterfaceService));

            container.Bind<ITestService>().To<TestService>().AsSingle();

            bool boundInterface = container.CanResolve(typeof(ITestService));
            bool builtInContainer = container.CanResolve(typeof(OnityContainer));
            bool builtInResolver = container.CanResolve(typeof(IResolver));

            Assert.That(missingInterface, Is.False);
            Assert.That(concreteFallback, Is.True);
            Assert.That(boundInterface, Is.True);
            Assert.That(builtInContainer, Is.True);
            Assert.That(builtInResolver, Is.True);
        }

        [Test]
        public void ImplicitAutoResolve_RegistersImplicitBindingSource()
        {
            using OnityContainer container = new OnityContainer();
            _ = container.Resolve<NoInterfaceService>();

            bool found = container.TryGetBindingSource(typeof(NoInterfaceService), out OnityBindingSourceInfo sourceInfo);

            Assert.That(found, Is.True);
            Assert.That(sourceInfo.IsImplicitRegistration, Is.True);
            Assert.That(sourceInfo.SourceName, Does.Contain("Implicit"));
        }

        [Test]
        public void Resolve_UsesInjectAttributedConstructor()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<ITestService>().To<TestService>().AsSingle();
            container.Bind<ConstructorTarget>().AsTransient();

            ConstructorTarget target = container.Resolve<ConstructorTarget>();

            Assert.That(target.UsedInjectConstructor, Is.True);
            Assert.That(target.Service, Is.Not.Null);
        }

        [Test]
        public void BindScriptableObject_InjectsAndBindsInstance()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<ITestService>().To<TestService>().AsSingle();

            TestConfigAsset asset = ScriptableObject.CreateInstance<TestConfigAsset>();

            try
            {
                container.BindScriptableObject<TestConfigAsset>(asset);
                TestConfigAsset resolved = container.Resolve<TestConfigAsset>();

                Assert.That(resolved, Is.SameAs(asset));
                Assert.That(asset.FieldService, Is.Not.Null);
                Assert.That(asset.MethodService, Is.Not.Null);
                Assert.That(asset.FieldService, Is.SameAs(asset.MethodService));
            }
            finally
            {
                ScriptableObject.DestroyImmediate(asset);
            }
        }

        [Test]
        public void Build_ExecutesRegisteredCallbacksOnce()
        {
            using OnityContainer container = new OnityContainer();
            int callbackCount = 0;

            container.RegisterBuildCallback(_ => callbackCount++);
            container.Build();
            container.Build();

            Assert.That(callbackCount, Is.EqualTo(1));
        }

        [Test]
        public async Task BuildAsync_ExecutesAsyncCallbacksOnce()
        {
            using OnityContainer container = new OnityContainer();
            int callbackCount = 0;

            container.RegisterBuildCallbackAsync(
                async _ =>
                {
                    await Task.Yield();
                    callbackCount++;
                });

            await container.BuildAsync();
            await container.BuildAsync();

            Assert.That(callbackCount, Is.EqualTo(1));
        }

        [Test]
        public void BuildAsync_CanceledToken_ThrowsOperationCanceledException()
        {
            using OnityContainer container = new OnityContainer();
            container.RegisterBuildCallbackAsync(
                async (_, cancellationToken) =>
                {
                    await Task.Delay(1, cancellationToken);
                });

            using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            cancellationTokenSource.Cancel();

            try
            {
                container.BuildAsync(cancellationTokenSource.Token).GetAwaiter().GetResult();
                Assert.Fail("Expected OperationCanceledException.");
            }
            catch (OperationCanceledException)
            {
            }
        }

        [Test]
        public void NonLazyBinding_Build_ResolvesImmediately()
        {
            NonLazyService.CtorCalls = 0;
            using OnityContainer container = new OnityContainer();
            container.Bind<INonLazyService>().To<NonLazyService>().AsSingle().NonLazy();

            container.Build();
            container.Build();

            Assert.That(NonLazyService.CtorCalls, Is.EqualTo(1));
        }

        [Test]
        public void NonLazy_BeforeBinding_Throws()
        {
            using OnityContainer container = new OnityContainer();
            TypeBindingBuilder<INonLazyService> builder = container.Bind<INonLazyService>().To<NonLazyService>();

            Assert.That(
                () => builder.NonLazy(),
                Throws.TypeOf<OnityBindingException>());
        }

        [Test]
        public void BindFactory_OneParameter_BindsFactoryInterface()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<IPrefixFormatter>().To<PrefixFormatter>().AsSingle();
            container.BindFactory<string, FactoryValue, PrefixFactory>();

            IFactory<string, FactoryValue> factory = container.Resolve<IFactory<string, FactoryValue>>();
            FactoryValue value = factory.Create("score");

            Assert.That(value.Content, Is.EqualTo("fmt:score"));
        }

        [Test]
        public void BindFactory_TwoParameters_BindsFactoryInterface()
        {
            using OnityContainer container = new OnityContainer();
            container.BindFactory<int, int, FactoryValue, SumFactory>();

            IFactory<int, int, FactoryValue> factory = container.Resolve<IFactory<int, int, FactoryValue>>();
            FactoryValue value = factory.Create(7, 4);

            Assert.That(value.Content, Is.EqualTo("11"));
        }

        [Test]
        public void RegisterBuildCallback_AfterBuild_Throws()
        {
            using OnityContainer container = new OnityContainer();
            container.Build();

            Assert.That(
                () => container.RegisterBuildCallback(_ => { }),
                Throws.TypeOf<OnityBindingException>());
        }

        [Test]
        public void RegisterBuildCallbackAsync_AfterBuild_Throws()
        {
            using OnityContainer container = new OnityContainer();
            container.Build();

            Assert.That(
                () => container.RegisterBuildCallbackAsync(_ => Task.CompletedTask),
                Throws.TypeOf<OnityBindingException>());
        }

        [Test]
        public void GetBindingDiagnostics_ContainsExplicitAndImplicitRows()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<ITestService>().To<TestService>().AsSingle();

            _ = container.Resolve<ITestService>();
            _ = container.Resolve<NoInterfaceService>();

            List<OnityBindingDiagnostics> rows = new List<OnityBindingDiagnostics>();
            container.GetBindingDiagnostics(rows);

            OnityBindingDiagnostics explicitRow = default;
            OnityBindingDiagnostics implicitRow = default;
            bool hasExplicit = false;
            bool hasImplicit = false;

            for (int i = 0; i < rows.Count; i++)
            {
                OnityBindingDiagnostics row = rows[i];

                if (row.ImplementationType == typeof(TestService))
                {
                    explicitRow = row;
                    hasExplicit = true;
                }

                if (row.ImplementationType == typeof(NoInterfaceService))
                {
                    implicitRow = row;
                    hasImplicit = true;
                }
            }

            Assert.That(hasExplicit, Is.True);
            Assert.That(explicitRow.IsImplicitRegistration, Is.False);
            Assert.That(explicitRow.Lifetime, Is.EqualTo("Singleton"));
            Assert.That(explicitRow.ContractTypes, Does.Contain(typeof(ITestService)));

            Assert.That(hasImplicit, Is.True);
            Assert.That(implicitRow.IsImplicitRegistration, Is.True);
            Assert.That(implicitRow.Lifetime, Is.EqualTo("Transient"));
            Assert.That(implicitRow.ContractTypes, Does.Contain(typeof(NoInterfaceService)));
        }

        [Test]
        public void GetBindingDiagnostics_CollectsResolveMetrics_WhenEnabled()
        {
            OnityContainer.DiagnosticsCollectionEnabled = true;

            using OnityContainer container = new OnityContainer();
            container.Bind<ITestService>().To<TestService>().AsSingle();

            _ = container.Resolve<ITestService>();
            _ = container.Resolve<ITestService>();
            _ = container.Resolve<ITestService>();

            List<OnityBindingDiagnostics> rows = new List<OnityBindingDiagnostics>();
            container.GetBindingDiagnostics(rows);

            bool found = false;

            for (int i = 0; i < rows.Count; i++)
            {
                OnityBindingDiagnostics row = rows[i];

                if (row.ImplementationType != typeof(TestService))
                {
                    continue;
                }

                found = true;
                Assert.That(row.ResolveCount, Is.GreaterThanOrEqualTo(3));
                Assert.That(row.AverageResolveMilliseconds, Is.GreaterThanOrEqualTo(0d));
                Assert.That(row.LastResolveMilliseconds, Is.GreaterThanOrEqualTo(0d));
                break;
            }

            Assert.That(found, Is.True);
        }

        [Test]
        public void OnitySources_DoNotUseSystemLinq()
        {
            string[] sourceRoots = GetSourceRoots();

            for (int rootIndex = 0; rootIndex < sourceRoots.Length; rootIndex++)
            {
                string sourceRoot = sourceRoots[rootIndex];

                if (Directory.Exists(sourceRoot) == false)
                {
                    continue;
                }

                string[] sourceFiles = Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories);

                for (int fileIndex = 0; fileIndex < sourceFiles.Length; fileIndex++)
                {
                    string filePath = sourceFiles[fileIndex];
                    string[] lines = File.ReadAllLines(filePath);

                    for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
                    {
                        string trimmedLine = lines[lineIndex].Trim();

                        if (trimmedLine.StartsWith("using ", StringComparison.Ordinal) == false)
                        {
                            continue;
                        }

                        if (UsesBannedSystemLinqNamespace(trimmedLine) == false)
                        {
                            continue;
                        }

                        Assert.Fail(
                            $"Source should avoid System.Linq (use loops): {filePath}:{lineIndex + 1}");
                    }
                }
            }
        }

        private static string[] GetSourceRoots()
        {
            string projectRoot = Directory.GetCurrentDirectory();

            return new[]
            {
                Path.Combine(projectRoot, "Assets", "Onity-Packages", "Onity")
            };
        }

        // Bans LINQ-to-objects (System.Linq and any System.Linq.* sub-namespace)
        // while allowing System.Linq.Expressions, which the compiled constructor
        // activator needs. Operates on the namespace token only so the allow is
        // scoped to exactly the Expressions namespace and never lets real LINQ
        // slip through (e.g. System.Linq.Queryable still fails).
        private static bool UsesBannedSystemLinqNamespace(string trimmedUsingLine)
        {
            string declaration = trimmedUsingLine.Substring("using ".Length).Trim();

            if (declaration.StartsWith("static ", StringComparison.Ordinal))
            {
                declaration = declaration.Substring("static ".Length).Trim();
            }

            int aliasSeparatorIndex = declaration.IndexOf('=');

            if (aliasSeparatorIndex >= 0)
            {
                declaration = declaration.Substring(aliasSeparatorIndex + 1).Trim();
            }

            int terminatorIndex = declaration.IndexOf(';');

            if (terminatorIndex >= 0)
            {
                declaration = declaration.Substring(0, terminatorIndex).Trim();
            }

            if (declaration.Equals("System.Linq", StringComparison.Ordinal))
            {
                return true;
            }

            if (declaration.StartsWith("System.Linq.", StringComparison.Ordinal) == false)
            {
                return false;
            }

            return declaration.StartsWith("System.Linq.Expressions", StringComparison.Ordinal) == false;
        }

        private interface ITestService
        {
        }

        private interface IAdditionalService
        {
        }

        private interface INonLazyService
        {
        }

        private interface IPrefixFormatter
        {
            string Format(string content);
        }

        private sealed class TestService : ITestService
        {
        }

        private sealed class InterfaceOnlyService : ITestService, IAdditionalService
        {
        }

        private sealed class NoInterfaceService
        {
        }

        private sealed class NonLazyService : INonLazyService
        {
            public static int CtorCalls;

            public NonLazyService()
            {
                CtorCalls++;
            }
        }

        private sealed class PrefixFormatter : IPrefixFormatter
        {
            public string Format(string content)
            {
                return $"fmt:{content}";
            }
        }

        private sealed class FactoryValue
        {
            public FactoryValue(string content)
            {
                Content = content;
            }

            public string Content { get; }
        }

        private sealed class PrefixFactory : IFactory<string, FactoryValue>
        {
            private readonly IPrefixFormatter m_formatter;

            public PrefixFactory(IPrefixFormatter formatter)
            {
                m_formatter = formatter;
            }

            public FactoryValue Create(string param)
            {
                return new FactoryValue(m_formatter.Format(param));
            }
        }

        private sealed class SumFactory : IFactory<int, int, FactoryValue>
        {
            public FactoryValue Create(int param1, int param2)
            {
                return new FactoryValue((param1 + param2).ToString());
            }
        }

        private sealed class InjectionTarget
        {
            [Inject]
            private ITestService m_fieldService = null;

            [Inject]
            private ITestService PropertyServiceInternal
            {
                set => PropertyService = value;
            }

            public ITestService FieldService => m_fieldService;

            public ITestService PropertyService { get; private set; }

            public ITestService MethodService { get; private set; }

            [Inject]
            private void Initialize(ITestService service)
            {
                MethodService = service;
            }
        }

        private sealed class ConstructorTarget
        {
            public ConstructorTarget()
            {
                UsedInjectConstructor = false;
            }

            [Inject]
            private ConstructorTarget(ITestService service)
            {
                Service = service;
                UsedInjectConstructor = true;
            }

            public ITestService Service { get; }

            public bool UsedInjectConstructor { get; }
        }

        private sealed class TestConfigAsset : ScriptableObject
        {
            [Inject]
            private ITestService m_fieldService = null;

            public ITestService FieldService => m_fieldService;

            public ITestService MethodService { get; private set; }

            [Inject]
            private void Setup(ITestService service)
            {
                MethodService = service;
            }
        }
    }
}
