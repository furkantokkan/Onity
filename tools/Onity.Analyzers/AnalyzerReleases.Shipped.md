; Shipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 1.0.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
ONITY001 | Onity.Usage | Warning | OnityResolveInUpdateAnalyzer: Resolve call inside Update/FixedUpdate/LateUpdate
ONITY002 | Onity.Usage | Warning | OnityRegisterAfterBuildAnalyzer: Bind/Resolve/BindInstance/BindFactory on a container after Build()
ONITY003 | Onity.Usage | Warning | OnitySubscribeWithoutAddToAnalyzer: Subscribe result discarded without AddTo or storing the IDisposable
ONITY004 | Onity.Usage | Warning | OnityMultipleInjectConstructorsAnalyzer: type declares 2+ constructors marked [Inject]
ONITY005 | Onity.Usage | Warning | OnityInvalidInjectMemberAnalyzer: [Inject] member with no setter, indexer, generic method, or static field/property/method
ONITY006 | Onity.Usage | Warning | OnityNewOnInjectableAnalyzer: 'new TService()' on a type the same file also binds/resolves through Onity
