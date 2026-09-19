using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Domains.GovernanceSecurity;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04GovernanceRemainingSnapshotRecoveryEvidenceResultV1(
    ulong LawRuleCount,
    ulong TaxFiscalCount,
    ulong DiplomacyCount,
    ulong SecurityIncidentCount,
    ulong InvestigationCount,
    ulong JudicialCaseCount,
    ulong EnforcementCount,
    ulong MilitaryAuthorityCount,
    ulong BorderControlCount,
    ulong LineageCount)
{
    public ulong TotalCount => checked(
        LawRuleCount + TaxFiscalCount + DiplomacyCount + SecurityIncidentCount + InvestigationCount +
        JudicialCaseCount + EnforcementCount + MilitaryAuthorityCount + BorderControlCount + LineageCount);
}

/// <summary>Production Snapshot/recovery semantic proof for the final QA-04 Governance records.</summary>
public static class Qa04GovernanceRemainingSnapshotRecoveryEvidenceV1
{
    public static Qa04GovernanceRemainingSnapshotRecoveryEvidenceResultV1 Verify(
        Qa04GovernanceRemainingCanonicalMaterializationV1 materialization)
    {
        ArgumentNullException.ThrowIfNull(materialization);
        if (materialization.MaterializedRecordCount != Qa04GovernanceRemainingCanonicalAuthorityV1.CanonicalCount)
            throw new InvalidDataException("qa04.governance.remaining-snapshot-record-count");

        var law = VerifyPartition(materialization.LawRules, static p => p.ToStandardPayload(), GovernanceLawRulePayloadV1.FromStandardPayload, static p => p.CanonicalDigest(), materialization.References);
        var tax = VerifyPartition(materialization.TaxFiscal, static p => p.ToStandardPayload(), GovernanceTaxFiscalPayloadV1.FromStandardPayload, static p => p.CanonicalDigest(), materialization.References);
        var diplomacy = VerifyPartition(materialization.Diplomacy, static p => p.ToStandardPayload(), GovernanceDiplomacyPayloadV1.FromStandardPayload, static p => p.CanonicalDigest(), materialization.References);
        var incidents = VerifyPartition(materialization.SecurityIncidents, static p => p.ToStandardPayload(), GovernanceSecurityIncidentPayloadV1.FromStandardPayload, static p => p.CanonicalDigest(), materialization.References);
        var investigations = VerifyPartition(materialization.Investigations, static p => p.ToStandardPayload(), GovernanceInvestigationPayloadV1.FromStandardPayload, static p => p.CanonicalDigest(), materialization.References);
        var judicial = VerifyPartition(materialization.JudicialCases, static p => p.ToStandardPayload(), GovernanceJudicialCasePayloadV1.FromStandardPayload, static p => p.CanonicalDigest(), materialization.References);
        var enforcement = VerifyPartition(materialization.Enforcements, static p => p.ToStandardPayload(), GovernanceEnforcementPayloadV1.FromStandardPayload, static p => p.CanonicalDigest(), materialization.References);
        var military = VerifyPartition(materialization.MilitaryAuthorities, static p => p.ToStandardPayload(), GovernanceMilitaryAuthorityPayloadV1.FromStandardPayload, static p => p.CanonicalDigest(), materialization.References);
        var border = VerifyPartition(materialization.BorderControls, static p => p.ToStandardPayload(), GovernanceBorderControlPayloadV1.FromStandardPayload, static p => p.CanonicalDigest(), materialization.References);
        var lineage = VerifyPartition(materialization.Lineages, static p => p.ToStandardPayload(), GovernanceLineagePayloadV1.FromStandardPayload, static p => p.CanonicalDigest(), materialization.References);

        var result = new Qa04GovernanceRemainingSnapshotRecoveryEvidenceResultV1(
            law, tax, diplomacy, incidents, investigations, judicial, enforcement, military, border, lineage);
        if (result.TotalCount != Qa04GovernanceRemainingCanonicalAuthorityV1.CanonicalCount ||
            law != Qa04GovernanceRemainingCanonicalAuthorityV1.LawRuleCount ||
            tax != Qa04GovernanceRemainingCanonicalAuthorityV1.TaxFiscalCount ||
            diplomacy != Qa04GovernanceRemainingCanonicalAuthorityV1.DiplomacyCount ||
            incidents != Qa04GovernanceRemainingCanonicalAuthorityV1.SecurityIncidentCount ||
            investigations != Qa04GovernanceRemainingCanonicalAuthorityV1.InvestigationCount ||
            judicial != Qa04GovernanceRemainingCanonicalAuthorityV1.JudicialCaseCount ||
            enforcement != Qa04GovernanceRemainingCanonicalAuthorityV1.EnforcementCount ||
            military != Qa04GovernanceRemainingCanonicalAuthorityV1.MilitaryAuthorityCount ||
            border != Qa04GovernanceRemainingCanonicalAuthorityV1.BorderControlCount ||
            lineage != Qa04GovernanceRemainingCanonicalAuthorityV1.LineageCount)
            throw new InvalidDataException("qa04.governance.remaining-snapshot-semantic-count");

        ValidateLineageDigests(materialization);
        return result;
    }

