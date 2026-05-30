using Microsoft.CodeAnalysis;

namespace Onity.Analyzers
{
    /// <summary>
    /// Central registry of Onity analyzer diagnostic identifiers and descriptors.
    /// Diagnostic ids use the <c>ONITY</c> prefix followed by a zero-padded number.
    /// </summary>
    public static class OnityDiagnostics
    {
        /// <summary>
        /// Category reported for every Onity usage diagnostic, surfaced in the
        /// IDE/compiler diagnostic list.
        /// </summary>
        public const string k_category = "Onity.Usage";

        /// <summary>
        /// ONITY001 id: a container <c>Resolve</c> call sits inside a per-frame
        /// Unity message (<c>Update</c>/<c>FixedUpdate</c>/<c>LateUpdate</c>).
        /// </summary>
        public const string k_resolveInUpdateId = "ONITY001";

        /// <summary>
        /// ONITY001 descriptor. The Onity AI Usage Guide forbids resolving inside
        /// per-frame methods: "DON'T <c>Resolve&lt;T&gt;()</c> inside
        /// <c>Update</c>/<c>FixedUpdate</c>/<c>LateUpdate</c> - resolve once in
        /// ctor/<c>Awake</c> and cache."
        /// </summary>
        public static readonly DiagnosticDescriptor ResolveInUpdate = new DiagnosticDescriptor(
            id: k_resolveInUpdateId,
            title: "Resolve call inside a per-frame Unity method",
            messageFormat: "'{0}.{1}' resolves from the container every frame; resolve once in an installer/Awake and cache it",
            category: k_category,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description:
                "Calling a container Resolve method inside Update, FixedUpdate, or LateUpdate runs a "
                + "resolution lookup on every frame, which allocates and costs CPU on the hot path. "
                + "Resolve the dependency once in an installer, constructor, or Awake and cache the "
                + "instance in a field.",
            helpLinkUri: "https://github.com/Onity/Onity/blob/main/docs/Onity-AI-Usage-Guide.md");

        /// <summary>
        /// ONITY002 id: a container binding/resolution call
        /// (<c>Bind</c>/<c>BindInstance</c>/<c>BindFactory</c>/<c>Resolve</c>) is
        /// made on a local after <c>Build()</c> was already called on that same
        /// local earlier in the method.
        /// </summary>
        public const string k_registerAfterBuildId = "ONITY002";

        /// <summary>
        /// ONITY002 descriptor. An <c>OnityContainer</c> is sealed by
        /// <c>Build()</c>; binding into it afterward either throws or silently
        /// has no effect on the already-baked resolution graph.
        /// </summary>
        public static readonly DiagnosticDescriptor RegisterAfterBuild = new DiagnosticDescriptor(
            id: k_registerAfterBuildId,
            title: "Container modified after Build()",
            messageFormat: "'{0}.{1}' is called after '{0}.Build()'; bindings must be registered before the container is built",
            category: k_category,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description:
                "Build() finalizes the container's resolution graph. Calling Bind, BindInstance, "
                + "BindFactory, or Resolve on the same container instance after Build() either throws "
                + "or does not affect the already-baked graph. Register every binding before calling "
                + "Build().",
            helpLinkUri: "https://github.com/Onity/Onity/blob/main/docs/Onity-AI-Usage-Guide.md");

        /// <summary>
        /// ONITY003 id: a <c>Subscribe(...)</c> result is discarded instead of
        /// being assigned, returned, awaited, passed onward, or chained into an
        /// <c>AddTo(...)</c> disposal scope.
        /// </summary>
        public const string k_subscribeWithoutAddToId = "ONITY003";

        /// <summary>
        /// ONITY003 descriptor. A <c>Subscribe</c> returns an
        /// <c>IDisposable</c> that owns the subscription; dropping it leaks the
        /// subscription for the lifetime of the source.
        /// </summary>
        public static readonly DiagnosticDescriptor SubscribeWithoutAddTo = new DiagnosticDescriptor(
            id: k_subscribeWithoutAddToId,
            title: "Subscribe result is not disposed",
            messageFormat: "The IDisposable returned by 'Subscribe' is discarded; chain '.AddTo(...)' or store it so the subscription can be disposed",
            category: k_category,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description:
                "Subscribe returns an IDisposable that controls the lifetime of the subscription. "
                + "Discarding it leaks the subscription until the source completes, which can keep "
                + "objects alive and run callbacks after the subscriber is gone. Chain .AddTo(...) "
                + "onto the subscription, assign it to a field, or return it so it can be disposed.",
            helpLinkUri: "https://github.com/Onity/Onity/blob/main/docs/Onity-AI-Usage-Guide.md");

