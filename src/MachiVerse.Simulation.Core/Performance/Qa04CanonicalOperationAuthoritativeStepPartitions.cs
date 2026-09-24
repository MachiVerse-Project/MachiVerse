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

/// <summary>
/// Gate-2 authoritative-Step partition boundary. Canonical Operations whose EffectiveStep is S are
/// applied while State(S) is frozen, and their six typed mutation results become resulting partition
/// material for State(S+1). This is intentionally separate from the earlier receipt-only candidate
/// proof so the ordinary FrozenStepInputV1 / StepCandidateV1 semantics remain unchanged.
/// </summary>
public static class Qa04CanonicalOperationAuthoritativeStepPartitionBinderV1
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
        IDomainRecordSchemaResolverV1 references,
        Qa04ProductionStep2CanonicalDigestCacheV1? digestCache = null)
    {
        ArgumentNullException.ThrowIfNull(basisState);
        ArgumentNullException.ThrowIfNull(orderedBindings);
        ArgumentNullException.ThrowIfNull(mutationResult);
        ArgumentNullException.ThrowIfNull(mutationResult.State);
        ArgumentNullException.ThrowIfNull(references);

        if (basisState.Header.WorldId != Qa04ReferenceLoadV1.WorldId)
            throw new InvalidDataException("qa04.full-step.authoritative-partition-world-id-drift");
        if (basisState.Header.Step == 0)
            throw new InvalidDataException("qa04.full-step.authoritative-partition-effective-step-zero");
        if (basisState.Header.Step == ulong.MaxValue)
            throw new InvalidDataException("qa04.full-step.authoritative-partition-step-overflow");
        if (mutationResult.EffectiveStep != basisState.Header.Step)
            throw new InvalidDataException("qa04.full-step.authoritative-partition-effective-step-drift");

        var targetStep = checked(basisState.Header.Step + 1UL);
        var bindingsByFamily = ValidateAndGroupBindings(
            orderedBindings,
            mutationResult,
            basisState.Header.Step);
        var state = mutationResult.State;
        var changesByPartition = GroupChanges(mutationResult.Changes);

        var bound = new[]
        {
            BindStandard(
                basisState,
                targetStep,
                bindingsByFamily[InfrastructureFamily],
                state.InfrastructureServiceQueue,
                changesByPartition[InfrastructureServiceQueuePayloadV1.PartitionId],
                payload => digestCache?.Infrastructure(payload) ??
                    StandardDomainPayloadCanonicalDigestV1.Compute(
                        InfrastructureServiceQueuePayloadV1.PartitionId,
                        payload.ToStandardPayload(),
                        references: references),
                digestCache is null ? null : digestCache.InfrastructureRecord,
                digestCache?.InfrastructureChunks),
            BindStandard(
                basisState,
                targetStep,
                bindingsByFamily[ResidentFamily],
                state.ResidentBehaviorState,
                changesByPartition[ResidentBehaviorStatePayloadV1.PartitionId],
                payload => digestCache?.Resident(payload) ??
                    StandardDomainPayloadCanonicalDigestV1.Compute(
                        ResidentBehaviorStatePayloadV1.PartitionId,
                        payload.ToStandardPayload(),
                        references: references),
                digestCache is null ? null : digestCache.ResidentRecord,
                digestCache?.ResidentChunks),
            BindStandard(
                basisState,
                targetStep,
                bindingsByFamily[PhysicalFamily],
                state.PhysicalPresence,
                changesByPartition[PhysicalPresencePayloadV1.PartitionId],
                payload => digestCache?.Physical(payload) ??
                    StandardDomainPayloadCanonicalDigestV1.Compute(
                        PhysicalPresencePayloadV1.PartitionId,
                        payload.ToStandardPayload(),
                        references: references),
                digestCache is null ? null : digestCache.PhysicalRecord,
                digestCache?.PhysicalChunks),
            BindMarket(
                basisState,
                targetStep,
                bindingsByFamily[MarketFamily],
                state.MarketTransaction,
                changesByPartition[SocietyMarketTransactionRecordSchemaV2.PartitionId],
                references,
                digestCache),
            BindStandard(
                basisState,
                targetStep,
                bindingsByFamily[GovernanceFamily],
                state.GovernanceSecurityIncident,
                changesByPartition[GovernanceSecurityIncidentPayloadV1.PartitionId],
                payload => digestCache?.Governance(payload) ??
                    StandardDomainPayloadCanonicalDigestV1.Compute(
                        GovernanceSecurityIncidentPayloadV1.PartitionId,
                        payload.ToStandardPayload(),
                        references: references),
                digestCache is null ? null : digestCache.GovernanceRecord,
                digestCache?.GovernanceChunks),
            BindStandard(
                basisState,
                targetStep,
                bindingsByFamily[EnvironmentFamily],
                state.EnvironmentHazard,
                changesByPartition[EnvironmentHazardPayloadV1.PartitionId],
                payload => digestCache?.Environment(payload) ??
                    StandardDomainPayloadCanonicalDigestV1.Compute(
                        EnvironmentHazardPayloadV1.PartitionId,
                        payload.ToStandardPayload(),
                        references: references),
                digestCache is null ? null : digestCache.EnvironmentRecord,
                digestCache?.EnvironmentChunks),
        }
        .OrderBy(static item => item.Candidate.PartitionId.Value, StringComparer.Ordinal)
        .ToArray();

        if (bound.Length != 6 || bound.Select(static item => item.Candidate.PartitionId).Distinct().Count() != 6)
            throw new InvalidDataException("qa04.full-step.authoritative-partition-coverage-drift");
        if (bound.Any(item => item.Candidate.BasisStep != basisState.Header.Step ||
                              item.Candidate.TargetStep != targetStep ||
                              item.Material.ResultingHeader.BasisStep != targetStep))
            throw new InvalidDataException("qa04.full-step.authoritative-partition-step-drift");

        return new Qa04CanonicalOperationPartitionCandidateBatchV1(
            basisState.Header.Step,
            targetStep,
            Array.AsReadOnly(bound));
    }

    public static async Task<Qa04CanonicalOperationPartitionCandidateBatchV1> BindParallelAsync(
        WorldStateV1 basisState,
        IReadOnlyList<Qa04CanonicalOperationBindingResultV1> orderedBindings,
        Qa04CanonicalOperationMutationBatchResultV1 mutationResult,
        IDomainRecordSchemaResolverV1 references,
        int workerCount,
        Qa04ProductionStep2CanonicalDigestCacheV1? digestCache = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(basisState);
        ArgumentNullException.ThrowIfNull(orderedBindings);
        ArgumentNullException.ThrowIfNull(mutationResult);
        ArgumentNullException.ThrowIfNull(mutationResult.State);
        ArgumentNullException.ThrowIfNull(references);
        if (workerCount < 1) throw new ArgumentOutOfRangeException(nameof(workerCount));

        if (basisState.Header.WorldId != Qa04ReferenceLoadV1.WorldId)
            throw new InvalidDataException("qa04.full-step.authoritative-partition-world-id-drift");
        if (basisState.Header.Step == 0)
            throw new InvalidDataException("qa04.full-step.authoritative-partition-effective-step-zero");
        if (basisState.Header.Step == ulong.MaxValue)
            throw new InvalidDataException("qa04.full-step.authoritative-partition-step-overflow");
        if (mutationResult.EffectiveStep != basisState.Header.Step)
            throw new InvalidDataException("qa04.full-step.authoritative-partition-effective-step-drift");

        var targetStep = checked(basisState.Header.Step + 1UL);
        var bindingsByFamily = ValidateAndGroupBindings(
            orderedBindings,
            mutationResult,
            basisState.Header.Step);
        var state = mutationResult.State;
        var changesByPartition = GroupChanges(mutationResult.Changes);
        var families = new[]
        {
            InfrastructureFamily,
            ResidentFamily,
            PhysicalFamily,
            MarketFamily,
            GovernanceFamily,
            EnvironmentFamily,
        };

        var batch = await DeterministicBatchExecutor.RunCpuBoundAsync(
            families,
            workerCount,
            (family, token) =>
            {
                token.ThrowIfCancellationRequested();
                return family switch
                {
                    InfrastructureFamily => BindStandard(
                        basisState,
                        targetStep,
                        bindingsByFamily[InfrastructureFamily],
                        state.InfrastructureServiceQueue,
                        changesByPartition[InfrastructureServiceQueuePayloadV1.PartitionId],
                        payload => digestCache?.Infrastructure(payload) ??
                            StandardDomainPayloadCanonicalDigestV1.Compute(
                                InfrastructureServiceQueuePayloadV1.PartitionId,
                                payload.ToStandardPayload(),
                                references: references),
                        digestCache is null ? null : digestCache.InfrastructureRecord,
                        digestCache?.InfrastructureChunks),
                    ResidentFamily => BindStandard(
                        basisState,
                        targetStep,
                        bindingsByFamily[ResidentFamily],
                        state.ResidentBehaviorState,
                        changesByPartition[ResidentBehaviorStatePayloadV1.PartitionId],
                        payload => digestCache?.Resident(payload) ??
                            StandardDomainPayloadCanonicalDigestV1.Compute(
                                ResidentBehaviorStatePayloadV1.PartitionId,
                                payload.ToStandardPayload(),
                                references: references),
                        digestCache is null ? null : digestCache.ResidentRecord,
                        digestCache?.ResidentChunks),
                    PhysicalFamily => BindStandard(
                        basisState,
                        targetStep,
                        bindingsByFamily[PhysicalFamily],
                        state.PhysicalPresence,
                        changesByPartition[PhysicalPresencePayloadV1.PartitionId],
                        payload => digestCache?.Physical(payload) ??
                            StandardDomainPayloadCanonicalDigestV1.Compute(
                                PhysicalPresencePayloadV1.PartitionId,
                                payload.ToStandardPayload(),
                                references: references),
                        digestCache is null ? null : digestCache.PhysicalRecord,
                        digestCache?.PhysicalChunks),
                    MarketFamily => BindMarket(
                        basisState,
                        targetStep,
                        bindingsByFamily[MarketFamily],
                        state.MarketTransaction,
                        changesByPartition[SocietyMarketTransactionRecordSchemaV2.PartitionId],
                        references,
                        digestCache),
                    GovernanceFamily => BindStandard(
                        basisState,
                        targetStep,
                        bindingsByFamily[GovernanceFamily],
                        state.GovernanceSecurityIncident,
                        changesByPartition[GovernanceSecurityIncidentPayloadV1.PartitionId],
                        payload => digestCache?.Governance(payload) ??
                            StandardDomainPayloadCanonicalDigestV1.Compute(
                                GovernanceSecurityIncidentPayloadV1.PartitionId,
                                payload.ToStandardPayload(),
                                references: references),
                        digestCache is null ? null : digestCache.GovernanceRecord,
                        digestCache?.GovernanceChunks),
                    EnvironmentFamily => BindStandard(
                        basisState,
                        targetStep,
                        bindingsByFamily[EnvironmentFamily],
                        state.EnvironmentHazard,
                        changesByPartition[EnvironmentHazardPayloadV1.PartitionId],
                        payload => digestCache?.Environment(payload) ??
                            StandardDomainPayloadCanonicalDigestV1.Compute(
                                EnvironmentHazardPayloadV1.PartitionId,
                                payload.ToStandardPayload(),
                                references: references),
                        digestCache is null ? null : digestCache.EnvironmentRecord,
                        digestCache?.EnvironmentChunks),
                    _ => throw new InvalidDataException(
                        $"qa04.full-step.authoritative-partition-family-unregistered:{family}"),
                };
            },
            cancellationToken).ConfigureAwait(false);

        var bound = batch.Outputs
            .OrderBy(static item => item.Candidate.PartitionId.Value, StringComparer.Ordinal)
            .ToArray();
        if (bound.Length != 6 || bound.Select(static item => item.Candidate.PartitionId).Distinct().Count() != 6)
            throw new InvalidDataException("qa04.full-step.authoritative-partition-coverage-drift");
        if (bound.Any(item => item.Candidate.BasisStep != basisState.Header.Step ||
                              item.Candidate.TargetStep != targetStep ||
                              item.Material.ResultingHeader.BasisStep != targetStep))
            throw new InvalidDataException("qa04.full-step.authoritative-partition-step-drift");

        return new Qa04CanonicalOperationPartitionCandidateBatchV1(
            basisState.Header.Step,
            targetStep,
            Array.AsReadOnly(bound))
        {
            CpuParallelism = batch.Observation,
        };
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<Qa04CanonicalOperationMutationChangeV1>> GroupChanges(
        IReadOnlyList<Qa04CanonicalOperationMutationChangeV1> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        var grouped = new Dictionary<string, List<Qa04CanonicalOperationMutationChangeV1>>(
            PartitionByFamily.Count,
            StringComparer.Ordinal);
        foreach (var partitionId in PartitionByFamily.Values)
            grouped.Add(partitionId, new List<Qa04CanonicalOperationMutationChangeV1>());

        for (var index = 0; index < changes.Count; index++)
        {
            var change = changes[index];
            if (!grouped.TryGetValue(change.PartitionId.Value, out var partitionChanges))
                throw new InvalidDataException("qa04.full-step.authoritative-partition-change-unregistered");
            partitionChanges.Add(change);
        }

        var result = new Dictionary<string, IReadOnlyList<Qa04CanonicalOperationMutationChangeV1>>(
            PartitionByFamily.Count,
            StringComparer.Ordinal);
        foreach (var partitionId in PartitionByFamily.Values)
        {
            var partitionChanges = grouped[partitionId];
            if (partitionChanges.Count == 0)
                throw new InvalidDataException($"qa04.full-step.authoritative-partition-change-coverage:{partitionId}");
            result.Add(partitionId, Array.AsReadOnly(partitionChanges.ToArray()));
        }

        return result;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<Qa04CanonicalOperationBindingResultV1>> ValidateAndGroupBindings(
        IReadOnlyList<Qa04CanonicalOperationBindingResultV1> orderedBindings,
        Qa04CanonicalOperationMutationBatchResultV1 mutationResult,
        ulong effectiveStep)
    {
        if (orderedBindings.Count == 0 || orderedBindings.Count != mutationResult.AppliedOperationIds.Count)
            throw new InvalidDataException("qa04.full-step.authoritative-partition-operation-count-drift");

        var familyCapacity = checked((orderedBindings.Count + PartitionByFamily.Count - 1) / PartitionByFamily.Count);
        var grouped = new Dictionary<string, List<Qa04CanonicalOperationBindingResultV1>>(
            PartitionByFamily.Count,
            StringComparer.Ordinal);
        foreach (var family in PartitionByFamily.Keys)
            grouped.Add(family, new List<Qa04CanonicalOperationBindingResultV1>(familyCapacity));

        SameStepOrderKey? previousOrderKey = null;
        var seenOperationIds = new HashSet<OpaqueId128>();
        for (var index = 0; index < orderedBindings.Count; index++)
        {
            var binding = orderedBindings[index]
                ?? throw new InvalidDataException("qa04.full-step.authoritative-partition-binding-null");
            if (binding.SourceDescriptor is null || binding.OrderKey is null || binding.ScheduledOperation is null)
                throw new InvalidDataException("qa04.full-step.authoritative-partition-binding-null");

            var family = binding.SourceDescriptor.FamilyToken.Value;
            if (!grouped.TryGetValue(family, out var familyBindings))
                throw new InvalidDataException($"qa04.full-step.authoritative-partition-family-unregistered:{family}");
            if (binding.ScheduledOperation.EffectiveStep != effectiveStep)
                throw new InvalidDataException("qa04.full-step.authoritative-partition-binding-step-drift");
            if (!binding.OrderKey.CanonicallyEquals(binding.ScheduledOperation.OrderKey))
                throw new InvalidDataException("qa04.full-step.authoritative-partition-order-key-drift");
            if (previousOrderKey is not null && previousOrderKey.CompareTo(binding.OrderKey) >= 0)
                throw new InvalidDataException("qa04.full-step.authoritative-partition-order-not-canonical");
            previousOrderKey = binding.OrderKey;

            var operationId = binding.SourceDescriptor.OperationId;
            if (operationId.IsZero || !seenOperationIds.Add(operationId))
                throw new InvalidDataException("qa04.full-step.authoritative-partition-operation-id-duplicate");
            if (mutationResult.AppliedOperationIds[index] != operationId)
                throw new InvalidDataException("qa04.full-step.authoritative-partition-receipt-order-drift");

            familyBindings.Add(binding);
        }

        var result = new Dictionary<string, IReadOnlyList<Qa04CanonicalOperationBindingResultV1>>(
            PartitionByFamily.Count,
            StringComparer.Ordinal);
        foreach (var family in PartitionByFamily.Keys)
        {
            var familyBindings = grouped[family];
            if (familyBindings.Count == 0)
                throw new InvalidDataException("qa04.full-step.authoritative-partition-family-coverage-drift");
            result.Add(family, Array.AsReadOnly(familyBindings.ToArray()));
        }
        return result;
    }

    private static Qa04CanonicalOperationPartitionMutationV1 BindStandard<TPayload>(
        WorldStateV1 basisState,
        ulong targetStep,
        IReadOnlyList<Qa04CanonicalOperationBindingResultV1> bindings,
        DomainPartitionStateV1<TPayload> resultingState,
        IReadOnlyList<Qa04CanonicalOperationMutationChangeV1> changes,
        Func<TPayload, byte[]> canonicalPayloadDigest,
        Func<DomainRecordEnvelopeV1<TPayload>, byte[]>? canonicalRecordEncoding = null,
        Qa04RecordIdPrefixPartitionDigestCacheV2<TPayload>? chunkCache = null)
    {
        var partitionId = resultingState.Identity.PartitionId.Value;
        RequireFamilyTarget(bindings, partitionId);
        var basisHeader = basisState.Partitions.Get(partitionId).Header;
        RequireBasisIdentity(basisHeader, resultingState.Identity);
        var resultingHeader = chunkCache is not null
            ? chunkCache.CreateHeader(
                resultingState,
                NextRevision(basisHeader),
                targetStep,
                basisHeader.DetailLevel,
                changes)
            : RecordIdPrefixPartitionDigestV2.CreateHeader(
                resultingState,
                NextRevision(basisHeader),
                targetStep,
                basisHeader.DetailLevel,
                canonicalRecordEncoding ??
                    (record => PartitionStateHeaderV1.EncodeCanonicalRecord(
                        record,
                        canonicalPayloadDigest(record.Payload))));
        return BindHeader(basisState, basisHeader, resultingHeader, targetStep, bindings);
    }

    private static Qa04CanonicalOperationPartitionMutationV1 BindMarket(
        WorldStateV1 basisState,
        ulong targetStep,
        IReadOnlyList<Qa04CanonicalOperationBindingResultV1> bindings,
        SocietyMarketTransactionPartitionStateV2 resultingState,
        IReadOnlyList<Qa04CanonicalOperationMutationChangeV1> changes,
        IDomainRecordSchemaResolverV1 references,
        Qa04ProductionStep2CanonicalDigestCacheV1? digestCache)
    {
        SocietyMarketTransactionPartitionIdentityV2.ValidateCanonicalContract();
        if (resultingState.State.Identity != SocietyMarketTransactionPartitionIdentityV2.Identity)
            throw new InvalidDataException("qa04.full-step.authoritative-partition-market-identity-drift");

        var partitionId = SocietyMarketTransactionRecordSchemaV2.PartitionId;
        RequireFamilyTarget(bindings, partitionId);
        var basisHeader = basisState.Partitions.Get(partitionId).Header;
        RequireBasisIdentity(basisHeader, SocietyMarketTransactionPartitionIdentityV2.Identity);
        var resultingHeader = digestCache is null
            ? RecordIdPrefixPartitionDigestV2.CreateHeader(
                resultingState.State,
                NextRevision(basisHeader),
                targetStep,
                basisHeader.DetailLevel,
                record => PartitionStateHeaderV1.EncodeCanonicalRecord(
                    record,
                    SocietyMarketTransactionPayloadCanonicalDigestV2.Compute(
                        record.Payload,
                        references)))
            : digestCache.MarketChunks.CreateHeader(
                resultingState.State,
                NextRevision(basisHeader),
                targetStep,
                basisHeader.DetailLevel,
                changes);
        return BindHeader(basisState, basisHeader, resultingHeader, targetStep, bindings);
    }

    private static Qa04CanonicalOperationPartitionMutationV1 BindHeader(
        WorldStateV1 basisState,
        PartitionStateHeaderV1 basisHeader,
        PartitionStateHeaderV1 resultingHeader,
        ulong targetStep,
        IReadOnlyList<Qa04CanonicalOperationBindingResultV1> bindings)
    {
        if (resultingHeader.Revision != checked(basisHeader.Revision + 1UL) ||
            resultingHeader.BasisStep != targetStep ||
            resultingHeader.DetailLevel != basisHeader.DetailLevel)
            throw new InvalidDataException("qa04.full-step.authoritative-partition-result-header-drift");

        if (bindings.Count == 0)
            throw new InvalidDataException("qa04.full-step.authoritative-partition-operation-id-drift");

        var receiptDigest = ComputeReceiptDigest(
            basisHeader,
            resultingHeader,
            basisState.Header.Step,
            targetStep,
            bindings);
        var candidate = CreateOwnerCandidate(
            basisState,
            basisHeader.PartitionId.Value,
            receiptDigest);
        var material = new StepPartitionStateMaterialV1(resultingHeader);

        if (candidate.CandidateRevision != resultingHeader.Revision ||
            candidate.BasisRevision != basisHeader.Revision ||
            candidate.BasisStep != basisState.Header.Step ||
            candidate.TargetStep != targetStep ||
            candidate.PartitionId != resultingHeader.PartitionId ||
            candidate.OwnerDomain != resultingHeader.OwnerDomain)
            throw new InvalidDataException("qa04.full-step.authoritative-partition-binding-drift");

        return new Qa04CanonicalOperationPartitionMutationV1(candidate, material);
    }

    private static PartitionCandidateV1 CreateOwnerCandidate(
        WorldStateV1 basisState,
        string partitionId,
        ReadOnlySpan<byte> digest)
        => partitionId switch
        {
            InfrastructureServiceQueuePayloadV1.PartitionId => InfrastructureInformationPartitionCandidateFactoryV1.Create(
                basisState, partitionId, digest),
            ResidentBehaviorStatePayloadV1.PartitionId => ResidentParticipationPartitionCandidateFactoryV1.CreateResident(
                basisState, partitionId, digest),
            PhysicalPresencePayloadV1.PartitionId => PhysicalBuiltPartitionCandidateFactoryV1.Create(
                basisState, partitionId, digest),
            SocietyMarketTransactionRecordSchemaV2.PartitionId => SocietyEconomyPartitionCandidateFactoryV1.Create(
                basisState, partitionId, digest),
            GovernanceSecurityIncidentPayloadV1.PartitionId => GovernanceSecurityPartitionCandidateFactoryV1.Create(
                basisState, partitionId, digest),
            EnvironmentHazardPayloadV1.PartitionId => DomainOwnedPartitionCandidateFactoryV1.CreateEnvironment(
                basisState, partitionId, digest),
            _ => throw new InvalidDataException($"qa04.full-step.authoritative-partition-unregistered:{partitionId}"),
        };

    private static byte[] ComputeReceiptDigest(
        PartitionStateHeaderV1 basisHeader,
        PartitionStateHeaderV1 resultingHeader,
        ulong basisStep,
        ulong targetStep,
        IReadOnlyList<Qa04CanonicalOperationBindingResultV1> bindings)
    {
        if (basisHeader.DigestAlgorithm == PartitionCanonicalDigestAlgorithmV1.LegacyFlatV1 &&
            resultingHeader.DigestAlgorithm == PartitionCanonicalDigestAlgorithmV1.LegacyFlatV1)
        {
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
                WriteOperationIds(writer, bindings);
            });
        }

        return HashSuite.DomainHash("mv.qa04.partition-receipt.v2", writer =>
        {
            writer.WriteMapStart(11);
            writer.WriteUnsigned(0); writer.WriteAsciiText(basisHeader.PartitionId.Value);
            writer.WriteUnsigned(1); writer.WriteAsciiText(basisHeader.OwnerDomain.Value);
            writer.WriteUnsigned(2); writer.WriteUnsigned(basisHeader.Revision);
            writer.WriteUnsigned(3); writer.WriteUnsigned(resultingHeader.Revision);
            writer.WriteUnsigned(4); writer.WriteUnsigned(basisStep);
            writer.WriteUnsigned(5); writer.WriteUnsigned(targetStep);
            writer.WriteUnsigned(6); writer.WriteUnsigned((byte)basisHeader.DigestAlgorithm);
            writer.WriteUnsigned(7); writer.WriteBytes(basisHeader.CanonicalDigest);
            writer.WriteUnsigned(8); writer.WriteUnsigned((byte)resultingHeader.DigestAlgorithm);
            writer.WriteUnsigned(9); writer.WriteBytes(resultingHeader.CanonicalDigest);
            writer.WriteUnsigned(10);
            WriteOperationIds(writer, bindings);
        });
    }

    private static void WriteOperationIds(
        MvDcborWriter writer,
        IReadOnlyList<Qa04CanonicalOperationBindingResultV1> bindings)
    {
        writer.WriteArrayStart(checked((ulong)bindings.Count));
        Span<byte> operationIdBytes = stackalloc byte[16];
        for (var index = 0; index < bindings.Count; index++)
        {
            bindings[index].SourceDescriptor.OperationId.WriteBytes(operationIdBytes);
            writer.WriteBytes(operationIdBytes);
        }
    }

    private static void RequireFamilyTarget(
        IReadOnlyList<Qa04CanonicalOperationBindingResultV1> bindings,
        string partitionId)
    {
        if (bindings.Count == 0)
            throw new InvalidDataException($"qa04.full-step.authoritative-partition-operation-empty:{partitionId}");
        var family = bindings[0].SourceDescriptor.FamilyToken.Value;
        if (!PartitionByFamily.TryGetValue(family, out var expectedPartition) ||
            !string.Equals(expectedPartition, partitionId, StringComparison.Ordinal))
            throw new InvalidDataException($"qa04.full-step.authoritative-partition-family-target-drift:{partitionId}");
    }

    private static void RequireBasisIdentity(
        PartitionStateHeaderV1 basisHeader,
        DomainPartitionIdentityV1 resultingIdentity)
    {
        if (basisHeader.PartitionId != resultingIdentity.PartitionId ||
            basisHeader.OwnerDomain != resultingIdentity.OwnerDomain ||
            basisHeader.Schema != resultingIdentity.PartitionSchema)
            throw new InvalidDataException($"qa04.full-step.authoritative-partition-basis-identity:{resultingIdentity.PartitionId.Value}");
    }

    private static ulong NextRevision(PartitionStateHeaderV1 basisHeader)
    {
        if (basisHeader.Revision == ulong.MaxValue)
            throw new InvalidDataException("qa04.full-step.authoritative-partition-revision-overflow");
        return basisHeader.Revision + 1UL;
    }
}
