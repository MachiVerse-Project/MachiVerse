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
        VerifyRuleAstFailsClosed();
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

    private static void VerifyRuleAstFailsClosed()
    {
        var jurisdiction = Ref("governance.jurisdiction", "0000000000000000000000000009b001");
        ICanonicalDomainNestedValueV1 predicate = new ParticipationPolicyRuleV1(10, new StableToken("predicate-probe"));
        ICanonicalDomainNestedValueV1 effect = new ParticipationPolicyRuleV1(20, new StableToken("effect-probe"));
        var law = new GovernanceLawRulePayloadV1(
            jurisdiction,
            Priority: 10,
            Specificity: 1,
            EffectiveFrom: 100,
            EffectiveUntil: null,
            PredicateAst: predicate,
            EffectAst: effect,
            Status: new StableToken("active"));

        ExpectInvalid(
            "governance predicate RuleAst must reject a nested type whose parent-field codec is not normatively registered",
            () => _ = DomainPartitionSnapshotWireCodecV1.EncodePayload(
                GovernanceLawRulePayloadV1.PartitionId,
                law.ToStandardPayload(),
                StandardDomainNestedSnapshotCodecRegistryV1.Default));
        ExpectInvalid(
            "governance RuleAst semantic digest must not reuse an unrelated nested codec",
            () => _ = law.CanonicalDigest());
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
