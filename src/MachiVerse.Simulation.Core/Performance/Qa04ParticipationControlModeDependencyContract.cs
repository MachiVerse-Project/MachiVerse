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
/// Fail-closed audit for the canonical transaction participant pool owned by
/// participation.control_mode.
///
/// Production Participation semantics already distinguish the four world-effective control modes,
/// and the standard payload schema is implemented. perf.reference.v1 does not currently define a
/// Participation initial-world population/count or identity derivation, does not define the stable
/// Token serialization vocabulary for the enum semantics, and does not define the genesis binding /
/// availability / input-authority-generation distribution needed to construct actual records.
/// Transaction genesis must therefore not synthesize an autonomous pool from Resident identities.
/// </summary>
public static class Qa04ParticipationControlModeDependencyContractV1
{
    public const string PartitionId = "participation.control_mode";

    private static readonly IReadOnlyList<Qa04ParticipationControlModeDependencyV1> BlockersValue =
        Array.AsReadOnly(new[]
        {
            Blocker(
                "workload.transaction.participation-control-mode.canonical-population",
                Qa04ParticipationControlModeDependencyKindV1.CanonicalPopulationAuthority,
                "qa04.workload.participation-control-mode-population-undefined"),
            Blocker(
                "workload.transaction.participation-control-mode.mode-token-vocabulary",
                Qa04ParticipationControlModeDependencyKindV1.ModeTokenVocabulary,
                "qa04.workload.participation-control-mode-token-vocabulary-undefined"),
            Blocker(
                "workload.transaction.participation-control-mode.genesis-control-state",
                Qa04ParticipationControlModeDependencyKindV1.GenesisControlState,
                "qa04.workload.participation-control-mode-genesis-state-undefined"),
        });

    public static IReadOnlyList<Qa04ParticipationControlModeDependencyV1> Blockers => BlockersValue;

    public static void ValidateCanonicalContract()
    {
        Qa04ReferenceLoadV1.ValidateCanonicalContract();

        var partition = StandardDomainPartitionRegistry.Get(PartitionId);
        if (partition.OwnerDomain.Value != "participation")
            throw new InvalidDataException("qa04.workload.participation-control-mode-owner-drift");

        var schema = StandardDomainPayloadSchemaRegistry.Get(PartitionId);
        RequireField(schema, "resident_ref", DomainPayloadFieldKindV1.Ref, optional: false);
        RequireField(schema, "binding_ref", DomainPayloadFieldKindV1.Ref, optional: true);
        RequireField(schema, "mode", DomainPayloadFieldKindV1.Token, optional: false);
        RequireField(schema, "effective_from", DomainPayloadFieldKindV1.Step, optional: false);
        RequireField(schema, "input_authority_generation", DomainPayloadFieldKindV1.UInt32, optional: false);

        var modes = Enum.GetValues<ResidentControlModeV1>();
        if (modes.Length != 4 ||
            !modes.Contains(ResidentControlModeV1.Autonomous) ||
            !modes.Contains(ResidentControlModeV1.DiverControlAvailable) ||
            !modes.Contains(ResidentControlModeV1.DiverAbsentPolicy) ||
            !modes.Contains(ResidentControlModeV1.BoundResidentDeceased))
            throw new InvalidDataException("qa04.workload.participation-control-mode-semantic-enum-drift");

        // The canonical benchmark initial-world classes intentionally expose no Participation
        // population today. If one is added, this blocker contract must be revised rather than
        // silently retaining the old failure state.
        if (Qa04ReferenceLoadV1.RecordClasses.Any(static item =>
                item.ClassToken.Value.StartsWith("participation.", StringComparison.Ordinal)))
            throw new InvalidDataException("qa04.workload.participation-control-mode-population-contract-stale");

        if (BlockersValue.Count != 3 ||
            BlockersValue.Select(static blocker => blocker.Kind).Distinct().Count() != BlockersValue.Count ||
            BlockersValue.Select(static blocker => blocker.DependencyId).Distinct().Count() != BlockersValue.Count ||
            BlockersValue.Select(static blocker => blocker.FailureCode).Distinct().Count() != BlockersValue.Count)
            throw new InvalidDataException("qa04.workload.participation-control-mode-dependency-drift");
    }

    private static Qa04ParticipationControlModeDependencyV1 Blocker(
        string dependencyId,
        Qa04ParticipationControlModeDependencyKindV1 kind,
        string failureCode)
        => new(new StableToken(dependencyId), kind, new StableToken(failureCode));

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
