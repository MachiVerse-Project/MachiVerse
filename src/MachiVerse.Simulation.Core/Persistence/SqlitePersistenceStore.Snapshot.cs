using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using Microsoft.Data.Sqlite;

namespace MachiVerse.Simulation.Core.Persistence;

public sealed record SnapshotCommitMaterial(
    OpaqueId128 SnapshotId,
    ulong SnapshotStep,
    HistoryAnchor HistoryAnchor,
    byte[] StateContinuityToken,
    byte[] SnapshotDigest,
    byte[] PhysicalManifestDigest,
    string RelativeDirectory);

public sealed record DurableSnapshotCommitResult(
    OpaqueId128 SnapshotId,
    ulong SnapshotStep,
    ulong HistorySequence);

public sealed partial class SqlitePersistenceStore
{
    public async Task<DurableSnapshotCommitResult> PersistSnapshotCommitAsync(
        SnapshotCommitMaterial snapshot,
        HistoryRecordMaterial history,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.SnapshotId.IsZero)
            throw new ArgumentException("SnapshotId ZERO is invalid.", nameof(snapshot));
        RequireHash256(snapshot.HistoryAnchor.Digest, "snapshot.history_anchor_digest");
        RequireHash256(snapshot.StateContinuityToken, nameof(snapshot.StateContinuityToken));
        RequireHash256(snapshot.SnapshotDigest, nameof(snapshot.SnapshotDigest));
        RequireHash256(snapshot.PhysicalManifestDigest, nameof(snapshot.PhysicalManifestDigest));
        ValidateSnapshotRelativeDirectory(snapshot.RelativeDirectory);
        ValidateHistoryMaterial(history, "snapshot.committed.v1");

        using var transaction = _connection.BeginTransaction();
        try
        {
            var transitionHead = await ReadTransitionHeadAsync(transaction, cancellationToken);
            if (snapshot.SnapshotStep > transitionHead.FinalizedStep)
                throw new InvalidDataException("persistence.snapshot-step-not-finalized");

            // If the frozen cut is still the current finalized head, bind its continuity token
            // directly to current persistence metadata. For a running snapshot whose background
            // drain finishes after later Steps commit, the cut's historical continuity remains
            // bound through its durable history anchor and the snapshot payload/manifest.
            if (snapshot.SnapshotStep == transitionHead.FinalizedStep &&
                !CryptographicOperations.FixedTimeEquals(snapshot.StateContinuityToken, transitionHead.StateContinuityToken))
                throw new InvalidDataException("persistence.snapshot-continuity-mismatch");

            var context = await ReadHistoryContextAsync(transaction, cancellationToken);
            if (!await SnapshotHistoryAnchorExistsAsync(snapshot.HistoryAnchor, transaction, cancellationToken))
                throw new InvalidDataException("persistence.snapshot-history-anchor-missing");
            if (snapshot.HistoryAnchor.Sequence > context.Anchor.Sequence)
                throw new InvalidDataException("persistence.snapshot-history-anchor-future");

            // snapshot.committed.v1 is a new fact at drain completion time, so it appends to the
            // current history head even when the snapshot itself points at an older frozen anchor.
            ValidateNextHistoryRecord(history, context);
            await InsertHistoryRecordAsync(history, transaction, cancellationToken);

            await using (var catalog = _connection.CreateCommand())
            {
                catalog.Transaction = transaction;
                catalog.CommandText = """
INSERT INTO snapshot_catalog (
  snapshot_id, snapshot_step, history_anchor_sequence, history_anchor_digest,
  state_continuity_token, snapshot_digest, physical_manifest_digest, relative_directory
) VALUES (
  $snapshot_id, $snapshot_step, $history_anchor_sequence, $history_anchor_digest,
  $state_continuity_token, $snapshot_digest, $physical_manifest_digest, $relative_directory
);
""";
                catalog.Parameters.AddWithValue("$snapshot_id", snapshot.SnapshotId.ToBytes());
                catalog.Parameters.AddWithValue("$snapshot_step", U64Be.Encode(snapshot.SnapshotStep));
                catalog.Parameters.AddWithValue("$history_anchor_sequence", U64Be.Encode(snapshot.HistoryAnchor.Sequence));
                catalog.Parameters.AddWithValue("$history_anchor_digest", snapshot.HistoryAnchor.Digest);
                catalog.Parameters.AddWithValue("$state_continuity_token", snapshot.StateContinuityToken);
                catalog.Parameters.AddWithValue("$snapshot_digest", snapshot.SnapshotDigest);
                catalog.Parameters.AddWithValue("$physical_manifest_digest", snapshot.PhysicalManifestDigest);
                catalog.Parameters.AddWithValue("$relative_directory", snapshot.RelativeDirectory);
                await catalog.ExecuteNonQueryAsync(cancellationToken);
            }

            await UpdateHistoryAnchorAsync(history, transaction, cancellationToken);
            transaction.Commit();
            return new DurableSnapshotCommitResult(snapshot.SnapshotId, snapshot.SnapshotStep, history.Sequence);
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private async Task<bool> SnapshotHistoryAnchorExistsAsync(
        HistoryAnchor anchor,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM history_record WHERE sequence=$sequence AND record_digest=$digest LIMIT 1;";
        command.Parameters.AddWithValue("$sequence", U64Be.Encode(anchor.Sequence));
        command.Parameters.AddWithValue("$digest", anchor.Digest);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }
}
