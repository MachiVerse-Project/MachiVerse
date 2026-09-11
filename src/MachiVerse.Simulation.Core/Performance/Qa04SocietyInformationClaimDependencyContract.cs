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
/// Exact fail-closed boundary for the canonical 25,000 society.information_claim records.
///
/// Alpha 1.1 fixes the descriptor range, non-specialized authoritative RecordId mapping, D2
/// envelope, status=active, and deterministic content_digest source. The standard Ref selector
/// table does not define which canonical actor pool owns claimant_ref/source_ref, the benchmark does
/// not define claim_token vocabulary, and no profile-specific rule defines payload created_step.
/// subject_refs/provenance_refs are required list fields but have no current semantic non-empty rule;
/// they therefore do not justify synthetic targets. Governance workload binding must not invent the
/// three missing authorities merely to satisfy InfoClaim(ordinal).
/// </summary>
public static class Qa04SocietyInformationClaimDependencyContractV1
{
    public const string PartitionId = "society.information_claim";
    public const ulong CanonicalStartOrdinal = 1_555_200;
    public const ulong CanonicalCount = 25_000;
    public const string ContentDigestFieldTag = "society.information_claim.content_digest";

    public static readonly StableToken CanonicalStatus = new("active");

    private static readonly IReadOnlyList<Qa04SocietyInformationClaimDependencyV1> BlockersValue =
        Array.AsReadOnly(new[]
        {
            Blocker(
                "society.info-claim.claimant-mapping",
                Qa04SocietyInformationClaimDependencyKindV1.ClaimantAuthorityMapping,
                "qa04.material.info-claim-claimant-undefined"),
            Blocker(
                "society.info-claim.claim-token-vocabulary",
                Qa04SocietyInformationClaimDependencyKindV1.ClaimTokenVocabulary,
                "qa04.material.info-claim-token-undefined"),
            Blocker(
                "society.info-claim.created-step-genesis",
                Qa04SocietyInformationClaimDependencyKindV1.GenesisCreatedStep,
                "qa04.material.info-claim-created-step-undefined"),
        }
        .OrderBy(static blocker => blocker.DependencyId.Value, StringComparer.Ordinal)
        .ToArray());

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
            ContentDigest(probe.Descriptor.RecordId).Length != 32 || CanonicalStatus.Value != "active")
            throw new InvalidDataException("qa04.society.info-claim-known-genesis-drift");

        if (BlockersValue.Count != 3 ||
            BlockersValue.Select(static blocker => blocker.Kind).Distinct().Count() != BlockersValue.Count ||
            BlockersValue.Select(static blocker => blocker.DependencyId).Distinct().Count() != BlockersValue.Count ||
            BlockersValue.Select(static blocker => blocker.FailureCode).Distinct().Count() != BlockersValue.Count)
            throw new InvalidDataException("qa04.society.info-claim-dependency-drift");
    }

    private static Qa04SocietyInformationClaimDependencyV1 Blocker(
        string dependencyId,
        Qa04SocietyInformationClaimDependencyKindV1 kind,
        string failureCode)
        => new(new StableToken(dependencyId), kind, new StableToken(failureCode));

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
