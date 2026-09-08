using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Runtime;

/// <summary>
/// Concrete authoritative material for one partition changed by a StepCandidate. The resulting
/// header must have been derived from the real resulting partition state (for example through
/// PartitionStateHeaderV1.CreateCanonical). The canonical change-set digest binds the candidate to
/// both its basis partition and this resulting partition header.
/// </summary>
public sealed class StepPartitionStateMaterialV1
{
    public StepPartitionStateMaterialV1(PartitionStateHeaderV1 resultingHeader)
    {
        ResultingHeader = resultingHeader ?? throw new ArgumentNullException(nameof(resultingHeader));
    }

    public PartitionStateHeaderV1 ResultingHeader { get; }

    public static byte[] ComputeChangeSetDigest(
        PartitionStateHeaderV1 basisHeader,
        PartitionStateHeaderV1 resultingHeader,
        ulong basisWorldStep,
        ulong targetWorldStep)
    {
        ArgumentNullException.ThrowIfNull(basisHeader);
        ArgumentNullException.ThrowIfNull(resultingHeader);
        if (targetWorldStep != checked(basisWorldStep + 1))
            throw new InvalidDataException("step-state.change-set-target-step-mismatch");
        if (basisHeader.PartitionId != resultingHeader.PartitionId ||
            basisHeader.OwnerDomain != resultingHeader.OwnerDomain ||
            basisHeader.Schema != resultingHeader.Schema)
            throw new InvalidDataException("step-state.change-set-partition-identity-mismatch");

        return HashSuite.DomainHash("mv.step-partition-change-set.v1", writer =>
        {
            writer.WriteMapStart(13);
            writer.WriteUnsigned(0); writer.WriteAsciiText(basisHeader.PartitionId.Value);
            writer.WriteUnsigned(1); writer.WriteAsciiText(basisHeader.OwnerDomain.Value);
            writer.WriteUnsigned(2); writer.WriteAsciiText(basisHeader.Schema.SchemaId.Value);
            writer.WriteUnsigned(3); writer.WriteUnsigned(basisHeader.Revision);
            writer.WriteUnsigned(4); writer.WriteUnsigned(resultingHeader.Revision);
            writer.WriteUnsigned(5); writer.WriteUnsigned(basisHeader.BasisStep);
            writer.WriteUnsigned(6); writer.WriteUnsigned(resultingHeader.BasisStep);
            writer.WriteUnsigned(7); writer.WriteUnsigned(basisWorldStep);
            writer.WriteUnsigned(8); writer.WriteUnsigned(targetWorldStep);
            writer.WriteUnsigned(9); writer.WriteBytes(basisHeader.CanonicalDigest);
            writer.WriteUnsigned(10); writer.WriteBytes(resultingHeader.CanonicalDigest);
            writer.WriteUnsigned(11); writer.WriteUnsigned((byte)resultingHeader.DetailLevel);
            writer.WriteUnsigned(12); writer.WriteUnsigned(resultingHeader.ItemCount);
        });
    }

    internal void ValidateFor(
        PartitionCandidateV1 candidate,
        PartitionStateHeaderV1 basisHeader,
        ulong basisWorldStep,
        ulong targetWorldStep)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(basisHeader);

        if (candidate.PartitionId != basisHeader.PartitionId ||
            candidate.OwnerDomain != basisHeader.OwnerDomain)
            throw new InvalidDataException("step-state.candidate-basis-partition-mismatch");
        if (ResultingHeader.PartitionId != candidate.PartitionId ||
            ResultingHeader.OwnerDomain != candidate.OwnerDomain ||
            ResultingHeader.Schema != basisHeader.Schema)
            throw new InvalidDataException("step-state.resulting-partition-identity-mismatch");
        if (candidate.BasisRevision != basisHeader.Revision)
            throw new InvalidDataException("step-state.candidate-basis-revision-mismatch");
        if (ResultingHeader.Revision != candidate.CandidateRevision)
            throw new InvalidDataException("step-state.resulting-partition-revision-mismatch");
        if (candidate.BasisStep != basisWorldStep || candidate.TargetStep != targetWorldStep)
            throw new InvalidDataException("step-state.candidate-step-mismatch");
        if (ResultingHeader.BasisStep != targetWorldStep)
            throw new InvalidDataException("step-state.resulting-partition-basis-step-mismatch");

