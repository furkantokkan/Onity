<!--
Thanks for contributing to Onity. Please fill in the sections below.
See CONTRIBUTING.md for build/test instructions and the honesty rules
(ZLinq is a real dependency; no verified zero-allocation claims).
-->

## Summary

<!-- What does this PR change, and why? -->

## Pillar(s) affected

<!-- Check all that apply. -->

- [ ] DI (`Onity.DI`)
- [ ] Reactive (`Onity.Reactive`)
- [ ] Events / Messaging (`Onity.Messaging`)
- [ ] Unity layer (`Onity.Unity`)
- [ ] Analyzer (`tools/Onity.Analyzers`)
- [ ] Docs only
- [ ] Other (describe below)

## How was this verified?

<!--
Describe how you tested the change. For behavior changes, list the tests you
added. Note whether it was verified in EditMode, PlayMode, or both.
-->

- [ ] Added/updated focused tests for the changed behavior
- [ ] EditMode tests pass
- [ ] PlayMode tests pass (if the change is Unity-integration specific)

## Checklist

- [ ] Follows the coding conventions (Unity naming `m_`/`s_`/`k_`, Allman
      braces, XML docs on public members, no `System.Linq`)
- [ ] Engine-free core assemblies stay free of `UnityEngine`
- [ ] Hot paths stay allocation-conscious (no per-emit/per-resolve allocations
      introduced)
- [ ] Claims in code/docs are accurate (ZLinq is a real dependency; no verified
      zero-allocation/"0 B/op" resolve claims; timing numbers carry the
      "indicative, not guaranteed" caveat)
- [ ] Changes are surgical (no unrelated reformatting/renames)
