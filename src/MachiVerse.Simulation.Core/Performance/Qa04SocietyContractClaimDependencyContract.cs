using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public enum Qa04SocietyContractClaimDependencyKindV1 : byte
{
    ContractKindVocabulary = 1,
    PartyAuthorityMapping = 2,
}

public sealed record Qa04SocietyContractClaimDependencyV1(
    StableToken DependencyId,
    Qa04SocietyContractClaimDependencyKindV1 Kind,
    StableToken FailureCode);

/// <summary>
/// Canonical 60,000 society.contract_claim dependency boundary.
/// Benchmark-only contract_kind and one-Resident party mapping are decided by the Alpha 1.1
/// Society/Governance amendment and production-proven by the canonical materializer.
/// </summary>
public static class Qa04SocietyContractClaimDependencyContractV1
{
    public const string PartitionId = "society.contract_claim";
    public const ulong CanonicalCount = 60_000;
    public const ulong CanonicalStartOrdinal = 210_000;
    public const string TermsDigestFieldTag = "society.contract_claim.terms_digest";

    public static readonly StableToken CanonicalStatus = new("active");

    private static readonly IReadOnlyList<Qa04SocietyContractClaimDependencyV1> BlockersValue =
        Array.AsReadOnly(Array.Empty<Qa04SocietyContractClaimDependencyV1>());

    public static IReadOnlyList<Qa04SocietyContractClaimDependencyV1> Blockers => BlockersValue;

    public static byte[] TermsDigest(OpaqueId128 recordId)
        => Qa04ReferenceGenesisValueSourceV1.Hash(recordId, TermsDigestFieldTag);

    public static void ValidateCanonicalContract()
    {
        Qa04SocietyGovernanceReferenceDecompositionV1.ValidateCanonicalContract();

        var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(PartitionId);
        if (slice.StartOrdinal != CanonicalStartOrdinal || slice.Count != CanonicalCount || slice.UsesSpecializedIdentity)
            throw new InvalidDataException("qa04.society.contract-claim-decomposition-drift");

        var partition = StandardDomainPartitionRegistry.Get(PartitionId);
        if (partition.OwnerDomain.Value != "society_economy")
            throw new InvalidDataException("qa04.society.contract-claim-owner-drift");

        var schema = StandardDomainPayloadSchemaRegistry.Get(PartitionId);
        RequireField(schema, "contract_kind", DomainPayloadFieldKindV1.Token, optional: false);
        RequireField(schema, "party_refs", DomainPayloadFieldKindV1.RefList, optional: false);
        RequireField(schema, "status", DomainPayloadFieldKindV1.Token, optional: false);
        RequireField(schema, "terms_digest", DomainPayloadFieldKindV1.Digest, optional: false);
        RequireField(schema, "claimant_ref", DomainPayloadFieldKindV1.Ref, optional: true);
        RequireField(schema, "obligor_ref", DomainPayloadFieldKindV1.Ref, optional: true);
        RequireField(schema, "amount", DomainPayloadFieldKindV1.Money, optional: true);
        RequireField(schema, "quantity", DomainPayloadFieldKindV1.Int64, optional: true);
        RequireField(schema, "due_step", DomainPayloadFieldKindV1.Step, optional: true);

        var probe = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(CanonicalStartOrdinal);
        if (probe.PartitionId.Value != PartitionId || probe.Descriptor.RecordId.IsZero ||
            CanonicalStatus.Value != "active" || TermsDigest(probe.Descriptor.RecordId).Length != 32 ||
            BlockersValue.Count != 0)
            throw new InvalidDataException("qa04.society.contract-claim-known-genesis-drift");
    }

    private static void RequireField(
        DomainPayloadSchemaDescriptorV1 schema,
        string fieldName,
        DomainPayloadFieldKindV1 kind,
        bool optional)
    {
        var field = schema.Fields.SingleOrDefault(candidate => string.Equals(candidate.Name, fieldName, StringComparison.Ordinal))
            ?? throw new InvalidDataException($"qa04.society.contract-claim-field-missing:{fieldName}");
        if (field.Kind != kind || field.Optional != optional)
            throw new InvalidDataException($"qa04.society.contract-claim-field-drift:{fieldName}");
    }
}