        var expected = ComputeChangeSetDigest(basisHeader, ResultingHeader, basisWorldStep, targetWorldStep);
        if (!CryptographicOperations.FixedTimeEquals(expected, candidate.ChangeSetDigest))
            throw new InvalidDataException("step-state.change-set-digest-mismatch");
    }
}

/// <summary>
/// A deterministic State(S+1) prepared from State(S) and a commit-eligible StepCandidate. It is
/// deliberately non-publishable: durability has not yet established authority.
///
/// This initial surface carries the four core substates and master/rate generations forward
/// unchanged. A future candidate type must explicitly bind changes to those substates before this
/// surface is widened; they are never silently accepted as unbound Step output here.
/// </summary>
public sealed class PreparedStepWorldStateV1
{
    internal PreparedStepWorldStateV1(
        OpaqueId128 candidateId,
        ulong basisStep,
        byte[] candidateDiagnosticDigest,
        byte[] basisStateDigest,
        WorldStateV1 resultingState)
    {
        CandidateId = candidateId;
        BasisStep = basisStep;
        CandidateDiagnosticDigest = candidateDiagnosticDigest.ToArray();
        BasisStateDigest = basisStateDigest.ToArray();
        ResultingState = resultingState;
    }

    public OpaqueId128 CandidateId { get; }
    public ulong BasisStep { get; }
    public ulong TargetStep => ResultingState.Header.Step;
    public byte[] CandidateDiagnosticDigest { get; }
    public byte[] BasisStateDigest { get; }
    public WorldStateV1 ResultingState { get; }
    public bool IsPublishable => false;
}

/// <summary>
/// World state whose publication authority is backed by a durable Step receipt for the exact
/// candidate and resulting Step used to prepare it.
/// </summary>
public sealed class AuthoritativeStepWorldStateV1
{
    internal AuthoritativeStepWorldStateV1(PreparedStepWorldStateV1 prepared, DurableStepReceiptV1 receipt)
    {
        Prepared = prepared;
        Receipt = receipt;
    }

    public PreparedStepWorldStateV1 Prepared { get; }
    public DurableStepReceiptV1 Receipt { get; }
    public WorldStateV1 State => Prepared.ResultingState;
    public bool IsPublishable => true;
}

