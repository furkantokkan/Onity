using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Onity.Analyzers
{
    /// <summary>
    /// ONITY003: reports a <c>Subscribe(...)</c> invocation whose returned
    /// <c>IDisposable</c> is discarded - the call is a standalone expression
    /// statement and is not chained into an <c>AddTo(...)</c>, assigned, returned,
    /// awaited, or passed as an argument.
    /// </summary>
    /// <remarks>
    /// The check is purely syntactic and high-confidence. It only fires when the
    /// <c>Subscribe</c> call (or a fluent chain whose outermost call is
    /// <c>Subscribe</c>) is the entire expression of an
    /// <see cref="ExpressionStatementSyntax"/>, which is exactly the shape that
    /// throws away the subscription handle. A <c>Subscribe</c> that is the receiver
    /// of a following <c>.AddTo(...)</c> is left to that outer call to own and is
    /// not flagged. It does not bind the receiver type, so a user method also named
    /// <c>Subscribe</c> that returns <c>void</c> would be flagged; that is accepted
    /// as a rare, low-cost false positive in exchange for zero project references.
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class OnitySubscribeWithoutAddToAnalyzer : DiagnosticAnalyzer
    {
        private const string k_subscribeMethodName = "Subscribe";

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

            if (!IsMemberCallNamed(invocation, k_subscribeMethodName))
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

            Diagnostic diagnostic = Diagnostic.Create(
                OnityDiagnostics.SubscribeWithoutAddTo,
                invocation.GetLocation());
            context.ReportDiagnostic(diagnostic);
        }

        /// <summary>
        /// Returns true when the invocation is a member call whose method name is
        /// <paramref name="name"/>, covering both <c>x.M(...)</c> and
        /// <c>x.M&lt;T&gt;(...)</c>.
        /// </summary>
        private static bool IsMemberCallNamed(InvocationExpressionSyntax invocation, string name)
        {
            if (!(invocation.Expression is MemberAccessExpressionSyntax memberAccess))
            {
                return false;
            }

            SimpleNameSyntax memberName = memberAccess.Name;
            return memberName != null && memberName.Identifier.ValueText == name;
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
        /// right-hand side, initializer, return, argument, await, member-access
        /// receiver, lambda body - consumes the value and is not flagged.
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
    }
}
