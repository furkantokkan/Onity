using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using NUnit.Framework;
using Onity.SourceGen;

namespace Onity.Analyzers.Tests
{
    [TestFixture]
    public sealed class OnitySourceGenTests
    {
        private const string k_fixtureSource = @"
namespace Onity.DI
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public sealed class OnityGenerateActivatorAttribute : System.Attribute { }
}

namespace Onity.DI.Internal
{
    public static class GeneratedActivators
    {
        public static void Register(
            System.Type type,
            System.Type[] parameterTypes,
            System.Func<object[], object> activator) { }
    }
}

[Onity.DI.OnityGenerateActivator]
public sealed class Service
{
    public Service(int value) { }
}";

        private const string k_moduleInitializerSource = @"
namespace System.Runtime.CompilerServices
{
    [System.AttributeUsage(System.AttributeTargets.Method, Inherited = false)]
    internal sealed class ModuleInitializerAttribute : System.Attribute { }
}";

        [Test]
        public void MissingModuleInitializerAttribute_EmitsCompilableDefinition()
        {
            GenerateAndCompile(k_fixtureSource, expectGeneratedDefinition: true);
        }

        [Test]
        public void ExistingModuleInitializerAttribute_DoesNotEmitDuplicateDefinition()
        {
            GenerateAndCompile(
                k_fixtureSource + k_moduleInitializerSource,
                expectGeneratedDefinition: false);
        }

        private static void GenerateAndCompile(string source, bool expectGeneratedDefinition)
        {
            string referencePath = Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "ReferenceAssemblies",
                "netstandard.dll");

            Assert.That(File.Exists(referencePath), Is.True, referencePath);

            CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.CSharp9);
            CSharpCompilation compilation = CSharpCompilation.Create(
                "Onity.SourceGen.TestFixture",
                new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
                new[] { MetadataReference.CreateFromFile(referencePath) },
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            Assert.That(
                compilation.GetTypeByMetadataName(
                    "System.Runtime.CompilerServices.ModuleInitializerAttribute") is null,
                Is.EqualTo(expectGeneratedDefinition));

            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new[] { new OnityActivatorGenerator().AsSourceGenerator() },
                parseOptions: parseOptions);
            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out Compilation updatedCompilation,
                out System.Collections.Immutable.ImmutableArray<Diagnostic> generatorDiagnostics);

            Assert.That(generatorDiagnostics, Is.Empty);
            GeneratorRunResult result = driver.GetRunResult().Results[0];
            Assert.That(result.GeneratedSources.Length, Is.EqualTo(1));

            string generatedSource = result.GeneratedSources[0].SourceText.ToString();
            Assert.That(
                generatedSource.Contains("internal sealed class ModuleInitializerAttribute"),
                Is.EqualTo(expectGeneratedDefinition));

            using MemoryStream output = new MemoryStream();
            EmitResult emitResult = updatedCompilation.Emit(output);
            Assert.That(
                emitResult.Success,
                Is.True,
                string.Join(Environment.NewLine, emitResult.Diagnostics.Select(diagnostic => diagnostic.ToString())));
        }
    }
}
