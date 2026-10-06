using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Environment;
using MachiVerse.Simulation.Core.Domains.GovernanceSecurity;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
using MachiVerse.Simulation.Core.Domains.Participation;
using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04CanonicalOperationPartitionCandidatesSmoke
{
    private const string InfrastructureFamily = "infrastructure-service-delivery";
    private const string ResidentFamily = "participation-control-resident-action";
    private const string PhysicalFamily = "physical-item-movement-work";
    private const string MarketFamily = "society-market-payment-contract";
    private const string GovernanceFamily = "governance-security";
    private const string EnvironmentFamily = "environment-spatial-admin-synthetic";

    public static void Run()
    {
        var bindings = Qa04ReferenceLoadV1.OperationsForStep(1)
            .GroupBy(static descriptor => descriptor.FamilyToken.Value, StringComparer.Ordinal)
            .Select(static group => group.First())
            .Select(static descriptor => Qa04CanonicalOperationBindingV1.Bind(descriptor, schedulingPolicyGeneration: 1))
            .ToArray();
        Array.Sort(bindings, static (left, right) => left.OrderKey.CompareTo(right.OrderKey));
        Require(bindings.Length == 6, "Gate2 partition candidate smoke requires all six canonical families");

        var effectiveStep = bindings[0].ScheduledOperation.EffectiveStep;
        Require(effectiveStep > 0 && bindings.All(binding => binding.ScheduledOperation.EffectiveStep == effectiveStep),
            "Gate2 partition candidate smoke requires one common effective step");
        var basisStep = effectiveStep - 1UL;

        var residentBinding = bindings.Single(static value =>
            value.SourceDescriptor.FamilyToken.Value == ResidentFamily);
        var physicalBinding = bindings.Single(static value =>
            value.SourceDescriptor.FamilyToken.Value == PhysicalFamily);
        var marketBinding = bindings.Single(static value =>
            value.SourceDescriptor.FamilyToken.Value == MarketFamily);

        var residentOrdinal = residentBinding.SourceDescriptor.FamilyOrdinal;
        var resident = Qa04ReferenceLoadV1.Record(new StableToken("resident.persistent-identity"), residentOrdinal);
        var residentRef = new PartitionRecordRefV1(ResidentIdentityLifecyclePayloadV1.PartitionId, resident.RecordId);
        var controlIdentity = StandardDomainPartitionRegistry.Get(ParticipationControlModePayloadV1.PartitionId);
        var controlMode = new DomainRecordEnvelopeV1<ParticipationControlModePayloadV1>(
            Qa04ParticipationControlModeCanonicalAuthorityV1.RecordId(residentOrdinal),
            controlIdentity.RecordSchema,
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
            controlIdentity,
            new[] { controlMode });

        var serviceQueue = new DomainPartitionStateV1<InfrastructureServiceQueuePayloadV1>(
            StandardDomainPartitionRegistry.Get(InfrastructureServiceQueuePayloadV1.PartitionId),
            Array.Empty<DomainRecordEnvelopeV1<InfrastructureServiceQueuePayloadV1>>());
        var behavior = new DomainPartitionStateV1<ResidentBehaviorStatePayloadV1>(
            StandardDomainPartitionRegistry.Get(ResidentBehaviorStatePayloadV1.PartitionId),
            Array.Empty<DomainRecordEnvelopeV1<ResidentBehaviorStatePayloadV1>>());

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
        var basisState = BuildBasisState(basisStep, initial, references);
        var mutation = Qa04CanonicalOperationMutationBatchV1.Apply(
            Qa04ReferenceLoadV1.WorldId,
            effectiveStep,
            bindings,
            initial,
            references);

        var bound = Qa04CanonicalOperationPartitionCandidateBinderV1.Bind(
            basisState,
            bindings,
            mutation,
            references);
        Require(bound.BasisStep == basisStep && bound.TargetStep == effectiveStep,
            "Gate2 partition candidate batch step drift");
        Require(bound.Partitions.Count == 6,
            "Gate2 partition candidate batch must bind exactly six changed partitions");
        Require(bound.Partitions.Select(static item => item.Candidate.PartitionId.Value)
                    .SequenceEqual(bound.Partitions.Select(static item => item.Candidate.PartitionId.Value)
                        .OrderBy(static value => value, StringComparer.Ordinal)),
            "Gate2 partition candidates must be canonical partition-id order");
        Require(bound.Partitions.Select(static item => item.Candidate.PartitionId).Distinct().Count() == 6,
            "Gate2 partition candidates must be unique by partition");
        Require(bound.Partitions.All(static item =>
                item.Candidate.PartitionId.Value != ParticipationControlModePayloadV1.PartitionId),
            "Gate2 must not emit a participation.control_mode candidate");

        var expectedItemCounts = new Dictionary<string, ulong>(StringComparer.Ordinal)
        {
            [InfrastructureServiceQueuePayloadV1.PartitionId] = mutation.State.InfrastructureServiceQueue.ItemCount,
            [ResidentBehaviorStatePayloadV1.PartitionId] = mutation.State.ResidentBehaviorState.ItemCount,
            [PhysicalPresencePayloadV1.PartitionId] = mutation.State.PhysicalPresence.ItemCount,
            [SocietyMarketTransactionRecordSchemaV2.PartitionId] = mutation.State.MarketTransaction.State.ItemCount,
            [GovernanceSecurityIncidentPayloadV1.PartitionId] = mutation.State.GovernanceSecurityIncident.ItemCount,
            [EnvironmentHazardPayloadV1.PartitionId] = mutation.State.EnvironmentHazard.ItemCount,
        };

        foreach (var item in bound.Partitions)
        {
            var partitionId = item.Candidate.PartitionId.Value;
            var basisHeader = basisState.Partitions.Get(partitionId).Header;
            var resultingHeader = item.Material.ResultingHeader;
            var family = FamilyForPartition(partitionId);
            var operationIds = bindings
                .Where(binding => binding.SourceDescriptor.FamilyToken.Value == family)
                .Select(static binding => binding.SourceDescriptor.OperationId)
                .ToArray();
            var expectedDigest = ComputeQa04ChangeSetDigest(
                basisHeader,
                resultingHeader,
                basisStep,
                effectiveStep,
                operationIds);

            Require(operationIds.Length > 0 && operationIds.Distinct().Count() == operationIds.Length,
                $"Gate2 operation-id binding drift: {partitionId}");
            Require(item.Candidate.BasisRevision == basisHeader.Revision,
                $"Gate2 candidate basis revision drift: {partitionId}");
            Require(item.Candidate.CandidateRevision == basisHeader.Revision + 1UL,
                $"Gate2 candidate revision drift: {partitionId}");
            Require(item.Candidate.BasisStep == basisStep && item.Candidate.TargetStep == effectiveStep,
                $"Gate2 candidate step drift: {partitionId}");
            Require(resultingHeader.Revision == item.Candidate.CandidateRevision &&
                    resultingHeader.BasisStep == effectiveStep &&
                    resultingHeader.DetailLevel == basisHeader.DetailLevel,
                $"Gate2 resulting header drift: {partitionId}");
            Require(resultingHeader.ItemCount == expectedItemCounts[partitionId],
                $"Gate2 resulting item count drift: {partitionId}");
            Require(expectedDigest.AsSpan().SequenceEqual(item.Candidate.ChangeSetDigest),
                $"Gate2 QA-04 canonical change-set digest drift: {partitionId}");
        }

        var authoritativeBasisState = BuildBasisState(effectiveStep, initial, references);
        var authoritativeSequential = Qa04CanonicalOperationAuthoritativeStepPartitionBinderV1.Bind(
            authoritativeBasisState,
            bindings,
            mutation,
            references);

        var canonicalChunkCache = new Qa04ProductionStep2CanonicalDigestCacheV1(references);
        var authoritativeChunked = Qa04CanonicalOperationAuthoritativeStepPartitionBinderV1.Bind(
            authoritativeBasisState,
            bindings,
            mutation,
            references,
            canonicalChunkCache);
        RequireSamePartitionDigests(
            authoritativeSequential,
            authoritativeChunked,
            "Gate2 canonical chunk first-step digest drift");

        var authoritativeChunkedReplay = Qa04CanonicalOperationAuthoritativeStepPartitionBinderV1.Bind(
            authoritativeBasisState,
            bindings,
            mutation,
            references,
            canonicalChunkCache);
        RequireSamePartitionDigests(
            authoritativeSequential,
            authoritativeChunkedReplay,
            "Gate2 canonical chunk repeated-material digest drift");

        var nextBasisState = AdvanceBasisState(authoritativeBasisState, authoritativeChunked);
        var nextBindings = Qa04ReferenceLoadV1.OperationsForStep(2)
            .GroupBy(static descriptor => descriptor.FamilyToken.Value, StringComparer.Ordinal)
            .Select(static group => group.First())
            .Select(static descriptor => Qa04CanonicalOperationBindingV1.Bind(descriptor, schedulingPolicyGeneration: 1))
            .ToArray();
        Array.Sort(nextBindings, static (left, right) => left.OrderKey.CompareTo(right.OrderKey));
        Require(nextBindings.Length == 6 &&
                nextBindings.All(binding => binding.ScheduledOperation.EffectiveStep == nextBasisState.Header.Step),
            "Gate2 canonical chunk second-step binding drift");
        var nextMutation = Qa04CanonicalOperationMutationBatchV1.Apply(
            Qa04ReferenceLoadV1.WorldId,
            nextBasisState.Header.Step,
            nextBindings,
            mutation.State,
            references);
        var nextUncached = Qa04CanonicalOperationAuthoritativeStepPartitionBinderV1.Bind(
            nextBasisState,
            nextBindings,
            nextMutation,
            references);
        var nextChunked = Qa04CanonicalOperationAuthoritativeStepPartitionBinderV1.Bind(
            nextBasisState,
            nextBindings,
            nextMutation,
            references,
            canonicalChunkCache);
        RequireSamePartitionDigests(
            nextUncached,
            nextChunked,
            "Gate2 canonical chunk incremental digest drift");

        var snapshotHeader = nextChunked.Partitions.Single(item =>
            item.Material.ResultingHeader.PartitionId.Value ==
                SocietyMarketTransactionRecordSchemaV2.PartitionId).Material.ResultingHeader;
        // Exercise the trusted preparation fast path used by the real production loop.
        var preparedFactory = typeof(SocietyMarketTransactionSnapshotAuthorityV2).GetMethod(
            "FromPreparedHeader", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new InvalidOperationException("Production Market prepared authority factory missing.");
        var snapshotAuthority = (SocietyMarketTransactionSnapshotAuthorityV2)preparedFactory.Invoke(
            null, [nextMutation.State.MarketTransaction, snapshotHeader])!;
        var marketCache = typeof(Qa04ProductionStep2CanonicalDigestCacheV1).GetProperty(
            "MarketChunks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(canonicalChunkCache)!;
        var captureCommitments = marketCache.GetType().GetMethod("CaptureSnapshotCommitments",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var attachCommitments = typeof(SocietyMarketTransactionSnapshotAuthorityV2).GetMethod(
            "WithSnapshotCommitments", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var captured = captureCommitments.Invoke(marketCache, [nextMutation.State.MarketTransaction.State, snapshotHeader])!;
        snapshotAuthority = (SocietyMarketTransactionSnapshotAuthorityV2)attachCommitments.Invoke(snapshotAuthority, [captured])!;
        var snapshotProvider = new SocietyMarketTransactionSnapshotSectionProviderV2();
        var snapshotSection = snapshotProvider.Create(snapshotAuthority, references);
        var recoveredSnapshot = snapshotProvider.CreateSemanticVerifier(snapshotHeader, references)
            .Verify(snapshotSection.Fragments);
        Require(recoveredSnapshot.LogicalContentDigest.AsSpan().SequenceEqual(snapshotHeader.CanonicalDigest),
            "Production Market RecordIdPrefixV2 snapshot recovery must preserve its canonical digest.");

        var runtimeOrder = nextMutation.State.MarketTransaction.State.RecordsCanonical.First(record =>
            record.CreatedStep > 0 && record.Payload is SocietyMarketOrderPayloadV2);
        var runtimePayload = (SocietyMarketOrderPayloadV2)runtimeOrder.Payload;
        var freshPayload = new SocietyMarketOrderPayloadV2(runtimePayload.MarketRef, runtimePayload.OwnerRef,
            runtimePayload.InstrumentToken, runtimePayload.Side, runtimePayload.LimitPriceMicrounit,
            runtimePayload.Quantity, runtimePayload.RemainingQuantity, runtimePayload.EligibleStep, runtimePayload.Status);
        var freshOrder = new DomainRecordEnvelopeV1<SocietyMarketTransactionRecordPayloadV2>(runtimeOrder.RecordId,
            runtimeOrder.RecordSchema, runtimeOrder.Revision, runtimeOrder.CreatedStep, runtimeOrder.RetiredStep,
            runtimeOrder.DetailLevel, runtimeOrder.LineageRef, freshPayload);
        var payloadCache = (System.Collections.IEnumerable)typeof(Qa04ProductionStep2CanonicalDigestCacheV1)
            .GetField("_market", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(canonicalChunkCache)!;
        var recordCache = (System.Collections.IEnumerable)typeof(Qa04ProductionStep2CanonicalDigestCacheV1)
            .GetField("_marketRecords", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(canonicalChunkCache)!;
        Require(!recordCache.Cast<object>().Any(),
            "The prefix chunk path must not retain a duplicate per-record encoding table.");
        var retainedBefore = payloadCache.Cast<object>().Count();
        var encodedOnce = canonicalChunkCache.MarketRecord(freshOrder);
        Require(payloadCache.Cast<object>().Count() == retainedBefore,
            "One-use runtime Market digests must not be retained in the payload cache.");
        var encodeMarket = typeof(PartitionStateHeaderV1).GetMethod("EncodeCanonicalRecord",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .MakeGenericMethod(typeof(SocietyMarketTransactionRecordPayloadV2));
        var rawEncoded = (byte[])encodeMarket.Invoke(null,
            [freshOrder, SocietyMarketTransactionPayloadCanonicalDigestV2.Compute(freshPayload, references)])!;
        Require(encodedOnce.AsSpan().SequenceEqual(rawEncoded),
            "Bypassing one-use payload caching must preserve every canonical record byte.");

        var corruptDigest = snapshotHeader.CanonicalDigest.ToArray();
        corruptDigest[0] ^= 1;
        var corruptHeader = new PartitionStateHeaderV1(
            nextMutation.State.MarketTransaction.State.Identity,
            snapshotHeader.Revision, snapshotHeader.BasisStep, snapshotHeader.DetailLevel,
            snapshotHeader.ItemCount, corruptDigest, snapshotHeader.DigestAlgorithm);
        var corruptPrepared = (SocietyMarketTransactionSnapshotAuthorityV2)preparedFactory.Invoke(
            null, [nextMutation.State.MarketTransaction, corruptHeader])!;
        ExpectInvalid(() => snapshotProvider.Create(corruptPrepared, references),
            "persistence.snapshot.society-market-v2-header-material");
        ExpectReflectionInvalid(() => captureCommitments.Invoke(marketCache,
            [nextMutation.State.MarketTransaction.State, corruptHeader]),
            "qa04.prefix-digest-cache.snapshot-header-drift");
        ExpectReflectionInvalid(() => attachCommitments.Invoke(corruptPrepared, [captured]),
            "persistence.snapshot.society-market-v2-commitment-root");
        var staleHeader = new PartitionStateHeaderV1(nextMutation.State.MarketTransaction.State.Identity,
            snapshotHeader.Revision, snapshotHeader.BasisStep - 1, snapshotHeader.DetailLevel,
            snapshotHeader.ItemCount, snapshotHeader.CanonicalDigest, snapshotHeader.DigestAlgorithm);
        ExpectReflectionInvalid(() => captureCommitments.Invoke(marketCache,
            [nextMutation.State.MarketTransaction.State, staleHeader]),
            "qa04.prefix-digest-cache.snapshot-basis-drift");

        // A matching State header must not authorize a different RecordSet for serialization.
        var serializedRecords = nextMutation.State.MarketTransaction.RecordSet.RecordsCanonical.ToArray();
        var orderIndex = Array.FindIndex(serializedRecords, static record => record.Payload is SocietyMarketOrderPayloadV2);
        var originalOrder = serializedRecords[orderIndex];
        var originalPayload = (SocietyMarketOrderPayloadV2)originalOrder.Payload;
        serializedRecords[orderIndex] = new SocietyMarketTransactionRecordMaterialV2(
            originalOrder.RecordId, originalOrder.Revision, originalOrder.CreatedStep, originalOrder.RetiredStep,
            originalOrder.DetailLevel, originalOrder.LineageRef,
            new SocietyMarketOrderPayloadV2(originalPayload.MarketRef, originalPayload.OwnerRef,
                originalPayload.InstrumentToken, originalPayload.Side, checked(originalPayload.LimitPriceMicrounit + 1),
                originalPayload.Quantity, originalPayload.RemainingQuantity, originalPayload.EligibleStep, originalPayload.Status));
        var materialFactory = typeof(SocietyMarketTransactionPartitionStateV2).GetMethod(
            "FromCanonicalMaterial", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new InvalidOperationException("Market canonical material factory missing.");
        var divergentMaterial = (SocietyMarketTransactionPartitionStateV2)materialFactory.Invoke(null,
            [serializedRecords, nextMutation.State.MarketTransaction.State.RecordsCanonical.ToArray()])!;
        var divergentPrepared = (SocietyMarketTransactionSnapshotAuthorityV2)preparedFactory.Invoke(
            null, [divergentMaterial, snapshotHeader])!;
        ExpectInvalid(() => snapshotProvider.Create(divergentPrepared, references),
            "persistence.snapshot.society-market-v2-record-material");

        // Both State and RecordSet agree with each other, but differ from the frozen cut. The
        // retained cut must identify the actual corrupt prefix and refuse snapshot publication.
        var wrongPartition = new SocietyMarketTransactionPartitionStateV2(serializedRecords);
        var wrongPrepared = (SocietyMarketTransactionSnapshotAuthorityV2)preparedFactory.Invoke(
            null, [wrongPartition, snapshotHeader])!;
        wrongPrepared = (SocietyMarketTransactionSnapshotAuthorityV2)attachCommitments.Invoke(wrongPrepared, [captured])!;
        try
        {
            snapshotProvider.Create(wrongPrepared, references);
            throw new InvalidOperationException("Wrong frozen Market material was accepted.");
        }
        catch (InvalidDataException failure)
        {
            Require(failure.Message.StartsWith("persistence.snapshot.society-market-v2-slice-material", StringComparison.Ordinal) &&
                failure.Message.Contains($"step={snapshotHeader.BasisStep} prefix={RecordIdPrefixPartitionDigestV2.PrefixOf(originalOrder.RecordId)} ", StringComparison.Ordinal),
                "Frozen Market mismatch must identify the exact cut and first corrupt prefix.");
        }

        // Advance the live cache after capture, then recover the older cut once more. Neither
        // changing the directory nor replacing its commitments may mutate frozen evidence.
        var futureBinding = Qa04CanonicalOperationBindingV1.Bind(Qa04ReferenceLoadV1.OperationsForStep(3)
            .First(descriptor => descriptor.FamilyToken.Value == "society-market-payment-contract"), 1);
        var futureMarket = Qa04MarketOrderApplicationV1.Apply(futureBinding, nextMutation.State.MarketTransaction, references);
        var futureRecord = futureMarket.CreatedOrder;
        var futureChange = new Qa04CanonicalOperationMutationChangeV1(futureBinding.SourceDescriptor.FamilyToken,
            futureBinding.Operation.OperationKind, futureBinding.SourceDescriptor.OperationId,
            futureBinding.Operation.ImmutablePayloadDigest.ToByteArray(), futureBinding.OrderKey,
            futureMarket.MarketState.State.Identity.PartitionId, futureRecord.RecordId, futureRecord.RecordSchema,
            futureRecord.Revision, futureRecord.CreatedStep, futureRecord.DetailLevel, new StableToken("create"));
        marketCache.GetType().GetMethod("CreateHeader")!.Invoke(marketCache,
            [futureMarket.MarketState.State, snapshotHeader.Revision + 1, snapshotHeader.BasisStep + 1,
                snapshotHeader.DetailLevel, new[] { futureChange }]);
        var frozenAgain = snapshotProvider.Create(snapshotAuthority, references);
        _ = snapshotProvider.CreateSemanticVerifier(snapshotHeader, references).Verify(frozenAgain.Fragments);

        var movedRecords = nextMutation.State.MarketTransaction.RecordSet.RecordsCanonical.ToArray();
        var missingFirst = movedRecords[0];
        movedRecords[0] = new SocietyMarketTransactionRecordMaterialV2(new OpaqueId128(UInt128.MaxValue),
            missingFirst.Revision, missingFirst.CreatedStep, missingFirst.RetiredStep,
            missingFirst.DetailLevel, missingFirst.LineageRef, missingFirst.Payload);
        var missingFirstAuthority = (SocietyMarketTransactionSnapshotAuthorityV2)preparedFactory.Invoke(null,
            [new SocietyMarketTransactionPartitionStateV2(movedRecords), snapshotHeader])!;
        missingFirstAuthority = (SocietyMarketTransactionSnapshotAuthorityV2)attachCommitments.Invoke(missingFirstAuthority, [captured])!;
        ExpectPrefixFailure(missingFirstAuthority, RecordIdPrefixPartitionDigestV2.PrefixOf(missingFirst.RecordId), false);

        // Fault injection of an expected-only tail: it must retain the same detailed provenance
        // as a mismatch encountered while the independent raw stream is still producing slices.
        var capturedArray = ((System.Collections.IEnumerable)captured).Cast<object>().ToArray();
        var tailArray = Array.CreateInstance(capturedArray[0].GetType(), capturedArray.Length + 1);
        for (var index = 0; index < capturedArray.Length; index++) tailArray.SetValue(capturedArray[index], index);
        tailArray.SetValue(Activator.CreateInstance(capturedArray[0].GetType(),
            (ushort)(RecordIdPrefixPartitionDigestV2.PrefixCount - 1), 1UL, new byte[32]), capturedArray.Length);
        var missingTailAuthority = (SocietyMarketTransactionSnapshotAuthorityV2)attachCommitments.Invoke(snapshotAuthority, [captured])!;
        typeof(SocietyMarketTransactionSnapshotAuthorityV2).GetField("_snapshotCommitments",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(missingTailAuthority, tailArray);
        ExpectPrefixFailure(missingTailAuthority, (ushort)(RecordIdPrefixPartitionDigestV2.PrefixCount - 1), true);

        void ExpectPrefixFailure(SocietyMarketTransactionSnapshotAuthorityV2 authority, ushort prefix, bool missingActual)
        {
            try { snapshotProvider.Create(authority, references); }
            catch (InvalidDataException failure) when (failure.Message.StartsWith(
                "persistence.snapshot.society-market-v2-slice-material", StringComparison.Ordinal) &&
                failure.Message.Contains($"step={snapshotHeader.BasisStep} prefix={prefix} ", StringComparison.Ordinal) &&
                (!missingActual || failure.Message.Contains("actual_items=0", StringComparison.Ordinal) &&
                    failure.Message.Contains("actual=missing", StringComparison.Ordinal))) { return; }
            throw new InvalidOperationException("Missing prefix was accepted or diagnosed at the wrong position.");
        }

        serializedRecords[orderIndex] = new SocietyMarketTransactionRecordMaterialV2(
            originalOrder.RecordId, originalOrder.Revision, originalOrder.CreatedStep, originalOrder.RetiredStep,
            originalOrder.DetailLevel, originalOrder.LineageRef,
            new SocietyMarketOrderPayloadV2(originalPayload.MarketRef, originalPayload.OwnerRef,
                originalPayload.InstrumentToken, originalPayload.Side, originalPayload.LimitPriceMicrounit,
                originalPayload.Quantity, originalPayload.RemainingQuantity, originalPayload.EligibleStep, originalPayload.Status));
        var equivalentMaterial = (SocietyMarketTransactionPartitionStateV2)materialFactory.Invoke(null,
            [serializedRecords, nextMutation.State.MarketTransaction.State.RecordsCanonical.ToArray()])!;
        var equivalentPrepared = (SocietyMarketTransactionSnapshotAuthorityV2)preparedFactory.Invoke(
            null, [equivalentMaterial, snapshotHeader])!;
        var equivalentSection = snapshotProvider.Create(equivalentPrepared, references);
        _ = snapshotProvider.CreateSemanticVerifier(snapshotHeader, references).Verify(equivalentSection.Fragments);

        foreach (var workerCount in new[] { 1, 4, 8, 16 })
        {
            var parallel = Qa04CanonicalOperationAuthoritativeStepPartitionBinderV1.BindParallelAsync(
                authoritativeBasisState,
                bindings,
                mutation,
                references,
                workerCount).GetAwaiter().GetResult();
            var observation = parallel.CpuParallelism
                ?? throw new InvalidOperationException("Gate2 parallel preparation observation missing.");
            Require(observation.RequestedWorkerCount == workerCount &&
                    observation.EffectiveWorkerCount == Math.Min(workerCount, 6) &&
                    observation.MaxObservedConcurrency is >= 1 &&
                    observation.MaxObservedConcurrency <= observation.EffectiveWorkerCount,
                $"Gate2 parallel preparation worker observation drifted for workers={workerCount}.");
            Require(parallel.Partitions.Count == authoritativeSequential.Partitions.Count,
                $"Gate2 parallel preparation partition count drifted for workers={workerCount}.");
            for (var index = 0; index < authoritativeSequential.Partitions.Count; index++)
            {
                var expected = authoritativeSequential.Partitions[index];
                var actual = parallel.Partitions[index];
                Require(actual.Candidate.PartitionId == expected.Candidate.PartitionId &&
                        actual.Candidate.ChangeSetDigest.AsSpan().SequenceEqual(expected.Candidate.ChangeSetDigest) &&
                        actual.Material.ResultingHeader.CanonicalDigest.AsSpan().SequenceEqual(
                            expected.Material.ResultingHeader.CanonicalDigest),
                    $"Gate2 parallel preparation canonical digest drifted for workers={workerCount} index={index}.");
            }
        }

        var marketBound = bound.Partitions.Single(static item =>
            item.Candidate.PartitionId.Value == SocietyMarketTransactionRecordSchemaV2.PartitionId);
        var marketBasisHeader = basisState.Partitions.Get(SocietyMarketTransactionRecordSchemaV2.PartitionId).Header;
        var expectedMarketHeader = PartitionStateHeaderV1.CreateCanonical(
            mutation.State.MarketTransaction.State,
            marketBasisHeader.Revision + 1UL,
            effectiveStep,
            marketBasisHeader.DetailLevel,
            payload => SocietyMarketTransactionPayloadCanonicalDigestV2.Compute(payload, references));
        Require(expectedMarketHeader.CanonicalDigest.AsSpan().SequenceEqual(
                marketBound.Material.ResultingHeader.CanonicalDigest),
            "Gate2 Market candidate must use the existing v2 canonical digest authority with references");

        var replay = Qa04CanonicalOperationPartitionCandidateBinderV1.Bind(
            basisState,
            bindings,
            mutation,
            references);
        Require(replay.Partitions.Count == bound.Partitions.Count,
            "Gate2 partition candidate replay count drift");
        for (var index = 0; index < bound.Partitions.Count; index++)
        {
            Require(replay.Partitions[index].Candidate.PartitionId == bound.Partitions[index].Candidate.PartitionId &&
                    replay.Partitions[index].Candidate.ChangeSetDigest.AsSpan().SequenceEqual(
                        bound.Partitions[index].Candidate.ChangeSetDigest) &&
                    replay.Partitions[index].Material.ResultingHeader.CanonicalDigest.AsSpan().SequenceEqual(
                        bound.Partitions[index].Material.ResultingHeader.CanonicalDigest),
                "Gate2 partition candidate replay drift");
        }

        ExpectInvalid(
            () => Qa04CanonicalOperationPartitionCandidateBinderV1.Bind(
                basisState,
                bindings,
                mutation with { EffectiveStep = effectiveStep + 1UL },
                references),
            "qa04.full-step.partition-candidate-effective-step-drift");

        var receiptDrift = mutation with
        {
            AppliedOperationIds = Array.AsReadOnly(mutation.AppliedOperationIds.Reverse().ToArray()),
        };
        ExpectInvalid(
            () => Qa04CanonicalOperationPartitionCandidateBinderV1.Bind(
                basisState,
                bindings,
                receiptDrift,
                references),
            "qa04.full-step.partition-candidate-receipt-order-drift");

        var reversedBindings = bindings.Reverse().ToArray();
        var reversedReceipt = mutation with
        {
            AppliedOperationIds = Array.AsReadOnly(mutation.AppliedOperationIds.Reverse().ToArray()),
        };
        ExpectInvalid(
            () => Qa04CanonicalOperationPartitionCandidateBinderV1.Bind(
                basisState,
                reversedBindings,
                reversedReceipt,
                references),
            "qa04.full-step.partition-candidate-order-not-canonical");

        ExpectInvalid(
            () => Qa04CanonicalOperationPartitionCandidateBinderV1.Bind(
                basisState,
                bindings,
                mutation,
                new RegistryResolver(residentRef)),
            expectedMessage: null);
    }

    private static WorldStateV1 BuildBasisState(
        ulong basisStep,
        Qa04CanonicalOperationMutationStateV1 initial,
        IDomainRecordSchemaResolverV1 references)
    {
        var template = Qa04ReferenceWorldMaterializerV1.MaterializeResidentIdentityLifecycle(1).WorldState;
        var replacements = new Dictionary<string, PartitionStateHeaderV1>(StringComparer.Ordinal)
        {
            [InfrastructureServiceQueuePayloadV1.PartitionId] = PartitionStateHeaderV1.CreateCanonical(
                initial.InfrastructureServiceQueue, 1, basisStep, DetailLevelV1.D0Entity,
                payload => StandardDomainPayloadCanonicalDigestV1.Compute(
                    InfrastructureServiceQueuePayloadV1.PartitionId,
                    payload.ToStandardPayload(),
                    references: references)),
            [ParticipationControlModePayloadV1.PartitionId] = PartitionStateHeaderV1.CreateCanonical(
                initial.ParticipationControlMode, 1, basisStep, DetailLevelV1.D0Entity,
                payload => StandardDomainPayloadCanonicalDigestV1.Compute(
                    ParticipationControlModePayloadV1.PartitionId,
                    payload.ToStandardPayload(),
                    references: references)),
            [ResidentBehaviorStatePayloadV1.PartitionId] = PartitionStateHeaderV1.CreateCanonical(
                initial.ResidentBehaviorState, 1, basisStep, DetailLevelV1.D0Entity,
                payload => StandardDomainPayloadCanonicalDigestV1.Compute(
                    ResidentBehaviorStatePayloadV1.PartitionId,
                    payload.ToStandardPayload(),
                    references: references)),
            [PhysicalPresencePayloadV1.PartitionId] = PartitionStateHeaderV1.CreateCanonical(
                initial.PhysicalPresence, 1, basisStep, DetailLevelV1.D0Entity,
                payload => StandardDomainPayloadCanonicalDigestV1.Compute(
                    PhysicalPresencePayloadV1.PartitionId,
                    payload.ToStandardPayload(),
                    references: references)),
            [SocietyMarketTransactionRecordSchemaV2.PartitionId] = PartitionStateHeaderV1.CreateCanonical(
                initial.MarketTransaction.State, 1, basisStep, DetailLevelV1.D2RegionalAggregate,
                payload => SocietyMarketTransactionPayloadCanonicalDigestV2.Compute(payload, references)),
            [GovernanceSecurityIncidentPayloadV1.PartitionId] = PartitionStateHeaderV1.CreateCanonical(
                initial.GovernanceSecurityIncident, 1, basisStep, DetailLevelV1.D0Entity,
                payload => StandardDomainPayloadCanonicalDigestV1.Compute(
                    GovernanceSecurityIncidentPayloadV1.PartitionId,
                    payload.ToStandardPayload(),
                    references: references)),
            [EnvironmentHazardPayloadV1.PartitionId] = PartitionStateHeaderV1.CreateCanonical(
                initial.EnvironmentHazard, 1, basisStep, DetailLevelV1.D0Entity,
                payload => StandardDomainPayloadCanonicalDigestV1.Compute(
                    EnvironmentHazardPayloadV1.PartitionId,
                    payload.ToStandardPayload(),
                    references: references)),
        };

        var partitions = template.Partitions.CanonicalEntries
            .Select(entry => replacements.TryGetValue(entry.Header.PartitionId.Value, out var replacement)
                ? new PartitionStateRefV1(replacement)
                : entry)
            .ToArray();
        var header = new WorldStateHeaderV1(
            template.Header.WorldId,
            basisStep,
            template.Header.WorldSeedDigest,
            template.Header.ConfigGeneration,
            template.Header.MasterGeneration,
            template.Header.RateGeneration);
        return new WorldStateV1(
            header,
            new OrderedPartitionDirectoryV1(partitions),
            template.SchedulerState,
            template.OperationState,
            template.DetailState,
            template.DomainRegistryState,
            template.Diagnostic.ConfigDigest);
    }

    private static byte[] ComputeQa04ChangeSetDigest(
        PartitionStateHeaderV1 basisHeader,
        PartitionStateHeaderV1 resultingHeader,
        ulong basisStep,
        ulong targetStep,
        IReadOnlyList<OpaqueId128> operationIds)
        => HashSuite.DomainHash("mv.state-diagnostic.v1", writer =>
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

    private static string FamilyForPartition(string partitionId)
        => partitionId switch
        {
            InfrastructureServiceQueuePayloadV1.PartitionId => InfrastructureFamily,
            ResidentBehaviorStatePayloadV1.PartitionId => ResidentFamily,
            PhysicalPresencePayloadV1.PartitionId => PhysicalFamily,
            SocietyMarketTransactionRecordSchemaV2.PartitionId => MarketFamily,
            GovernanceSecurityIncidentPayloadV1.PartitionId => GovernanceFamily,
            EnvironmentHazardPayloadV1.PartitionId => EnvironmentFamily,
            _ => throw new InvalidDataException($"Unknown Gate2 partition in smoke: {partitionId}"),
        };

    private static WorldStateV1 AdvanceBasisState(
        WorldStateV1 basisState,
        Qa04CanonicalOperationPartitionCandidateBatchV1 bound)
    {
        var replacements = bound.Partitions.ToDictionary(
            static item => item.Candidate.PartitionId.Value,
            static item => item.Material.ResultingHeader,
            StringComparer.Ordinal);
        var partitions = basisState.Partitions.CanonicalEntries
            .Select(entry => replacements.TryGetValue(entry.Header.PartitionId.Value, out var replacement)
                ? new PartitionStateRefV1(replacement)
                : entry)
            .ToArray();
        var header = new WorldStateHeaderV1(
            basisState.Header.WorldId,
            bound.TargetStep,
            basisState.Header.WorldSeedDigest,
            basisState.Header.ConfigGeneration,
            basisState.Header.MasterGeneration,
            basisState.Header.RateGeneration,
            basisState.Diagnostic.StateDigest);
        return new WorldStateV1(
            header,
            new OrderedPartitionDirectoryV1(partitions),
            basisState.SchedulerState,
            basisState.OperationState,
            basisState.DetailState,
            basisState.DomainRegistryState,
            basisState.Diagnostic.ConfigDigest);
    }

    private static void RequireSamePartitionDigests(
        Qa04CanonicalOperationPartitionCandidateBatchV1 expected,
        Qa04CanonicalOperationPartitionCandidateBatchV1 actual,
        string message)
    {
        Require(expected.Partitions.Count == actual.Partitions.Count, message + " count");
        for (var index = 0; index < expected.Partitions.Count; index++)
        {
            Require(
                expected.Partitions[index].Candidate.PartitionId == actual.Partitions[index].Candidate.PartitionId &&
                expected.Partitions[index].Material.ResultingHeader.CanonicalDigest.AsSpan().SequenceEqual(
                    actual.Partitions[index].Material.ResultingHeader.CanonicalDigest),
                message + $" index={index}");
        }
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

    private static void ExpectInvalid(Action action, string? expectedMessage)
    {
        try
        {
            action();
            throw new InvalidOperationException("Expected Gate2 partition candidate binding to reject invalid input.");
        }
        catch (InvalidDataException ex)
        {
            if (expectedMessage is not null && ex.Message != expectedMessage)
                throw new InvalidOperationException($"Unexpected Gate2 partition-candidate rejection: {ex.Message}");
        }
    }

    private static void ExpectReflectionInvalid(Action action, string expectedMessage)
    {
        try { action(); }
        catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is InvalidDataException failure &&
            failure.Message == expectedMessage) { return; }
        throw new InvalidOperationException($"Expected reflection target to reject {expectedMessage}.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class RegistryResolver(PartitionRecordRefV1? denied = null) : IDomainRecordSchemaResolverV1
    {
        public bool Exists(PartitionRecordRefV1 reference)
            => denied != reference && !reference.RecordId.IsZero && TryResolve(reference, out _);

        public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
        {
            if (denied == reference)
            {
                schema = default!;
                return false;
            }
            return TryResolve(reference, out schema);
        }

        private static bool TryResolve(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
        {
            schema = default!;
            if (reference.RecordId.IsZero) return false;
            try
            {
                schema = reference.PartitionId.Value == SocietyMarketTransactionRecordSchemaV2.PartitionId
                    ? SocietyMarketTransactionRecordSchemaV2.RecordSchema
                    : StandardDomainPartitionRegistry.Get(reference.PartitionId.Value).RecordSchema;
                return true;
            }
            catch (KeyNotFoundException)
            {
                return false;
            }
        }
    }
}
