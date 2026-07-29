using System;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace Onity.DI.Internal
{
    /// <summary>
    /// Compiled member setter delegate. Replaces
    /// <see cref="FieldInfo.SetValue(object, object)" /> and
    /// <see cref="PropertyInfo.SetValue(object, object, object[])" /> on the
    /// member-injection path so field and property injection avoid reflection
    /// per call.
    /// </summary>
    /// <param name="target">Instance receiving the injected value.</param>
    /// <param name="value">Resolved dependency to assign, boxed for value types.</param>
    internal delegate void MemberSetterDelegate(object target, object value);

    /// <summary>
    /// Compiled inject-method invoker delegate. Replaces
    /// <see cref="MethodInfo.Invoke(object, object[])" /> on the member-injection
    /// path so [Inject] methods avoid reflection per call.
    /// </summary>
    /// <param name="target">Instance whose method is invoked.</param>
    /// <param name="args">Argument array. Length must match the compiled
    /// method's parameter count. May be empty for parameterless methods.</param>
    internal delegate void MethodInvokerDelegate(object target, object[] args);

    /// <summary>
    /// Builds <see cref="MemberSetterDelegate" /> and
    /// <see cref="MethodInvokerDelegate" /> instances for fields, properties, and
    /// [Inject] methods. Mirrors <see cref="ActivatorCompiler" />: uses
    /// <see cref="Expression.Compile" /> where dynamic code is usable (see
    /// <see cref="RuntimeCompileSupport" />) and falls back to reflection otherwise
    /// (AOT/IL2CPP runtimes, or a member whose compilation throws on a stripped
    /// build). Each member is built exactly once per process and reused across every
    /// container build, so the cost never touches the resolve hot path (only read
    /// during plan construction).
    /// </summary>
    internal static class MemberSetterCompiler
    {
        // MemberInfo identity is stable per process, so each member is compiled
        // exactly once across every container Build. The compiled delegate is read
        // only during plan construction, never on the resolve hot path.
        private static readonly ConcurrentDictionary<FieldInfo, MemberSetterDelegate> s_compiledFieldSetters =
            new ConcurrentDictionary<FieldInfo, MemberSetterDelegate>();

        private static readonly ConcurrentDictionary<PropertyInfo, MemberSetterDelegate> s_compiledPropertySetters =
            new ConcurrentDictionary<PropertyInfo, MemberSetterDelegate>();

        private static readonly ConcurrentDictionary<MethodInfo, MethodInvokerDelegate> s_compiledMethodInvokers =
            new ConcurrentDictionary<MethodInfo, MethodInvokerDelegate>();

        private static readonly Func<FieldInfo, MemberSetterDelegate> s_compileFieldSetter = CompileFieldSetterCore;
        private static readonly Func<PropertyInfo, MemberSetterDelegate> s_compilePropertySetter = CompilePropertySetterCore;
        private static readonly Func<MethodInfo, MethodInvokerDelegate> s_compileMethodInvoker = CompileMethodInvokerCore;

        /// <summary>
        /// Returns a cached setter for the given field, compiling it once per
        /// process on first request.
        /// </summary>
        /// <param name="field">Field to wrap.</param>
        /// <returns>Compiled field setter delegate.</returns>
        public static MemberSetterDelegate CompileFieldSetter(FieldInfo field)
        {
            if (field == null)
            {
                throw new ArgumentNullException(nameof(field));
            }

            return s_compiledFieldSetters.GetOrAdd(field, s_compileFieldSetter);
        }

        /// <summary>
        /// Returns a cached setter for the given property, compiling it once per
        /// process on first request.
        /// </summary>
        /// <param name="property">Property to wrap. Must declare a set accessor.</param>
        /// <returns>Compiled property setter delegate.</returns>
        public static MemberSetterDelegate CompilePropertySetter(PropertyInfo property)
        {
            if (property == null)
            {
                throw new ArgumentNullException(nameof(property));
            }

            return s_compiledPropertySetters.GetOrAdd(property, s_compilePropertySetter);
        }

        /// <summary>
        /// Returns a cached invoker for the given method, compiling it once per
        /// process on first request.
        /// </summary>
        /// <param name="method">Method to wrap.</param>
        /// <returns>Compiled method invoker delegate.</returns>
        public static MethodInvokerDelegate CompileMethodInvoker(MethodInfo method)
        {
            if (method == null)
            {
                throw new ArgumentNullException(nameof(method));
            }

            return s_compiledMethodInvokers.GetOrAdd(method, s_compileMethodInvoker);
        }

        private static MemberSetterDelegate CompileFieldSetterCore(FieldInfo field)
        {
            if (RuntimeCompileSupport.IsExpressionCompileSupported == false)
            {
                return BuildReflectionFieldSetter(field);
            }

            try
            {
                ParameterExpression targetParameter = Expression.Parameter(typeof(object), "target");
                ParameterExpression valueParameter = Expression.Parameter(typeof(object), "value");

                Expression typedTarget = ConvertTarget(targetParameter, field.DeclaringType);
                Expression typedValue = Expression.Convert(valueParameter, field.FieldType);

                MemberExpression fieldAccess = Expression.Field(typedTarget, field);
                BinaryExpression assignment = Expression.Assign(fieldAccess, typedValue);

                return Expression.Lambda<MemberSetterDelegate>(assignment, targetParameter, valueParameter).Compile();
            }
            catch (Exception)
            {
                return BuildReflectionFieldSetter(field);
            }
        }

        private static MemberSetterDelegate CompilePropertySetterCore(PropertyInfo property)
        {
            if (RuntimeCompileSupport.IsExpressionCompileSupported == false)
            {
                return BuildReflectionPropertySetter(property);
            }

            try
            {
                // Use the set accessor directly so private setters compile the same way
                // public ones do, matching PropertyInfo.SetValue(target, value, null).
                MethodInfo setMethod = property.GetSetMethod(true);

                ParameterExpression targetParameter = Expression.Parameter(typeof(object), "target");
                ParameterExpression valueParameter = Expression.Parameter(typeof(object), "value");

                Expression typedTarget = ConvertTarget(targetParameter, property.DeclaringType);
                Expression typedValue = Expression.Convert(valueParameter, property.PropertyType);

                MethodCallExpression setterCall = Expression.Call(typedTarget, setMethod, typedValue);

                return Expression.Lambda<MemberSetterDelegate>(setterCall, targetParameter, valueParameter).Compile();
            }
            catch (Exception)
            {
                return BuildReflectionPropertySetter(property);
            }
        }

        private static MethodInvokerDelegate CompileMethodInvokerCore(MethodInfo method)
        {
            if (RuntimeCompileSupport.IsExpressionCompileSupported == false)
            {
                return BuildReflectionMethodInvoker(method);
            }

            try
            {
                ParameterExpression targetParameter = Expression.Parameter(typeof(object), "target");
                ParameterExpression argsParameter = Expression.Parameter(typeof(object[]), "args");

                Expression typedTarget = ConvertTarget(targetParameter, method.DeclaringType);
                ParameterInfo[] parameters = method.GetParameters();

                if (parameters.Length == 0)
                {
                    MethodCallExpression noArgCall = Expression.Call(typedTarget, method);
                    return Expression.Lambda<MethodInvokerDelegate>(noArgCall, targetParameter, argsParameter).Compile();
                }

                Expression[] argumentExpressions = new Expression[parameters.Length];

                for (int i = 0; i < parameters.Length; i++)
                {
                    BinaryExpression indexExpression = Expression.ArrayIndex(
                        argsParameter,
                        Expression.Constant(i));
                    argumentExpressions[i] = Expression.Convert(indexExpression, parameters[i].ParameterType);
                }

                MethodCallExpression call = Expression.Call(typedTarget, method, argumentExpressions);
                return Expression.Lambda<MethodInvokerDelegate>(call, targetParameter, argsParameter).Compile();
            }
            catch (Exception)
            {
                return BuildReflectionMethodInvoker(method);
            }
        }

        // Reflection fallbacks used on AOT/IL2CPP runtimes (and as a per-member
        // safety net). The resolve path hands setters (target, value) directly and
        // hands invokers an exactly-sized argument array (Array.Empty for
        // parameterless methods), so no length adjustment is needed.
        private static MemberSetterDelegate BuildReflectionFieldSetter(FieldInfo field)
        {
            return (target, value) => field.SetValue(target, value);
        }

        private static MemberSetterDelegate BuildReflectionPropertySetter(PropertyInfo property)
        {
            // PropertyInfo.SetValue invokes the property's set accessor, including a
            // non-public one, matching the compiled path's GetSetMethod(true).
            return (target, value) => property.SetValue(target, value);
        }

        private static MethodInvokerDelegate BuildReflectionMethodInvoker(MethodInfo method)
        {
            return (target, args) => method.Invoke(target, args);
        }

        // A boxed value type must be unboxed (not reference-converted) before its
        // members can be touched. Reference types use a plain cast. This keeps the
        // compiled behavior identical to reflection for both class and struct
        // declaring types.
        private static Expression ConvertTarget(ParameterExpression targetParameter, Type declaringType)
        {
            if (declaringType.IsValueType)
            {
                return Expression.Unbox(targetParameter, declaringType);
            }

            return Expression.Convert(targetParameter, declaringType);
        }
    }
}
