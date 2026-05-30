# Onity.Analyzers

Roslyn analyzers for the Onity framework. These run **inside the C# compiler**
(both `dotnet build` and the Unity script compilation pipeline) and surface
Onity-specific usage mistakes as compiler diagnostics.

This project is a standalone `netstandard2.0` class library, **not** a Unity
`asmdef`. It is built with `dotnet build` and the resulting DLL is imported into
Unity as a precompiled analyzer asset. It is engine-free and must never
reference `UnityEngine`.

## Diagnostic ids

| Id         | Title                                              | What it catches                                                                                                                                                          | Code fix |
| ---------- | -------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | -------- |
| `ONITY001` | Resolve call inside a per-frame Unity method       | A `Resolve<T>(...)` / `Resolve(...)` call sitting directly inside `Update` / `FixedUpdate` / `LateUpdate`. Resolve once in an installer/`Awake` and cache the instance. | Yes      |
| `ONITY002` | Container modified after `Build()`                 | A `Bind` / `BindInstance` / `BindFactory` / `Resolve` call on a container local after `Build()` was already called on that same local earlier in the method.            | No       |
| `ONITY003` | Subscribe result is not disposed                   | A `Subscribe(...)` result discarded as a standalone statement instead of `.AddTo(...)`, assigned, or returned. The subscription leaks.                                  | Yes      |
| `ONITY004` | Type has multiple `[Inject]` constructors          | A type declaring two or more constructors marked `[Inject]`, leaving the injection constructor ambiguous. Mark exactly one constructor with `[Inject]`.                 | No       |
| `ONITY005` | `[Inject]` member cannot be injected               | An `[Inject]` property with no setter, an `[Inject]` indexer, a generic `[Inject]` method, or a static `[Inject]` field/property/method.                                 | No       |
| `ONITY006` | Manual construction of an Onity-managed type       | A `new TService()` on a type the same file also binds/resolves through Onity, so the hand-built instance bypasses the container and its injection.                      | No       |

All six rules are `Onity.Usage` / `Warning` and enabled by default. Only
`ONITY001` and `ONITY003` ship a companion code-fix provider; each inserts a
non-destructive guidance comment above the offending statement (no auto-rewrite,
which would require type binding and behavior changes).

### How each rule decides

`ONITY002`, `ONITY003`, `ONITY004`, `ONITY005`, and `ONITY006` are purely
syntactic and high-confidence:

- `ONITY001` matches the member name `Resolve` on a member-access invocation
  whose enclosing method is `Update`/`FixedUpdate`/`LateUpdate`. A `Resolve`
  nested inside a lambda, local function, or property accessor declared within an
  `Update` is intentionally **not** flagged, because that code runs on a
  different cadence than the per-frame method body.
- `ONITY002` does a best-effort, straight-line intra-method dataflow walk over a
  single body. It records each `x.Build()` on a named receiver (`container` or
  `this.container`) and flags a later `x.Bind*`/`x.Resolve` on the same name. It
  does not reason across branches, lambdas, or method calls, so it only fires on
  the unambiguous "build then register on the same local" mistake.
- `ONITY003` fires only when the `Subscribe(...)` call (or a fluent chain whose
  outermost call is `Subscribe`) is the entire expression of an expression
  statement — exactly the shape that throws away the `IDisposable`. A
  `Subscribe(...).AddTo(...)` (or any chain consuming the result) is not flagged.
- `ONITY004` counts constructor declarations carrying an attribute whose simple
  name is `Inject`/`InjectAttribute`. Static constructors are ignored.
- `ONITY005` matches a member carrying an attribute whose simple name is
  `Inject`/`InjectAttribute` and inspects that member's own syntax (accessors,
  indexer parameter list, type-parameter list, `static` modifier). It mirrors the
  runtime checks in `OnityContainer`: a property with no set/init accessor, an
  indexer, or a generic `[Inject]` method throws `OnityBindingException`, and a
  static `[Inject]` member is silently skipped because only instance members are
  scanned.
- `ONITY006` is per-file guidance. It collects the simple type names a file
  binds/resolves through a generic `Bind<T>`/`BindInstance<T>`/`BindFactory<T>`/
  `Resolve<T>` call and the `new T(...)` sites in the same file, then flags a
  `new T(...)` whose simple type name also appears as a bound/resolved type. It
  fires only when one file both constructs and binds/resolves the type, so
  factories and intentionally hand-built types are not flagged.

`ONITY001` enforces the Onity AI Usage Guide rule:

> DON'T `Resolve<T>()` inside `Update`/`FixedUpdate`/`LateUpdate` — resolve once
> in ctor/`Awake` and cache.

## Building

