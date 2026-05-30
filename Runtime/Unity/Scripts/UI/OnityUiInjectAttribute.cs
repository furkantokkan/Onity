using System;

namespace Onity.Unity.UI
{
    /// <summary>
    /// Marks presenter properties for automatic UI service locator injection.
    /// Prefer <see cref="Onity.DI.InjectAttribute" /> for a unified injection attribute across Onity modules.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class OnityUiInjectAttribute : Attribute
    {
    }
}
