using System;

namespace Onity.DI
{
    /// <summary>
    /// Marks constructors, fields, properties, or methods for dependency injection.
    /// </summary>
    [AttributeUsage(AttributeTargets.Constructor | AttributeTargets.Field | AttributeTargets.Property |
        AttributeTargets.Method | AttributeTargets.Parameter)]
    public sealed class InjectAttribute : Attribute
    {
        /// <summary>
        /// Identifies the binding used for this field, property, or parameter.
        /// Null selects the unkeyed binding. On constructors and methods,
        /// annotate individual parameters to select their identifiers.
        /// </summary>
        public object Id { get; set; }
    }
}