        /// <summary>
        /// ONITY004 id: a type declares two or more constructors marked with
        /// <c>[Inject]</c>, leaving the injection constructor ambiguous.
        /// </summary>
        public const string k_multipleInjectConstructorsId = "ONITY004";

        /// <summary>
        /// ONITY004 descriptor. Onity selects a single <c>[Inject]</c>-marked
        /// constructor; declaring more than one is ambiguous and is rejected at
        /// resolution time.
        /// </summary>
        public static readonly DiagnosticDescriptor MultipleInjectConstructors = new DiagnosticDescriptor(
            id: k_multipleInjectConstructorsId,
            title: "Type has multiple [Inject] constructors",
            messageFormat: "Type '{0}' declares {1} constructors marked [Inject]; mark exactly one constructor with [Inject]",
            category: k_category,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description:
                "Onity resolves a type through a single injection constructor. When more than one "
                + "constructor is marked [Inject], the constructor to use is ambiguous and resolution "
                + "fails. Mark exactly one constructor with [Inject].",
            helpLinkUri: "https://github.com/Onity/Onity/blob/main/docs/Onity-AI-Usage-Guide.md");

        /// <summary>
        /// ONITY005 id: an <c>[Inject]</c> member cannot be injected as written -
        /// a property without a setter, an indexer property, a generic method, or a
        /// static field/property/method.
        /// </summary>
        public const string k_invalidInjectMemberId = "ONITY005";

        /// <summary>
        /// ONITY005 descriptor. Onity injects into instance, non-indexed, settable
        /// members and non-generic methods. A get-only property, an indexer, or a
        /// generic <c>[Inject]</c> method throws <c>OnityBindingException</c> when the
        /// type is built/resolved, and a static <c>[Inject]</c> member is silently
        /// never injected because only instance members are scanned. The
        /// <c>{0}</c>/<c>{1}</c>/<c>{2}</c> arguments name the member kind, the member,
        /// and the reason it cannot be injected.
        /// </summary>
        public static readonly DiagnosticDescriptor InvalidInjectMember = new DiagnosticDescriptor(
            id: k_invalidInjectMemberId,
            title: "[Inject] member cannot be injected",
            messageFormat: "[Inject] {0} '{1}' {2}",
            category: k_category,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description:
                "Onity injects into instance, non-indexed, settable members and non-generic methods. "
                + "An [Inject] property with no setter, an [Inject] indexer, or a generic [Inject] "
                + "method throws OnityBindingException when the owning type is built or resolved. A "
                + "static [Inject] field, property, or method is silently never injected because only "
                + "instance members are scanned. Add a (private) setter, make the property non-indexed, "
                + "make the method non-generic, or make the member an instance member.",
            helpLinkUri: "https://github.com/Onity/Onity/blob/main/docs/Onity-AI-Usage-Guide.md");

        /// <summary>
        /// ONITY006 id: a type is constructed with <c>new TService()</c> in the same
        /// file that also binds/resolves <c>TService</c> through Onity, bypassing the
        /// container.
        /// </summary>
        public const string k_newOnInjectableId = "ONITY006";

        /// <summary>
        /// ONITY006 descriptor. Constructing a type that the same file registers or
        /// resolves through Onity sidesteps the container: the instance gets no
        /// dependency injection and is not the resolved (often singleton) instance.
        /// This is guidance only and is intentionally restricted to types that are
        /// both <c>new</c>-ed and bound/resolved in the same file to keep false
        /// positives low. The <c>{0}</c> argument names the type.
        /// </summary>
        public static readonly DiagnosticDescriptor NewOnInjectable = new DiagnosticDescriptor(
            id: k_newOnInjectableId,
            title: "Manual construction of an Onity-managed type",
            messageFormat: "'{0}' is constructed with 'new' but is also bound/resolved through Onity in this file; resolve it from the container so it receives its dependencies",
            category: k_category,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description:
                "Calling 'new' on a type that this file also binds or resolves through Onity bypasses "
                + "the container: the manually constructed instance receives no constructor, field, "
                + "property, or method injection, and it is not the same instance the container "
                + "resolves (which is usually a single shared instance). Resolve the type from the "
                + "container, or inject it, instead of constructing it by hand. This check only fires "
                + "when the same file both constructs and binds/resolves the type, so factories and "
                + "value types that are intentionally hand-built elsewhere are not flagged.",
            helpLinkUri: "https://github.com/Onity/Onity/blob/main/docs/Onity-AI-Usage-Guide.md");
    }
}
