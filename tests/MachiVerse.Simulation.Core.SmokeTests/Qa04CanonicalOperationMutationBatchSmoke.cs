using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Environment;
using MachiVerse.Simulation.Core.Domains.GovernanceSecurity;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
using MachiVerse.Simulation.Core.Domains.Participation;
using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04CanonicalOperationMutationBatchSmoke
{
    public static void Run()
    {
        var bindings = Qa04ReferenceLoadV1.OperationsForStep(1)
            .GroupBy(static descriptor => descriptor.FamilyToken.Value, StringComparer.Ordinal)
            .Select(static group => group.First())
            .Select(static descriptor => Qa04CanonicalOperationBindingV1.Bind(descriptor, schedulingPolicyGeneration: 1))
            .ToArray();
        Array.Sort(bindings, static (left, right) => left.OrderKey.CompareTo(right.OrderKey));

        Require(bindings.Length == 6, "Gate2 mutation batch smoke requires one binding from every canonical family");
        var effectiveStep = bindings[0].ScheduledOperation.EffectiveStep;
        Require(bindings.All(binding => binding.ScheduledOperation.EffectiveStep == effectiveStep),
            "Gate2 smoke bindings must share one effective step");

        var residentBinding = bindings.Single(static value =>
            value.SourceDescriptor.FamilyToken.Value == "participation-control-resident-action");
        var physicalBinding = bindings.Single(static value =>
            value.SourceDescriptor.FamilyToken.Value == "physical-item-movement-work");
        var marketBinding = bindings.Single(static value =>
            value.SourceDescriptor.FamilyToken.Value == "society-market-payment-contract");

        var residentOrdinal = residentBinding.SourceDescriptor.FamilyOrdinal;
        var residentRecord = Qa04ReferenceLoadV1.Record(
            new StableToken("resident.persistent-identity"),
            residentOrdinal);
        var residentRef = new PartitionRecordRefV1(
            ResidentIdentityLifecyclePayloadV1.PartitionId,
            residentRecord.RecordId);
        var controlModeIdentity = StandardDomainPartitionRegistry.Get(ParticipationControlModePayloadV1.PartitionId);
        var controlMode = new DomainRecordEnvelopeV1<ParticipationControlModePayloadV1>(
            Qa04ParticipationControlModeCanonicalAuthorityV1.RecordId(residentOrdinal),
            controlModeIdentity.RecordSchema,
            revision: 1,
            createdStep: 0,
            retiredStep: null,
            Qa04ReferenceLoadV1.ResidentDetailLevel(residentOrdinal),
            lineageRef: null,
            new ParticipationControlModePayloadV1(
                residentRef,
                BindingRef: null,
                Qa04ParticipationControlModeCanonicalAuthorityV1.Autonomous,
                Qa04ParticipationControlModeCanonicalAuthorityV1.InitialEffectiveFrom,
                Qa04ParticipationControlModeCanonicalAuthorityV1.InitialInputAuthorityGeneration));
        var controlModes = new DomainPartitionStateV1<ParticipationControlModePayloadV1>(
            controlModeIdentity,
            new[] { controlMode });

        var behavior = new DomainPartitionStateV1<ResidentBehaviorStatePayloadV1>(
            StandardDomainPartitionRegistry.Get(ResidentBehaviorStatePayloadV1.PartitionId),
            Array.Empty<DomainRecordEnvelopeV1<ResidentBehaviorStatePayloadV1>>());
        var serviceQueue = new DomainPartitionStateV1<InfrastructureServiceQueuePayloadV1>(
            StandardDomainPartitionRegistry.Get(InfrastructureServiceQueuePayloadV1.PartitionId),
            Array.Empty<DomainRecordEnvelopeV1<InfrastructureServiceQueuePayloadV1>>());

        var physical = PhysicalMaterial(physicalBinding.SourceDescriptor.FamilyOrdinal);
        var presence = new DomainPartitionStateV1<PhysicalPresencePayloadV1>(
            StandardDomainPartitionRegistry.Get(PhysicalPresencePayloadV1.PartitionId),
            new[] { physical.Presence });

        var marketScope = checked((uint)(marketBinding.SourceDescriptor.FamilyOrdinal %
            (ulong)Qa04ReferenceScenariosV1.MarketScopeCount));
        var market = Qa04MarketMaterializerV1.CreateMarketState(
            marketScope,
            Qa04SpatialTileScopeAuthorityV1.ScopeRef,
            out _);
        var marketState = new SocietyMarketTransactionPartitionStateV2(new[] { market });

        var incidents = new DomainPartitionStateV1<GovernanceSecurityIncidentPayloadV1>(
            StandardDomainPartitionRegistry.Get(GovernanceSecurityIncidentPayloadV1.PartitionId),
            Array.Empty<DomainRecordEnvelopeV1<GovernanceSecurityIncidentPayloadV1>>());
        var hazards = new DomainPartitionStateV1<EnvironmentHazardPayloadV1>(
            StandardDomainPartitionRegistry.Get(EnvironmentHazardPayloadV1.PartitionId),
            Array.Empty<DomainRecordEnvelopeV1<EnvironmentHazardPayloadV1>>());

        var initial = new Qa04CanonicalOperationMutationStateV1(
            serviceQueue,
            controlModes,
            behavior,
            presence,
            marketState,
            incidents,
            hazards);
        var references = new RegistryResolver();

        var result = Qa04CanonicalOperationMutationBatchV1.Apply(
            Qa04ReferenceLoadV1.WorldId,
            effectiveStep,
            bindings,
            initial,
            references);

        Require(result.EffectiveStep == effectiveStep, "Gate2 mutation effective step drift");
        Require(result.AppliedOperationIds.Count == 6 && result.AppliedOperationIds.Distinct().Count() == 6,
            "Gate2 mutation batch must apply six unique operations");
        Require(result.AppliedCountByFamily.Count == 6 && result.AppliedCountByFamily.Values.All(static count => count == 1),
            "Gate2 mutation batch must dispatch exactly one operation to every family");
        Require(result.State.InfrastructureServiceQueue.ItemCount == 1,
            "Gate2 infrastructure mutation was not applied");
        Require(result.State.ResidentBehaviorState.ItemCount == 1,
            "Gate2 resident mutation was not applied");
        Require(result.State.PhysicalPresence.TryGet(physical.Presence.RecordId, out var revisedPresence) &&
                revisedPresence is not null && revisedPresence.Revision == 2,
            "Gate2 physical mutation was not applied");
        Require(result.State.MarketTransaction.State.ItemCount == marketState.State.ItemCount + 1UL,
            "Gate2 market mutation was not applied");
        Require(result.State.GovernanceSecurityIncident.ItemCount == 1,
            "Gate2 governance mutation was not applied");
        Require(result.State.EnvironmentHazard.ItemCount == 1,
            "Gate2 environment mutation was not applied");
        Require(ReferenceEquals(result.State.ParticipationControlMode, controlModes),
            "Gate2 mutation stage must not mutate participation control authority");

        Qa04CanonicalOperationMutationBatchResultV1? persistentIndexBasis = null;
        foreach (var workerCount in new[] { 1, 4, 8, 16 })
        {
            var parallel = Qa04CanonicalOperationMutationBatchV1.ApplyParallelAsync(
                Qa04ReferenceLoadV1.WorldId,
                effectiveStep,
                bindings,
                initial,
                references,
                workerCount).GetAwaiter().GetResult();
            var observation = parallel.CpuParallelism
                ?? throw new InvalidOperationException("Gate2 parallel mutation observation missing.");
            Require(observation.RequestedWorkerCount == workerCount &&
                    observation.EffectiveWorkerCount == Math.Min(workerCount, 6) &&
                    observation.MaxObservedConcurrency is >= 1 &&
                    observation.MaxObservedConcurrency <= observation.EffectiveWorkerCount,
                $"Gate2 parallel mutation worker observation drifted for workers={workerCount}.");
            Require(observation.MinimumShardItemCount >= 1 &&
                    observation.MaximumShardItemCount >= observation.MinimumShardItemCount &&
                    observation.ShardItemCountSpread <= 1,
                $"Gate2 parallel mutation static shard assignment drifted for workers={workerCount}.");
            Require(observation.MinimumShardElapsedTimeTicks >= 0 &&
                    observation.MaximumShardElapsedTimeTicks >= observation.MinimumShardElapsedTimeTicks &&
                    observation.ShardElapsedTimeSpreadTicks >= 0,
                $"Gate2 parallel mutation shard timing observation drifted for workers={workerCount}.");
            Require(parallel.AppliedOperationIds.SequenceEqual(result.AppliedOperationIds),
                $"Gate2 parallel mutation operation order drifted for workers={workerCount}.");
            Require(parallel.Changes.Count == result.Changes.Count,
                $"Gate2 parallel mutation change cardinality drifted for workers={workerCount}.");
            for (var index = 0; index < result.Changes.Count; index++)
            {
                var expected = result.Changes[index];
                var actual = parallel.Changes[index];
                Require(actual.OperationId == expected.OperationId &&
                        actual.FamilyToken == expected.FamilyToken &&
                        actual.PartitionId == expected.PartitionId &&
                        actual.ChangedRecordId == expected.ChangedRecordId &&
                        actual.ResultRevision == expected.ResultRevision &&
                        actual.ImmutablePayloadDigest.SequenceEqual(expected.ImmutablePayloadDigest) &&
                        actual.OrderKey.ToDatabaseBytes().SequenceEqual(expected.OrderKey.ToDatabaseBytes()),
                    $"Gate2 parallel mutation semantic output drifted for workers={workerCount} index={index}.");
            }
            Require(parallel.State.InfrastructureServiceQueue.ItemCount == result.State.InfrastructureServiceQueue.ItemCount &&
                    parallel.State.ResidentBehaviorState.ItemCount == result.State.ResidentBehaviorState.ItemCount &&
                    parallel.State.PhysicalPresence.ItemCount == result.State.PhysicalPresence.ItemCount &&
                    parallel.State.MarketTransaction.State.ItemCount == result.State.MarketTransaction.State.ItemCount &&
                    parallel.State.GovernanceSecurityIncident.ItemCount == result.State.GovernanceSecurityIncident.ItemCount &&
                    parallel.State.EnvironmentHazard.ItemCount == result.State.EnvironmentHazard.ItemCount,
                $"Gate2 parallel mutation state cardinality drifted for workers={workerCount}.");
            if (workerCount == 4)
                persistentIndexBasis = parallel;
        }

        var indexedBasis = persistentIndexBasis
            ?? throw new InvalidOperationException("Gate2 resident persistent index basis missing.");
        var nextBindings = Qa04ReferenceLoadV1.OperationsForStep(2)
            .GroupBy(static descriptor => descriptor.FamilyToken.Value, StringComparer.Ordinal)
            .Select(static group => group.First())
            .Select(static descriptor => Qa04CanonicalOperationBindingV1.Bind(descriptor, schedulingPolicyGeneration: 1))
            .ToArray();
        Array.Sort(nextBindings, static (left, right) => left.OrderKey.CompareTo(right.OrderKey));
        var nextEffectiveStep = nextBindings[0].ScheduledOperation.EffectiveStep;
        Require(nextEffectiveStep == checked(effectiveStep + 1UL) &&
                nextBindings.All(binding => binding.ScheduledOperation.EffectiveStep == nextEffectiveStep),
            "Gate2 resident persistent index next-step schedule drift");
        var nextParallel = Qa04CanonicalOperationMutationBatchV1.ApplyParallelAsync(
            Qa04ReferenceLoadV1.WorldId,
            nextEffectiveStep,
            nextBindings,
            indexedBasis.State,
            references,
            workerCount: 4).GetAwaiter().GetResult();
        var firstBehavior = indexedBasis.State.ResidentBehaviorState.RecordsCanonical.Single();
        Require(nextParallel.State.ResidentBehaviorState.TryGet(firstBehavior.RecordId, out var nextBehavior) &&
                nextBehavior is not null &&
                nextBehavior.Revision == checked(firstBehavior.Revision + 1UL),
            "Gate2 resident persistent index must preserve consecutive revision semantics");

        var replay = Qa04CanonicalOperationMutationBatchV1.Apply(
            Qa04ReferenceLoadV1.WorldId,
            effectiveStep,
            bindings,
            initial,
            references);
        Require(replay.AppliedOperationIds.SequenceEqual(result.AppliedOperationIds),
            "Gate2 mutation replay operation order drift");
        Require(replay.State.InfrastructureServiceQueue.ItemCount == result.State.InfrastructureServiceQueue.ItemCount &&
                replay.State.ResidentBehaviorState.ItemCount == result.State.ResidentBehaviorState.ItemCount &&
                replay.State.PhysicalPresence.ItemCount == result.State.PhysicalPresence.ItemCount &&
                replay.State.MarketTransaction.State.ItemCount == result.State.MarketTransaction.State.ItemCount &&
                replay.State.GovernanceSecurityIncident.ItemCount == result.State.GovernanceSecurityIncident.ItemCount &&
                replay.State.EnvironmentHazard.ItemCount == result.State.EnvironmentHazard.ItemCount,
            "Gate2 mutation replay typed-state cardinality drift");
        Require(replay.State.PhysicalPresence.TryGet(physical.Presence.RecordId, out var replayPresence) &&
                replayPresence is not null && revisedPresence is not null &&
                replayPresence.Revision == revisedPresence.Revision &&
                replayPresence.Payload == revisedPresence.Payload,
            "Gate2 mutation replay physical target drift");

        var reversed = bindings.Reverse().ToArray();
        ExpectInvalid(
            () => Qa04CanonicalOperationMutationBatchV1.Apply(
                Qa04ReferenceLoadV1.WorldId,
                effectiveStep,
                reversed,
                initial,
                references),
            "qa04.full-step.mutation-order-not-canonical");
    }

    private static Qa04PhysicalD0RecordMaterialV1 PhysicalMaterial(ulong ordinal)
        => Qa04PhysicalD0MaterializerV1.Create(ordinal, PhysicalPresenceBinding(ordinal), TerrainBinding);

    private static Qa04PhysicalPresenceGenesisBindingV1 PhysicalPresenceBinding(ulong ordinal)
        => new(
            new PartitionRecordRefV1("resident.identity_lifecycle", Id(checked((byte)(1 + ordinal % 200)))),
            new PartitionRecordRefV1("spatial.world_frame", Id(0xF0)),
            new Vec3Int64V1(1000, 2000, 3000),
            new QuaternionQ30V1(0, 0, 0, 1 << 30),
            new Vec3Int64V1(0, 0, 0),
            new Vec3Int64V1(0, 0, 0),
            null,
            new StableToken("active"));

    private static Qa04PhysicalTerrainRootBindingV1 TerrainBinding(ushort _)
        => new(
            new PartitionRecordRefV1("spatial.terrain_geometry", Id(0xE0)),
            new Vec3Int64V1(-500, -500, -500),
            new Vec3Int64V1(500, 500, 500));

    private static OpaqueId128 Id(byte suffix)
    {
        var bytes = new byte[16];
        bytes[^1] = suffix;
        return OpaqueId128.FromBytes(bytes);
    }

    private static void ExpectInvalid(Action action, string expectedMessage)
    {
        try
        {
            action();
            throw new InvalidOperationException("Expected Gate2 mutation batch to reject invalid input.");
        }
        catch (InvalidDataException ex)
        {
            if (ex.Message != expectedMessage)
                throw new InvalidOperationException($"Unexpected Gate2 rejection code: {ex.Message}");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class RegistryResolver : IDomainRecordSchemaResolverV1
    {
        public bool Exists(PartitionRecordRefV1 reference)
            => !reference.RecordId.IsZero && TryResolve(reference, out _);

        public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
            => TryResolve(reference, out schema);

        private static bool TryResolve(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
        {
            schema = default!;
            if (reference.RecordId.IsZero) return false;
            try
            {
                schema = StandardDomainPartitionRegistry.Get(reference.PartitionId.Value).RecordSchema;
                return true;
            }
            catch (KeyNotFoundException)
            {
                return false;
            }
        }
    }
}
