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
    /// Code fix stub for <see cref="OnityResolveInUpdateAnalyzer"/> (ONITY001).
    /// Inserts a guidance comment above the offending statement reminding the
    /// author to resolve once in an installer/Awake and cache the instance.
    /// </summary>
    /// <remarks>
    /// This is intentionally a non-destructive stub. A correct mechanical fix
    /// (hoisting the Resolve into a cached field and rewriting the call site)
    /// requires receiver/type binding and field generation that can change
    /// behavior; that is left to the developer. The comment marks the spot so the
    /// fix is one keystroke away from the diagnostic.
    /// </remarks>
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(OnityResolveInUpdateCodeFixProvider))]
    [Shared]
    public sealed class OnityResolveInUpdateCodeFixProvider : CodeFixProvider
    {
        private const string k_title = "Mark to resolve once in Awake and cache";

        private const string k_guidanceComment =
            "// ONITY001: resolve this once in an installer/Awake and cache it in a field instead of resolving every frame.";

        /// <inheritdoc />
        public override ImmutableArray<string> FixableDiagnosticIds
        {
            get { return ImmutableArray.Create(OnityDiagnostics.k_resolveInUpdateId); }
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
                    equivalenceKey: OnityDiagnostics.k_resolveInUpdateId);

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
