using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public enum Qa04SocietyInformationClaimDependencyKindV1 : byte
{
    ClaimantAuthorityMapping = 1,
    ClaimTokenVocabulary = 2,
    GenesisCreatedStep = 3,
}

public sealed record Qa04SocietyInformationClaimDependencyV1(
    StableToken DependencyId,
    Qa04SocietyInformationClaimDependencyKindV1 Kind,
    StableToken FailureCode);

/// <summary>
/// Canonical 25,000 society.information_claim dependency boundary.
/// Benchmark-only Resident claimant mapping, claim_token and payload created_step=0 are decided by
/// the Alpha 1.1 Society/Governance amendment and production-proven by the canonical materializer.
/// </summary>
public static class Qa04SocietyInformationClaimDependencyContractV1
{
    public const string PartitionId = "society.information_claim";
    public const ulong CanonicalStartOrdinal = 1_555_200;
    public const ulong CanonicalCount = 25_000;
    public const string ContentDigestFieldTag = "society.information_claim.content_digest";

    public static readonly StableToken CanonicalStatus = new("active");

    private static readonly IReadOnlyList<Qa04SocietyInformationClaimDependencyV1> BlockersValue =
        Array.AsReadOnly(Array.Empty<Qa04SocietyInformationClaimDependencyV1>());

    public static IReadOnlyList<Qa04SocietyInformationClaimDependencyV1> Blockers => BlockersValue;

    public static byte[] ContentDigest(OpaqueId128 recordId)
        => Qa04ReferenceGenesisValueSourceV1.Hash(recordId, ContentDigestFieldTag);

    public static void ValidateCanonicalContract()
    {
        Qa04SocietyGovernanceReferenceDecompositionV1.ValidateCanonicalContract();

        var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(PartitionId);
        if (slice.StartOrdinal != CanonicalStartOrdinal || slice.Count != CanonicalCount || slice.UsesSpecializedIdentity)
            throw new InvalidDataException("qa04.society.info-claim-decomposition-drift");

        var identity = StandardDomainPartitionRegistry.Get(PartitionId);
        if (identity.OwnerDomain.Value != "society_economy")
            throw new InvalidDataException("qa04.society.info-claim-owner-drift");

        var schema = StandardDomainPayloadSchemaRegistry.Get(PartitionId);
        RequireField(schema, "claimant_ref", DomainPayloadFieldKindV1.Ref);
        RequireField(schema, "subject_refs", DomainPayloadFieldKindV1.RefList);
        RequireField(schema, "claim_token", DomainPayloadFieldKindV1.Token);
        RequireField(schema, "content_digest", DomainPayloadFieldKindV1.Digest);
        RequireField(schema, "provenance_refs", DomainPayloadFieldKindV1.RefList);
        RequireField(schema, "created_step", DomainPayloadFieldKindV1.Step);
        RequireField(schema, "status", DomainPayloadFieldKindV1.Token);

        var probe = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(CanonicalStartOrdinal);
        if (probe.PartitionId.Value != PartitionId || probe.Descriptor.RecordId.IsZero ||
            ContentDigest(probe.Descriptor.RecordId).Length != 32 || CanonicalStatus.Value != "active" ||
            BlockersValue.Count != 0)
            throw new InvalidDataException("qa04.society.info-claim-known-genesis-drift");
    }

    private static void RequireField(
        DomainPayloadSchemaDescriptorV1 schema,
        string fieldName,
        DomainPayloadFieldKindV1 kind)
    {
        var field = schema.Fields.SingleOrDefault(candidate => string.Equals(candidate.Name, fieldName, StringComparison.Ordinal))
            ?? throw new InvalidDataException($"qa04.society.info-claim-field-missing:{fieldName}");
        if (field.Kind != kind || field.Optional)
            throw new InvalidDataException($"qa04.society.info-claim-field-drift:{fieldName}");
    }
}
