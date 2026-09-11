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
/// Exact fail-closed boundary for the canonical 60,000 society.contract_claim records.
/// The descriptor range and production payload schema are already fixed. The benchmark still lacks
/// a canonical contract_kind vocabulary and a canonical actual-record mapping for party_refs. Those
/// two decisions are required before contract material can be produced; optional amount/quantity/
/// claimant/obligor/due fields are not used to hide either authority gap.
/// </summary>
public static class Qa04SocietyContractClaimDependencyContractV1
{
    public const ulong CanonicalCount = 60_000;
    public const ulong CanonicalStartOrdinal = 210_000;

    private static readonly IReadOnlyList<Qa04SocietyContractClaimDependencyV1> BlockersValue =
        Array.AsReadOnly(new[]
        {
            Blocker(
                "society.contract-claim.contract-kind-vocabulary",
                Qa04SocietyContractClaimDependencyKindV1.ContractKindVocabulary,
                "qa04.material.contract-kind-vocabulary-undefined"),
            Blocker(
                "society.contract-claim.party-ref-mapping",
                Qa04SocietyContractClaimDependencyKindV1.PartyAuthorityMapping,
                "qa04.material.contract-party-mapping-undefined"),
        }
        .OrderBy(static blocker => blocker.DependencyId.Value, StringComparer.Ordinal)
        .ToArray());

    public static IReadOnlyList<Qa04SocietyContractClaimDependencyV1> Blockers => BlockersValue;

    public static void ValidateCanonicalContract()
    {
        Qa04SocietyGovernanceReferenceDecompositionV1.ValidateCanonicalContract();

        var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get("society.contract_claim");
        if (slice.StartOrdinal != CanonicalStartOrdinal || slice.Count != CanonicalCount || slice.UsesSpecializedIdentity)
            throw new InvalidDataException("qa04.society.contract-claim-decomposition-drift");

        var partition = StandardDomainPartitionRegistry.Get("society.contract_claim");
        if (partition.OwnerDomain.Value != "society_economy")
            throw new InvalidDataException("qa04.society.contract-claim-owner-drift");

        var schema = StandardDomainPayloadSchemaRegistry.Get("society.contract_claim");
        RequireField(schema, "contract_kind", DomainPayloadFieldKindV1.Token, optional: false);
        RequireField(schema, "party_refs", DomainPayloadFieldKindV1.RefList, optional: false);
        RequireField(schema, "status", DomainPayloadFieldKindV1.Token, optional: false);
        RequireField(schema, "terms_digest", DomainPayloadFieldKindV1.Digest, optional: false);
        RequireField(schema, "claimant_ref", DomainPayloadFieldKindV1.Ref, optional: true);
        RequireField(schema, "obligor_ref", DomainPayloadFieldKindV1.Ref, optional: true);

        if (BlockersValue.Count != 2 ||
            BlockersValue.Select(static blocker => blocker.DependencyId).Distinct().Count() != BlockersValue.Count ||
            BlockersValue.Select(static blocker => blocker.FailureCode).Distinct().Count() != BlockersValue.Count ||
            BlockersValue.Any(static blocker => !Enum.IsDefined(blocker.Kind)))
            throw new InvalidDataException("qa04.society.contract-claim-dependency-drift");
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

    private static Qa04SocietyContractClaimDependencyV1 Blocker(
        string dependencyId,
        Qa04SocietyContractClaimDependencyKindV1 kind,
        string failureCode)
        => new(new StableToken(dependencyId), kind, new StableToken(failureCode));
}
