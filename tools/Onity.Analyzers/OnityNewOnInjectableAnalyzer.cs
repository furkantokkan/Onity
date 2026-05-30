using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Onity.Analyzers
{
    /// <summary>
    /// ONITY006: reports a <c>new TService()</c> where <c>TService</c> is also a
    /// type the same file binds or resolves through Onity (a generic
    /// <c>Bind&lt;TService&gt;</c>/<c>BindInstance&lt;TService&gt;</c>/
    /// <c>BindFactory&lt;TService&gt;</c>/<c>Resolve&lt;TService&gt;</c> call).
    /// Constructing such a type by hand bypasses the container.
    /// </summary>
    /// <remarks>
    /// This is best-effort guidance with deliberately low false-positive risk: it
    /// fires only when the same syntax tree both constructs the type with
    /// <c>new</c> and binds/resolves it through an Onity method, matched by the
    /// simple type-name written at each site. The analysis is purely syntactic - it
    /// does not bind types or the container receiver, compares unqualified type
    /// names (so two unrelated types with the same simple name in one file could be
    /// conflated, which is rare), and offers no auto-fix. A type that is only
    /// constructed, or only bound/resolved, in a file is never flagged, so
    /// factories and intentionally hand-built types are left alone.
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class OnityNewOnInjectableAnalyzer : DiagnosticAnalyzer
    {
        private static readonly ImmutableHashSet<string> s_onityGenericMethodNames =
            ImmutableHashSet.Create("Bind", "BindInstance", "BindFactory", "Resolve");

        /// <inheritdoc />
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get { return ImmutableArray.Create(OnityDiagnostics.NewOnInjectable); }
        }

        /// <inheritdoc />
        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxTreeAction(AnalyzeTree);
        }

        private static void AnalyzeTree(SyntaxTreeAnalysisContext context)
        {
            SyntaxNode root = context.Tree.GetRoot(context.CancellationToken);

            // First pass: collect the simple type names bound/resolved via Onity in
            // this file, and every 'new T(...)' creation site keyed by simple name.
            HashSet<string> boundOrResolvedTypeNames = null;
            List<ObjectCreationExpressionSyntax> creations = null;

            foreach (SyntaxNode node in root.DescendantNodes())
            {
                switch (node)
                {
                    case InvocationExpressionSyntax invocation:
                    {
                        string resolvedTypeName = GetOnityGenericTypeArgumentName(invocation);
                        if (resolvedTypeName != null)
                        {
                            if (boundOrResolvedTypeNames == null)
                            {
                                boundOrResolvedTypeNames = new HashSet<string>();
                            }

                            boundOrResolvedTypeNames.Add(resolvedTypeName);
                        }

                        break;
                    }

                    case ObjectCreationExpressionSyntax creation:
                    {
                        if (creations == null)
                        {
                            creations = new List<ObjectCreationExpressionSyntax>();
                        }

                        creations.Add(creation);
                        break;
                    }
                }
            }

            if (boundOrResolvedTypeNames == null || creations == null)
            {
                return;
            }

            // Second pass: flag any 'new T(...)' whose simple type name is also
            // bound/resolved through Onity in this same file.
            foreach (ObjectCreationExpressionSyntax creation in creations)
            {
                string createdTypeName = GetSimpleTypeName(creation.Type);
                if (createdTypeName == null)
                {
                    continue;
                }

                if (!boundOrResolvedTypeNames.Contains(createdTypeName))
                {
                    continue;
                }

                Diagnostic diagnostic = Diagnostic.Create(
                    OnityDiagnostics.NewOnInjectable,
                    creation.GetLocation(),
                    createdTypeName);
                context.ReportDiagnostic(diagnostic);
            }
        }

        /// <summary>
        /// Returns the simple name of the single type argument of an Onity generic
        /// binding/resolution call (<c>x.Bind&lt;T&gt;()</c>,
        /// <c>x.Resolve&lt;T&gt;()</c>, etc.), or <c>null</c> when the invocation is
        /// not such a call. Only a single type argument is considered so that
        /// multi-arg overloads (interface-to-implementation maps) do not produce an
        /// ambiguous match.
        /// </summary>
        private static string GetOnityGenericTypeArgumentName(InvocationExpressionSyntax invocation)
        {
            SimpleNameSyntax name = GetInvokedSimpleName(invocation);
            if (!(name is GenericNameSyntax genericName))
            {
                return null;
            }

            if (!s_onityGenericMethodNames.Contains(genericName.Identifier.ValueText))
            {
                return null;
            }

            SeparatedSyntaxList<TypeSyntax> typeArguments = genericName.TypeArgumentList.Arguments;
            if (typeArguments.Count != 1)
            {
                return null;
            }

            return GetSimpleTypeName(typeArguments[0]);
        }

        /// <summary>
        /// Returns the invoked simple name for either a member-access call
        /// (<c>x.Resolve&lt;T&gt;()</c>) or a bare call (<c>Resolve&lt;T&gt;()</c>),
        /// or <c>null</c> for any other invocation shape.
        /// </summary>
        private static SimpleNameSyntax GetInvokedSimpleName(InvocationExpressionSyntax invocation)
        {
            switch (invocation.Expression)
            {
                case MemberAccessExpressionSyntax memberAccess:
                    return memberAccess.Name;

                case SimpleNameSyntax simpleName:
                    return simpleName;

                default:
                    return null;
            }
        }

        /// <summary>
        /// Returns the rightmost simple identifier of a type reference, unwrapping
        /// qualified names (<c>Onity.Foo</c>), aliased names (<c>global::Foo</c>),
        /// and generic names (<c>Foo&lt;T&gt;</c> yields <c>Foo</c>). Returns
        /// <c>null</c> for arrays, pointers, tuples, predefined keywords, and other
        /// shapes that are not user injectable types.
        /// </summary>
        private static string GetSimpleTypeName(TypeSyntax type)
        {
            switch (type)
            {
                case IdentifierNameSyntax identifier:
                    return identifier.Identifier.ValueText;

                case GenericNameSyntax generic:
                    return generic.Identifier.ValueText;

                case QualifiedNameSyntax qualified:
                    return GetSimpleTypeName(qualified.Right);

                case AliasQualifiedNameSyntax aliasQualified:
                    return GetSimpleTypeName(aliasQualified.Name);

                default:
                    return null;
            }
        }
    }
}
