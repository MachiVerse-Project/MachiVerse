using MachiVerse.Simulation.Core.Domains;
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
    private const string ChangeSetHashDomain = "mv.qa04.partition-change-set.v1";

    public static Qa04CanonicalOperationPartitionCandidateBatchV1 Bind(
        WorldStateV1 basisState,
        Qa04CanonicalOperationMutationBatchResultV1 mutationResult,
        IDomainRecordSchemaResolverV1 references)
    {
        ArgumentNullException.ThrowIfNull(basisState);
        ArgumentNullException.ThrowIfNull(mutationResult);
        ArgumentNullException.ThrowIfNull(mutationResult.State);
        ArgumentNullException.ThrowIfNull(references);

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
                payload => StandardDomainPayloadCanonicalDigestV1.Compute(
                    InfrastructureServiceQueuePayloadV1.PartitionId,
                    payload.ToStandardPayload(),
                    references: references)),
            BindStandard(
                basisState,
                targetStep,
                state.ResidentBehaviorState,
                payload => StandardDomainPayloadCanonicalDigestV1.Compute(
                    ResidentBehaviorStatePayloadV1.PartitionId,
                    payload.ToStandardPayload(),
                    references: references)),
            BindStandard(
                basisState,
                targetStep,
                state.PhysicalPresence,
                payload => StandardDomainPayloadCanonicalDigestV1.Compute(
                    PhysicalPresencePayloadV1.PartitionId,
                    payload.ToStandardPayload(),
                    references: references)),
            BindMarket(basisState, targetStep, state.MarketTransaction, references),
            BindStandard(
                basisState,
                targetStep,
                state.GovernanceSecurityIncident,
                payload => StandardDomainPayloadCanonicalDigestV1.Compute(
                    GovernanceSecurityIncidentPayloadV1.PartitionId,
                    payload.ToStandardPayload(),
                    references: references)),
            BindStandard(
                basisState,
                targetStep,
                state.EnvironmentHazard,
                payload => StandardDomainPayloadCanonicalDigestV1.Compute(
                    EnvironmentHazardPayloadV1.PartitionId,
                    payload.ToStandardPayload(),
                    references: references)),
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
        SocietyMarketTransactionPartitionStateV2 resultingState,
        IDomainRecordSchemaResolverV1 references)
    {
        ArgumentNullException.ThrowIfNull(resultingState);
        ArgumentNullException.ThrowIfNull(references);
        SocietyMarketTransactionPartitionIdentityV2.ValidateCanonicalContract();
        if (resultingState.State.Identity != SocietyMarketTransactionPartitionIdentityV2.Identity)
            throw new InvalidDataException("qa04.full-step.partition-candidate-market-identity-drift");

        var basisHeader = basisState.Partitions.Get(SocietyMarketTransactionRecordSchemaV2.PartitionId).Header;
        RequireBasisIdentity(basisHeader, SocietyMarketTransactionPartitionIdentityV2.Identity);
        var resultingRevision = NextRevision(basisHeader);
        var resultingHeader = PartitionStateHeaderV1.CreateCanonical(
            resultingState.State,
            resultingRevision,
            targetStep,
            basisHeader.DetailLevel,
            payload => SocietyMarketTransactionPayloadCanonicalDigestV2.Compute(payload, references));
        return BindHeader(basisState, basisHeader, resultingHeader, targetStep);
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

        var changeSetDigest = ComputeQa04ChangeSetDigest(
            basisHeader,
            resultingHeader,
            basisState.Header.Step,
            targetStep);
        var candidate = CreateOwnerCandidate(
            basisState,
            basisHeader.PartitionId.Value,
            changeSetDigest);
        var material = new StepPartitionStateMaterialV1(resultingHeader);

        if (candidate.CandidateRevision != resultingHeader.Revision ||
            candidate.BasisRevision != basisHeader.Revision ||
            candidate.BasisStep != basisState.Header.Step ||
            candidate.TargetStep != targetStep ||
            candidate.PartitionId != resultingHeader.PartitionId ||
            candidate.OwnerDomain != resultingHeader.OwnerDomain ||
            !candidate.ChangeSetDigest.AsSpan().SequenceEqual(changeSetDigest))
            throw new InvalidDataException("qa04.full-step.partition-candidate-binding-drift");

        return new Qa04CanonicalOperationPartitionMutationV1(candidate, material);
    }

    private static PartitionCandidateV1 CreateOwnerCandidate(
        WorldStateV1 basisState,
        string partitionId,
        ReadOnlySpan<byte> changeSetDigest)
        => partitionId switch
        {
            InfrastructureServiceQueuePayloadV1.PartitionId =>
                InfrastructureInformationPartitionCandidateFactoryV1.Create(
                    basisState,
                    partitionId,
                    changeSetDigest),
            ResidentBehaviorStatePayloadV1.PartitionId =>
                ResidentParticipationPartitionCandidateFactoryV1.CreateResident(
                    basisState,
                    partitionId,
                    changeSetDigest),
            PhysicalPresencePayloadV1.PartitionId =>
                PhysicalBuiltPartitionCandidateFactoryV1.Create(
                    basisState,
                    partitionId,
                    changeSetDigest),
            SocietyMarketTransactionRecordSchemaV2.PartitionId =>
                SocietyEconomyPartitionCandidateFactoryV1.Create(
                    basisState,
                    partitionId,
                    changeSetDigest),
            GovernanceSecurityIncidentPayloadV1.PartitionId =>
                GovernanceSecurityPartitionCandidateFactoryV1.Create(
                    basisState,
                    partitionId,
                    changeSetDigest),
            EnvironmentHazardPayloadV1.PartitionId =>
                DomainOwnedPartitionCandidateFactoryV1.CreateEnvironment(
                    basisState,
                    partitionId,
                    changeSetDigest),
            _ => throw new InvalidDataException($"qa04.full-step.partition-candidate-owner-unregistered:{partitionId}"),
        };

    private static byte[] ComputeQa04ChangeSetDigest(
        PartitionStateHeaderV1 basisHeader,
        PartitionStateHeaderV1 resultingHeader,
        ulong basisStep,
        ulong targetStep)
    {
        if (targetStep != checked(basisStep + 1UL))
            throw new InvalidDataException("qa04.full-step.partition-candidate-target-step-drift");
        if (basisHeader.PartitionId != resultingHeader.PartitionId ||
            basisHeader.OwnerDomain != resultingHeader.OwnerDomain ||
            basisHeader.Schema != resultingHeader.Schema)
            throw new InvalidDataException("qa04.full-step.partition-candidate-transition-identity-drift");
        if (resultingHeader.Revision != checked(basisHeader.Revision + 1UL))
            throw new InvalidDataException("qa04.full-step.partition-candidate-revision-drift");
        if (basisHeader.CanonicalDigest.Length != 32 || resultingHeader.CanonicalDigest.Length != 32)
            throw new InvalidDataException("qa04.full-step.partition-candidate-partition-digest-length");

        return HashSuite.DomainHash(ChangeSetHashDomain, writer =>
        {
            writer.WriteMapStart(7);
            writer.WriteUnsigned(0); writer.WriteAsciiText(basisHeader.PartitionId.Value);
            writer.WriteUnsigned(1); writer.WriteUnsigned(basisHeader.Revision);
            writer.WriteUnsigned(2); writer.WriteUnsigned(basisStep);
            writer.WriteUnsigned(3); writer.WriteBytes(basisHeader.CanonicalDigest);
            writer.WriteUnsigned(4); writer.WriteUnsigned(resultingHeader.Revision);
            writer.WriteUnsigned(5); writer.WriteUnsigned(targetStep);
            writer.WriteUnsigned(6); writer.WriteBytes(resultingHeader.CanonicalDigest);
        });
    }

    private static void RequireBasisIdentity(
        PartitionStateHeaderV1 basisHeader,
        DomainPartitionIdentityV1 resultingIdentity)
    {
        if (basisHeader.PartitionId != resultingIdentity.PartitionId ||
            basisHeader.OwnerDomain != resultingIdentity.OwnerDomain ||
            basisHeader.Schema != resultingIdentity.PartitionSchema)
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
