using System;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace Onity.DI.Internal
{
    /// <summary>
    /// Compiled constructor activator delegate. Replaces
    /// <see cref="ConstructorInfo.Invoke(object[])" /> on the resolve hot path
    /// so transient and complex graph resolves avoid reflection per call.
    /// </summary>
    /// <param name="args">Constructor argument array. Length must match the
    /// compiled constructor's parameter count. Unused indices may be null.</param>
    /// <returns>Newly constructed instance, boxed for value types.</returns>
    internal delegate object ActivatorDelegate(object[] args);

    /// <summary>
    /// Builds <see cref="ActivatorDelegate" /> instances for constructors. Uses
    /// <see cref="Expression.Compile" /> on runtimes where dynamic code is usable
    /// (see <see cref="RuntimeCompileSupport" />) and falls back to a
    /// reflection-based delegate otherwise: on AOT/IL2CPP runtimes, or for an
    /// individual constructor whose compilation throws on a stripped build. The
    /// resolve path is identical for both — only the per-constructor delegate
    /// differs — so the container runs instead of crashing where dynamic code is
    /// unavailable.
    /// </summary>
    internal static class ActivatorCompiler
    {
        // ConstructorInfo identity is stable per process, so each constructor is
        // compiled exactly once across every container Build. Reusing the
        // delegate removes the per-build Expression.Compile cost (warm Build) and
        // never touches the resolve hot path (only read during plan construction).
        private static readonly ConcurrentDictionary<ConstructorInfo, ActivatorDelegate> s_compiledActivators =
            new ConcurrentDictionary<ConstructorInfo, ActivatorDelegate>();

        private static readonly Func<ConstructorInfo, ActivatorDelegate> s_compileCore = CompileCore;

        /// <summary>
        /// Returns a cached activator for the given constructor, compiling it
        /// once per process on first request.
        /// </summary>
        /// <param name="constructor">Constructor to wrap.</param>
        /// <returns>Compiled activator delegate.</returns>
        public static ActivatorDelegate Compile(ConstructorInfo constructor)
        {
            if (constructor == null)
            {
                throw new ArgumentNullException(nameof(constructor));
            }

            return s_compiledActivators.GetOrAdd(constructor, s_compileCore);
        }

        private static ActivatorDelegate CompileCore(ConstructorInfo constructor)
        {
            if (RuntimeCompileSupport.IsExpressionCompileSupported == false)
            {
                return BuildReflectionActivator(constructor);
            }

            try
            {
                ParameterInfo[] parameters = constructor.GetParameters();
                ParameterExpression argsParameter = Expression.Parameter(typeof(object[]), "args");

                if (parameters.Length == 0)
                {
                    NewExpression noArgNew = Expression.New(constructor);
                    UnaryExpression noArgBody = Expression.Convert(noArgNew, typeof(object));
                    return Expression.Lambda<ActivatorDelegate>(noArgBody, argsParameter).Compile();
                }

                Expression[] argumentExpressions = new Expression[parameters.Length];

                for (int i = 0; i < parameters.Length; i++)
                {
                    BinaryExpression indexExpression = Expression.ArrayIndex(
                        argsParameter,
                        Expression.Constant(i));
                    argumentExpressions[i] = Expression.Convert(indexExpression, parameters[i].ParameterType);
                }

                NewExpression newExpression = Expression.New(constructor, argumentExpressions);
                UnaryExpression body = Expression.Convert(newExpression, typeof(object));
                return Expression.Lambda<ActivatorDelegate>(body, argsParameter).Compile();
            }
            catch (Exception)
            {
                // The runtime probe reported support, but this individual constructor
                // failed to compile (e.g. a type the AOT linker stripped). Reflection
                // still constructs it correctly.
                return BuildReflectionActivator(constructor);
            }
        }

        // The resolve path always hands the activator an exactly-sized argument
        // array: Array.Empty for parameterless constructors, otherwise an
        // ArgumentArrayPool buffer of the parameter count. So ConstructorInfo.Invoke
        // needs no length adjustment here.
        private static ActivatorDelegate BuildReflectionActivator(ConstructorInfo constructor)
        {
            return args => constructor.Invoke(args);
        }
    }
}
