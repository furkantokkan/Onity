using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Onity.Analyzers
{
    /// <summary>
    /// Code fix stub for <see cref="OnitySubscribeWithoutAddToAnalyzer"/>
    /// (ONITY003). Inserts a guidance comment above the offending statement
    /// reminding the author to dispose the subscription via <c>.AddTo(...)</c> or
    /// by storing the returned <c>IDisposable</c>.
    /// </summary>
    /// <remarks>
    /// This is intentionally a non-destructive stub, matching the ONITY001 fix. A
    /// real fix would append <c>.AddTo(scope)</c>, but the disposal scope to use
    /// (a CompositeDisposable field, a Unity destroy-cancellation token, and so on)
    /// is context-specific and cannot be chosen safely without binding. The comment
    /// marks the spot so the fix is one step away from the diagnostic.
    /// </remarks>
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(OnitySubscribeWithoutAddToCodeFixProvider))]
    [Shared]
    public sealed class OnitySubscribeWithoutAddToCodeFixProvider : CodeFixProvider
    {
        private const string k_title = "Mark to dispose subscription via AddTo or a stored IDisposable";

        private const string k_guidanceComment =
            "// ONITY003: dispose this subscription - chain .AddTo(scope) or store the returned IDisposable.";

        /// <inheritdoc />
        public override ImmutableArray<string> FixableDiagnosticIds
        {
            get { return ImmutableArray.Create(OnityDiagnostics.k_subscribeWithoutAddToId); }
        }

        /// <inheritdoc />
        public override FixAllProvider GetFixAllProvider()
        {
            return WellKnownFixAllProviders.BatchFixer;
        }

        /// <inheritdoc />
        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            SyntaxNode root = await context.Document
                .GetSyntaxRootAsync(context.CancellationToken)
                .ConfigureAwait(false);

            if (root == null)
            {
                return;
            }

            foreach (Diagnostic diagnostic in context.Diagnostics)
            {
                TextSpan span = diagnostic.Location.SourceSpan;
                SyntaxNode node = root.FindNode(span);

                StatementSyntax statement = node.FirstAncestorOrSelf<StatementSyntax>();
                if (statement == null)
                {
                    continue;
                }

                CodeAction action = CodeAction.Create(
                    title: k_title,
                    createChangedDocument: ct => AddGuidanceCommentAsync(context.Document, statement, ct),
                    equivalenceKey: OnityDiagnostics.k_subscribeWithoutAddToId);

                context.RegisterCodeFix(action, diagnostic);
            }
        }

        private static async Task<Document> AddGuidanceCommentAsync(
            Document document,
            StatementSyntax statement,
            CancellationToken cancellationToken)
        {
            SyntaxNode root = await document
                .GetSyntaxRootAsync(cancellationToken)
                .ConfigureAwait(false);

            if (root == null)
            {
                return document;
            }

            SyntaxTriviaList leading = statement.GetLeadingTrivia();

            // Skip if the guidance comment is already present to keep the fix idempotent.
            foreach (SyntaxTrivia trivia in leading)
            {
                if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
                    && trivia.ToString() == k_guidanceComment)
                {
                    return document;
                }
            }

            SyntaxTriviaList newLeading = leading
                .Add(SyntaxFactory.Comment(k_guidanceComment))
                .Add(SyntaxFactory.ElasticCarriageReturnLineFeed);

            StatementSyntax newStatement = statement.WithLeadingTrivia(newLeading);
            SyntaxNode newRoot = root.ReplaceNode(statement, newStatement);
            return document.WithSyntaxRoot(newRoot);
        }
    }
}
