using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;

namespace Onity.Analyzers.Tests
{
    /// <summary>
    /// Minimal NUnit-backed <see cref="IVerifier"/> for the Roslyn analyzer test
    /// harness. The official test-framework adapter packages
    /// (<c>Microsoft.CodeAnalysis.*.Testing.NUnit</c>) are not available in this
    /// environment, so the suite supplies its own thin adapter that routes the
    /// harness assertions to <see cref="Assert"/>.
    /// </summary>
    internal sealed class OnityNUnitVerifier : IVerifier
    {
        private readonly ImmutableStack<string> m_context;

        /// <summary>Creates a verifier with an empty assertion context stack.</summary>
        public OnityNUnitVerifier()
            : this(ImmutableStack<string>.Empty)
        {
        }

        private OnityNUnitVerifier(ImmutableStack<string> context)
        {
            m_context = context;
        }

        /// <inheritdoc />
        public void Empty<T>(string collectionName, IEnumerable<T> collection)
        {
            int count = 0;
            foreach (T _ in collection)
            {
                count++;
            }

            if (count != 0)
            {
                Assert.Fail(WithContext($"'{collectionName}' is not empty (count {count})."));
            }
        }

        /// <inheritdoc />
        public void NotEmpty<T>(string collectionName, IEnumerable<T> collection)
        {
            foreach (T _ in collection)
            {
                return;
            }

            Assert.Fail(WithContext($"'{collectionName}' is empty."));
        }

        /// <inheritdoc />
        public void Equal<T>(T expected, T actual, string message = null)
        {
            EqualityComparer<T> comparer = EqualityComparer<T>.Default;
            if (!comparer.Equals(expected, actual))
            {
                Assert.Fail(WithContext(message ?? $"Expected '{expected}' but found '{actual}'."));
            }
        }

        /// <inheritdoc />
        public void True(bool assert, string message = null)
        {
            if (!assert)
            {
                Assert.Fail(WithContext(message ?? "Expected condition to be true."));
            }
        }

        /// <inheritdoc />
        public void False(bool assert, string message = null)
        {
            if (assert)
            {
                Assert.Fail(WithContext(message ?? "Expected condition to be false."));
            }
        }

        /// <inheritdoc />
        public void Fail(string message = null)
        {
            Assert.Fail(WithContext(message ?? "Verification failed."));
        }

        /// <inheritdoc />
        public void LanguageIsSupported(string language)
        {
            // Both analyzers and the harness are C#-only; nothing extra to assert.
        }

        /// <inheritdoc />
        public void SequenceEqual<T>(
            IEnumerable<T> expected,
            IEnumerable<T> actual,
            IEqualityComparer<T> equalityComparer = null,
            string message = null)
        {
            IEqualityComparer<T> comparer = equalityComparer ?? EqualityComparer<T>.Default;

            using (IEnumerator<T> expectedEnumerator = expected.GetEnumerator())
            using (IEnumerator<T> actualEnumerator = actual.GetEnumerator())
            {
                while (true)
                {
                    bool expectedNext = expectedEnumerator.MoveNext();
                    bool actualNext = actualEnumerator.MoveNext();

                    if (expectedNext != actualNext)
                    {
                        Assert.Fail(WithContext(message ?? "Sequences differ in length."));
                        return;
                    }

                    if (!expectedNext)
                    {
                        return;
                    }

                    if (!comparer.Equals(expectedEnumerator.Current, actualEnumerator.Current))
                    {
                        Assert.Fail(WithContext(
                            message ?? $"Sequences differ: expected '{expectedEnumerator.Current}' but found '{actualEnumerator.Current}'."));
                        return;
                    }
                }
            }
        }

        /// <inheritdoc />
        public IVerifier PushContext(string context)
        {
            return new OnityNUnitVerifier(m_context.Push(context));
        }

        private string WithContext(string message)
        {
            if (m_context.IsEmpty)
            {
                return message;
            }

            List<string> frames = new List<string>();
            foreach (string frame in m_context)
            {
                frames.Add(frame);
            }

            frames.Reverse();
            return string.Join(" > ", frames) + ": " + message;
        }
    }

    /// <summary>
    /// Helper that runs a single <typeparamref name="TAnalyzer"/> against a source
    /// snippet using the Roslyn analyzer test harness wired to
    /// <see cref="OnityNUnitVerifier"/>.
    /// </summary>
    /// <typeparam name="TAnalyzer">The analyzer under test.</typeparam>
    internal static class OnityAnalyzerVerifier<TAnalyzer>
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        /// <summary>
        /// Builds a diagnostic-result descriptor for the given id so tests can
        /// declare expected diagnostics with <c>.WithSpan(...).WithArguments(...)</c>.
        /// </summary>
        public static DiagnosticResult Diagnostic(string diagnosticId)
        {
            return new DiagnosticResult(diagnosticId, DiagnosticSeverity.Warning);
        }

        /// <summary>
        /// Compiles <paramref name="source"/> with the analyzer and asserts that the
        /// produced diagnostics exactly match <paramref name="expected"/>.
        /// </summary>
        public static void Verify(string source, params DiagnosticResult[] expected)
        {
            Test test = new Test
            {
                TestCode = source,
            };

            test.ExpectedDiagnostics.AddRange(expected);
            test.Run();
        }

        private sealed class Test : CSharpAnalyzerTest<TAnalyzer, OnityNUnitVerifier>
        {
            /// <summary>
            /// Runs the harness synchronously so failures surface as NUnit
            /// assertion failures on the calling test thread.
            /// </summary>
            public void Run()
            {
                RunAsync().GetAwaiter().GetResult();
            }
        }
    }
}
