using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.GovernanceSecurity;
using MachiVerse.Simulation.Core.Domains.Participation;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

internal static class GovernanceSecuritySnapshotMaterialSmoke
{
    [ModuleInitializer]
    internal static void Initialize() => Run();

    internal static void Run()
    {
        VerifyTypedOwnerRoots();
        VerifyJurisdictionRoundTrip();
        VerifyRuleAstRoundTrip();
    }

    private static void VerifyTypedOwnerRoots()
    {
        var frozen = Qa04ReferenceWorldMaterializerV1.MaterializeResidentIdentityLifecycle(1).WorldState;
        var state = GovernanceSecurityDomainStateV1.CreateEmpty();
        var material = state.BindSnapshotMaterial(frozen);
        var providers = GovernanceSecurityDomainSnapshotProviderV1.CreateAll();
        Require(material.Authorities.Count == 17 && providers.Count == 17,
            "Governance/Security must own exactly 17 typed roots/providers.");
        Require(providers.Select(x => x.SectionId).SequenceEqual(material.Authorities.Select(x => x.PartitionId.Value)),
            "Governance/Security provider/root sets must match in canonical order.");
        foreach (var authority in material.Authorities)
        {
            var provider = providers.Single(x => x.SectionId == authority.PartitionId.Value);
            var section = provider.Create(authority);
            Require(section.LogicalItemCount == 0 && section.Fragments.Count == 1,
                $"Governance typed empty root must emit one empty fragment: {authority.PartitionId.Value}.");
            Require(section.Fragments[0].ItemCount == 0 &&
                    section.Fragments[0].FirstRecordId is null &&
                    section.Fragments[0].LastRecordId is null,
                $"Governance typed empty root must not fabricate record ranges: {authority.PartitionId.Value}.");
            var restored = provider.CreateSemanticVerifier(authority.Header).Verify(section.Fragments);
            Require(restored.LogicalContentDigest.SequenceEqual(authority.Header.CanonicalDigest),
                $"Governance typed empty recovery must rehash exactly: {authority.PartitionId.Value}.");
        }
    }

    private static void VerifyJurisdictionRoundTrip()
    {
        var polity = Ref("governance.polity", "0000000000000000000000000009a001");
        var scope = Ref("spatial.scope_registry", "0000000000000000000000000009a002");
        var payload = new GovernanceJurisdictionPayloadV1(
            polity,
            scope,
            new StableToken("territorial"),
            Array.AsReadOnly(new[] { new StableToken("resident"), new StableToken("structure") }),
            EffectiveFrom: 100,
            EffectiveUntil: 500);
        var identity = StandardDomainPartitionRegistry.Get(GovernanceJurisdictionPayloadV1.PartitionId);
        var record = new DomainRecordEnvelopeV1<GovernanceJurisdictionPayloadV1>(
            OpaqueId128.Parse("0000000000000000000000000009a101"),
            identity.RecordSchema,
            1,
            100,
            null,
            DetailLevelV1.D0Entity,
            null,
            payload);
        var partition = new DomainPartitionStateV1<GovernanceJurisdictionPayloadV1>(identity, [record]);
        var header = PartitionStateHeaderV1.CreateCanonical(
            partition,
            1,
            100,
            DetailLevelV1.D0Entity,
            static value => value.CanonicalDigest());
        var authority = new DomainPartitionSnapshotAuthorityV1<GovernanceJurisdictionPayloadV1>(
            partition,
            header,
            static value => value.CanonicalDigest());
        var provider = GovernanceSecurityDomainSnapshotProviderV1.CreateAll()
            .Single(x => x.SectionId == GovernanceJurisdictionPayloadV1.PartitionId);
        var resolver = new Resolver([polity, scope]);
        var section = provider.Create(authority, resolver);
        var verifier = provider.CreateSemanticVerifier(header);
        var restored = verifier.VerifyWithContext!(
            section.Fragments,
            new SnapshotSectionSemanticVerificationContextV1(resolver));
        Require(restored.LogicalContentDigest.SequenceEqual(header.CanonicalDigest),
            "Governance jurisdiction recovery must preserve canonical digest.");
        var decoded = DomainPartitionSnapshotWireCodecV1.DecodeFragment(
            GovernanceJurisdictionPayloadV1.PartitionId,
            section.Fragments.Single().FragmentPayload,
            StandardDomainNestedSnapshotCodecRegistryV1.Default);
        var round = GovernanceJurisdictionPayloadV1.FromStandardPayload(decoded.Records.Single().Payload);
        Require(round.PolityRef == payload.PolityRef &&
                round.ScopeRef == payload.ScopeRef &&
                round.SubjectClasses.SequenceEqual(payload.SubjectClasses) &&
                round.CanonicalDigest().SequenceEqual(payload.CanonicalDigest()),
            "Governance jurisdiction payload must round-trip losslessly.");
        ExpectInvalid(
            "governance jurisdiction missing scope target",
            () => _ = provider.Create(authority, new Resolver([polity])));
    }

