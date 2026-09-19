using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04PartitionCandidateBatchResultV1(
    ulong BasisStep,
    ulong TargetStep,
    IReadOnlyList<PartitionCandidateV1> Candidates);

/// <summary>
/// Gate-2 Step-2 boundary: converts actual Gate-1 typed mutation receipts into owner-validated
/// PartitionCandidateV1 values. This does not assemble StepCandidateV1 or cross COMMIT/publish.
/// </summary>
public static class Qa04PartitionCandidateBatchV1
{
    private const string ReceiptSchema = "qa04.partition-change-set.v1";

    private const string InfrastructureFamily = "infrastructure-service-delivery";
    private const string ResidentFamily = "participation-control-resident-action";
    private const string PhysicalFamily = "physical-item-movement-work";
    private const string MarketFamily = "society-market-payment-contract";
    private const string GovernanceFamily = "governance-security";
    private const string EnvironmentFamily = "environment-spatial-admin-synthetic";

    private const string InfrastructurePartition = "infrastructure.service_queue";
    private const string ResidentPartition = "resident.behavior_state";
    private const string PhysicalPartition = "physical.presence";
    private const string MarketPartition = "society.market_transaction";
    private const string GovernancePartition = "governance.security_incident";
    private const string EnvironmentPartition = "environment.hazard";

    public static Qa04PartitionCandidateBatchResultV1 Create(
        WorldStateV1 frozenState,
        Qa04CanonicalOperationMutationBatchResultV1 mutation)
    {
        ArgumentNullException.ThrowIfNull(frozenState);
        ArgumentNullException.ThrowIfNull(mutation);
        if (frozenState.Header.WorldId != Qa04ReferenceLoadV1.WorldId)
            throw new InvalidDataException("qa04.full-step.candidate-world-id-drift");
        if (frozenState.Header.Step == ulong.MaxValue)
            throw new InvalidDataException("qa04.full-step.candidate-step-overflow");

        var targetStep = checked(frozenState.Header.Step + 1UL);
        if (mutation.EffectiveStep != targetStep)
            throw new InvalidDataException("qa04.full-step.candidate-effective-step-drift");
        if (mutation.Changes.Count == 0 || mutation.Changes.Count != mutation.AppliedOperationIds.Count)
            throw new InvalidDataException("qa04.full-step.candidate-change-coverage");
        if (!mutation.Changes.Select(static change => change.OperationId)
                .SequenceEqual(mutation.AppliedOperationIds))
            throw new InvalidDataException("qa04.full-step.candidate-operation-order-drift");

        ValidateChanges(mutation.Changes);

        var candidates = new List<PartitionCandidateV1>();
        foreach (var group in mutation.Changes
                     .GroupBy(static change => change.PartitionId.Value, StringComparer.Ordinal)
                     .OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            var partitionId = group.Key;
            var changes = group.ToArray();
            var basis = frozenState.Partitions.Get(partitionId).Header;
            var identity = StandardDomainPartitionRegistry.Get(partitionId);
            if (basis.PartitionId != identity.PartitionId || basis.OwnerDomain != identity.OwnerDomain)
                throw new InvalidDataException("qa04.full-step.candidate-basis-identity-drift");
            if (basis.BasisStep > frozenState.Header.Step)
                throw new InvalidDataException("qa04.full-step.candidate-basis-ahead");

            var postItemCount = PostItemCount(mutation.State, partitionId);
            var digest = ComputeReceiptDigest(
                basis,
                frozenState.Header.Step,
                targetStep,
                postItemCount,
                changes);
            var candidate = CreateOwnedCandidate(frozenState, partitionId, digest);

            if (candidate.PartitionId.Value != partitionId ||
                candidate.OwnerDomain != identity.OwnerDomain ||
                candidate.BasisRevision != basis.Revision ||
                candidate.CandidateRevision != checked(basis.Revision + 1UL) ||
                candidate.BasisStep != frozenState.Header.Step ||
                candidate.TargetStep != targetStep ||
                !candidate.ChangeSetDigest.AsSpan().SequenceEqual(digest))
            {
                throw new InvalidDataException("qa04.full-step.candidate-result-drift");
            }

            candidates.Add(candidate);
        }

        return new Qa04PartitionCandidateBatchResultV1(
            frozenState.Header.Step,
            targetStep,
            Array.AsReadOnly(candidates.ToArray()));
    }

    private static void ValidateChanges(IReadOnlyList<Qa04CanonicalOperationMutationChangeV1> changes)
    {
        SameStepOrderKey? previous = null;
        var operationIds = new HashSet<OpaqueId128>();
        foreach (var change in changes)
        {
            ArgumentNullException.ThrowIfNull(change);
            if (!operationIds.Add(change.OperationId))
                throw new InvalidDataException("qa04.full-step.candidate-operation-duplicate");
            if (previous is not null && previous.CompareTo(change.OrderKey) >= 0)
                throw new InvalidDataException("qa04.full-step.candidate-order-not-canonical");
            previous = change.OrderKey;
            if (change.ImmutablePayloadDigest is null || change.ImmutablePayloadDigest.Length != 32)
                throw new InvalidDataException("qa04.full-step.candidate-payload-digest-length");
            if (change.ChangedRecordId.IsZero || change.ResultRevision == 0)
                throw new InvalidDataException("qa04.full-step.candidate-result-envelope-invalid");
            if (change.MutationMode.Value is not ("create" or "revise"))
                throw new InvalidDataException("qa04.full-step.candidate-mutation-mode-invalid");

            var expectedPartition = ExpectedPartition(change.FamilyToken.Value);
            if (!string.Equals(change.PartitionId.Value, expectedPartition, StringComparison.Ordinal))
                throw new InvalidDataException("qa04.full-step.candidate-family-partition-drift");
        }
    }

