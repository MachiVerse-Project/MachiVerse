using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Environment;
using MachiVerse.Simulation.Core.Domains.GovernanceSecurity;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04CanonicalOperationDomainOutputBatchV1(
    ulong BasisStep,
    ulong TargetStep,
    IReadOnlyList<DomainCandidateOutputV1> Outputs);

/// <summary>
/// Gate-2 Step 3 boundary. Places the six QA-04 local partition candidates onto the standard
/// eight-domain output surface. This stage does not invent MutationIntent values, build a
/// StepCandidate, cross SQLite COMMIT, or publish State(S+1).
/// </summary>
public static class Qa04CanonicalOperationDomainOutputBinderV1
{
    private static readonly IReadOnlyDictionary<string, StableToken> ExpectedOwners =
        new Dictionary<string, StableToken>(StringComparer.Ordinal)
        {
            [InfrastructureServiceQueuePayloadV1.PartitionId] = new("infrastructure_information"),
            [ResidentBehaviorStatePayloadV1.PartitionId] = new("resident"),
            [PhysicalPresencePayloadV1.PartitionId] = new("physical_built"),
            [SocietyMarketTransactionRecordSchemaV2.PartitionId] = new("society_economy"),
            [GovernanceSecurityIncidentPayloadV1.PartitionId] = new("governance_security"),
            [EnvironmentHazardPayloadV1.PartitionId] = new("environment"),
        };

    public static Qa04CanonicalOperationDomainOutputBatchV1 Bind(
        WorldStateV1 basisState,
        Qa04CanonicalOperationPartitionCandidateBatchV1 partitionBatch)
    {
        ArgumentNullException.ThrowIfNull(basisState);
        ArgumentNullException.ThrowIfNull(partitionBatch);
        ArgumentNullException.ThrowIfNull(partitionBatch.Partitions);

        if (basisState.Header.WorldId != Qa04ReferenceLoadV1.WorldId)
            throw new InvalidDataException("qa04.full-step.domain-output-world-id-drift");
        if (basisState.Header.Step == ulong.MaxValue)
            throw new InvalidDataException("qa04.full-step.domain-output-step-overflow");

        var targetStep = checked(basisState.Header.Step + 1UL);
        if (partitionBatch.BasisStep != basisState.Header.Step || partitionBatch.TargetStep != targetStep)
            throw new InvalidDataException("qa04.full-step.domain-output-step-drift");
        if (partitionBatch.Partitions.Count != ExpectedOwners.Count)
            throw new InvalidDataException("qa04.full-step.domain-output-partition-count-drift");

        var candidateByPartition = new Dictionary<string, PartitionCandidateV1>(StringComparer.Ordinal);
        foreach (var mutation in partitionBatch.Partitions)
        {
            ArgumentNullException.ThrowIfNull(mutation);
            ArgumentNullException.ThrowIfNull(mutation.Candidate);
            ArgumentNullException.ThrowIfNull(mutation.Material);

            var candidate = mutation.Candidate;
            var partitionId = candidate.PartitionId.Value;
            if (!ExpectedOwners.TryGetValue(partitionId, out var expectedOwner))
                throw new InvalidDataException($"qa04.full-step.domain-output-partition-unregistered:{partitionId}");
            if (candidate.OwnerDomain != expectedOwner)
                throw new InvalidDataException($"qa04.full-step.domain-output-owner-drift:{partitionId}");
            if (candidate.BasisStep != basisState.Header.Step || candidate.TargetStep != targetStep)
                throw new InvalidDataException($"qa04.full-step.domain-output-candidate-step-drift:{partitionId}");

            var basisHeader = basisState.Partitions.Get(partitionId).Header;
            if (basisHeader.OwnerDomain != candidate.OwnerDomain ||
                basisHeader.Revision != candidate.BasisRevision ||
                candidate.CandidateRevision != checked(candidate.BasisRevision + 1UL))
            {
                throw new InvalidDataException($"qa04.full-step.domain-output-candidate-basis-drift:{partitionId}");
            }

            var resultingHeader = mutation.Material.ResultingHeader;
            if (resultingHeader.PartitionId != candidate.PartitionId ||
                resultingHeader.OwnerDomain != candidate.OwnerDomain ||
                resultingHeader.Revision != candidate.CandidateRevision ||
                resultingHeader.BasisStep != targetStep)
            {
                throw new InvalidDataException($"qa04.full-step.domain-output-material-drift:{partitionId}");
            }

            if (!candidateByPartition.TryAdd(partitionId, candidate))
                throw new InvalidDataException($"qa04.full-step.domain-output-duplicate-partition:{partitionId}");
        }

        if (!candidateByPartition.Keys.ToHashSet(StringComparer.Ordinal)
                .SetEquals(ExpectedOwners.Keys))
            throw new InvalidDataException("qa04.full-step.domain-output-partition-coverage-drift");

        var plan = StandardDomainExecutionPlanV1.Create();
        if (plan.Entries.Count != 8)
            throw new InvalidDataException("qa04.full-step.domain-output-standard-domain-count-drift");

        var candidatesByOwner = candidateByPartition.Values
            .GroupBy(static candidate => candidate.OwnerDomain)
            .ToDictionary(
                static group => group.Key,
                static group => group.OrderBy(candidate => candidate.PartitionId.Value, StringComparer.Ordinal).ToArray());

        var outputs = plan.Entries
            .Select(entry => new DomainCandidateOutputV1(
                entry.DomainToken,
                basisState.Header.Step,
                intents: Array.Empty<MutationIntentCandidateV1>(),
                localPartitionCandidates: candidatesByOwner.TryGetValue(entry.DomainToken, out var local)
                    ? local
                    : Array.Empty<PartitionCandidateV1>()))
            .ToArray();

        if (outputs.Length != 8 || outputs.Select(static output => output.DomainToken).Distinct().Count() != 8)
            throw new InvalidDataException("qa04.full-step.domain-output-coverage-drift");
        if (outputs.Sum(static output => output.LocalPartitionCandidates.Count) != ExpectedOwners.Count)
            throw new InvalidDataException("qa04.full-step.domain-output-local-candidate-count-drift");
        if (outputs.Any(static output => output.Intents.Count != 0))
            throw new InvalidDataException("qa04.full-step.domain-output-unexpected-intent");

        var emptyOwners = outputs
            .Where(static output => output.LocalPartitionCandidates.Count == 0)
            .Select(static output => output.DomainToken.Value)
            .ToHashSet(StringComparer.Ordinal);
        if (!emptyOwners.SetEquals(new[] { "spatial", "participation" }))
            throw new InvalidDataException("qa04.full-step.domain-output-empty-owner-drift");

        return new Qa04CanonicalOperationDomainOutputBatchV1(
            basisState.Header.Step,
            targetStep,
            Array.AsReadOnly(outputs));
    }
}
