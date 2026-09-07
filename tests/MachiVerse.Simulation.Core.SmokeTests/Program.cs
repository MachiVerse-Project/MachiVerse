using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Configuration;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var worldId = OpaqueId128.Parse("00000000000000000000000000000001");
var creatorId = OpaqueId128.Parse("00000000000000000000000000000002");
var domain = new StableToken("sim.resident");
var kind = new StableToken("resident.birth");
var entityA = DerivedIdentity.DeriveEntityId(worldId, 42, domain, creatorId, kind, 0);
var entityB = DerivedIdentity.DeriveEntityId(worldId, 42, domain, creatorId, kind, 0);
Require(entityA == entityB && !entityA.IsZero, "EntityId derivation must be stable and non-zero.");

var seed = new WorldSeed256(new byte[32]);
var context = new RandomContextV1(worldId, 42, domain, new StableToken("birth-trait"), entityA, OpaqueId128.Zero, OpaqueId128.Zero, 0);
var randomA = DeterministicRandom.RandomWord64(seed, context, 0);
var randomB = DeterministicRandom.RandomWord64(seed, context, 0);
Require(randomA == randomB, "RandomWord64 must be addressable and stable.");
Require(DeterministicRandom.BoundedUInt64(seed, context, 1, 7) < 7, "Bounded random result out of range.");

var results = await DeterministicBatchExecutor.RunAsync(new[] { 4, 3, 2, 1 }, 4, static (value, _) => ValueTask.FromResult(value * value));
Require(results.SequenceEqual(new[] { 16, 9, 4, 1 }), "Worker completion must not reorder semantic output slots.");

var scopeDigest = Enumerable.Range(0, 32).Select(static value => (byte)value).ToArray();
var intentA = OpaqueId128.Parse("00000000000000000000000000000010");
var intentB = OpaqueId128.Parse("00000000000000000000000000000011");
var orderA = new SameStepOrderKey(1, 2, scopeDigest, -1, intentA);
var orderB = new SameStepOrderKey(1, 2, scopeDigest, 0, intentB);
Require(orderA.CompareTo(orderB) < 0, "SameStepOrderKey must use signed semantic priority ascending.");
Require(orderA.ToDatabaseBytes().Length == SameStepOrderKey.DatabaseKeyLength, "SameStepOrderKey DB encoding must be 55 bytes.");
Require(orderA.ToDatabaseBytes().AsSpan().SequenceCompareTo(orderB.ToDatabaseBytes()) < 0, "DB byte order must match logical SameStepOrderKey order.");

var half = FixedQ32_32.FromRatio(1, 2);
Require(half.Raw == 1L << 31, "FixedQ32_32 half encoding mismatch.");
Require((half * FixedQ32_32.FromInteger(2)) == FixedQ32_32.One, "FixedQ32_32 multiplication mismatch.");
Require(FixedQ32_32.FromRatio(5, 2).RoundToInteger() == 2, "Round-to-even must round 2.5 to 2.");
Require(FixedQ32_32.FromRatio(7, 2).RoundToInteger() == 4, "Round-to-even must round 3.5 to 4.");

var coordinator = new CoreConfigCoordinator();
var initial = coordinator.LoadStartup("""
[meta]
format = "machiverse-config"
schema_version = "1.0"
component = "simulation-core"

[simulation.step-rate]
numerator = 60
denominator = 2
""");
Require(initial.Generation == 1, "Initial ConfigGeneration must be 1.");
Require(initial.Get<long>("simulation.step-rate.numerator") == 30, "Step rate numerator must be reduced.");
Require(initial.Get<long>("simulation.step-rate.denominator") == 1, "Step rate denominator must be reduced.");
Require(initial.Get<long>("runtime.worker-count") == 4, "Missing fields must receive schema defaults.");
Require(initial.Digest.Length == 32, "ConfigDigest must be SHA-256 length.");

var operational = coordinator.ValidateRuntimeChange(
    new ConfigChangeSet(initial.Generation, [new ConfigChange("runtime.worker-count", 8L)], null),
    minimumNextApplicableStep: 100);
Require(!operational.ContainsSimulationImpact && !operational.IsNoChange, "Worker count must be an operational runtime change.");
var appliedOperational = coordinator.ApplyAtBoundary(operational);
Require(appliedOperational.Generation == 2 && appliedOperational.Get<long>("runtime.worker-count") == 8, "Operational change must atomically advance generation.");

var simulationRejected = false;
try
{
    coordinator.ValidateRuntimeChange(
        new ConfigChangeSet(appliedOperational.Generation, [new ConfigChange("scheduling.min-lead-steps", 3L)], null),
        minimumNextApplicableStep: 100);
}
catch (InvalidDataException ex) when (ex.Message == "config.effective-step-required")
{
    simulationRejected = true;
}
Require(simulationRejected, "Simulation-impact runtime change must require effective Step.");

