using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

internal static class StepStateApplicationSmoke
{
    internal static void Run()
    {
        var materialized = Qa04ReferenceWorldMaterializerV1.MaterializeResidentIdentityLifecycle(8);
        var basis = materialized.WorldState;
        var scheduler = new OperationSchedulerStateV1(0, null, Array.Empty<ScheduledOperationRefV1>());
        var frozen = StepInputFreezerV1.Freeze(basis, scheduler);
        var basisResident = basis.Partitions.Get("resident.identity_lifecycle").Header;
        var resultingResident = PartitionStateHeaderV1.CreateCanonical(
            materialized.Partition,
            revision: 2,
            basisStep: 1,
            detailLevel: basisResident.DetailLevel,
            static payload => payload.CanonicalDigest());
        var changeSetDigest = StepPartitionStateMaterialV1.ComputeChangeSetDigest(
            basisResident,
            resultingResident,
            basisWorldStep: 0,
            targetWorldStep: 1);
        var residentCandidate = new PartitionCandidateV1(
            new StableToken("resident.identity_lifecycle"),
            new StableToken("resident"),
            basisRevision: 1,
            basisStep: 0,
            changeSetDigest);
        var candidate = BuildCandidate(
            OpaqueId128.Parse("00000000000000000000000000000a01"),
            basis,
            frozen,
            residentCandidate);

        var prepared = StepStateApplicationV1.Prepare(
            basis,
            candidate,
            [new StepPartitionStateMaterialV1(resultingResident)]);
        Require(!prepared.IsPublishable && prepared.TargetStep == 1,
            "Prepared State(S+1) must remain non-publishable before durability.");
        Require(prepared.ResultingState.Header.PreviousStateDigest is not null &&
                prepared.ResultingState.Header.PreviousStateDigest.SequenceEqual(basis.Diagnostic.StateDigest),
            "Prepared State(S+1) must point to the exact State(S) digest.");
        var appliedResident = prepared.ResultingState.Partitions.Get("resident.identity_lifecycle").Header;
        Require(appliedResident.Revision == 2 && appliedResident.BasisStep == 1 &&
                appliedResident.CanonicalDigest.SequenceEqual(resultingResident.CanonicalDigest),
            "Prepared State(S+1) did not install the validated resulting partition header.");
        var unchangedSpatial = prepared.ResultingState.Partitions.Get("spatial.world_frame").Header;
        var basisSpatial = basis.Partitions.Get("spatial.world_frame").Header;
        Require(unchangedSpatial.Revision == basisSpatial.Revision &&
                unchangedSpatial.BasisStep == basisSpatial.BasisStep &&
                unchangedSpatial.CanonicalDigest.SequenceEqual(basisSpatial.CanonicalDigest),
            "Unchanged partitions must preserve their historical header instead of being rewritten at every Step.");

        var receipt = new DurableStepReceiptV1(
            candidate.CandidateId,
            candidate.BasisStep,
            candidate.TargetStep,
            HistorySequence: 2,
            candidate.DiagnosticDigest.ToArray());
        var published = StepStateApplicationV1.Publish(prepared, receipt);
        Require(published.IsPublishable && published.State == prepared.ResultingState,
            "Only the exact prepared state backed by a matching durable receipt may publish.");

        RequireReject(
            () => StepStateApplicationV1.Prepare(basis, candidate, Array.Empty<StepPartitionStateMaterialV1>()),
            "step-state.partition-material-coverage-mismatch");

        var wrongRevision = new PartitionStateHeaderV1(
            StandardDomainPartitionRegistry.Get("resident.identity_lifecycle"),
            revision: 3,
            basisStep: 1,
            detailLevel: resultingResident.DetailLevel,
            itemCount: resultingResident.ItemCount,
            canonicalDigest: resultingResident.CanonicalDigest);
        RequireReject(
            () => StepStateApplicationV1.Prepare(
                basis,
                candidate,
                [new StepPartitionStateMaterialV1(wrongRevision)]),
            "step-state.resulting-partition-revision-mismatch");

        var badDigestCandidate = BuildCandidate(
            OpaqueId128.Parse("00000000000000000000000000000a02"),
            basis,
            frozen,
            new PartitionCandidateV1(
                new StableToken("resident.identity_lifecycle"),
                new StableToken("resident"),
                basisRevision: 1,
                basisStep: 0,
                SHA256.HashData("unbound-change-set"u8)));
        RequireReject(
            () => StepStateApplicationV1.Prepare(
                basis,
                badDigestCandidate,
                [new StepPartitionStateMaterialV1(resultingResident)]),
            "step-state.change-set-digest-mismatch");

        var wrongReceipt = new DurableStepReceiptV1(
            OpaqueId128.Parse("00000000000000000000000000000aff"),
            candidate.BasisStep,
            candidate.TargetStep,
            HistorySequence: 2,
            candidate.DiagnosticDigest.ToArray());
        RequireReject(
            () => StepStateApplicationV1.Publish(prepared, wrongReceipt),
            "step-state.receipt-candidate-mismatch");
    }

    private static StepCandidateV1 BuildCandidate(
        OpaqueId128 candidateId,
        WorldStateV1 basis,
        FrozenStepInputV1 frozen,
        PartitionCandidateV1 residentCandidate)
    {
        var outputs = StandardDomainExecutionPlanV1.Create().Entries
            .Select(entry => entry.DomainToken.Value == "resident"
                ? new DomainCandidateOutputV1(entry.DomainToken, basis.Header.Step, localPartitionCandidates: [residentCandidate])
                : new DomainCandidateOutputV1(entry.DomainToken, basis.Header.Step))
            .ToArray();
        return StepCandidateV1.Build(
            candidateId,
            basis,
            frozen,
            outputs,
            Array.Empty<ConflictGroupResolutionV1>());
    }

    private static void RequireReject(Action action, string expectedMessage)
    {
        try
        {
            action();
        }
        catch (InvalidDataException ex) when (ex.Message == expectedMessage)
        {
            return;
        }
        throw new InvalidOperationException($"Expected Step-state rejection: {expectedMessage}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
