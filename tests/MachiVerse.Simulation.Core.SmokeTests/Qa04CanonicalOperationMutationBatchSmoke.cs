using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.Runtime.Qa04;

internal static class Qa04CanonicalOperationMutationBatchSmoke
{
    public static void Run()
    {
        var references = Qa04ReferenceLoadV1.Create();
        var effectiveStep = 12L;
        var controlModes = Qa04ParticipationControlModeSetV1.CreateDefault();
        var initial = Qa04ReferenceWorldStateV1.CreateInitial(references, controlModes);
        var bindings = Qa04CanonicalOperationBindingBatchV1.CreateReferenceBindings(
            Qa04ReferenceLoadV1.WorldId,
            effectiveStep,
            references);

        var result = Qa04CanonicalOperationMutationBatchV1.Apply(
            Qa04ReferenceLoadV1.WorldId,
            effectiveStep,
            bindings,
            initial,
            references);

        Require(result.AppliedOperationIds.Count == bindings.Count,
            "Gate2 canonical mutation batch did not apply all bindings");
        Require(result.Changes.Count == bindings.Count,
            "Gate2 canonical mutation batch change cardinality drifted");
        Require(result.AppliedOperationIds.SequenceEqual(
                bindings.Select(static binding => binding.Operation.OperationId)),
            "Gate2 canonical mutation batch operation order drifted");

        for (var index = 0; index < result.Changes.Count; index++)
        {
            var binding = bindings[index];
            var change = result.Changes[index];
            Require(change.OperationId == binding.Operation.OperationId,
                $"Gate2 canonical mutation operation id drifted at index={index}");
            Require(change.FamilyToken == binding.FamilyToken,
                $"Gate2 canonical mutation family token drifted at index={index}");
            Require(change.PartitionId == binding.Operation.PartitionId,
                $"Gate2 canonical mutation partition id drifted at index={index}");
            Require(change.OrderKey.ToDatabaseBytes().SequenceEqual(
                    binding.Operation.OrderKey.ToDatabaseBytes()),
                $"Gate2 canonical mutation order key drifted at index={index}");
        }

        var infrastructure = bindings.Single(binding =>
            binding.FamilyToken == Qa04CanonicalOperationFamilyTokenV1.InfrastructureServiceQueue);
        var resident = bindings.Single(binding =>
            binding.FamilyToken == Qa04CanonicalOperationFamilyTokenV1.ResidentBehaviorState);
        var physical = bindings.Single(binding =>
            binding.FamilyToken == Qa04CanonicalOperationFamilyTokenV1.PhysicalPresence);
        var market = bindings.Single(binding =>
            binding.FamilyToken == Qa04CanonicalOperationFamilyTokenV1.MarketTransaction);
        var governance = bindings.Single(binding =>
            binding.FamilyToken == Qa04CanonicalOperationFamilyTokenV1.GovernanceSecurityIncident);
        var environment = bindings.Single(binding =>
            binding.FamilyToken == Qa04CanonicalOperationFamilyTokenV1.EnvironmentHazard);

        var infrastructureState = result.State.InfrastructureServiceQueue;
        var residentState = result.State.ResidentBehaviorState;
        var physicalState = result.State.PhysicalPresence;
        var marketState = result.State.MarketTransaction;
        var governanceState = result.State.GovernanceSecurityIncident;
        var environmentState = result.State.EnvironmentHazard;

        Require(infrastructureState.ItemCount == 1,
            "Gate2 infrastructure mutation was not applied");
        Require(residentState.ItemCount == 1,
            "Gate2 resident mutation was not applied");
        Require(physicalState.TryGet(physical.Presence.RecordId, out var revisedPresence) &&
                revisedPresence is not null && revisedPresence.Revision == 2,
            "Gate2 physical mutation was not applied");
        Require(marketState.State.ItemCount == 1,
            "Gate2 market mutation was not applied");
        Require(governanceState.ItemCount == 1,
            "Gate2 governance mutation was not applied");
        Require(environmentState.ItemCount == 1,
            "Gate2 environment mutation was not applied");
        Require(ReferenceEquals(result.State.ParticipationControlMode, controlModes),
            "Gate2 mutation stage must not mutate participation control authority");

        var infrastructureChange = result.Changes.Single(change =>
            change.OperationId == infrastructure.Operation.OperationId);
        var residentChange = result.Changes.Single(change =>
            change.OperationId == resident.Operation.OperationId);
        var physicalChange = result.Changes.Single(change =>
            change.OperationId == physical.Operation.OperationId);
        var marketChange = result.Changes.Single(change =>
            change.OperationId == market.Operation.OperationId);
        var governanceChange = result.Changes.Single(change =>
            change.OperationId == governance.Operation.OperationId);
        var environmentChange = result.Changes.Single(change =>
            change.OperationId == environment.Operation.OperationId);

        Require(infrastructureChange.ChangedRecordId == infrastructure.ServiceQueue.RecordId,
            "Gate2 infrastructure mutation record id drifted");
        Require(residentChange.ChangedRecordId == resident.ResidentState.RecordId,
            "Gate2 resident mutation record id drifted");
        Require(physicalChange.ChangedRecordId == physical.Presence.RecordId,
            "Gate2 physical mutation record id drifted");
        Require(marketChange.ChangedRecordId == market.TransactionState.RecordId,
            "Gate2 market mutation record id drifted");
        Require(governanceChange.ChangedRecordId == governance.Incident.RecordId,
            "Gate2 governance mutation record id drifted");
        Require(environmentChange.ChangedRecordId == environment.Hazard.RecordId,
            "Gate2 environment mutation record id drifted");

        Require(infrastructureChange.ResultRevision == infrastructure.ServiceQueue.Revision,
            "Gate2 infrastructure mutation revision drifted");
        Require(residentChange.ResultRevision == resident.ResidentState.Revision,
            "Gate2 resident mutation revision drifted");
        Require(physicalChange.ResultRevision == physical.Presence.Revision,
            "Gate2 physical mutation revision drifted");
        Require(marketChange.ResultRevision == market.TransactionState.Revision,
            "Gate2 market mutation revision drifted");
        Require(governanceChange.ResultRevision == governance.Incident.Revision,
            "Gate2 governance mutation revision drifted");
        Require(environmentChange.ResultRevision == environment.Hazard.Revision,
            "Gate2 environment mutation revision drifted");

        Require(result.State.InfrastructureServiceQueue.ItemCount == 1,
            "Gate2 infrastructure mutation was not applied");
        Require(result.State.ResidentBehaviorState.ItemCount == 1,
            "Gate2 resident mutation was not applied");
        Require(result.State.PhysicalPresence.TryGet(physical.Presence.RecordId, out var revisedPresence2) &&
                revisedPresence2 is not null && revisedPresence2.Revision == 2,
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
            Require(observation.MinimumShardItemCount >= 0 &&
                    observation.MaximumShardItemCount >= observation.MinimumShardItemCount &&
                    observation.MaximumShardItemCount >= 1,
                $"Gate2 parallel mutation dynamic worker assignment drifted for workers={workerCount}.");
            Require(observation.MinimumShardElapsedTimeTicks >= 0 &&
                    observation.MaximumShardElapsedTimeTicks >= observation.MinimumShardElapsedTimeTicks &&
                    observation.ShardElapsedTimeSpreadTicks >= 0,
                $"Gate2 parallel mutation worker timing observation drifted for workers={workerCount}.");
            Require(observation.ClaimChunkSize >= 1 &&
                    observation.ClaimChunkCount >= 1 &&
                    observation.ClaimChunkCount <= bindings.Count,
                $"Gate2 parallel mutation dynamic chunk observation drifted for workers={workerCount}.");
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
        var residentIndex = indexedBasis.State.ResidentBehaviorState.Index;
        Require(residentIndex is not null,
            "Gate2 parallel mutation resident persistent index missing.");
        Require(residentIndex.TryGet(resident.ResidentState.RecordId, out var indexedResidentState) &&
                indexedResidentState is not null &&
                indexedResidentState.Revision == resident.ResidentState.Revision,
            "Gate2 parallel mutation resident persistent index lookup drifted.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