Build the analyzer DLL with the .NET SDK. Run from the repository root:

```sh
dotnet build "tools/Onity.Analyzers/Onity.Analyzers.csproj" -c Release
```

The compiled assembly is emitted to:

```
tools/Onity.Analyzers/bin/Release/Onity.Analyzers.dll
```

(A `-c Debug` build emits to `bin/Debug/Onity.Analyzers.dll`. Ship the
`Release` DLL to Unity.)

## Consuming the DLL in Unity

Unity loads a managed plugin as an analyzer when the DLL carries the
**`RoslynAnalyzer`** asset label and is **excluded from every platform** (so it
never ships in a player build — it only participates in compilation). Unity
**2022.3+ and Unity 6** apply such a project-wide labeled analyzer to **all**
user assembly definitions automatically.

Steps:

1. **Build the DLL** (Release):

   ```sh
   dotnet build "tools/Onity.Analyzers/Onity.Analyzers.csproj" -c Release
   ```

2. **Copy the DLL into the Unity project.** Place `Onity.Analyzers.dll` under,
   for example:

   ```
   Assets/Onity-Packages/Onity/Analyzers/Onity.Analyzers.dll
   ```

   Copy **only** `Onity.Analyzers.dll`. Do **not** copy the
   `Microsoft.CodeAnalysis.*` dependencies — Unity already provides the Roslyn
   assemblies its compiler runs against.

3. **Set the asset label to `RoslynAnalyzer`.** Select the DLL in the Project
   window. In the **Inspector**, open the **Asset Labels** popup (the blue label
   icon at the bottom of the Inspector) and add the label `RoslynAnalyzer`.

4. **Uncheck all platforms in the plugin importer.** Still in the Inspector
   (Plugin importer), under **Select platforms for plugin**, uncheck **Any
   Platform** and uncheck **every individual platform** so the plugin is
   included in none. An analyzer must not be a runtime dependency.

5. **Apply.** Unity reimports and feeds the analyzer to its C# compiler. The
   `ONITY001`–`ONITY006` warnings now appear in the **Console** and in IDEs that
   read Unity's generated `.csproj` files.

### Scoping the analyzer to specific assemblies (optional)

By default the labeled DLL applies to all user assemblies. To run the analyzer
only against chosen assembly definitions, reference the labeled DLL from the
specific `asmdef` analyzer reference list instead of relying on the project-wide
default.

## Layout

```
Onity.Analyzers/
  Onity.Analyzers.csproj                        netstandard2.0, references Microsoft.CodeAnalysis.CSharp(.Workspaces) 4.x
  OnityDiagnostics.cs                           diagnostic ids + descriptors (ONITY001..ONITY006)
  OnityResolveInUpdateAnalyzer.cs               ONITY001 DiagnosticAnalyzer
  OnityResolveInUpdateCodeFixProvider.cs        ONITY001 code fix (guidance-comment stub)
  OnityRegisterAfterBuildAnalyzer.cs            ONITY002 DiagnosticAnalyzer
  OnitySubscribeWithoutAddToAnalyzer.cs         ONITY003 DiagnosticAnalyzer
  OnitySubscribeWithoutAddToCodeFixProvider.cs  ONITY003 code fix (guidance-comment stub)
  OnityMultipleInjectConstructorsAnalyzer.cs    ONITY004 DiagnosticAnalyzer
  OnityInvalidInjectMemberAnalyzer.cs           ONITY005 DiagnosticAnalyzer
  OnityNewOnInjectableAnalyzer.cs               ONITY006 DiagnosticAnalyzer
  AnalyzerReleases.Shipped.md                   shipped rule manifest (release 1.0.0; satisfies RS2008)
  AnalyzerReleases.Unshipped.md                 unshipped rule manifest (empty)
  README.md                                     this file
  Tests/                                        dotnet-test suite (NOT a Unity asmdef; excluded from the analyzer DLL)
    Onity.Analyzers.Tests.csproj                net8.0 test project (NUnit + Roslyn analyzer test harness)
    OnityAnalyzerTestVerifier.cs                minimal NUnit IVerifier + CSharpAnalyzerTest helper
    OnityAnalyzerRulesTests.cs                  ONITY002/003/004 positive + negative cases
    OnityInjectMemberAndNewRulesTests.cs        ONITY005/006 positive + negative cases
```

The `Tests/` project is built and run with `dotnet test` and is intentionally
excluded from the analyzer assembly (see the `Compile Remove` item in the
`.csproj`) so the engine-free analyzer DLL never carries NUnit or the test
harness. Run it with:

```sh
dotnet test "tools/Onity.Analyzers/Tests/Onity.Analyzers.Tests.csproj" -c Debug
```
