using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Onity.SourceGen
{
    /// <summary>
    /// Incremental source generator that emits AOT-safe constructor activators
    /// for types marked with an attribute whose simple name is
    /// <c>OnityGenerateActivator</c>.
    /// </summary>
    /// <remarks>
    /// For each selected type the generator emits a static
    /// <c>System.Func&lt;object[], object&gt;</c> that constructs the type by
    /// calling its constructor directly in plain C#
    /// (<c>new T((TArg0)args[0], (TArg1)args[1], ...)</c>) — no reflection and no
    /// <c>Expression.Compile</c>. A generated
    /// <c>[System.Runtime.CompilerServices.ModuleInitializer]</c>
    /// method registers every activator with the runtime hook
    /// <c>Onity.DI.Internal.GeneratedActivators.Register(System.Type, System.Type[], System.Func&lt;object[], object&gt;)</c>.
    ///
    /// This covers constructor injection selected by an explicit attribute.
    /// Member-setter injection and binding-site type discovery are intentionally
    /// left to the normal runtime plan. The hook is referenced by fully-qualified
    /// name in generated code; this generator never creates files under
    /// <c>Assets/</c> or <c>Onity.DI</c>.
    /// </remarks>
    [Generator(LanguageNames.CSharp)]
    public sealed class OnityActivatorGenerator : IIncrementalGenerator
    {
        /// <summary>
        /// Simple name (without the <c>Attribute</c> suffix) of the marker
        /// attribute that opts a type into activator generation.
        /// </summary>
        private const string k_attributeSimpleName = "OnityGenerateActivator";

        /// <summary>
        /// Simple name of Onity's constructor-selection attribute.
        /// </summary>
        private const string k_injectAttributeSimpleName = "Inject";

        /// <summary>
        /// Fully-qualified runtime hook the generated module initializer calls to
        /// register each activator.
        /// </summary>
        private const string k_registerHook = "global::Onity.DI.Internal.GeneratedActivators.Register";

        /// <summary>
        /// Hint-name suffix for the single generated registration file.
        /// </summary>
        private const string k_generatedFileName = "OnityGeneratedActivators.g.cs";

        /// <summary>
        /// Namespace of the emitted registrar class.
        /// </summary>
        private const string k_generatedNamespace = "Onity.SourceGen.Generated";

        /// <summary>
        /// Name of the emitted registrar class.
        /// </summary>
        private const string k_generatedClassName = "OnityGeneratedActivators";

        /// <inheritdoc />
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Find every type declaration carrying an attribute whose simple name
            // is OnityGenerateActivator, then map each to a small, equatable model.
            IncrementalValuesProvider<ActivatorModel> models = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => IsCandidateType(node),
                    transform: static (syntaxContext, _) => TryBuildModel(syntaxContext))
                .Where(static model => model is not null)
                .Select(static (model, _) => model.Value);

            IncrementalValueProvider<(Compilation Compilation, System.Collections.Immutable.ImmutableArray<ActivatorModel> Models)> combined =
                context.CompilationProvider.Combine(models.Collect());

            context.RegisterSourceOutput(
                combined,
                static (productionContext, source) => Execute(productionContext, source.Models));
        }

        /// <summary>
        /// Cheap syntactic pre-filter: a class or struct declaration that carries
        /// at least one attribute. The semantic check in
        /// <see cref="TryBuildModel" /> confirms the attribute's simple name.
        /// </summary>
        private static bool IsCandidateType(SyntaxNode node)
        {
            return node is TypeDeclarationSyntax typeDeclaration
                && typeDeclaration.AttributeLists.Count > 0
                && (typeDeclaration is ClassDeclarationSyntax || typeDeclaration is StructDeclarationSyntax);
        }

        /// <summary>
        /// Confirms the marker attribute semantically and, if present, selects the
        /// activator constructor and captures the data needed to emit it. Returns
        /// <c>null</c> when the type does not qualify.
        /// </summary>
        private static ActivatorModel? TryBuildModel(GeneratorSyntaxContext syntaxContext)
        {
            TypeDeclarationSyntax typeDeclaration = (TypeDeclarationSyntax)syntaxContext.Node;

            if (!(syntaxContext.SemanticModel.GetDeclaredSymbol(typeDeclaration) is INamedTypeSymbol typeSymbol))
            {
                return null;
            }

            if (!HasMarkerAttribute(typeSymbol))
            {
                return null;
            }

            // Abstract types and unbound generics cannot be constructed with a
            // plain `new`, so a scaffold activator cannot be emitted for them.
            if (typeSymbol.IsAbstract || typeSymbol.IsStatic || typeSymbol.IsUnboundGenericType || typeSymbol.IsGenericType)
            {
                return null;
            }

            IMethodSymbol constructor = SelectConstructor(typeSymbol);
            if (constructor is null)
            {
                return null;
            }

            string fullyQualifiedType = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            List<string> parameterTypes = new List<string>(constructor.Parameters.Length);
            foreach (IParameterSymbol parameter in constructor.Parameters)
            {
                parameterTypes.Add(parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            }

            return new ActivatorModel(fullyQualifiedType, parameterTypes);
        }

        /// <summary>
        /// Returns true when the type (or its base chain) declares an attribute
        /// whose simple name matches <see cref="k_attributeSimpleName" />, with or
        /// without the <c>Attribute</c> suffix.
        /// </summary>
        private static bool HasMarkerAttribute(INamedTypeSymbol typeSymbol)
        {
            foreach (AttributeData attribute in typeSymbol.GetAttributes())
            {
                INamedTypeSymbol attributeClass = attribute.AttributeClass;
                if (attributeClass is null)
                {
                    continue;
                }

                if (MatchesAttributeName(attributeClass.Name, k_attributeSimpleName))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Compares an attribute type name against the marker simple name, tolerating
        /// the optional <c>Attribute</c> suffix that C# strips at usage sites.
        /// </summary>
        private static bool MatchesAttributeName(string attributeTypeName, string expectedSimpleName)
        {
            if (attributeTypeName == expectedSimpleName)
            {
                return true;
            }

            return attributeTypeName == expectedSimpleName + "Attribute";
        }

        /// <summary>
        /// Picks the constructor the activator will call. Mirrors the runtime
        /// rule for constructors this generated source can call: prefer a single
        /// accessible [Inject] constructor; otherwise use the public/internal
        /// constructor with the best score. Returns <c>null</c> when no usable
        /// instance constructor exists.
        /// </summary>
        private static IMethodSymbol SelectConstructor(INamedTypeSymbol typeSymbol)
        {
            IMethodSymbol attributed = null;
            bool hasInaccessibleAttributedConstructor = false;
            IMethodSymbol best = null;

            foreach (IMethodSymbol constructor in typeSymbol.InstanceConstructors)
            {
                if (constructor.IsStatic)
                {
                    continue;
                }

                bool accessible =
                    constructor.DeclaredAccessibility == Accessibility.Public
                    || constructor.DeclaredAccessibility == Accessibility.Internal;
                bool hasInjectAttribute = HasInjectAttribute(constructor);

                if (!accessible)
                {
                    if (hasInjectAttribute)
                    {
                        hasInaccessibleAttributedConstructor = true;
                    }

                    continue;
                }

                if (hasInjectAttribute)
                {
                    if (attributed != null)
                    {
                        return null;
                    }

                    attributed = constructor;
                    continue;
                }

                if (best is null || IsPreferredOver(constructor, best))
                {
                    best = constructor;
                }
            }

            if (hasInaccessibleAttributedConstructor)
            {
                return null;
            }

            if (attributed != null)
            {
                return attributed;
            }

            return best;
        }

        private static bool HasInjectAttribute(IMethodSymbol constructor)
        {
            foreach (AttributeData attribute in constructor.GetAttributes())
            {
                INamedTypeSymbol attributeClass = attribute.AttributeClass;
                if (attributeClass is null)
                {
                    continue;
                }

                if (MatchesAttributeName(attributeClass.Name, k_injectAttributeSimpleName))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Tie-break rule for <see cref="SelectConstructor" />: a public constructor
        /// outranks a non-public one; otherwise the one with more parameters wins.
        /// </summary>
        private static bool IsPreferredOver(IMethodSymbol candidate, IMethodSymbol current)
        {
            bool candidatePublic = candidate.DeclaredAccessibility == Accessibility.Public;
            bool currentPublic = current.DeclaredAccessibility == Accessibility.Public;

            if (candidatePublic != currentPublic)
            {
                return candidatePublic;
            }

            return candidate.Parameters.Length > current.Parameters.Length;
        }

        /// <summary>
        /// Emits the single registration file: one activator field per selected
        /// type plus a <c>[ModuleInitializer]</c> method that registers them all.
        /// </summary>
        private static void Execute(
            SourceProductionContext context,
            System.Collections.Immutable.ImmutableArray<ActivatorModel> models)
        {
            if (models.IsDefaultOrEmpty)
            {
                return;
            }

            StringBuilder builder = new StringBuilder();

            builder.AppendLine("// <auto-generated/>");
            builder.AppendLine("// Generated by Onity.SourceGen.OnityActivatorGenerator. Do not edit.");
            builder.AppendLine("#nullable disable");
            builder.AppendLine();
            builder.Append("namespace ").Append(k_generatedNamespace).AppendLine();
            builder.AppendLine("{");
            builder.Append("    internal static class ").Append(k_generatedClassName).AppendLine();
            builder.AppendLine("    {");

            // Emit one strongly-typed activator method per selected type. Using a
            // named method (rather than a lambda) keeps the `new T(...)` call site
            // explicit and trivially AOT-compilable.
            for (int i = 0; i < models.Length; i++)
            {
                AppendActivatorMethod(builder, models[i], i);
            }

            builder.AppendLine();
            builder.AppendLine("        [global::System.Runtime.CompilerServices.ModuleInitializer]");
            builder.AppendLine("        internal static void Register()");
            builder.AppendLine("        {");

            for (int i = 0; i < models.Length; i++)
            {
                ActivatorModel model = models[i];
                builder
                    .Append("            ")
                    .Append(k_registerHook)
                    .Append("(typeof(")
                    .Append(model.FullyQualifiedType)
                    .Append("), ")
                    .Append(BuildParameterTypeArray(model))
                    .Append(", Activate_")
                    .Append(i)
                    .AppendLine(");");
            }

            builder.AppendLine("        }");
            builder.AppendLine("    }");
            builder.AppendLine("}");

            context.AddSource(k_generatedFileName, SourceText.From(builder.ToString(), Encoding.UTF8));
        }

        /// <summary>
        /// Appends one activator method:
        /// <c>private static object Activate_i(object[] args) =&gt; new T((TArg0)args[0], ...);</c>.
        /// </summary>
        private static void AppendActivatorMethod(StringBuilder builder, ActivatorModel model, int index)
        {
            builder
                .Append("        private static object Activate_")
                .Append(index)
                .AppendLine("(object[] args)");
            builder.AppendLine("        {");
            builder.Append("            return new ").Append(model.FullyQualifiedType).Append('(');

            for (int p = 0; p < model.ParameterTypes.Count; p++)
            {
                if (p > 0)
                {
                    builder.Append(", ");
                }

                builder
                    .Append('(')
                    .Append(model.ParameterTypes[p])
                    .Append(")args[")
                    .Append(p)
                    .Append(']');
            }

            builder.AppendLine(");");
            builder.AppendLine("        }");
            builder.AppendLine();
        }

        private static string BuildParameterTypeArray(ActivatorModel model)
        {
            if (model.ParameterTypes.Count == 0)
            {
                return "global::System.Type.EmptyTypes";
            }

            StringBuilder builder = new StringBuilder();
            builder.Append("new global::System.Type[] { ");

            for (int i = 0; i < model.ParameterTypes.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                builder
                    .Append("typeof(")
                    .Append(model.ParameterTypes[i])
                    .Append(')');
            }

            builder.Append(" }");
            return builder.ToString();
        }
    }
}