    private static void VerifyRuleAstRoundTrip()
    {
        var jurisdiction = Ref("governance.jurisdiction", "0000000000000000000000000009b001");
        var factEquals = new LawPredicateNodeV1(
            LawPredicateNodeKindV1.FactEquals,
            Array.Empty<LawPredicateNodeV1>(),
            Key: new StableToken("subject.class"),
            TokenValue: new StableToken("resident"));
        var timeRange = new LawPredicateNodeV1(
            LawPredicateNodeKindV1.TimeStepRange,
            Array.Empty<LawPredicateNodeV1>(),
            FromStep: 200,
            UntilStep: 300);
        var notTimeRange = new LawPredicateNodeV1(
            LawPredicateNodeKindV1.Not,
            Array.AsReadOnly(new[] { timeRange }));
        var predicateRuntime = new LawPredicateNodeV1(
            LawPredicateNodeKindV1.And,
            Array.AsReadOnly(new[] { factEquals, notTimeRange }));
        var effectRuntime = new LawEffectV1(
            LawEffectKindV1.Permit,
            new StableToken("law.permit"));
        var predicate = GovernanceRulePredicateAstNestedValueV1.FromRuntime(predicateRuntime);
        var effect = GovernanceRuleEffectAstNestedValueV1.FromRuntime(effectRuntime);
        var registry = StandardDomainNestedSnapshotCodecRegistryV1.Default;

        var predicateCodec = registry.GetForBinding(GovernanceLawRulePayloadV1.PartitionId, "predicate_ast");
        var effectCodec = registry.GetForBinding(GovernanceLawRulePayloadV1.PartitionId, "effect_ast");
        Require(predicateCodec.Descriptor.Schema == new SchemaRefV1("domain.governance.rule-predicate-ast") &&
                predicateCodec.Descriptor.Fields.Count == 8 && predicateCodec.AllowsSelfRecursion,
            "Governance predicate AST must use the exact registered eight-field self-recursive schema.");
        Require(effectCodec.Descriptor.Schema == new SchemaRefV1("domain.governance.rule-effect-ast") &&
                effectCodec.Descriptor.Fields.Count == 2 && !effectCodec.AllowsSelfRecursion,
            "Governance effect AST must use the exact registered two-field non-recursive schema.");

        var predicateWire = DomainNestedSnapshotWireCodecV1.EncodeValue(
            GovernanceLawRulePayloadV1.PartitionId,
            "predicate_ast",
            predicate,
            registry);
        var predicateDecoded = DomainNestedSnapshotWireCodecV1.DecodeValue(
            GovernanceLawRulePayloadV1.PartitionId,
            "predicate_ast",
            predicateWire,
            registry);
        Require(predicateDecoded is GovernanceRulePredicateAstNestedValueV1,
            "Governance predicate AST must decode to the exact registered wrapper.");
        var predicateRound = (GovernanceRulePredicateAstNestedValueV1)predicateDecoded;
        var predicateWireRound = DomainNestedSnapshotWireCodecV1.EncodeValue(
            GovernanceLawRulePayloadV1.PartitionId,
            "predicate_ast",
            predicateRound,
            registry);
        Require(predicateWireRound.SequenceEqual(predicateWire),
            "Governance recursive predicate AST decode->encode must be byte-canonical.");
        Require(predicateRound.Node.Kind == LawPredicateNodeKindV1.And &&
                predicateRound.Node.Children.Count == 2 &&
                predicateRound.Node.Children[0].Kind == LawPredicateNodeKindV1.FactEquals &&
                predicateRound.Node.Children[1].Kind == LawPredicateNodeKindV1.Not &&
                predicateRound.Node.Children[1].Children.Single().Kind == LawPredicateNodeKindV1.TimeStepRange,
            "Governance predicate AST runtime structure must reconstruct losslessly.");

        var context = new LawEvaluationContextV1(
            jurisdiction.RecordId,
            150,
            new Dictionary<StableToken, StableToken>
            {
                [new StableToken("subject.class")] = new StableToken("resident"),
            },
            new Dictionary<StableToken, long>(),
            new HashSet<StableToken>(),
            new HashSet<StableToken>(),
            new HashSet<StableToken>());
        Require(predicateRuntime.Evaluate(context) && predicateRound.ToRuntime().Evaluate(context),
            "Governance predicate AST must preserve executable deterministic semantics after snapshot roundtrip.");

        var effectWire = DomainNestedSnapshotWireCodecV1.EncodeValue(
            GovernanceLawRulePayloadV1.PartitionId,
            "effect_ast",
            effect,
            registry);
        var effectDecoded = DomainNestedSnapshotWireCodecV1.DecodeValue(
            GovernanceLawRulePayloadV1.PartitionId,
            "effect_ast",
            effectWire,
            registry);
        Require(effectDecoded is GovernanceRuleEffectAstNestedValueV1 effectRound &&
                effectRound.ToRuntime() == effectRuntime,
            "Governance effect AST must reconstruct the exact runtime effect.");

        var law = new GovernanceLawRulePayloadV1(
            jurisdiction,
            Priority: 10,
            Specificity: 1,
            EffectiveFrom: 100,
            EffectiveUntil: null,
            PredicateAst: predicate,
            EffectAst: effect,
            Status: new StableToken("active"));
        var payloadWire = DomainPartitionSnapshotWireCodecV1.EncodePayload(
            GovernanceLawRulePayloadV1.PartitionId,
            law.ToStandardPayload(),
            registry);
        var lawRound = GovernanceLawRulePayloadV1.FromStandardPayload(
            DomainPartitionSnapshotWireCodecV1.DecodePayload(
                GovernanceLawRulePayloadV1.PartitionId,
                payloadWire,
                registry));
        Require(lawRound.PredicateAst is GovernanceRulePredicateAstNestedValueV1 &&
                lawRound.EffectAst is GovernanceRuleEffectAstNestedValueV1 &&
                lawRound.CanonicalDigest().SequenceEqual(law.CanonicalDigest()),
            "Governance law_rule payload must preserve recursive AST semantic digest through standard wire.");

        var identity = StandardDomainPartitionRegistry.Get(GovernanceLawRulePayloadV1.PartitionId);
        var record = new DomainRecordEnvelopeV1<GovernanceLawRulePayloadV1>(
            OpaqueId128.Parse("0000000000000000000000000009b101"),
            identity.RecordSchema,
            1,
            100,
            null,
            DetailLevelV1.D0Entity,
            null,
            law);
        var partition = new DomainPartitionStateV1<GovernanceLawRulePayloadV1>(identity, [record]);
        var resolver = new Resolver([jurisdiction]);
        var header = PartitionStateHeaderV1.CreateCanonical(
            partition,
            1,
            100,
            DetailLevelV1.D0Entity,
            static value => value.CanonicalDigest());
        var authority = new DomainPartitionSnapshotAuthorityV1<GovernanceLawRulePayloadV1>(
            partition,
            header,
            static value => value.CanonicalDigest());
        var provider = GovernanceSecurityDomainSnapshotProviderV1.CreateAll()
            .Single(x => x.SectionId == GovernanceLawRulePayloadV1.PartitionId);
        var section = provider.Create(authority, resolver);
        var verifier = provider.CreateSemanticVerifier(header);
        var restored = verifier.VerifyWithContext!(
            section.Fragments,
            new SnapshotSectionSemanticVerificationContextV1(resolver));
        Require(restored.LogicalContentDigest.SequenceEqual(header.CanonicalDigest),
            "Governance law_rule production provider/recovery must rehash recursive AST authority exactly.");

        ICanonicalDomainNestedValueV1 unrelated = new ParticipationPolicyRuleV1(
            10,
            new StableToken("predicate-probe"));
        var invalidLaw = new GovernanceLawRulePayloadV1(
            jurisdiction,
            Priority: 10,
            Specificity: 1,
            EffectiveFrom: 100,
            EffectiveUntil: null,
            PredicateAst: unrelated,
            EffectAst: effect,
            Status: new StableToken("active"));
        ExpectInvalid(
            "governance predicate AST must reject an unrelated nested type",
            () => _ = DomainPartitionSnapshotWireCodecV1.EncodePayload(
                GovernanceLawRulePayloadV1.PartitionId,
                invalidLaw.ToStandardPayload(),
                registry));
    }

    private static PartitionRecordRefV1 Ref(string partitionId, string recordId)
        => new(partitionId, OpaqueId128.Parse(recordId));

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void ExpectInvalid(string name, Action action)
    {
        try
        {
            action();
        }
        catch (InvalidDataException)
        {
            return;
        }
        throw new InvalidOperationException($"Expected rejection: {name}.");
    }

    private sealed class Resolver(IEnumerable<PartitionRecordRefV1> references) : IDomainRecordSchemaResolverV1
    {
        private readonly HashSet<PartitionRecordRefV1> _references = references.ToHashSet();

        public bool Exists(PartitionRecordRefV1 reference) => _references.Contains(reference);

        public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
        {
            if (!_references.Contains(reference))
            {
                schema = default;
                return false;
            }
            schema = StandardDomainPartitionRegistry.Get(reference.PartitionId.Value).RecordSchema;
            return true;
        }
    }
}
