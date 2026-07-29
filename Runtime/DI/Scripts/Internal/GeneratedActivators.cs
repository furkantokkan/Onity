using System;
using System.Collections.Generic;
using System.Reflection;

namespace Onity.DI.Internal
{
    /// <summary>
    /// Process-wide registry for source-generated constructor activators.
    /// Generated code registers plain-C# <c>new T(...)</c> delegates here so
    /// <see cref="ActivatorCompiler" /> can prefer them over runtime
    /// <c>Expression.Compile</c> or <see cref="ConstructorInfo.Invoke(object[])" />.
    /// </summary>
    public static class GeneratedActivators
    {
        private static readonly object s_gate = new object();
        private static readonly Dictionary<Type, List<Entry>> s_entriesByType =
            new Dictionary<Type, List<Entry>>(128);
        private static int s_registeredCount;

        /// <summary>
        /// Gets the number of generated activator registrations currently known to
        /// this process. Intended for diagnostics and benchmark reporting.
        /// </summary>
        public static int RegisteredCount
        {
            get
            {
                lock (s_gate)
                {
                    return s_registeredCount;
                }
            }
        }

        /// <summary>
        /// Registers a generated activator for a type. This overload is kept for
        /// older generated code and matches any selected constructor for the type.
        /// Prefer <see cref="Register(Type, Type[], Func{object[], object})" /> so
        /// constructor signatures are validated before the activator is used.
        /// </summary>
        /// <param name="implementationType">Concrete type constructed by the activator.</param>
        /// <param name="activator">Plain-C# constructor delegate.</param>
        public static void Register(Type implementationType, Func<object[], object> activator)
        {
            RegisterCore(implementationType, null, activator);
        }

        /// <summary>
        /// Registers a generated activator for a concrete constructor signature.
        /// </summary>
        /// <param name="implementationType">Concrete type constructed by the activator.</param>
        /// <param name="constructorParameterTypes">Constructor parameter types in order.</param>
        /// <param name="activator">Plain-C# constructor delegate.</param>
        public static void Register(
            Type implementationType,
            Type[] constructorParameterTypes,
            Func<object[], object> activator)
        {
            if (constructorParameterTypes == null)
            {
                throw new ArgumentNullException(nameof(constructorParameterTypes));
            }

            Type[] parameterTypes = constructorParameterTypes.Length == 0
                ? Type.EmptyTypes
                : (Type[])constructorParameterTypes.Clone();

            RegisterCore(implementationType, parameterTypes, activator);
        }

        internal static bool TryGet(ConstructorInfo constructor, out ActivatorDelegate activator)
        {
            if (constructor == null)
            {
                throw new ArgumentNullException(nameof(constructor));
            }

            Type declaringType = constructor.DeclaringType;

            if (declaringType == null)
            {
                activator = null;
                return false;
            }

            ParameterInfo[] parameters = null;

            lock (s_gate)
            {
                if (s_entriesByType.TryGetValue(declaringType, out List<Entry> entries) == false)
                {
                    activator = null;
                    return false;
                }

                parameters = constructor.GetParameters();

                for (int i = 0; i < entries.Count; i++)
                {
                    Entry entry = entries[i];

                    if (entry.Matches(parameters) == false)
                    {
                        continue;
                    }

                    activator = entry.Activator.Invoke;
                    return true;
                }
            }

            activator = null;
            return false;
        }

        private static void RegisterCore(
            Type implementationType,
            Type[] constructorParameterTypes,
            Func<object[], object> activator)
        {
            if (implementationType == null)
            {
                throw new ArgumentNullException(nameof(implementationType));
            }

            if (activator == null)
            {
                throw new ArgumentNullException(nameof(activator));
            }

            lock (s_gate)
            {
                if (s_entriesByType.TryGetValue(implementationType, out List<Entry> entries) == false)
                {
                    entries = new List<Entry>(1);
                    s_entriesByType.Add(implementationType, entries);
                }

                for (int i = 0; i < entries.Count; i++)
                {
                    if (entries[i].HasSameSignature(constructorParameterTypes))
                    {
                        entries[i] = new Entry(constructorParameterTypes, activator);
                        return;
                    }
                }

                entries.Add(new Entry(constructorParameterTypes, activator));
                s_registeredCount++;
            }
        }

        private readonly struct Entry
        {
            public readonly Type[] ParameterTypes;
            public readonly Func<object[], object> Activator;

            public Entry(Type[] parameterTypes, Func<object[], object> activator)
            {
                ParameterTypes = parameterTypes;
                Activator = activator;
            }

            public bool Matches(ParameterInfo[] parameters)
            {
                Type[] parameterTypes = ParameterTypes;

                if (parameterTypes == null)
                {
                    return true;
                }

                if (parameters == null || parameters.Length != parameterTypes.Length)
                {
                    return false;
                }

                for (int i = 0; i < parameterTypes.Length; i++)
                {
                    if (parameters[i].ParameterType != parameterTypes[i])
                    {
                        return false;
                    }
                }

                return true;
            }

            public bool HasSameSignature(Type[] parameterTypes)
            {
                if (ParameterTypes == null || parameterTypes == null)
                {
                    return ParameterTypes == null && parameterTypes == null;
                }

                if (ParameterTypes.Length != parameterTypes.Length)
                {
                    return false;
                }

                for (int i = 0; i < ParameterTypes.Length; i++)
                {
                    if (ParameterTypes[i] != parameterTypes[i])
                    {
                        return false;
                    }
                }

                return true;
            }
        }
    }
}
