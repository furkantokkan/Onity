# Typed builder context feasibility (2026-09-24)

**Verdict: NO-GO** for the proposed public-API path that captures
`ExecutionContext` while temporarily clearing `SynchronizationContext.Current`.
On the target Unity 2022 Mono runtime, the captured context changes what an
`AsyncLocal` change callback observes before the suspended method resumes.

## Reproduction

- Base product commit: `19fa8ab0f9c4cf77552cf964e7730cf201dc3175`.
- Runtime: Mono JIT 6.13.0 bundled with Unity 2022.3.62f3.
- Script host: bundled Roslyn `csi.exe` 3.7.0-5.20367.1.
- Probe: [`tools/probes/onitytask-builder-context.csx`](../../../tools/probes/onitytask-builder-context.csx).

Run from this repository root in PowerShell:

```powershell
$unityMono = 'C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Data\MonoBleedingEdge'
& "$unityMono\bin\mono.exe" "$unityMono\lib\mono\msbuild\Current\bin\Roslyn\csi.exe" tools/probes/onitytask-builder-context.csx
```

The native-style gate calls the registered continuation synchronously. Its
baseline uses `async Task<int>` and a custom `ICriticalNotifyCompletion`
awaiter. The candidate captures execution context with the synchronization
context temporarily set to `null`, restores it in `finally`, and runs the
continuation through `ExecutionContext.Run`. Registration and completion use
distinct named synchronization contexts. The trace starts immediately before
the gate signals.

```text
BASELINE=notify:completion>method:True:sc=completion|move:method:sc=completion|notify:method>completion:True:sc=completion
CANDIDATE=notify:completion>method:True:sc=null|move:method:sc=null|notify:method>completion:True:sc=completion
NO-GO: AsyncLocal notification or MoveNext context differs.
```

The first `AsyncLocal` notification happens before `MoveNext`. Restoring the
synchronization context inside the continuation would occur too late to make
that notification match baseline behavior. The script exits with code 1 when
the traces differ.

This is a standalone bundled-Mono feasibility probe. It is not a Unity Editor
test or performance benchmark. No Onity product code was changed.
