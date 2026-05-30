using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Onity.Analyzers
{
    /// <summary>
    /// ONITY005: reports an <c>[Inject]</c> member that Onity cannot inject as
    /// written - a property with no setter, an indexer property, a generic method
    /// (one that declares type parameters), or a static field/property/method.
    /// </summary>
    /// <remarks>
    /// The check is purely syntactic and high-confidence: it matches a member
    /// carrying an attribute whose simple name is <c>Inject</c> or
    /// <c>InjectAttribute</c> (the same name match used by ONITY004) and inspects
    /// the member's own syntax (accessors, indexer parameter list, type-parameter
    /// list, and the <c>static</c> modifier). It does not bind the attribute to
    /// <c>Onity.DI.InjectAttribute</c>, so an unrelated attribute named
    /// <c>Inject</c> would also be considered; that is accepted as a rare, low-cost
    /// trade for zero project references. The get-only-property, indexer, and
    /// generic-method shapes throw <c>OnityBindingException</c> when the owning type
    /// is built or resolved; a static <c>[Inject]</c> member is silently never
    /// injected because Onity scans only instance members.
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class OnityInvalidInjectMemberAnalyzer : DiagnosticAnalyzer
    {
        private const string k_injectAttributeShortName = "Inject";
        private const string k_injectAttributeFullName = "InjectAttribute";

        /// <inheritdoc />
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get { return ImmutableArray.Create(OnityDiagnostics.InvalidInjectMember); }
        }

        /// <inheritdoc />
        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(AnalyzeProperty, SyntaxKind.PropertyDeclaration);
            context.RegisterSyntaxNodeAction(AnalyzeIndexer, SyntaxKind.IndexerDeclaration);
            context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
            context.RegisterSyntaxNodeAction(AnalyzeField, SyntaxKind.FieldDeclaration);
        }

        private static void AnalyzeProperty(SyntaxNodeAnalysisContext context)
        {
            PropertyDeclarationSyntax property = (PropertyDeclarationSyntax)context.Node;

            if (!HasInjectAttribute(property.AttributeLists))
            {
                return;
            }

            // A static [Inject] member is never injected (only instance members are
            // scanned). Report that first; the setter requirement is secondary.
            if (IsStatic(property.Modifiers))
            {
                Report(
                    context,
                    property.Identifier.GetLocation(),
                    "property",
                    property.Identifier.ValueText,
                    "is static, so it is never injected; only instance members are scanned. Make it an instance property.");
                return;
            }

            if (!HasSetter(property))
            {
                Report(
                    context,
                    property.Identifier.GetLocation(),
                    "property",
                    property.Identifier.ValueText,
                    "has no setter, so injection throws OnityBindingException at resolve. Add a (private) set accessor, or move [Inject] to a backing field or an Initialize method.");
            }
        }

        private static void AnalyzeIndexer(SyntaxNodeAnalysisContext context)
        {
            IndexerDeclarationSyntax indexer = (IndexerDeclarationSyntax)context.Node;

            if (!HasInjectAttribute(indexer.AttributeLists))
            {
                return;
            }

            // An indexer has no plain name; describe it by its 'this[...]' keyword.
            Report(
                context,
                indexer.ThisKeyword.GetLocation(),
                "property",
                "this[]",
                "is an indexer, so injection throws OnityBindingException at resolve. Inject into a non-indexed property, a field, or a method parameter instead.");
        }

        private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
        {
            MethodDeclarationSyntax method = (MethodDeclarationSyntax)context.Node;

            if (!HasInjectAttribute(method.AttributeLists))
            {
                return;
            }

            if (IsStatic(method.Modifiers))
            {
                Report(
                    context,
                    method.Identifier.GetLocation(),
                    "method",
                    method.Identifier.ValueText,
                    "is static, so it is never invoked for injection; only instance members are scanned. Make it an instance method.");
                return;
            }

            if (method.TypeParameterList != null && method.TypeParameterList.Parameters.Count > 0)
            {
                Report(
                    context,
                    method.Identifier.GetLocation(),
                    "method",
                    method.Identifier.ValueText,
                    "is generic, so injection throws OnityBindingException at resolve. Use a non-generic [Inject] method whose parameter types are concrete and resolvable.");
            }
        }

        private static void AnalyzeField(SyntaxNodeAnalysisContext context)
        {
            FieldDeclarationSyntax field = (FieldDeclarationSyntax)context.Node;

            if (!HasInjectAttribute(field.AttributeLists))
            {
                return;
            }

            if (!IsStatic(field.Modifiers))
            {
                return;
            }

            // A field declaration can declare several variables; name each one.
            foreach (VariableDeclaratorSyntax variable in field.Declaration.Variables)
            {
                Report(
                    context,
                    variable.Identifier.GetLocation(),
                    "field",
                    variable.Identifier.ValueText,
                    "is static, so it is never injected; only instance members are scanned. Make it an instance field.");
            }
        }

        private static void Report(
            SyntaxNodeAnalysisContext context,
            Location location,
            string memberKind,
            string memberName,
            string reason)
        {
            Diagnostic diagnostic = Diagnostic.Create(
                OnityDiagnostics.InvalidInjectMember,
                location,
                memberKind,
                memberName,
                reason);
            context.ReportDiagnostic(diagnostic);
        }

        /// <summary>
        /// Returns true when <paramref name="property"/> declares a set (or init)
        /// accessor, or is an expression-bodied property (which is get-only). An
        /// expression body has no set accessor, so it is treated as having no setter.
        /// </summary>
        private static bool HasSetter(PropertyDeclarationSyntax property)
        {
            if (property.AccessorList == null)
            {
                // Expression-bodied property (=> expr) is get-only.
                return false;
            }

            foreach (AccessorDeclarationSyntax accessor in property.AccessorList.Accessors)
            {
                if (accessor.IsKind(SyntaxKind.SetAccessorDeclaration)
                    || accessor.IsKind(SyntaxKind.InitAccessorDeclaration))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsStatic(SyntaxTokenList modifiers)
        {
            return modifiers.Any(SyntaxKind.StaticKeyword);
        }

        /// <summary>
        /// Returns true when any attribute in <paramref name="attributeLists"/> has a
        /// simple name of <c>Inject</c> or <c>InjectAttribute</c>.
        /// </summary>
        private static bool HasInjectAttribute(SyntaxList<AttributeListSyntax> attributeLists)
        {
            foreach (AttributeListSyntax attributeList in attributeLists)
            {
                foreach (AttributeSyntax attribute in attributeList.Attributes)
                {
                    if (IsInjectAttributeName(attribute.Name))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Returns true when the attribute name (handling qualified names like
        /// <c>Onity.DI.Inject</c> and aliased <c>global::...</c> forms) resolves to
        /// the simple identifier <c>Inject</c> or <c>InjectAttribute</c>.
        /// </summary>
        private static bool IsInjectAttributeName(NameSyntax name)
        {
            string simpleName = GetRightmostIdentifier(name);
            return simpleName == k_injectAttributeShortName
                || simpleName == k_injectAttributeFullName;
        }

        private static string GetRightmostIdentifier(NameSyntax name)
        {
            switch (name)
            {
                case IdentifierNameSyntax identifier:
                    return identifier.Identifier.ValueText;

                case QualifiedNameSyntax qualified:
                    return qualified.Right.Identifier.ValueText;

                case AliasQualifiedNameSyntax aliasQualified:
                    return aliasQualified.Name.Identifier.ValueText;

                default:
                    return null;
            }
        }
    }
}