    private static string ExpectedPartition(string family)
        => family switch
        {
            InfrastructureFamily => InfrastructurePartition,
            ResidentFamily => ResidentPartition,
            PhysicalFamily => PhysicalPartition,
            MarketFamily => MarketPartition,
            GovernanceFamily => GovernancePartition,
            EnvironmentFamily => EnvironmentPartition,
            _ => throw new InvalidDataException($"qa04.full-step.candidate-family-unregistered:{family}"),
        };

    private static ulong PostItemCount(Qa04CanonicalOperationMutationStateV1 state, string partitionId)
        => partitionId switch
        {
            InfrastructurePartition => state.InfrastructureServiceQueue.ItemCount,
            ResidentPartition => state.ResidentBehaviorState.ItemCount,
            PhysicalPartition => state.PhysicalPresence.ItemCount,
            MarketPartition => state.MarketTransaction.State.ItemCount,
            GovernancePartition => state.GovernanceSecurityIncident.ItemCount,
            EnvironmentPartition => state.EnvironmentHazard.ItemCount,
            _ => throw new InvalidDataException($"qa04.full-step.candidate-partition-unregistered:{partitionId}"),
        };

    private static byte[] ComputeReceiptDigest(
        PartitionStateHeaderV1 basis,
        ulong basisStep,
        ulong targetStep,
        ulong postItemCount,
        IReadOnlyList<Qa04CanonicalOperationMutationChangeV1> changes)
        => HashSuite.DomainHash("mv.state-diagnostic.v1", writer =>
        {
            writer.WriteMapStart(9);
            writer.WriteUnsigned(0); writer.WriteAsciiText(ReceiptSchema);
            writer.WriteUnsigned(1); writer.WriteAsciiText(basis.PartitionId.Value);
            writer.WriteUnsigned(2); writer.WriteAsciiText(basis.OwnerDomain.Value);
            writer.WriteUnsigned(3); writer.WriteUnsigned(basis.Revision);
            writer.WriteUnsigned(4); writer.WriteUnsigned(basisStep);
            writer.WriteUnsigned(5); writer.WriteUnsigned(targetStep);
            writer.WriteUnsigned(6); writer.WriteBytes(basis.CanonicalDigest);
            writer.WriteUnsigned(7); writer.WriteUnsigned(postItemCount);
            writer.WriteUnsigned(8);
            writer.WriteArrayStart((ulong)changes.Count);
            foreach (var change in changes)
            {
                writer.WriteMapStart(12);
                writer.WriteUnsigned(0); writer.WriteBytes(change.OperationId.ToBytes());
                writer.WriteUnsigned(1); writer.WriteAsciiText(change.FamilyToken.Value);
                writer.WriteUnsigned(2); writer.WriteAsciiText(change.OperationKind);
                writer.WriteUnsigned(3); writer.WriteBytes(change.ImmutablePayloadDigest);
                writer.WriteUnsigned(4); writer.WriteBytes(change.ChangedRecordId.ToBytes());
                writer.WriteUnsigned(5); writer.WriteAsciiText(change.RecordSchema.SchemaId.Value);
                writer.WriteUnsigned(6); writer.WriteUnsigned(change.RecordSchema.Version.Major);
                writer.WriteUnsigned(7); writer.WriteUnsigned(change.RecordSchema.Version.Minor);
                writer.WriteUnsigned(8); writer.WriteUnsigned(change.ResultRevision);
                writer.WriteUnsigned(9); writer.WriteUnsigned(change.ResultCreatedStep);
                writer.WriteUnsigned(10); writer.WriteUnsigned((byte)change.ResultDetailLevel);
                writer.WriteUnsigned(11); writer.WriteAsciiText(change.MutationMode.Value);
            }
        });

    private static PartitionCandidateV1 CreateOwnedCandidate(
        WorldStateV1 frozenState,
        string partitionId,
        ReadOnlySpan<byte> digest)
        => partitionId switch
        {
            InfrastructurePartition => InfrastructureInformationPartitionCandidateFactoryV1.Create(
                frozenState, partitionId, digest),
            ResidentPartition => ResidentParticipationPartitionCandidateFactoryV1.CreateResident(
                frozenState, partitionId, digest),
            PhysicalPartition => PhysicalBuiltPartitionCandidateFactoryV1.Create(
                frozenState, partitionId, digest),
            MarketPartition => SocietyEconomyPartitionCandidateFactoryV1.Create(
                frozenState, partitionId, digest),
            GovernancePartition => GovernanceSecurityPartitionCandidateFactoryV1.Create(
                frozenState, partitionId, digest),
            EnvironmentPartition => DomainOwnedPartitionCandidateFactoryV1.CreateEnvironment(
                frozenState, partitionId, digest),
            _ => throw new InvalidDataException($"qa04.full-step.candidate-partition-unregistered:{partitionId}"),
        };
}
