using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Onity.Analyzers
{
    /// <summary>
    /// ONITY004: reports a type that declares two or more instance constructors
    /// marked with <c>[Inject]</c>, which leaves the injection constructor
    /// ambiguous.
    /// </summary>
    /// <remarks>
    /// The check is purely syntactic and high-confidence: it counts constructor
    /// declarations carrying an attribute whose simple name is <c>Inject</c> or
    /// <c>InjectAttribute</c>. It does not bind the attribute to
    /// <c>Onity.DI.InjectAttribute</c>, so an unrelated attribute named
    /// <c>Inject</c> on two constructors of the same type would also be flagged;
    /// that is accepted as a rare, low-cost trade for zero project references.
    /// Static constructors cannot take attributes used for injection and are
    /// ignored.
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class OnityMultipleInjectConstructorsAnalyzer : DiagnosticAnalyzer
    {
        private const string k_injectAttributeShortName = "Inject";
        private const string k_injectAttributeFullName = "InjectAttribute";

        /// <inheritdoc />
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get { return ImmutableArray.Create(OnityDiagnostics.MultipleInjectConstructors); }
        }

        /// <inheritdoc />
        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(
                AnalyzeTypeDeclaration,
                SyntaxKind.ClassDeclaration,
                SyntaxKind.StructDeclaration,
                SyntaxKind.RecordDeclaration,
                SyntaxKind.RecordStructDeclaration);
        }

        private static void AnalyzeTypeDeclaration(SyntaxNodeAnalysisContext context)
        {
            TypeDeclarationSyntax typeDeclaration = (TypeDeclarationSyntax)context.Node;

            int injectConstructorCount = 0;

            foreach (MemberDeclarationSyntax member in typeDeclaration.Members)
            {
                if (!(member is ConstructorDeclarationSyntax constructor))
                {
                    continue;
                }

                // Static constructors are not injection points.
                if (constructor.Modifiers.Any(SyntaxKind.StaticKeyword))
                {
                    continue;
                }

                if (HasInjectAttribute(constructor))
                {
                    injectConstructorCount++;
                }
            }

            if (injectConstructorCount < 2)
            {
                return;
            }

            Diagnostic diagnostic = Diagnostic.Create(
                OnityDiagnostics.MultipleInjectConstructors,
                typeDeclaration.Identifier.GetLocation(),
                typeDeclaration.Identifier.ValueText,
                injectConstructorCount);
            context.ReportDiagnostic(diagnostic);
        }

        /// <summary>
        /// Returns true when <paramref name="constructor"/> carries an attribute
        /// whose simple name is <c>Inject</c> or <c>InjectAttribute</c>.
        /// </summary>
        private static bool HasInjectAttribute(ConstructorDeclarationSyntax constructor)
        {
            foreach (AttributeListSyntax attributeList in constructor.AttributeLists)
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
