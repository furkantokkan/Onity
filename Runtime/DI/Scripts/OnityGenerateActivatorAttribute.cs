using System;

namespace Onity.DI
{
    /// <summary>
    /// Marks a DI-managed type for AOT-safe constructor activator generation.
    /// When the Onity source generator is installed, marked types receive a
    /// generated plain-C# activator that is registered with the DI runtime before
    /// the container builds its construction plan.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
    public sealed class OnityGenerateActivatorAttribute : Attribute
    {
    }
}