public static class StepStateApplicationV1
{
    public static PreparedStepWorldStateV1 Prepare(
        WorldStateV1 basisState,
        StepCandidateV1 candidate,
        IEnumerable<StepPartitionStateMaterialV1> partitionMaterials)
    {
        ArgumentNullException.ThrowIfNull(basisState);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(partitionMaterials);

        if (candidate.WorldId != basisState.Header.WorldId || candidate.BasisStep != basisState.Header.Step)
            throw new InvalidDataException("step-state.candidate-world-basis-mismatch");
        if (candidate.TargetStep != checked(basisState.Header.Step + 1))
            throw new InvalidDataException("step-state.candidate-target-step-mismatch");
        if (!candidate.CommitDecision.CanCommit)
            throw new InvalidDataException("step-state.candidate-commit-blocked");
        if (candidate.IsPublishable)
            throw new InvalidDataException("step-state.candidate-premature-authority");
        if (candidate.ConfigGeneration < basisState.Header.ConfigGeneration)
            throw new InvalidDataException("step-state.config-generation-regression");
        if (candidate.ConfigGeneration == basisState.Header.ConfigGeneration &&
            !CryptographicOperations.FixedTimeEquals(candidate.ConfigDigest, basisState.Diagnostic.ConfigDigest))
            throw new InvalidDataException("step-state.config-digest-mismatch-at-generation");

        var materials = partitionMaterials.ToArray();
        if (materials.Any(static material => material is null))
            throw new ArgumentNullException(nameof(partitionMaterials));
        if (materials.Select(static material => material.ResultingHeader.PartitionId).Distinct().Count() != materials.Length)
            throw new InvalidDataException("step-state.duplicate-partition-material");

        var candidates = candidate.PartitionCandidates.ToDictionary(static item => item.PartitionId);
        var materialByPartition = materials.ToDictionary(static item => item.ResultingHeader.PartitionId);
        if (!candidates.Keys.ToHashSet().SetEquals(materialByPartition.Keys))
            throw new InvalidDataException("step-state.partition-material-coverage-mismatch");

        foreach (var pair in candidates)
        {
            var basisHeader = basisState.Partitions.Get(pair.Key.Value).Header;
            materialByPartition[pair.Key].ValidateFor(
                pair.Value,
                basisHeader,
                candidate.BasisStep,
                candidate.TargetStep);
        }

        var nextPartitions = basisState.Partitions.CanonicalEntries
            .Select(partition => materialByPartition.TryGetValue(partition.Header.PartitionId, out var material)
                ? new PartitionStateRefV1(material.ResultingHeader)
                : partition)
            .ToArray();

        var nextHeader = new WorldStateHeaderV1(
            basisState.Header.WorldId,
            candidate.TargetStep,
            basisState.Header.WorldSeedDigest,
            candidate.ConfigGeneration,
            basisState.Header.MasterGeneration,
            basisState.Header.RateGeneration,
            previousStateDigest: basisState.Diagnostic.StateDigest);
        var nextState = new WorldStateV1(
            nextHeader,
            new OrderedPartitionDirectoryV1(nextPartitions),
            basisState.SchedulerState,
            basisState.OperationState,
            basisState.DetailState,
            basisState.DomainRegistryState,
            candidate.ConfigDigest);

        if (nextState.Header.PreviousStateDigest is null ||
            !CryptographicOperations.FixedTimeEquals(nextState.Header.PreviousStateDigest, basisState.Diagnostic.StateDigest))
            throw new InvalidDataException("step-state.previous-state-digest-mismatch");
        if (nextState.Header.Step != candidate.TargetStep)
            throw new InvalidDataException("step-state.resulting-step-mismatch");

        return new PreparedStepWorldStateV1(
            candidate.CandidateId,
            candidate.BasisStep,
            candidate.DiagnosticDigest,
            basisState.Diagnostic.StateDigest,
            nextState);
    }

    public static AuthoritativeStepWorldStateV1 Publish(
        PreparedStepWorldStateV1 prepared,
        DurableStepReceiptV1 receipt)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(receipt);

        if (!receipt.IsPublishable)
            throw new InvalidDataException("step-state.receipt-not-publishable");
        if (receipt.CandidateId != prepared.CandidateId)
            throw new InvalidDataException("step-state.receipt-candidate-mismatch");
        if (receipt.BasisStep != prepared.BasisStep || receipt.ResultingStep != prepared.TargetStep)
            throw new InvalidDataException("step-state.receipt-step-mismatch");
        if (!CryptographicOperations.FixedTimeEquals(
                receipt.CandidateDiagnosticDigest,
                prepared.CandidateDiagnosticDigest))
            throw new InvalidDataException("step-state.receipt-candidate-diagnostic-mismatch");
        if (prepared.ResultingState.Header.PreviousStateDigest is null ||
            !CryptographicOperations.FixedTimeEquals(
                prepared.ResultingState.Header.PreviousStateDigest,
                prepared.BasisStateDigest))
            throw new InvalidDataException("step-state.prepared-chain-mismatch");

        return new AuthoritativeStepWorldStateV1(prepared, receipt);
    }
}
