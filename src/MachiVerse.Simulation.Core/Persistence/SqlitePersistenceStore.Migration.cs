using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.Persistence;

public sealed record PersistenceMigrationRecordInput(
    ulong SourcePersistenceGeneration,
    ulong TargetPersistenceGeneration,
    ushort SourceSchemaMajor,
    ushort SourceSchemaMinor,
    ushort TargetSchemaMajor,
    ushort TargetSchemaMinor,
    byte[] SourceTerminalHistoryDigest,
    byte[] TargetTerminalHistoryDigest,
    byte[] MigrationRecipeDigest,
    byte[] PayloadProtobufBytes);

public sealed partial class SqlitePersistenceStore
{
    public async Task<HistoryAnchor> PersistMigrationRecordAsync(
        PersistenceMigrationRecordInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.SourcePersistenceGeneration == 0 || input.TargetPersistenceGeneration == 0)
            throw new ArgumentOutOfRangeException(nameof(input), "PersistenceGeneration starts at 1.");
        if (input.SourcePersistenceGeneration == ulong.MaxValue || input.TargetPersistenceGeneration != input.SourcePersistenceGeneration + 1)
            throw new InvalidDataException("persistence.migration-generation-not-next");
        RequireHash256(input.SourceTerminalHistoryDigest, nameof(input.SourceTerminalHistoryDigest));
        RequireHash256(input.TargetTerminalHistoryDigest, nameof(input.TargetTerminalHistoryDigest));
        RequireHash256(input.MigrationRecipeDigest, nameof(input.MigrationRecipeDigest));
        if (input.PayloadProtobufBytes is null || input.PayloadProtobufBytes.Length == 0)
            throw new ArgumentException("Migration protobuf payload cannot be empty.", nameof(input));

        using var transaction = _connection.BeginTransaction();
        try
        {
            var context = await ReadHistoryContextAsync(transaction, cancellationToken);
            if (!context.Anchor.Digest.SequenceEqual(input.TargetTerminalHistoryDigest))
                throw new InvalidDataException("persistence.migration-target-history-digest-mismatch");
            if (context.Anchor.Sequence == ulong.MaxValue)
                throw new OverflowException("HistorySequence cannot wrap.");

            var history = HistoryRecordMaterial.Create(
                context.WorldId,
                checked(context.Anchor.Sequence + 1),
                context.Anchor.Digest,
                recordType: "persistence.migrated.v1",
                payloadSchemaId: "persistence.migrated",
                payloadSchemaMajor: 1,
                payloadSchemaMinor: 0,
                payloadBytes: input.PayloadProtobufBytes,
                writeNormalizedPayload: writer =>
                {
                    writer.WriteMapStart(9);
                    writer.WriteUnsigned(0); writer.WriteUnsigned(input.SourcePersistenceGeneration);
                    writer.WriteUnsigned(1); writer.WriteUnsigned(input.TargetPersistenceGeneration);
                    writer.WriteUnsigned(2); writer.WriteUnsigned(input.SourceSchemaMajor);
                    writer.WriteUnsigned(3); writer.WriteUnsigned(input.SourceSchemaMinor);
                    writer.WriteUnsigned(4); writer.WriteUnsigned(input.TargetSchemaMajor);
                    writer.WriteUnsigned(5); writer.WriteUnsigned(input.TargetSchemaMinor);
                    writer.WriteUnsigned(6); writer.WriteBytes(input.SourceTerminalHistoryDigest);
                    writer.WriteUnsigned(7); writer.WriteBytes(input.TargetTerminalHistoryDigest);
                    writer.WriteUnsigned(8); writer.WriteBytes(input.MigrationRecipeDigest);
                });

            await InsertHistoryRecordAsync(history, transaction, cancellationToken);
            await UpdateHistoryAnchorAsync(history, transaction, cancellationToken);
            transaction.Commit();
            return new HistoryAnchor(history.Sequence, history.RecordDigest);
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }
}
