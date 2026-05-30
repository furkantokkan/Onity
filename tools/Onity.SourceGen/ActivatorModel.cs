using System;
using System.Collections.Generic;

namespace Onity.SourceGen
{
    /// <summary>
    /// Equatable description of one activator to emit: the fully-qualified type
    /// name and the fully-qualified parameter types of the selected constructor,
    /// in order.
    /// </summary>
    /// <remarks>
    /// Incremental generator pipeline values must be value-equatable so Roslyn can
    /// cache and skip unchanged outputs. This struct implements
    /// <see cref="IEquatable{T}" /> with an ordered element comparison over
    /// <see cref="ParameterTypes" /> instead of relying on the list's reference
    /// equality.
    /// </remarks>
    internal readonly struct ActivatorModel : IEquatable<ActivatorModel>
    {
        /// <summary>
        /// Initializes a new <see cref="ActivatorModel" />.
        /// </summary>
        /// <param name="fullyQualifiedType">Fully-qualified name of the type to construct.</param>
        /// <param name="parameterTypes">Fully-qualified parameter type names of the selected constructor, in declaration order.</param>
        public ActivatorModel(string fullyQualifiedType, IReadOnlyList<string> parameterTypes)
        {
            FullyQualifiedType = fullyQualifiedType;
            ParameterTypes = parameterTypes;
        }

        /// <summary>
        /// Fully-qualified name of the type the activator constructs.
        /// </summary>
        public string FullyQualifiedType { get; }

        /// <summary>
        /// Fully-qualified parameter type names of the selected constructor, in
        /// declaration order. Empty for a parameterless constructor.
        /// </summary>
        public IReadOnlyList<string> ParameterTypes { get; }

        /// <inheritdoc />
        public bool Equals(ActivatorModel other)
        {
            if (!string.Equals(FullyQualifiedType, other.FullyQualifiedType, StringComparison.Ordinal))
            {
                return false;
            }

            IReadOnlyList<string> left = ParameterTypes;
            IReadOnlyList<string> right = other.ParameterTypes;

            if (left is null || right is null)
            {
                return left is null && right is null;
            }

            if (left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is ActivatorModel other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = FullyQualifiedType is null
                    ? 0
                    : StringComparer.Ordinal.GetHashCode(FullyQualifiedType);

                if (ParameterTypes != null)
                {
                    for (int i = 0; i < ParameterTypes.Count; i++)
                    {
                        string parameterType = ParameterTypes[i];
                        int element = parameterType is null
                            ? 0
                            : StringComparer.Ordinal.GetHashCode(parameterType);
                        hash = (hash * 397) ^ element;
                    }
                }

                return hash;
            }
        }
    }
}
