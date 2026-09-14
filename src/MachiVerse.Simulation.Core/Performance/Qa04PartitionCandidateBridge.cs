using System.Security.Cryptography;
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

public sealed record Qa04PartitionCandidateTransitionV1(
    string FamilyToken,
    PartitionCandidateV1 Candidate,
    PartitionStateHeaderV1 CandidateHeader,
    IReadOnlyList<OpaqueId128> OperationIds);

public sealed record Qa04PartitionCandidateBridgeResultV1(
    ulong BasisStep,
    ulong TargetStep,
    IReadOnlyList<Qa04PartitionCandidateTransitionV1> Transitions)
{
    public IReadOnlyList<PartitionCandidateV1> Candidates { get; } =
        Array.AsReadOnly(Transitions.Select(static transition => transition.Candidate).ToArray());
}

/// <summary>
/// Gate-2 bridge from the six already-authoritative QA-04 typed mutation targets into the existing
/// owner-validated PartitionCandidateV1 boundary. The benchmark-only change-set commitment is
/// specified by phase4-alpha11-gate2-partition-change-set-authority.md.
/// </summary>
public static class Qa04PartitionCandidateBridgeV1
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

    public static Qa04PartitionCandidateBridgeResultV1 Create(
        WorldStateV1 basisState,
        IReadOnlyList<Qa04CanonicalOperationBindingResultV1> orderedBindings,
        Qa04CanonicalOperationMutationBatchResultV1 mutationResult)
    {
        ArgumentNullException.ThrowIfNull(basisState);
        ArgumentNullException.ThrowIfNull(orderedBindings);
        ArgumentNullException.ThrowIfNull(mutationResult);

        if (basisState.Header.WorldId != Qa04ReferenceLoadV1.WorldId)
            throw new InvalidDataException("qa04.full-step.partition-candidate-world-id");
        if (mutationResult.EffectiveStep == 0 ||
            basisState.Header.Step != mutationResult.EffectiveStep - 1UL)
            throw new InvalidDataException("qa04.full-step.partition-candidate-basis-step");
        if (orderedBindings.Count != mutationResult.AppliedOperationIds.Count)
            throw new InvalidDataException("qa04.full-step.partition-candidate-operation-count");

        ValidateBindingOrderAndReceipt(orderedBindings, mutationResult);

        var grouped = orderedBindings
            .GroupBy(static binding => binding.SourceDescriptor.FamilyToken.Value, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => (IReadOnlyList<Qa04CanonicalOperationBindingResultV1>)Array.AsReadOnly(group.ToArray()),
                StringComparer.Ordinal);

        if (grouped.Count != PartitionByFamily.Count || PartitionByFamily.Keys.Any(family => !grouped.ContainsKey(family)))
            throw new InvalidDataException("qa04.full-step.partition-candidate-family-coverage");

        var transitions = new List<Qa04PartitionCandidateTransitionV1>(PartitionByFamily.Count)
        {
            CreateGeneric(
                basisState,
                mutationResult,
                grouped[InfrastructureFamily],
                InfrastructureFamily,
                InfrastructureServiceQueuePayloadV1.PartitionId,
                mutationResult.State.InfrastructureServiceQueue,
                static payload => payload.CanonicalDigest(),
                static (state, partitionId, digest) =>
                    InfrastructureInformationPartitionCandidateFactoryV1.Create(state, partitionId, digest)),

            CreateGeneric(
                basisState,
                mutationResult,
                grouped[ResidentFamily],
                ResidentFamily,
                ResidentBehaviorStatePayloadV1.PartitionId,
                mutationResult.State.ResidentBehaviorState,
                static payload => payload.CanonicalDigest(),
                static (state, partitionId, digest) =>
                    ResidentParticipationPartitionCandidateFactoryV1.CreateResident(state, partitionId, digest)),

            CreateGeneric(
                basisState,
                mutationResult,
                grouped[PhysicalFamily],
                PhysicalFamily,
                PhysicalPresencePayloadV1.PartitionId,
                mutationResult.State.PhysicalPresence,
                static payload => payload.CanonicalDigest(),
                static (state, partitionId, digest) =>
                    PhysicalBuiltPartitionCandidateFactoryV1.Create(state, partitionId, digest)),

            CreateMarket(
                basisState,
                mutationResult,
                grouped[MarketFamily]),

            CreateGeneric(
                basisState,
                mutationResult,
                grouped[GovernanceFamily],
                GovernanceFamily,
                GovernanceSecurityIncidentPayloadV1.PartitionId,
                mutationResult.State.GovernanceSecurityIncident,
                static payload => payload.CanonicalDigest(),
                static (state, partitionId, digest) =>
                    GovernanceSecurityPartitionCandidateFactoryV1.Create(state, partitionId, digest)),

            CreateGeneric(
                basisState,
                mutationResult,
                grouped[EnvironmentFamily],
                EnvironmentFamily,
                EnvironmentHazardPayloadV1.PartitionId,
                mutationResult.State.EnvironmentHazard,
                static payload => payload.CanonicalDigest(),
                static (state, partitionId, digest) =>
                    DomainOwnedPartitionCandidateFactoryV1.CreateEnvironment(state, partitionId, digest)),
        };

        var canonical = transitions
            .OrderBy(static transition => transition.Candidate.PartitionId.Value, StringComparer.Ordinal)
            .ToArray();
        if (canonical.Select(static transition => transition.Candidate.PartitionId).Distinct().Count() != canonical.Length)
            throw new InvalidDataException("qa04.full-step.partition-candidate-duplicate-partition");

        return new Qa04PartitionCandidateBridgeResultV1(
            basisState.Header.Step,
            mutationResult.EffectiveStep,
            Array.AsReadOnly(canonical));
    }

    private static Qa04PartitionCandidateTransitionV1 CreateGeneric<TPayload>(
        WorldStateV1 basisState,
        Qa04CanonicalOperationMutationBatchResultV1 mutationResult,
        IReadOnlyList<Qa04CanonicalOperationBindingResultV1> bindings,
        string family,
        string partitionId,
        DomainPartitionStateV1<TPayload> postState,
        Func<TPayload, byte[]> payloadDigest,
        Func<WorldStateV1, string, byte[], PartitionCandidateV1> candidateFactory)
    {
        var basis = RequireBasisHeader(basisState, family, partitionId);
        var candidateHeader = PartitionStateHeaderV1.CreateCanonical(
            postState,
            checked(basis.Revision + 1UL),
            mutationResult.EffectiveStep,
            basis.DetailLevel,
            payloadDigest);
        return CreateTransition(
            basisState,
            mutationResult,
            bindings,
            family,
            partitionId,
            basis,
            candidateHeader,
            candidateFactory);
    }

    private static Qa04PartitionCandidateTransitionV1 CreateMarket(
        WorldStateV1 basisState,
        Qa04CanonicalOperationMutationBatchResultV1 mutationResult,
        IReadOnlyList<Qa04CanonicalOperationBindingResultV1> bindings)
    {
        var partitionId = SocietyMarketTransactionRecordSchemaV2.PartitionId;
        var basis = RequireBasisHeader(basisState, MarketFamily, partitionId);
        var authority = SocietyMarketTransactionSnapshotAuthorityV2.CreateCanonical(
            mutationResult.State.MarketTransaction,
            checked(basis.Revision + 1UL),
            mutationResult.EffectiveStep,
            basis.DetailLevel);
        return CreateTransition(
            basisState,
            mutationResult,
            bindings,
            MarketFamily,
            partitionId,
            basis,
            authority.Header,
            static (state, id, digest) => SocietyEconomyPartitionCandidateFactoryV1.Create(state, id, digest));
    }

    private static Qa04PartitionCandidateTransitionV1 CreateTransition(
        WorldStateV1 basisState,
        Qa04CanonicalOperationMutationBatchResultV1 mutationResult,
        IReadOnlyList<Qa04CanonicalOperationBindingResultV1> bindings,
        string family,
        string partitionId,
        PartitionStateHeaderV1 basisHeader,
        PartitionStateHeaderV1 candidateHeader,
        Func<WorldStateV1, string, byte[], PartitionCandidateV1> candidateFactory)
    {
        if (bindings.Count == 0)
            throw new InvalidDataException($"qa04.full-step.partition-candidate-operation-empty:{family}");
        if (!PartitionByFamily.TryGetValue(family, out var expectedPartition) ||
            !string.Equals(expectedPartition, partitionId, StringComparison.Ordinal))
            throw new InvalidDataException($"qa04.full-step.partition-candidate-family-target:{family}");
        if (bindings.Any(binding => binding.SourceDescriptor.FamilyToken.Value != family ||
                                    binding.ScheduledOperation.EffectiveStep != mutationResult.EffectiveStep))
            throw new InvalidDataException($"qa04.full-step.partition-candidate-binding-drift:{family}");

        ValidateCandidateHeader(basisHeader, candidateHeader, mutationResult.EffectiveStep);

        var operationIds = bindings.Select(static binding => binding.SourceDescriptor.OperationId).ToArray();
        if (operationIds.Any(static id => id.IsZero) || operationIds.Distinct().Count() != operationIds.Length)
            throw new InvalidDataException($"qa04.full-step.partition-candidate-operation-id:{family}");

        var changeSetDigest = ComputeChangeSetDigest(basisHeader, candidateHeader, operationIds);
        var candidate = candidateFactory(basisState, partitionId, changeSetDigest);
        if (candidate.PartitionId != candidateHeader.PartitionId ||
            candidate.OwnerDomain != candidateHeader.OwnerDomain ||
            candidate.BasisRevision != basisHeader.Revision ||
            candidate.CandidateRevision != candidateHeader.Revision ||
            candidate.BasisStep != basisState.Header.Step ||
            candidate.TargetStep != mutationResult.EffectiveStep ||
            !CryptographicOperations.FixedTimeEquals(candidate.ChangeSetDigest, changeSetDigest))
        {
            throw new InvalidDataException($"qa04.full-step.partition-candidate-envelope:{family}");
        }

        return new Qa04PartitionCandidateTransitionV1(
            family,
            candidate,
            candidateHeader,
            Array.AsReadOnly(operationIds));
    }

    private static PartitionStateHeaderV1 RequireBasisHeader(
        WorldStateV1 basisState,
        string family,
        string partitionId)
    {
        var expected = StandardDomainPartitionRegistry.Get(partitionId);
        var header = basisState.Partitions.Get(partitionId).Header;
        if (header.PartitionId != expected.PartitionId ||
            header.OwnerDomain != expected.OwnerDomain ||
            header.Schema != expected.PartitionSchema ||
            header.Revision == ulong.MaxValue ||
            header.BasisStep > basisState.Header.Step)
        {
            throw new InvalidDataException($"qa04.full-step.partition-candidate-basis-header:{family}");
        }
        return header;
    }

    private static void ValidateCandidateHeader(
        PartitionStateHeaderV1 basis,
        PartitionStateHeaderV1 candidate,
        ulong effectiveStep)
    {
        if (candidate.PartitionId != basis.PartitionId ||
            candidate.OwnerDomain != basis.OwnerDomain ||
            candidate.Schema != basis.Schema ||
            candidate.Revision != checked(basis.Revision + 1UL) ||
            candidate.BasisStep != effectiveStep ||
            candidate.DetailLevel != basis.DetailLevel ||
            candidate.CanonicalDigest.Length != 32)
        {
            throw new InvalidDataException($"qa04.full-step.partition-candidate-header:{basis.PartitionId.Value}");
        }
    }

    private static byte[] ComputeChangeSetDigest(
        PartitionStateHeaderV1 basis,
        PartitionStateHeaderV1 candidate,
        IReadOnlyList<OpaqueId128> operationIds)
        => HashSuite.DomainHash("mv.state-diagnostic.v1", writer =>
        {
            writer.WriteMapStart(9);
            writer.WriteUnsigned(0); writer.WriteAsciiText(basis.PartitionId.Value);
            writer.WriteUnsigned(1); writer.WriteAsciiText(basis.OwnerDomain.Value);
            writer.WriteUnsigned(2); writer.WriteUnsigned(basis.Revision);
            writer.WriteUnsigned(3); writer.WriteUnsigned(candidate.Revision);
            writer.WriteUnsigned(4); writer.WriteUnsigned(checked(candidate.BasisStep - 1UL));
            writer.WriteUnsigned(5); writer.WriteUnsigned(candidate.BasisStep);
            writer.WriteUnsigned(6); writer.WriteBytes(basis.CanonicalDigest);
            writer.WriteUnsigned(7); writer.WriteBytes(candidate.CanonicalDigest);
            writer.WriteUnsigned(8);
            writer.WriteArrayStart(checked((ulong)operationIds.Count));
            foreach (var operationId in operationIds)
                writer.WriteBytes(operationId.ToBytes());
        });

    private static void ValidateBindingOrderAndReceipt(
        IReadOnlyList<Qa04CanonicalOperationBindingResultV1> orderedBindings,
        Qa04CanonicalOperationMutationBatchResultV1 mutationResult)
    {
        SameStepOrderKey? previous = null;
        for (var index = 0; index < orderedBindings.Count; index++)
        {
            var binding = orderedBindings[index]
                ?? throw new InvalidDataException("qa04.full-step.partition-candidate-binding-null");
            if (binding.SourceDescriptor is null || binding.OrderKey is null || binding.ScheduledOperation is null)
                throw new InvalidDataException("qa04.full-step.partition-candidate-binding-null");
            if (!PartitionByFamily.ContainsKey(binding.SourceDescriptor.FamilyToken.Value))
                throw new InvalidDataException(
                    $"qa04.full-step.partition-candidate-family-unregistered:{binding.SourceDescriptor.FamilyToken.Value}");
            if (binding.ScheduledOperation.EffectiveStep != mutationResult.EffectiveStep)
                throw new InvalidDataException("qa04.full-step.partition-candidate-effective-step");
            if (!binding.OrderKey.ToDatabaseBytes().AsSpan().SequenceEqual(
                    binding.ScheduledOperation.OrderKey.ToDatabaseBytes()))
                throw new InvalidDataException("qa04.full-step.partition-candidate-order-key");
            if (previous is not null && previous.CompareTo(binding.OrderKey) >= 0)
                throw new InvalidDataException("qa04.full-step.partition-candidate-order");
            previous = binding.OrderKey;

            if (mutationResult.AppliedOperationIds[index] != binding.SourceDescriptor.OperationId)
                throw new InvalidDataException("qa04.full-step.partition-candidate-receipt-order");
        }
    }
}
