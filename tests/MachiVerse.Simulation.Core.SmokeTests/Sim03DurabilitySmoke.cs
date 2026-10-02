using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;

public static class Sim03DurabilitySmoke
{
    public static async Task RunAsync(
        SqlitePersistenceStore store,
        OpaqueId128 worldId,
        OpaqueId128 operationId,
        byte[] configDigest)
    {
        var anchor = await store.ReadHistoryAnchorAsync();
        if (anchor.Sequence != 2) throw new InvalidOperationException("SIM-03 scheduling smoke requires accepted history sequence 2.");

        var orderKey = new SameStepOrderKey(
            phase: 1,
            domainRank: 1,
            conflictScopeDigest: SHA256.HashData("sim03-scope"u8),
            semanticPriority: 0,
            intentId: operationId);
        var orderKeyBytes = orderKey.ToDatabaseBytes();

        var scheduledRecord = HistoryRecordMaterial.Create(
            worldId,
            sequence: 3,
            previousRecordDigest: anchor.Digest,
            recordType: "operation.scheduled.v1",
            payloadSchemaId: "core.operation-scheduled.v1",
            payloadSchemaMajor: 1,
            payloadSchemaMinor: 0,
            payloadBytes: [5, 6, 7],
            writeNormalizedPayload: writer =>
            {
                writer.WriteMapStart(3);
                writer.WriteUnsigned(0); writer.WriteBytes(operationId.ToBytes());
                writer.WriteUnsigned(1); writer.WriteUnsigned(0);
                writer.WriteUnsigned(2); writer.WriteBytes(orderKeyBytes);
            });

        var scheduled = await store.PersistScheduledOperationAsync(operationId, 0, orderKey, scheduledRecord);
        if (scheduled.Status != DurableSchedulingStatus.Scheduled || scheduled.ScheduledSequence != 3 || scheduled.EffectiveStep != 0)
            throw new InvalidOperationException("Scheduling durability transaction failed.");

        var duplicateSchedule = await store.PersistScheduledOperationAsync(operationId, 0, orderKey, scheduledRecord);
        if (duplicateSchedule.Status != DurableSchedulingStatus.Duplicate || duplicateSchedule.ScheduledSequence != 3)
            throw new InvalidOperationException("Duplicate scheduling must converge without appending history.");
        if ((await store.ReadHistoryAnchorAsync()).Sequence != 3)
            throw new InvalidOperationException("Duplicate scheduling must not advance history.");

        var beforeTransition = await store.ReadRecoveryHeadAsync();
        var transitionRecord = HistoryRecordMaterial.Create(
            worldId,
            sequence: 4,
            previousRecordDigest: scheduledRecord.RecordDigest,
            recordType: "transition.committed.v1",
            payloadSchemaId: "core.transition-committed.v1",
            payloadSchemaMajor: 1,
            payloadSchemaMinor: 0,
            payloadBytes: [8, 9, 10],
            writeNormalizedPayload: writer =>
            {
                writer.WriteMapStart(6);
                writer.WriteUnsigned(0); writer.WriteUnsigned(0);
                writer.WriteUnsigned(1); writer.WriteUnsigned(1);
                writer.WriteUnsigned(2); writer.WriteUnsigned(1);
                writer.WriteUnsigned(3); writer.WriteBytes(configDigest);
                writer.WriteUnsigned(4); writer.WriteBytes(beforeTransition.ContinuityToken);
                writer.WriteUnsigned(5); writer.WriteBytes(operationId.ToBytes());
            });
        var resultingContinuity = HistoryIntegrity.ComputeTransitionContinuityToken(
            worldId,
            resultingStep: 1,
            beforeTransition.ContinuityToken,
            transitionRecord.RecordDigest);

        var transactionKind = CrossDomainTransactionKindRegistryV1.Get("transaction.birth");
        var resident = StandardDomainExecutionPlanV1.Create().Entries.Single(entry => entry.DomainToken.Value == "resident");
        var transactionParticipant = new PersistentTransactionParticipantV1(
            resident.DomainToken,
            resident.OwnedPartitions[0],
            [OpaqueId128.Parse("00000000000000000000000000000041")],
            required: true,
            TransactionParticipantOutcomeV1.Ready,
            SHA256.HashData("sim03-cross-domain-effect"u8));
        var transactionInvariant = new InvariantResultV1(
            CrossDomainTransactionInvariantRegistryV1.GetRequiredInvariantIds(transactionKind).Single(),
            InvariantSeverityV1.CommitBlocking,
            InvariantOutcomeV1.Pass,
            Array.Empty<CausalityRefV1>(),
            null);
        var transactionState = new CrossDomainTransactionStateV1(
            OpaqueId128.Parse("00000000000000000000000000000040"),
            transactionKind,
            TransactionLifecycleV1.Active,
            createdStep: 1,
            updatedStep: 1,
            terminalStep: null,
            new CausalityRefV1(CausalityRefKindV1.Operation, operationId.ToBytes(), 0),
            [OpaqueId128.Parse("00000000000000000000000000000042")],
            [transactionParticipant],
            [transactionInvariant]);
        var transactionWire = new byte[] { 0x54, 0x58, 0x01 };

        var transition = await store.PersistTransitionCommitWithCrossDomainTransactionsAsync(
            effectiveStep: 0,
            resultingStep: 1,
            resultingStateContinuityToken: resultingContinuity,
            activeConfigGeneration: 1,
            activeConfigDigest: configDigest,
            history: transitionRecord,
            terminalOperations:
            [
                new TerminalOperationCommit(
                    operationId,
                    TerminalStatus: 1,
                    ResultCode: "operation.ok",
                    RichResultPayload: [11, 12])
            ],
            crossDomainTransactions:
            [
                new CrossDomainTransactionStateCommitV1(transactionState, transactionWire)
            ]);

        if (transition.ResultingStep != 1 || transition.HistorySequence != 4)
            throw new InvalidOperationException("Transition durability transaction result mismatch.");

        var durableTransaction = await store.ReadCrossDomainTransactionStateAsync(transactionState.TransactionId);
        if (durableTransaction is null || durableTransaction.Lifecycle != TransactionLifecycleV1.Active ||
            durableTransaction.CreatedStep != 1 || durableTransaction.UpdatedStep != 1 ||
            durableTransaction.TerminalStep is not null || !durableTransaction.StateWire.SequenceEqual(transactionWire) ||
            !durableTransaction.StateDigest.SequenceEqual(transactionState.CanonicalDigest()))
            throw new InvalidOperationException("CrossDomainTransaction authority must commit atomically with transition history.");

        var recoveryHead = await store.ReadRecoveryHeadAsync();
        if (recoveryHead.FinalizedStep != 1 || !recoveryHead.ContinuityToken.SequenceEqual(resultingContinuity))
            throw new InvalidOperationException("Recovery head must advance atomically with transition commit.");
        if (recoveryHead.ConfigGeneration != 1 || !recoveryHead.ConfigDigest.SequenceEqual(configDigest))
            throw new InvalidOperationException("Transition commit must persist active Config identity.");
        if ((await store.ReadHistoryAnchorAsync()).Sequence != 4)
            throw new InvalidOperationException("Transition commit must advance history atomically.");

        var staleTransitionRejected = false;
        try
        {
            await store.PersistTransitionCommitAsync(
                effectiveStep: 0,
                resultingStep: 1,
                resultingStateContinuityToken: resultingContinuity,
                activeConfigGeneration: 1,
                activeConfigDigest: configDigest,
                history: transitionRecord,
                terminalOperations: []);
        }
        catch (InvalidDataException ex) when (ex.Message == "persistence.transition-base-step-mismatch")
        {
            staleTransitionRejected = true;
        }

        if (!staleTransitionRejected)
            throw new InvalidOperationException("A transition from a stale finalized Step must be rejected.");
        if ((await store.ReadHistoryAnchorAsync()).Sequence != 4)
            throw new InvalidOperationException("Rejected transition must not advance history.");
    }
}
