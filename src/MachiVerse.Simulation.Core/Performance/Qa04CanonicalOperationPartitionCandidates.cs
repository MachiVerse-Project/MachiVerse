using MachiVerse.Simulation.Core.Determinism;
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
    IReadOnlyList<Qa04CanonicalOperationPartitionMutationV1> Partitions)
{
    public DeterministicCpuBatchObservationV1? CpuParallelism { get; init; }
}

/// <summary>
/// Gate-2 Step 2 boundary. Binds the six typed mutation results from the canonical QA-04 operation
/// batch to ordinary Step partition candidates and exact resulting partition-state material. The
/// benchmark-only change-set commitment is defined by
/// phase4-alpha11-gate2-partition-change-set-authority.md. This stage does not assemble domain
/// outputs, build a StepCandidate, cross SQLite COMMIT, or publish State(S+1).
/// </summary>
public static class Qa04CanonicalOperationPartitionCandidateBinderV1
{
    private const string InfrastructureFamily = "infrastructure-service-delivery";
    private const string ResidentFamily = "participation-control-resident-action";
    private const string PhysicalFamily = "physical-item-movement-work";
    private const string MarketFamily = "society-market-payment-contract";
    private const string GovernanceFamily = "governance-security";
    private const string EnvironmentFamily = "environment-spatial-admin-synthetic";

