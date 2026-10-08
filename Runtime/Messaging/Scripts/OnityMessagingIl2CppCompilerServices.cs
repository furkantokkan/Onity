// Unity compiles Onity.Messaging as its own assembly, so it carries its own internal copy of these types.
// The engine-free CI build (onity-core-ci.csproj) compiles Onity.Reactive and Onity.Messaging into one
// assembly, where Onity.Reactive's identical copy already defines them; this copy is therefore compiled only
// by Unity.
#if UNITY_5_3_OR_NEWER
using System;

// IL2CPP recognizes these attributes by their full names, so internal copies inside Onity.Messaging steer
// its code generation without a dependency on another assembly or on UnityEngine. The type and member names
// below are fixed by that contract, which is why they do not follow Onity's m_/k_ naming rules.
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
    /// the receivers are non-null by construction, array indexes stay inside the tracked count, and
    /// every public entry point validates its arguments.
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
}
#endif
