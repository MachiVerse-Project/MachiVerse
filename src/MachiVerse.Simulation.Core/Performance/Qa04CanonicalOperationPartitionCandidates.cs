using MachiVerse.Simulation.Core.Domains.Environment;
using MachiVerse.Simulation.Core.Domains.GovernanceSecurity;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04CanonicalOperationPartitionMutationV1(
    PartitionCandidateV1 Candidate,
    StepPartitionStateMaterialV1 Material);

public sealed record Qa04CanonicalOperationPartitionCandidateBatchV1(
    ulong BasisStep,
    ulong TargetStep,
    IReadOnlyList<Qa04CanonicalOperationPartitionMutationV1> Partitions);

/// <summary>
/// Gate-2 Step 2 boundary. Binds the six typed mutation results from the canonical QA-04 operation
/// batch to ordinary Step partition candidates and exact resulting partition-state material. This
/// stage does not assemble domain outputs, build a StepCandidate, cross SQLite COMMIT, or publish
/// State(S+1).
/// </summary>
public static class Qa04CanonicalOperationPartitionCandidateBinderV1
{
    public static Qa04CanonicalOperationPartitionCandidateBatchV1 Bind(
        WorldStateV1 basisState,
        Qa04CanonicalOperationMutationBatchResultV1 mutationResult)
    {
        ArgumentNullException.ThrowIfNull(basisState);
        ArgumentNullException.ThrowIfNull(mutationResult);
        ArgumentNullException.ThrowIfNull(mutationResult.State);

        if (basisState.Header.WorldId != Qa04ReferenceLoadV1.WorldId)
            throw new InvalidDataException("qa04.full-step.partition-candidate-world-id-drift");
        if (basisState.Header.Step == ulong.MaxValue)
            throw new InvalidDataException("qa04.full-step.partition-candidate-step-overflow");

        var targetStep = checked(basisState.Header.Step + 1UL);
        if (mutationResult.EffectiveStep != targetStep)
            throw new InvalidDataException("qa04.full-step.partition-candidate-effective-step-drift");

        var state = mutationResult.State;
        var bound = new[]
        {
            BindStandard(
                basisState,
                targetStep,
                state.InfrastructureServiceQueue,
                static payload => payload.CanonicalDigest()),
            BindStandard(
                basisState,
                targetStep,
                state.ResidentBehaviorState,
                static payload => payload.CanonicalDigest()),
            BindStandard(
                basisState,
                targetStep,
                state.PhysicalPresence,
                static payload => payload.CanonicalDigest()),
            BindMarket(basisState, targetStep, state.MarketTransaction),
            BindStandard(
                basisState,
                targetStep,
                state.GovernanceSecurityIncident,
                static payload => payload.CanonicalDigest()),
            BindStandard(
                basisState,
                targetStep,
                state.EnvironmentHazard,
                static payload => payload.CanonicalDigest()),
        }
        .OrderBy(static item => item.Candidate.PartitionId.Value, StringComparer.Ordinal)
        .ToArray();

        if (bound.Length != 6 || bound.Select(static item => item.Candidate.PartitionId).Distinct().Count() != 6)
            throw new InvalidDataException("qa04.full-step.partition-candidate-coverage-drift");
        if (bound.Any(item => item.Candidate.BasisStep != basisState.Header.Step ||
                              item.Candidate.TargetStep != targetStep ||
                              item.Material.ResultingHeader.BasisStep != targetStep))
            throw new InvalidDataException("qa04.full-step.partition-candidate-step-drift");

        return new Qa04CanonicalOperationPartitionCandidateBatchV1(
            basisState.Header.Step,
            targetStep,
            Array.AsReadOnly(bound));
    }

