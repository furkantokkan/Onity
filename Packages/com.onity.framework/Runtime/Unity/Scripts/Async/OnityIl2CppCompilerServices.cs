using System;

// IL2CPP recognizes these attributes by their full names, so internal copies inside Onity.Unity steer
// its code generation without a dependency on a public copy. The type and member names below are
// fixed by that contract, which is why they do not follow Onity's m_/k_ naming rules.
namespace Unity.IL2CPP.CompilerServices
{
    /// <summary>IL2CPP code generation checks that <see cref="Il2CppSetOptionAttribute"/> can turn off.</summary>
    internal enum Option
    {
        NullChecks = 1,
        ArrayBoundsChecks = 2,
        DivideByZeroChecks = 3,
    }

    /// <summary>
    /// Changes one IL2CPP code generation check for the annotated member or type. Applied only where
    /// the receivers are non-null by construction and every public entry point validates its arguments.
    /// </summary>
    [AttributeUsage(
        AttributeTargets.Assembly | AttributeTargets.Struct | AttributeTargets.Class | AttributeTargets.Method
        | AttributeTargets.Property | AttributeTargets.Delegate,
        Inherited = false,
        AllowMultiple = true)]
    internal sealed class Il2CppSetOptionAttribute : Attribute
    {
        public Il2CppSetOptionAttribute(Option option, object value)
        {
            Option = option;
            Value = value;
        }

        public Option Option { get; }

        public object Value { get; }
    }

    /// <summary>
    /// Runs the static constructor of the annotated type when IL2CPP starts the player, so accesses to
    /// its static fields carry no class-initialization check. The constructor must not call Unity APIs.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
    internal sealed class Il2CppEagerStaticClassConstructionAttribute : Attribute
    {
    }
}
