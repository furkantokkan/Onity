using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Onity.Analyzers
{
    /// <summary>
    /// ONITY003: reports a <c>Subscribe(...)</c> or <c>SubscribeAwait(...)</c>
    /// invocation whose returned <c>IDisposable</c> is discarded - the call is a
    /// standalone expression statement and is not chained into an
    /// <c>AddTo(...)</c>, assigned, returned, awaited, or passed as an argument.
    /// </summary>
    /// <remarks>
    /// The rule is a cheap syntactic shape check followed by one semantic check.
    /// The shape check only lets through a member call named <c>Subscribe</c> or
    /// <c>SubscribeAwait</c> (or a fluent chain whose outermost call is one of
    /// them) that is the entire expression of an
    /// <see cref="ExpressionStatementSyntax"/>, which is exactly the shape that
    /// throws away the subscription handle. A call that is the receiver of a
    /// following <c>.AddTo(...)</c> is left to that outer call to own and is not
    /// flagged. Only a call that passes the shape check is bound: the invoked
    /// method must be named <c>Subscribe</c> or <c>SubscribeAwait</c> and return
    /// <c>System.IDisposable</c> or a type that implements it. A
    /// <c>void</c>-returning overload, such as a stream consumer whose lifetime is
    /// owned by a <c>CancellationToken</c> argument, has no handle to dispose and
    /// is never reported, nor is a method that returns something that is not
    /// disposable. The receiver type and the declaring namespace are not
    /// restricted, as before, and a call that does not bind (a compile error) is
    /// not reported.
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class OnitySubscribeWithoutAddToAnalyzer : DiagnosticAnalyzer
    {
        private static readonly ImmutableHashSet<string> s_subscribeMethodNames =
            ImmutableHashSet.Create("Subscribe", "SubscribeAwait");

        /// <inheritdoc />
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get { return ImmutableArray.Create(OnityDiagnostics.SubscribeWithoutAddTo); }
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

            if (!IsSubscribeMemberCall(invocation))
            {
                return;
            }

            // Only the outermost call in a fluent chain is the value of the
            // statement. If this Subscribe is itself the receiver of a following
            // member access (for example x.Subscribe(...).AddTo(scope)), the result
            // is consumed by that outer call, so it is not discarded here.
            if (IsReceiverOfMemberAccess(invocation))
            {
                return;
            }

            if (!IsResultDiscarded(invocation))
            {
                return;
            }

            // Bind only the calls that look discarded, so the semantic model is
            // never consulted for the vast majority of invocations.
            IMethodSymbol method = context.SemanticModel
                .GetSymbolInfo(invocation, context.CancellationToken)
                .Symbol as IMethodSymbol;

            if (method == null
                || !s_subscribeMethodNames.Contains(method.Name)
                || !ReturnsDisposable(method))
            {
                return;
            }

            Diagnostic diagnostic = Diagnostic.Create(
                OnityDiagnostics.SubscribeWithoutAddTo,
                invocation.GetLocation(),
                method.Name);
            context.ReportDiagnostic(diagnostic);
        }

        /// <summary>
        /// Returns true when the invocation is a member call whose method name is
        /// <c>Subscribe</c> or <c>SubscribeAwait</c>, covering both <c>x.M(...)</c>
        /// and <c>x.M&lt;T&gt;(...)</c>.
        /// </summary>
        private static bool IsSubscribeMemberCall(InvocationExpressionSyntax invocation)
        {
            if (!(invocation.Expression is MemberAccessExpressionSyntax memberAccess))
            {
                return false;
            }

            SimpleNameSyntax memberName = memberAccess.Name;
            return memberName != null && s_subscribeMethodNames.Contains(memberName.Identifier.ValueText);
        }

        /// <summary>
        /// Returns true when <paramref name="invocation"/> is the receiver
        /// expression of an enclosing member-access (a following <c>.Member</c>),
        /// such as the <c>Subscribe(...)</c> in <c>Subscribe(...).AddTo(...)</c>.
        /// In that case some outer call consumes the subscription, so this call is
        /// not the one discarding it.
        /// </summary>
        private static bool IsReceiverOfMemberAccess(InvocationExpressionSyntax invocation)
        {
            return invocation.Parent is MemberAccessExpressionSyntax memberAccess
                && memberAccess.Expression == invocation;
        }

        /// <summary>
        /// Returns true when the value of <paramref name="invocation"/> is thrown
        /// away: it is the whole expression of an expression statement (after
        /// unwrapping redundant parentheses). Any other position - assignment
        /// right-hand side (including a <c>_ =</c> discard), initializer, return,
        /// argument, await, member-access receiver, lambda body - consumes the
        /// value and is not flagged.
        /// </summary>
        private static bool IsResultDiscarded(InvocationExpressionSyntax invocation)
        {
            SyntaxNode current = invocation;
            SyntaxNode parent = current.Parent;

            // Unwrap any parentheses around the invocation: (x.Subscribe(...));
            while (parent is ParenthesizedExpressionSyntax)
            {
                current = parent;
                parent = current.Parent;
            }

            return parent is ExpressionStatementSyntax statement && statement.Expression == current;
        }

        /// <summary>
        /// Returns true when <paramref name="method"/> returns
        /// <c>System.IDisposable</c> or a type that implements it. A <c>void</c>
        /// method has no handle to dispose, so it never qualifies.
        /// </summary>
        private static bool ReturnsDisposable(IMethodSymbol method)
        {
            if (method.ReturnsVoid)
            {
                return false;
            }

            ITypeSymbol returnType = method.ReturnType;
            if (returnType.SpecialType == SpecialType.System_IDisposable)
            {
                return true;
            }

            foreach (INamedTypeSymbol implemented in returnType.AllInterfaces)
            {
                if (implemented.SpecialType == SpecialType.System_IDisposable)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