    private static Qa04CanonicalOperationPartitionMutationV1 BindStandard<TPayload>(
        WorldStateV1 basisState,
        ulong targetStep,
        DomainPartitionStateV1<TPayload> resultingState,
        Func<TPayload, byte[]> canonicalPayloadDigest)
    {
        ArgumentNullException.ThrowIfNull(resultingState);
        ArgumentNullException.ThrowIfNull(canonicalPayloadDigest);

        var basisHeader = basisState.Partitions.Get(resultingState.Identity.PartitionId.Value).Header;
        RequireBasisIdentity(basisHeader, resultingState.Identity);
        var resultingRevision = NextRevision(basisHeader);
        var resultingHeader = PartitionStateHeaderV1.CreateCanonical(
            resultingState,
            resultingRevision,
            targetStep,
            basisHeader.DetailLevel,
            canonicalPayloadDigest);
        return BindHeader(basisState, basisHeader, resultingHeader, targetStep);
    }

    private static Qa04CanonicalOperationPartitionMutationV1 BindMarket(
        WorldStateV1 basisState,
        ulong targetStep,
        SocietyMarketTransactionPartitionStateV2 resultingState)
    {
        ArgumentNullException.ThrowIfNull(resultingState);
        SocietyMarketTransactionPartitionIdentityV2.ValidateCanonicalContract();

        var basisHeader = basisState.Partitions.Get(SocietyMarketTransactionRecordSchemaV2.PartitionId).Header;
        RequireBasisIdentity(basisHeader, SocietyMarketTransactionPartitionIdentityV2.Identity);
        var resultingRevision = NextRevision(basisHeader);
        var authority = SocietyMarketTransactionSnapshotAuthorityV2.CreateCanonical(
            resultingState,
            resultingRevision,
            targetStep,
            basisHeader.DetailLevel);
        authority.VerifyBoundAuthority();
        return BindHeader(basisState, basisHeader, authority.Header, targetStep);
    }

    private static Qa04CanonicalOperationPartitionMutationV1 BindHeader(
        WorldStateV1 basisState,
        PartitionStateHeaderV1 basisHeader,
        PartitionStateHeaderV1 resultingHeader,
        ulong targetStep)
    {
        if (resultingHeader.Revision != checked(basisHeader.Revision + 1UL))
            throw new InvalidDataException("qa04.full-step.partition-candidate-revision-drift");
        if (resultingHeader.BasisStep != targetStep || resultingHeader.DetailLevel != basisHeader.DetailLevel)
            throw new InvalidDataException("qa04.full-step.partition-candidate-header-drift");

        var changeSetDigest = StepPartitionStateMaterialV1.ComputeChangeSetDigest(
            basisHeader,
            resultingHeader,
            basisState.Header.Step,
            targetStep);
        var candidate = new PartitionCandidateV1(
            basisHeader.PartitionId,
            basisHeader.OwnerDomain,
            basisHeader.Revision,
            basisState.Header.Step,
            changeSetDigest);
        var material = new StepPartitionStateMaterialV1(resultingHeader);

        if (candidate.CandidateRevision != resultingHeader.Revision ||
            candidate.TargetStep != targetStep ||
            candidate.PartitionId != resultingHeader.PartitionId ||
            candidate.OwnerDomain != resultingHeader.OwnerDomain)
            throw new InvalidDataException("qa04.full-step.partition-candidate-binding-drift");

        return new Qa04CanonicalOperationPartitionMutationV1(candidate, material);
    }

    private static void RequireBasisIdentity(
        PartitionStateHeaderV1 basisHeader,
        DomainPartitionIdentityV1 resultingIdentity)
    {
        if (basisHeader.PartitionId != resultingIdentity.PartitionId ||
            basisHeader.OwnerDomain != resultingIdentity.OwnerDomain ||
            basisHeader.Schema != resultingIdentity.RecordSchema)
        {
            throw new InvalidDataException(
                $"qa04.full-step.partition-candidate-basis-identity:{resultingIdentity.PartitionId.Value}");
        }
    }

    private static ulong NextRevision(PartitionStateHeaderV1 basisHeader)
    {
        if (basisHeader.Revision == ulong.MaxValue)
            throw new InvalidDataException("qa04.full-step.partition-candidate-revision-overflow");
        return basisHeader.Revision + 1UL;
    }
}
