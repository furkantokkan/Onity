using System;

namespace Onity.DI
{
    /// <summary>
    /// Marks constructors, fields, properties, or methods for dependency injection.
    /// </summary>
    [AttributeUsage(AttributeTargets.Constructor | AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public sealed class InjectAttribute : Attribute
    {
    }
}
