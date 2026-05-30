using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Onity.Analyzers
{
    /// <summary>
    /// ONITY002: reports a container binding/resolution call
    /// (<c>Bind</c>, <c>BindInstance</c>, <c>BindFactory</c>, or <c>Resolve</c>)
    /// made on a local that already had <c>Build()</c> called on it earlier in the
    /// same method body.
    /// </summary>
    /// <remarks>
    /// The analysis is a best-effort, purely-syntactic intra-method dataflow walk:
    /// it visits the statements of a single method/accessor/lambda/local-function
    /// body in source order, records the simple receiver name of each
    /// <c>x.Build()</c> call, and then flags any later <c>x.Bind*(...)</c> or
    /// <c>x.Resolve(...)</c> on a receiver with the same name. It does not bind the
    /// receiver type, follow the local across method calls, or reason about
    /// branches, so it intentionally only fires on the high-confidence
    /// straight-line "build then register" mistake on a single named local.
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class OnityRegisterAfterBuildAnalyzer : DiagnosticAnalyzer
    {
        private const string k_buildMethodName = "Build";

        private static readonly ImmutableHashSet<string> s_registerMethodNames =
            ImmutableHashSet.Create("Bind", "BindInstance", "BindFactory", "Resolve");

        /// <inheritdoc />
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get { return ImmutableArray.Create(OnityDiagnostics.RegisterAfterBuild); }
        }

        /// <inheritdoc />
        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(AnalyzeMethodBody, SyntaxKind.MethodDeclaration);
            context.RegisterSyntaxNodeAction(AnalyzeAccessorBody, SyntaxKind.GetAccessorDeclaration);
            context.RegisterSyntaxNodeAction(AnalyzeAccessorBody, SyntaxKind.SetAccessorDeclaration);
            context.RegisterSyntaxNodeAction(AnalyzeLocalFunctionBody, SyntaxKind.LocalFunctionStatement);
            context.RegisterSyntaxNodeAction(AnalyzeConstructorBody, SyntaxKind.ConstructorDeclaration);
        }

        private static void AnalyzeMethodBody(SyntaxNodeAnalysisContext context)
        {
            MethodDeclarationSyntax method = (MethodDeclarationSyntax)context.Node;
            AnalyzeBody(context, method.Body, method.ExpressionBody);
        }

        private static void AnalyzeConstructorBody(SyntaxNodeAnalysisContext context)
        {
            ConstructorDeclarationSyntax ctor = (ConstructorDeclarationSyntax)context.Node;
            AnalyzeBody(context, ctor.Body, ctor.ExpressionBody);
        }

        private static void AnalyzeAccessorBody(SyntaxNodeAnalysisContext context)
        {
            AccessorDeclarationSyntax accessor = (AccessorDeclarationSyntax)context.Node;
            AnalyzeBody(context, accessor.Body, accessor.ExpressionBody);
        }

        private static void AnalyzeLocalFunctionBody(SyntaxNodeAnalysisContext context)
        {
            LocalFunctionStatementSyntax localFunction = (LocalFunctionStatementSyntax)context.Node;
            AnalyzeBody(context, localFunction.Body, localFunction.ExpressionBody);
        }

        /// <summary>
        /// Runs the straight-line build-then-register walk over the statements of a
        /// single block body. An expression body cannot both build and register a
        /// container, so it is not analyzed.
        /// </summary>
        private static void AnalyzeBody(
            SyntaxNodeAnalysisContext context,
            BlockSyntax block,
            ArrowExpressionClauseSyntax expressionBody)
        {
            if (block == null)
            {
                return;
            }

            // Receiver names that have already had Build() called on them in this body.
            HashSet<string> builtReceivers = null;

            foreach (StatementSyntax statement in block.Statements)
            {
                // A nested lambda/local function has its own body and is analyzed by
                // its own registered action; skip the invocations inside it here so
                // a deferred Build/register is not attributed to this body's order.
                foreach (InvocationExpressionSyntax invocation in EnumerateTopLevelInvocations(statement))
                {
                    if (!(invocation.Expression is MemberAccessExpressionSyntax memberAccess))
                    {
                        continue;
                    }

                    string receiver = GetReceiverIdentifier(memberAccess);
                    if (receiver == null)
                    {
                        continue;
                    }

                    string calledMethod = memberAccess.Name.Identifier.ValueText;

                    if (builtReceivers != null
                        && builtReceivers.Contains(receiver)
                        && s_registerMethodNames.Contains(calledMethod))
                    {
                        Diagnostic diagnostic = Diagnostic.Create(
                            OnityDiagnostics.RegisterAfterBuild,
                            invocation.GetLocation(),
                            receiver,
                            calledMethod);
                        context.ReportDiagnostic(diagnostic);
                        continue;
                    }

                    if (calledMethod == k_buildMethodName)
                    {
                        if (builtReceivers == null)
                        {
                            builtReceivers = new HashSet<string>();
                        }

                        builtReceivers.Add(receiver);
                    }
                }
            }
        }

        /// <summary>
        /// Yields the invocations in source order within <paramref name="statement"/>
        /// that belong to this body, stopping the descent at any nested
        /// lambda/anonymous-method/local-function boundary so their deferred
        /// invocations are excluded.
        /// </summary>
        private static IEnumerable<InvocationExpressionSyntax> EnumerateTopLevelInvocations(StatementSyntax statement)
        {
            Stack<SyntaxNode> pending = new Stack<SyntaxNode>();

            // Push children in reverse so the stack pops them in source order.
            PushChildrenInReverse(pending, statement);

            while (pending.Count > 0)
            {
                SyntaxNode current = pending.Pop();

                switch (current)
                {
                    case SimpleLambdaExpressionSyntax _:
                    case ParenthesizedLambdaExpressionSyntax _:
                    case AnonymousMethodExpressionSyntax _:
                    case LocalFunctionStatementSyntax _:
                        // Deferred body analyzed by its own action; do not descend.
                        continue;
                }

                if (current is InvocationExpressionSyntax invocation)
                {
                    yield return invocation;
                }

                PushChildrenInReverse(pending, current);
            }
        }

        private static void PushChildrenInReverse(Stack<SyntaxNode> stack, SyntaxNode node)
        {
            // ChildNodes() is in source order; reverse so the first child is popped first.
            List<SyntaxNode> children = new List<SyntaxNode>(node.ChildNodes());
            for (int i = children.Count - 1; i >= 0; i--)
            {
                stack.Push(children[i]);
            }
        }

        /// <summary>
        /// Returns the simple name of the member-access receiver when it is a bare
        /// identifier (<c>container</c>) or <c>this.container</c>, or <c>null</c>
        /// when the receiver is a more complex expression that cannot be matched by
        /// name alone.
        /// </summary>
        private static string GetReceiverIdentifier(MemberAccessExpressionSyntax memberAccess)
        {
            switch (memberAccess.Expression)
            {
                case IdentifierNameSyntax identifier:
                    return identifier.Identifier.ValueText;

                case MemberAccessExpressionSyntax inner
                    when inner.Expression is ThisExpressionSyntax:
                    return inner.Name.Identifier.ValueText;

                default:
                    return null;
            }
        }
    }
}