    private static ulong VerifyPartition<TPayload>(
        DomainPartitionStateV1<TPayload> partition,
        Func<TPayload, IReadOnlyDictionary<string, object?>> toStandardPayload,
        Func<IReadOnlyDictionary<string, object?>, TPayload> fromStandardPayload,
        Func<TPayload, byte[]> digest,
        IDomainRecordSchemaResolverV1 references)
    {
        var header = PartitionStateHeaderV1.CreateCanonical(
            partition, revision: 1, basisStep: 0, DetailLevelV1.D2RegionalAggregate, digest);
        var authority = new DomainPartitionSnapshotAuthorityV1<TPayload>(partition, header, digest);
        var provider = new DomainPartitionSnapshotSectionProviderV1<TPayload>(
            partition.Identity.PartitionId.Value,
            toStandardPayload,
            fromStandardPayload,
            digest,
            StandardDomainNestedSnapshotCodecRegistryV1.Default);

        var section = provider.Create(authority, references);
        if (section.LogicalItemCount != header.ItemCount ||
            !CryptographicOperations.FixedTimeEquals(section.LogicalContentDigest, header.CanonicalDigest))
            throw new InvalidDataException($"qa04.governance.remaining-snapshot-section:{partition.Identity.PartitionId.Value}");

        var verifier = provider.CreateSemanticVerifier(header, references);
        var recovered = verifier.VerifyWithContext is not null
            ? verifier.VerifyWithContext(section.Fragments, new SnapshotSectionSemanticVerificationContextV1(references))
            : verifier.Verify(section.Fragments);
        if (recovered.LogicalItemCount != header.ItemCount ||
            !CryptographicOperations.FixedTimeEquals(recovered.LogicalContentDigest, header.CanonicalDigest))
            throw new InvalidDataException($"qa04.governance.remaining-snapshot-semantic-rehash:{partition.Identity.PartitionId.Value}");
        return recovered.LogicalItemCount;
    }

    private static void ValidateLineageDigests(Qa04GovernanceRemainingCanonicalMaterializationV1 materialization)
    {
        var lawById = materialization.LawRuleRecordsByOrdinal.ToDictionary(static record => record.RecordId);
        foreach (var lineage in materialization.Lineages.RecordsCanonical)
        {
            if (lineage.Payload.SubjectRef.PartitionId.Value != GovernanceLawRulePayloadV1.PartitionId ||
                !lawById.TryGetValue(lineage.Payload.SubjectRef.RecordId, out var source))
                throw new InvalidDataException("qa04.governance.remaining-lineage-subject-missing");
            var expected = source.Payload.CanonicalDigest();
            if (lineage.Payload.CausalityDigest.Length != 32 ||
                !CryptographicOperations.FixedTimeEquals(lineage.Payload.CausalityDigest, expected))
                throw new InvalidDataException("qa04.governance.remaining-lineage-digest-drift");
        }
    }
}
