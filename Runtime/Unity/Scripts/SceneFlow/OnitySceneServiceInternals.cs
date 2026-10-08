using System.Runtime.CompilerServices;

// The scene service's load backend and Loading-scene handoff are internal seams. The package's own test
// assemblies replace the backend with runtime-built scenes, so they see these internals.
[assembly: InternalsVisibleTo("Onity.Tests.EditMode")]
[assembly: InternalsVisibleTo("Onity.Tests.PlayMode")]
