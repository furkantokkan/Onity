# Onity.SourceGen

A Roslyn **incremental source generator** for the Onity framework. It emits
**AOT-safe constructor activators** so IL2CPP (and any full-AOT runtime) keeps
compiled-activator speed **without** `System.Linq.Expressions.Expression.Compile`.

This project runs **inside the C# compiler** (both `dotnet build` and Unity's
script compilation pipeline). It is a standalone `netstandard2.0` class library,
**not** a Unity `asmdef`, and is engine-free — it must never reference
`UnityEngine`.

## Why this exists

`Onity.DI`'s `ActivatorCompiler` builds a constructor activator delegate per
type. On runtimes that support dynamic code it uses `Expression.Compile` for
fast, allocation-light construction; on AOT/IL2CPP it falls back to reflection
(`ConstructorInfo.Invoke`), which is correct but slower and allocates an `object`
boxing path through reflection.

This generator removes that AOT penalty: at **compile time** it bakes a plain-C#
activator —

```csharp
private static object Activate_0(object[] args)
{
    return new global::MyGame.PlayerService((global::MyGame.IClock)args[0], (global::MyGame.ILogger)args[1]);
}
```

— with **no reflection** and **no `Expression.Compile`**. The compiler turns
this into direct IL the AOT toolchain can fully ahead-of-time compile, so the
resolve hot path runs at compiled speed on every platform.

## How it selects types

The generator emits an activator for every **non-abstract, non-static,
non-generic** class or struct marked with an attribute whose **simple name** is
`OnityGenerateActivator` (with or without the `Attribute` suffix). Matching is by
simple name only, so the marker attribute can live in any namespace.

```csharp
[OnityGenerateActivator]
public sealed class PlayerService
{
    public PlayerService(IClock clock, ILogger logger) { /* ... */ }
}
```

Constructor selection mirrors the runtime rule for constructors that generated
source can call: a single accessible `[Inject]` constructor wins; otherwise the
public/internal constructor with the best score wins. Private/protected
constructors stay on the normal runtime fallback path because generated user
source cannot call them safely.

## What it emits

One file, `OnityGeneratedActivators.g.cs`, containing an internal static class
in namespace `Onity.SourceGen.Generated` with:

- one `private static object Activate_N(object[] args)` method per selected type
  that does `new T((TArg0)args[0], (TArg1)args[1], ...)`, and
- a single `[System.Runtime.CompilerServices.ModuleInitializer]` method that
  registers each activator by calling, by fully-qualified name:

  ```csharp
  Onity.DI.Internal.GeneratedActivators.Register(
      typeof(T),
      new[] { typeof(TArg0), typeof(TArg1) },
      Activate_N);
  ```

The module initializer runs once when the generated assembly is loaded, so
registration happens automatically with no bootstrap call.

## Runtime hook

`Onity.DI.Internal.GeneratedActivators` is the runtime registry used by the
generated module initializer. `ActivatorCompiler` checks this registry before the
`Expression.Compile` / reflection path, so a generated activator is preferred
when it matches the constructor signature selected by the container plan.

Other follow-up work beyond this scaffold:

- **Member-setter injection** — the scaffold only covers constructor injection.
  Generated activators do not yet set `[Inject]` properties/fields.
- **Type discovery without an attribute** — selection is currently opt-in via the
  `OnityGenerateActivator` attribute. A future version may discover Onity-managed
  types from binding sites instead of requiring the marker.

## Building

Build the generator DLL with the .NET SDK. Run from the repository root:

```sh
dotnet build "tools/Onity.SourceGen/Onity.SourceGen.csproj" -c Release
```

The compiled assembly is emitted to:

```
tools/Onity.SourceGen/bin/Release/Onity.SourceGen.dll
```

## Consuming the DLL in Unity

Like `Onity.Analyzers`, Unity loads this as a **`RoslynAnalyzer`**-labeled
managed plugin that is **excluded from every platform** (it participates only in
compilation, never in a player build). Copy **only** `Onity.SourceGen.dll` into
the Unity project (for example under
`Assets/Onity-Packages/Onity/Analyzers/`), set its asset label to
`RoslynAnalyzer`, uncheck **Any Platform** and every individual platform in the
plugin importer, and apply. Do **not** copy the `Microsoft.CodeAnalysis.*`
dependencies — Unity already provides the Roslyn assemblies its compiler runs
against. See `tools/Onity.Analyzers/README.md` for the full step-by-step import
procedure; it is identical for this DLL.

Generated activators register automatically via the module initializer when the
compiled assembly loads; no Unity-side bootstrap call is required.

## Layout

```
Onity.SourceGen/
  Onity.SourceGen.csproj        netstandard2.0, IsRoslynComponent, references Microsoft.CodeAnalysis.CSharp 4.x
  OnityActivatorGenerator.cs    IIncrementalGenerator: selects [OnityGenerateActivator] types, emits activators + module initializer
  ActivatorModel.cs             equatable per-type model used by the incremental pipeline
  README.md                     this file
```