    private static readonly IReadOnlyDictionary<string, string> PartitionByFamily =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [InfrastructureFamily] = InfrastructureServiceQueuePayloadV1.PartitionId,
            [ResidentFamily] = ResidentBehaviorStatePayloadV1.PartitionId,
            [PhysicalFamily] = PhysicalPresencePayloadV1.PartitionId,
            [MarketFamily] = SocietyMarketTransactionRecordSchemaV2.PartitionId,
            [GovernanceFamily] = GovernanceSecurityIncidentPayloadV1.PartitionId,
            [EnvironmentFamily] = EnvironmentHazardPayloadV1.PartitionId,
        };

    public static Qa04CanonicalOperationPartitionCandidateBatchV1 Bind(
        WorldStateV1 basisState,
        IReadOnlyList<Qa04CanonicalOperationBindingResultV1> orderedBindings,
        Qa04CanonicalOperationMutationBatchResultV1 mutationResult,
        IDomainRecordSchemaResolverV1 references)
    {
        ArgumentNullException.ThrowIfNull(basisState);
        ArgumentNullException.ThrowIfNull(orderedBindings);
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

        var bindingsByFamily = ValidateAndGroupBindings(orderedBindings, mutationResult, targetStep);
        var state = mutationResult.State;
        var bound = new[]
        {
            BindStandard(
                basisState,
                targetStep,
                bindingsByFamily[InfrastructureFamily],
                state.InfrastructureServiceQueue,
                payload => StandardDomainPayloadCanonicalDigestV1.Compute(
                    InfrastructureServiceQueuePayloadV1.PartitionId,
                    payload.ToStandardPayload(),
                    references: references)),
            BindStandard(
                basisState,
                targetStep,
                bindingsByFamily[ResidentFamily],
                state.ResidentBehaviorState,
                payload => StandardDomainPayloadCanonicalDigestV1.Compute(
                    ResidentBehaviorStatePayloadV1.PartitionId,
                    payload.ToStandardPayload(),
                    references: references)),
            BindStandard(
                basisState,
                targetStep,
                bindingsByFamily[PhysicalFamily],
                state.PhysicalPresence,
                payload => StandardDomainPayloadCanonicalDigestV1.Compute(
                    PhysicalPresencePayloadV1.PartitionId,
                    payload.ToStandardPayload(),
                    references: references)),
            BindMarket(
                basisState,
                targetStep,
                bindingsByFamily[MarketFamily],
                state.MarketTransaction,
                references),
            BindStandard(
                basisState,
                targetStep,
                bindingsByFamily[GovernanceFamily],
                state.GovernanceSecurityIncident,
                payload => StandardDomainPayloadCanonicalDigestV1.Compute(
                    GovernanceSecurityIncidentPayloadV1.PartitionId,
                    payload.ToStandardPayload(),
                    references: references)),
            BindStandard(
                basisState,
                targetStep,
                bindingsByFamily[EnvironmentFamily],
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

    private static IReadOnlyDictionary<string, IReadOnlyList<Qa04CanonicalOperationBindingResultV1>> ValidateAndGroupBindings(
        IReadOnlyList<Qa04CanonicalOperationBindingResultV1> orderedBindings,
        Qa04CanonicalOperationMutationBatchResultV1 mutationResult,
        ulong targetStep)
    {
        if (orderedBindings.Count == 0 || orderedBindings.Count != mutationResult.AppliedOperationIds.Count)
            throw new InvalidDataException("qa04.full-step.partition-candidate-operation-count-drift");

        SameStepOrderKey? previousOrderKey = null;
        var seenOperationIds = new HashSet<OpaqueId128>();
        for (var index = 0; index < orderedBindings.Count; index++)
        {
            var binding = orderedBindings[index]
                ?? throw new InvalidDataException("qa04.full-step.partition-candidate-binding-null");
            if (binding.SourceDescriptor is null || binding.OrderKey is null || binding.ScheduledOperation is null)
                throw new InvalidDataException("qa04.full-step.partition-candidate-binding-null");

            var family = binding.SourceDescriptor.FamilyToken.Value;
            if (!PartitionByFamily.ContainsKey(family))
                throw new InvalidDataException($"qa04.full-step.partition-candidate-family-unregistered:{family}");
            if (binding.ScheduledOperation.EffectiveStep != targetStep)
                throw new InvalidDataException("qa04.full-step.partition-candidate-binding-step-drift");
            if (!binding.OrderKey.ToDatabaseBytes().AsSpan().SequenceEqual(
                    binding.ScheduledOperation.OrderKey.ToDatabaseBytes()))
                throw new InvalidDataException("qa04.full-step.partition-candidate-order-key-drift");
            if (previousOrderKey is not null && previousOrderKey.CompareTo(binding.OrderKey) >= 0)
                throw new InvalidDataException("qa04.full-step.partition-candidate-order-not-canonical");
            previousOrderKey = binding.OrderKey;

            var operationId = binding.SourceDescriptor.OperationId;
            if (operationId.IsZero || !seenOperationIds.Add(operationId))
                throw new InvalidDataException("qa04.full-step.partition-candidate-operation-id-duplicate");
            if (mutationResult.AppliedOperationIds[index] != operationId)
                throw new InvalidDataException("qa04.full-step.partition-candidate-receipt-order-drift");
        }

        var grouped = orderedBindings
            .GroupBy(static binding => binding.SourceDescriptor.FamilyToken.Value, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => (IReadOnlyList<Qa04CanonicalOperationBindingResultV1>)Array.AsReadOnly(group.ToArray()),
                StringComparer.Ordinal);
        if (grouped.Count != PartitionByFamily.Count || PartitionByFamily.Keys.Any(family => !grouped.ContainsKey(family)))
            throw new InvalidDataException("qa04.full-step.partition-candidate-family-coverage-drift");

        foreach (var pair in grouped)
        {
            if (pair.Value.Count == 0 ||
                pair.Value.Any(binding => binding.SourceDescriptor.FamilyToken.Value != pair.Key))
                throw new InvalidDataException($"qa04.full-step.partition-candidate-family-binding-drift:{pair.Key}");
        }

        return grouped;
    }

    private static Qa04CanonicalOperationPartitionMutationV1 BindStandard<TPayload>(
        WorldStateV1 basisState,
        ulong targetStep,
        IReadOnlyList<Qa04CanonicalOperationBindingResultV1> bindings,
        DomainPartitionStateV1<TPayload> resultingState,
        Func<TPayload, byte[]> canonicalPayloadDigest)
    {
        ArgumentNullException.ThrowIfNull(resultingState);
        ArgumentNullException.ThrowIfNull(canonicalPayloadDigest);

        var partitionId = resultingState.Identity.PartitionId.Value;
        RequireFamilyTarget(bindings, partitionId);
        var basisHeader = basisState.Partitions.Get(partitionId).Header;
        RequireBasisIdentity(basisHeader, resultingState.Identity);
        var resultingRevision = NextRevision(basisHeader);
        var resultingHeader = PartitionStateHeaderV1.CreateCanonical(
            resultingState,
            resultingRevision,
            targetStep,
            basisHeader.DetailLevel,
            canonicalPayloadDigest);
        return BindHeader(basisState, basisHeader, resultingHeader, targetStep, bindings);
    }

    private static Qa04CanonicalOperationPartitionMutationV1 BindMarket(
        WorldStateV1 basisState,
        ulong targetStep,
        IReadOnlyList<Qa04CanonicalOperationBindingResultV1> bindings,
        SocietyMarketTransactionPartitionStateV2 resultingState,
        IDomainRecordSchemaResolverV1 references)
    {
        ArgumentNullException.ThrowIfNull(resultingState);
        ArgumentNullException.ThrowIfNull(references);
        SocietyMarketTransactionPartitionIdentityV2.ValidateCanonicalContract();
        if (resultingState.State.Identity != SocietyMarketTransactionPartitionIdentityV2.Identity)
            throw new InvalidDataException("qa04.full-step.partition-candidate-market-identity-drift");

        var partitionId = SocietyMarketTransactionRecordSchemaV2.PartitionId;
        RequireFamilyTarget(bindings, partitionId);
        var basisHeader = basisState.Partitions.Get(partitionId).Header;
        RequireBasisIdentity(basisHeader, SocietyMarketTransactionPartitionIdentityV2.Identity);
        var resultingRevision = NextRevision(basisHeader);
        var resultingHeader = PartitionStateHeaderV1.CreateCanonical(
            resultingState.State,
            resultingRevision,
            targetStep,
            basisHeader.DetailLevel,
            payload => SocietyMarketTransactionPayloadCanonicalDigestV2.Compute(payload, references));
        return BindHeader(basisState, basisHeader, resultingHeader, targetStep, bindings);
    }

    private static Qa04CanonicalOperationPartitionMutationV1 BindHeader(
        WorldStateV1 basisState,
        PartitionStateHeaderV1 basisHeader,
        PartitionStateHeaderV1 resultingHeader,
        ulong targetStep,
        IReadOnlyList<Qa04CanonicalOperationBindingResultV1> bindings)
    {
        if (resultingHeader.Revision != checked(basisHeader.Revision + 1UL))
            throw new InvalidDataException("qa04.full-step.partition-candidate-revision-drift");
        if (resultingHeader.BasisStep != targetStep || resultingHeader.DetailLevel != basisHeader.DetailLevel)
            throw new InvalidDataException("qa04.full-step.partition-candidate-header-drift");

        var operationIds = bindings.Select(static binding => binding.SourceDescriptor.OperationId).ToArray();
        if (operationIds.Length == 0 || operationIds.Any(static id => id.IsZero) ||
            operationIds.Distinct().Count() != operationIds.Length)
            throw new InvalidDataException("qa04.full-step.partition-candidate-operation-id-drift");

        var changeSetDigest = ComputeQa04ChangeSetDigest(
            basisHeader,
            resultingHeader,
            basisState.Header.Step,
            targetStep,
            operationIds);
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
        ulong targetStep,
        IReadOnlyList<OpaqueId128> operationIds)
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
        if (operationIds.Count == 0 || operationIds.Any(static id => id.IsZero) ||
            operationIds.Distinct().Count() != operationIds.Count)
            throw new InvalidDataException("qa04.full-step.partition-candidate-operation-id-drift");

        return HashSuite.DomainHash("mv.state-diagnostic.v1", writer =>
        {
            writer.WriteMapStart(9);
            writer.WriteUnsigned(0); writer.WriteAsciiText(basisHeader.PartitionId.Value);
            writer.WriteUnsigned(1); writer.WriteAsciiText(basisHeader.OwnerDomain.Value);
            writer.WriteUnsigned(2); writer.WriteUnsigned(basisHeader.Revision);
            writer.WriteUnsigned(3); writer.WriteUnsigned(resultingHeader.Revision);
            writer.WriteUnsigned(4); writer.WriteUnsigned(basisStep);
            writer.WriteUnsigned(5); writer.WriteUnsigned(targetStep);
            writer.WriteUnsigned(6); writer.WriteBytes(basisHeader.CanonicalDigest);
            writer.WriteUnsigned(7); writer.WriteBytes(resultingHeader.CanonicalDigest);
            writer.WriteUnsigned(8);
            writer.WriteArrayStart(checked((ulong)operationIds.Count));
            foreach (var operationId in operationIds)
                writer.WriteBytes(operationId.ToBytes());
        });
    }

    private static void RequireFamilyTarget(
        IReadOnlyList<Qa04CanonicalOperationBindingResultV1> bindings,
        string partitionId)
    {
        if (bindings.Count == 0)
            throw new InvalidDataException($"qa04.full-step.partition-candidate-operation-empty:{partitionId}");
        var family = bindings[0].SourceDescriptor.FamilyToken.Value;
        if (!PartitionByFamily.TryGetValue(family, out var expectedPartition) ||
            !string.Equals(expectedPartition, partitionId, StringComparison.Ordinal) ||
            bindings.Any(binding => binding.SourceDescriptor.FamilyToken.Value != family))
        {
            throw new InvalidDataException($"qa04.full-step.partition-candidate-family-target-drift:{partitionId}");
        }
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
