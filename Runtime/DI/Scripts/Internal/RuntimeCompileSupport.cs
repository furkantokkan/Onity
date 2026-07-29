using System;
using System.Linq.Expressions;

namespace Onity.DI.Internal
{
    /// <summary>
    /// Decides, once per process, whether the DI layer instantiates types and
    /// injects members through compiled <see cref="Expression.Compile" /> delegates
    /// or through reflection.
    /// <para>
    /// On JIT runtimes (the Unity Editor and Mono/.NET standalone players) dynamic
    /// code generation is available, the startup probe succeeds, and the fast path
    /// uses compiled constructor activators and member setters.
    /// </para>
    /// <para>
    /// On fully ahead-of-time runtimes (IL2CPP, console AOT) there is no JIT, so
    /// <see cref="Expression.Compile" /> can throw or return a delegate that throws
    /// on first invocation. The probe detects this and the DI layer falls back to
    /// reflection-based activation: slower per call, but allocation-comparable and
    /// guaranteed to run instead of crashing the container.
    /// </para>
    /// <para>
    /// <see cref="ForceReflection" /> (surfaced as
    /// <c>OnityContainer.ForceReflectionActivation</c>) forces the reflection path
    /// even on a JIT runtime, so a graph can be pre-flighted in the Editor under the
    /// exact activation strategy an IL2CPP build will use.
    /// </para>
    /// </summary>
    internal static class RuntimeCompileSupport
    {
        // Mirrors the real activator shape (cast args[0], pass it to a constructor)
        // so the probe stresses the same expression nodes the activators emit.
        private sealed class CompileProbe
        {
            public CompileProbe(object value)
            {
                Value = value;
            }

            public object Value { get; }
        }

        // Probed once on first access. The probe both compiles AND invokes the
        // lambda, because some AOT runtimes let Compile() succeed yet throw only when
        // the compiled delegate is first called.
        private static readonly bool s_expressionCompileWorks = Probe();

        /// <summary>
        /// When true, the DI layer uses reflection even where compilation is
        /// supported. Defaults to false (auto-detect). Set before the first container
        /// <c>Build()</c>; the per-process activator cache is populated on first use
        /// and is not re-evaluated afterward.
        /// </summary>
        internal static bool ForceReflection { get; set; }

        /// <summary>
        /// True when the DI layer should emit compiled activators and member setters;
        /// false when it must use reflection (an AOT/IL2CPP runtime, or
        /// <see cref="ForceReflection" /> is set).
        /// </summary>
        internal static bool IsExpressionCompileSupported
        {
            get { return !ForceReflection && s_expressionCompileWorks; }
        }

        private static bool Probe()
        {
            try
            {
                ParameterExpression argsParameter = Expression.Parameter(typeof(object[]), "args");
                BinaryExpression index = Expression.ArrayIndex(argsParameter, Expression.Constant(0));
                NewExpression construct = Expression.New(
                    typeof(CompileProbe).GetConstructors()[0],
                    Expression.Convert(index, typeof(object)));
                UnaryExpression body = Expression.Convert(construct, typeof(object));

                Func<object[], object> compiled =
                    Expression.Lambda<Func<object[], object>>(body, argsParameter).Compile();

                return compiled(new object[] { null }) is CompileProbe;
            }
            catch
            {
                // Any failure (compile-time or invoke-time) means dynamic code is not
                // usable on this runtime; the DI layer must use reflection.
                return false;
            }
        }
    }
}