var simulation = coordinator.ValidateRuntimeChange(
    new ConfigChangeSet(appliedOperational.Generation, [new ConfigChange("scheduling.min-lead-steps", 3L)], 100),
    minimumNextApplicableStep: 100);
Require(simulation.ContainsSimulationImpact && simulation.Candidate.Generation == 3, "Simulation change candidate generation mismatch.");

var unknownRejected = false;
try
{
    new CoreConfigCoordinator().LoadStartup("""
[meta]
format = "machiverse-config"
schema_version = "1.0"
component = "simulation-core"
unknown = 1
""");
}
catch (InvalidDataException)
{
    unknownRejected = true;
}
Require(unknownRejected, "Unknown Config fields must be rejected.");

Require(U64Be.Decode(U64Be.Encode(0)) == 0, "U64BE zero round-trip failed.");
Require(U64Be.Decode(U64Be.Encode(ulong.MaxValue)) == ulong.MaxValue, "U64BE max round-trip failed.");
Require(U64Be.Encode(1).AsSpan().SequenceCompareTo(U64Be.Encode(2)) < 0, "U64BE byte ordering must match unsigned ordering.");

var persistenceRoot = Path.Combine(Path.GetTempPath(), "machiverse-sim03-" + Guid.NewGuid().ToString("N"));
try
{
    var paths = PersistenceLayout.Resolve(persistenceRoot, worldId, 1);
    PersistenceLayout.EnsureGenerationDirectories(paths);
    Require(Path.GetFileName(paths.GenerationDirectory) == "0000000000000001", "PersistenceGeneration directory encoding mismatch.");

    await PersistenceLayout.WriteCurrentAsync(paths, 1);
    Require(new FileInfo(paths.CurrentPath).Length == 17, "CURRENT must be exactly 17 bytes.");
    Require(PersistenceLayout.ReadCurrent(paths) == 1, "CURRENT generation round-trip failed.");

    await using var store = await SqlitePersistenceStore.OpenOrCreateAsync(paths);
    var pragmas = await store.ReadRequiredPragmasAsync();
    Require(string.Equals(pragmas.JournalMode, "wal", StringComparison.OrdinalIgnoreCase), "SQLite journal_mode must be WAL.");
    Require(pragmas.Synchronous == 2, "SQLite synchronous must be FULL.");
    Require(pragmas.ForeignKeys == 1, "SQLite foreign_keys must be ON.");
    Require(pragmas.WalAutoCheckpoint == 0, "SQLite wal_autocheckpoint must be disabled.");
    Require(pragmas.BusyTimeout == 5000, "SQLite busy_timeout must be 5000ms.");
    await store.ValidateQuickCheckAsync();

    foreach (var table in new[]
    {
        "persistence_meta",
        "history_record",
        "operation_state",
        "scheduled_operation",
        "simulation_config_state",
        "core_operational_state",
        "snapshot_catalog"
    })
    {
        Require(await store.HasTableAsync(table), $"SIM-03 schema table missing: {table}");
    }

    var zeroDigest = new byte[32];
    var genesisRecord = HistoryRecordMaterial.Create(
        worldId,
        sequence: 1,
        previousRecordDigest: zeroDigest,
        recordType: "world.genesis.v1",
        payloadSchemaId: "core.world-genesis.v1",
        payloadSchemaMajor: 1,
        payloadSchemaMinor: 0,
        payloadBytes: [0, 1, 2, 3],
        writeNormalizedPayload: writer =>
        {
            writer.WriteMapStart(6);
            writer.WriteUnsigned(0); writer.WriteBytes(worldId.ToBytes());
            writer.WriteUnsigned(1); writer.WriteBytes(seed.ToBytes());
            writer.WriteUnsigned(2); writer.WriteUnsigned(0);
            writer.WriteUnsigned(3); writer.WriteUnsigned(1);
            writer.WriteUnsigned(4); writer.WriteBytes(initial.Digest);
            writer.WriteUnsigned(5); writer.WriteUnsigned(1);
        });
    var initialContinuity = HistoryIntegrity.ComputeGenesisContinuityToken(worldId, genesisRecord.RecordDigest);

    await store.InitializeWorldMetadataAsync(
        new WorldPersistenceMetadataSeed(
            worldId,
            PersistenceGeneration: 1,
            seed,
            initialContinuity,
            ConfigGeneration: 1,
            initial.Digest,
            MasterGeneration: 1),
        genesisRecord);

    var initialAnchor = await store.ReadHistoryAnchorAsync();
    Require(initialAnchor.Sequence == 1 && initialAnchor.Digest.SequenceEqual(genesisRecord.RecordDigest),
        "Genesis metadata/history must commit atomically at sequence 1.");
    Require(await store.HistoryAnchorExistsAsync(1, genesisRecord.RecordDigest), "Genesis history anchor must be queryable for recovery.");

    var operationId = OpaqueId128.Parse("00000000000000000000000000000020");
    var operationDigest = SHA256.HashData("operation-payload"u8);
    var acceptedRecord = HistoryRecordMaterial.Create(
        worldId,
        sequence: 2,
        previousRecordDigest: initialAnchor.Digest,
        recordType: "operation.accepted.v1",
        payloadSchemaId: "core.operation-accepted.v1",
        payloadSchemaMajor: 1,
        payloadSchemaMinor: 0,
        payloadBytes: [1, 2, 3, 4],
        writeNormalizedPayload: writer =>
        {
            writer.WriteMapStart(2);
            writer.WriteUnsigned(0); writer.WriteBytes(operationId.ToBytes());
            writer.WriteUnsigned(1); writer.WriteBytes(operationDigest);
        });

    var accepted = await store.PersistAcceptedOperationAsync(operationId, operationDigest, acceptedRecord);
    Require(accepted.Status == DurableAcceptanceStatus.Accepted && accepted.AcceptedSequence == 2, "Durable Operation acceptance failed.");
    var anchorAfterAccept = await store.ReadHistoryAnchorAsync();
    Require(anchorAfterAccept.Sequence == 2 && anchorAfterAccept.Digest.SequenceEqual(acceptedRecord.RecordDigest),
        "History anchor must advance atomically with Operation acceptance.");

    var duplicate = await store.PersistAcceptedOperationAsync(operationId, operationDigest, acceptedRecord);
    Require(duplicate.Status == DurableAcceptanceStatus.Duplicate && duplicate.AcceptedSequence == 2,
        "Same OperationId/digest must resolve as duplicate without new history.");
    Require((await store.ReadHistoryAnchorAsync()).Sequence == 2, "Duplicate acceptance must not append history.");

    var mismatchRejected = false;
    try
    {
        await store.PersistAcceptedOperationAsync(operationId, SHA256.HashData("different-payload"u8), acceptedRecord);
    }
    catch (InvalidDataException ex) when (ex.Message == "protocol.operation-payload-mismatch")
    {
        mismatchRejected = true;
    }
    Require(mismatchRejected, "Same OperationId with different digest must be rejected.");

    var badChainRejected = false;
    try
    {
        var secondOperation = OpaqueId128.Parse("00000000000000000000000000000021");
        var secondDigest = SHA256.HashData("operation-2"u8);
        var badRecord = HistoryRecordMaterial.Create(
            worldId,
            sequence: 3,
            previousRecordDigest: zeroDigest,
            recordType: "operation.accepted.v1",
            payloadSchemaId: "core.operation-accepted.v1",
            payloadSchemaMajor: 1,
            payloadSchemaMinor: 0,
            payloadBytes: [4, 3, 2, 1],
            writeNormalizedPayload: writer =>
            {
                writer.WriteMapStart(2);
                writer.WriteUnsigned(0); writer.WriteBytes(secondOperation.ToBytes());
                writer.WriteUnsigned(1); writer.WriteBytes(secondDigest);
            });
        await store.PersistAcceptedOperationAsync(secondOperation, secondDigest, badRecord);
    }
    catch (InvalidDataException ex) when (ex.Message == "persistence.history-previous-digest-mismatch")
    {
        badChainRejected = true;
    }
    Require(badChainRejected, "Broken history predecessor must reject the whole durable acceptance transaction.");
    Require((await store.ReadHistoryAnchorAsync()).Sequence == 2, "Rejected acceptance must not advance history anchor.");

    await Sim03DurabilitySmoke.RunAsync(store, worldId, operationId, initial.Digest);
    await Sim03SnapshotCommitSmoke.RunAsync(store, paths, worldId);
}
finally
{
    if (Directory.Exists(persistenceRoot)) Directory.Delete(persistenceRoot, recursive: true);
}

