using System.Runtime.CompilerServices;

// The optional uGUI assembly (Runtime/Unity.UGUI) builds its EventSystems triggers on the internal trigger
// handler and its BindTo overloads on the internal binding loop.
[assembly: InternalsVisibleTo("Onity.Unity.UGUI")]
