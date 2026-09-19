using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Participation;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public enum Qa04ParticipationControlModeDependencyKindV1 : byte
{
    CanonicalPopulationAuthority = 1,
    ModeTokenVocabulary = 2,
    GenesisControlState = 3,
}

public sealed record Qa04ParticipationControlModeDependencyV1(
    StableToken DependencyId,
    Qa04ParticipationControlModeDependencyKindV1 Kind,
    StableToken FailureCode);

/// <summary>
/// Closed dependency contract for the canonical transaction participant pool owned by
/// participation.control_mode. The former population, Token-vocabulary, and genesis-state gaps are
/// fixed by the approved Alpha 1.1 benchmark authority and implemented through
/// Qa04ParticipationControlModeCanonicalAuthorityV1 with full production Snapshot/recovery proof.
/// </summary>
public static class Qa04ParticipationControlModeDependencyContractV1
{
    public const string PartitionId = "participation.control_mode";

    private static readonly IReadOnlyList<Qa04ParticipationControlModeDependencyV1> BlockersValue =
        Array.AsReadOnly(Array.Empty<Qa04ParticipationControlModeDependencyV1>());

    public static IReadOnlyList<Qa04ParticipationControlModeDependencyV1> Blockers => BlockersValue;

    public static void ValidateCanonicalContract()
    {
        Qa04ReferenceLoadV1.ValidateCanonicalContract();
        Qa04ParticipationControlModeCanonicalAuthorityV1.ValidateCanonicalContract();

        var partition = StandardDomainPartitionRegistry.Get(PartitionId);
        if (partition.OwnerDomain.Value != "participation")
            throw new InvalidDataException("qa04.workload.participation-control-mode-owner-drift");

        var schema = StandardDomainPayloadSchemaRegistry.Get(PartitionId);
        RequireField(schema, "resident_ref", DomainPayloadFieldKindV1.Ref, optional: false);
        RequireField(schema, "binding_ref", DomainPayloadFieldKindV1.Ref, optional: true);
        RequireField(schema, "mode", DomainPayloadFieldKindV1.Token, optional: false);
        RequireField(schema, "effective_from", DomainPayloadFieldKindV1.Step, optional: false);
        RequireField(schema, "input_authority_generation", DomainPayloadFieldKindV1.UInt32, optional: false);

        var expectedModes = new[]
        {
            "autonomous",
            "diver-control-available",
            "diver-absent-policy",
            "bound-resident-deceased",
        };
        var actualModes = Enum.GetValues<ResidentControlModeV1>()
            .Select(Qa04ParticipationControlModeCanonicalAuthorityV1.ModeToken)
            .Select(static token => token.Value)
            .ToArray();
        if (!actualModes.SequenceEqual(expectedModes, StringComparer.Ordinal))
            throw new InvalidDataException("qa04.workload.participation-control-mode-semantic-enum-drift");

        var referenceClass = Qa04ReferenceLoadV1.RecordClasses.SingleOrDefault(
            static item => item.ClassToken.Value == PartitionId)
            ?? throw new InvalidDataException("qa04.workload.participation-control-mode-population-missing");
        if (referenceClass.Count != Qa04ParticipationControlModeCanonicalAuthorityV1.CanonicalCount)
            throw new InvalidDataException("qa04.workload.participation-control-mode-population-drift");

        if (BlockersValue.Count != 0)
            throw new InvalidDataException("qa04.workload.participation-control-mode-dependency-drift");
    }

    private static void RequireField(
        DomainPayloadSchemaDescriptorV1 schema,
        string fieldName,
        DomainPayloadFieldKindV1 kind,
        bool optional)
    {
        var field = schema.Fields.SingleOrDefault(candidate => string.Equals(candidate.Name, fieldName, StringComparison.Ordinal))
            ?? throw new InvalidDataException($"qa04.workload.participation-control-mode-field-missing:{fieldName}");
        if (field.Kind != kind || field.Optional != optional)
            throw new InvalidDataException($"qa04.workload.participation-control-mode-field-drift:{fieldName}");
    }
}