SnapshotManifestSmoke.Run();
await PersistenceSnapshotSmoke.RunAsync();
await PersistenceMigrationSmoke.RunAsync();
await PortableWorldExportSmoke.RunAsync();
Sim04WorldStateSmoke.Run();
Sim04DiagnosticSmoke.Run();
Sim05OperationLifecycleSmoke.Run();
await Sim05DurableOperationSmoke.RunAsync();
await Sim05CoordinatorSmoke.RunAsync();
await Sim06StepCoordinatorSmoke.RunAsync();
await Sim06ConfigBoundarySmoke.RunAsync();
await Sim06DependencyDagSmoke.RunAsync();
await Sim06DurableFinalizeSmoke.RunAsync();
await Sim06DurableFinalizationSmoke.RunAsync();
Sim07SpatialSmoke.Run();
Sim07EnvironmentSmoke.Run();
Sim08MotionPathSmoke.Run();
Sim08CollisionSmoke.Run();
Sim08CrossOwnerIntentSmoke.Run();
Sim08HandoffSmoke.Run();
Sim08PrimitiveCollisionSmoke.Run();
Sim08SequentialImpulseSmoke.Run();
await Sim08StateRuntimeSmoke.RunAsync();
Sim09ResidentCoreSmoke.Run();

Console.WriteLine("SIM-01..SIM-08 component smoke tests passed; SIM-09 Resident deterministic core foundation smoke passed.");
