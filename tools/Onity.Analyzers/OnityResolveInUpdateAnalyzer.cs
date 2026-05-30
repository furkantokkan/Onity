using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Onity.Analyzers
{
    /// <summary>
    /// ONITY001: reports a member call named <c>Resolve</c> (generic
    /// <c>Resolve&lt;T&gt;(...)</c> or non-generic <c>Resolve(...)</c>) whose
    /// nearest enclosing method is a per-frame Unity message
    /// (<c>Update</c>, <c>FixedUpdate</c>, or <c>LateUpdate</c>).
    /// </summary>
    /// <remarks>
    /// The check is purely syntactic and high-confidence: it matches the member
    /// name <c>Resolve</c> on a member-access invocation. It does not bind the
    /// receiver type, so it is intentionally conservative about naming the
    /// declaring type+method in the message rather than asserting the receiver is
    /// an <c>OnityContainer</c>/<c>IResolver</c>.
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class OnityResolveInUpdateAnalyzer : DiagnosticAnalyzer
    {
        private const string k_resolveMethodName = "Resolve";

        private static readonly ImmutableHashSet<string> s_perFrameMethodNames =
            ImmutableHashSet.Create("Update", "FixedUpdate", "LateUpdate");

        /// <inheritdoc />
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get { return ImmutableArray.Create(OnityDiagnostics.ResolveInUpdate); }
        }

        /// <inheritdoc />
        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
        }

        private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
        {
            InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;

            if (!IsResolveCall(invocation))
            {
                return;
            }

            MethodDeclarationSyntax enclosingMethod = FindEnclosingMethod(invocation);
            if (enclosingMethod == null)
            {
                return;
            }

            string methodName = enclosingMethod.Identifier.ValueText;
            if (!s_perFrameMethodNames.Contains(methodName))
            {
                return;
            }

            string typeName = GetEnclosingTypeName(enclosingMethod);

            Diagnostic diagnostic = Diagnostic.Create(
                OnityDiagnostics.ResolveInUpdate,
                invocation.GetLocation(),
                typeName,
                methodName);

            context.ReportDiagnostic(diagnostic);
        }

        /// <summary>
        /// Returns true when the invocation is a member call whose method name is
        /// <c>Resolve</c>, covering both <c>x.Resolve(...)</c> and
        /// <c>x.Resolve&lt;T&gt;(...)</c>.
        /// </summary>
        private static bool IsResolveCall(InvocationExpressionSyntax invocation)
        {
            if (!(invocation.Expression is MemberAccessExpressionSyntax memberAccess))
            {
                return false;
            }

            SimpleNameSyntax name = memberAccess.Name;
            return name != null && name.Identifier.ValueText == k_resolveMethodName;
        }

        /// <summary>
        /// Walks up the syntax tree to the nearest <see cref="MethodDeclarationSyntax"/>,
        /// stopping at a lambda/local-function/accessor boundary so that a Resolve
        /// inside a nested closure is not falsely attributed to the outer Update.
        /// </summary>
        private static MethodDeclarationSyntax FindEnclosingMethod(SyntaxNode node)
        {
            SyntaxNode current = node.Parent;
            while (current != null)
            {
                switch (current)
                {
                    case MethodDeclarationSyntax method:
                        return method;

                    // A Resolve inside any of these executes on a different cadence
                    // than the enclosing per-frame method, so stop the search.
                    case SimpleLambdaExpressionSyntax _:
                    case ParenthesizedLambdaExpressionSyntax _:
                    case AnonymousMethodExpressionSyntax _:
                    case LocalFunctionStatementSyntax _:
                    case AccessorDeclarationSyntax _:
                        return null;
                }

                current = current.Parent;
            }

            return null;
        }

        /// <summary>
        /// Returns the simple name of the type that declares <paramref name="method"/>,
        /// or <c>&lt;global&gt;</c> when it cannot be determined syntactically.
        /// </summary>
        private static string GetEnclosingTypeName(MethodDeclarationSyntax method)
        {
            SyntaxNode current = method.Parent;
            while (current != null)
            {
                if (current is BaseTypeDeclarationSyntax typeDeclaration)
                {
                    return typeDeclaration.Identifier.ValueText;
                }

                current = current.Parent;
            }

            return "<global>";
        }
    }
}
