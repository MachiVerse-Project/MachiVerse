using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.GovernanceSecurity;
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
            polity, scope, new StableToken("territorial"),
            Array.AsReadOnly(new[] { new StableToken("resident"), new StableToken("structure") }),
            EffectiveFrom: 100, EffectiveUntil: 500);
        var identity = StandardDomainPartitionRegistry.Get(GovernanceJurisdictionPayloadV1.PartitionId);
        var record = new DomainRecordEnvelopeV1<GovernanceJurisdictionPayloadV1>(
            OpaqueId128.Parse("0000000000000000000000000009a101"), identity.RecordSchema, 1, 100, null, DetailLevelV1.D0Entity, null, payload);
        var partition = new DomainPartitionStateV1<GovernanceJurisdictionPayloadV1>(identity, [record]);
        var header = PartitionStateHeaderV1.CreateCanonical(partition, 1, 100, DetailLevelV1.D0Entity, x => x.CanonicalDigest());
        var authority = new DomainPartitionSnapshotAuthorityV1<GovernanceJurisdictionPayloadV1>(partition, header, x => x.CanonicalDigest());
        var provider = GovernanceSecurityDomainSnapshotProviderV1.CreateAll().Single(x => x.SectionId == GovernanceJurisdictionPayloadV1.PartitionId);
        var resolver = new Resolver([polity, scope]);
        var section = provider.Create(authority, resolver);
        var verifier = provider.CreateSemanticVerifier(header);
        var restored = verifier.VerifyWithContext!(section.Fragments, new SnapshotSectionSemanticVerificationContextV1(resolver));
        Require(restored.LogicalContentDigest.SequenceEqual(header.CanonicalDigest), "Governance jurisdiction recovery must preserve canonical digest.");
        var decoded = DomainPartitionSnapshotWireCodecV1.DecodeFragment(GovernanceJurisdictionPayloadV1.PartitionId, section.Fragments.Single().FragmentPayload, StandardDomainNestedSnapshotCodecRegistryV1.Default);
        var round = GovernanceJurisdictionPayloadV1.FromStandardPayload(decoded.Records.Single().Payload);
        Require(round.PolityRef == payload.PolityRef && round.ScopeRef == payload.ScopeRef && round.SubjectClasses.SequenceEqual(payload.SubjectClasses) && round.CanonicalDigest().SequenceEqual(payload.CanonicalDigest()),
            "Governance jurisdiction payload must round-trip losslessly.");
        ExpectInvalid("governance jurisdiction missing scope target", () => _ = provider.Create(authority, new Resolver([polity])));
    }

    private static PartitionRecordRefV1 Ref(string p, string r) => new(p, OpaqueId128.Parse(r));
    private static void Require(bool c, string m) { if (!c) throw new InvalidOperationException(m); }
    private static void ExpectInvalid(string n, Action a) { try { a(); } catch (InvalidDataException) { return; } throw new InvalidOperationException($"Expected rejection: {n}."); }
    private sealed class Resolver(IEnumerable<PartitionRecordRefV1> refs) : IDomainRecordSchemaResolverV1
    {
        private readonly HashSet<PartitionRecordRefV1> _refs = refs.ToHashSet();
        public bool Exists(PartitionRecordRefV1 r) => _refs.Contains(r);
        public bool TryGetRecordSchema(PartitionRecordRefV1 r, out SchemaRefV1 s) { if (!_refs.Contains(r)) { s = default; return false; } s = StandardDomainPartitionRegistry.Get(r.PartitionId.Value).RecordSchema; return true; }
    }
}
