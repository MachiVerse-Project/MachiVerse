using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;

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
            if (snapshot.SnapshotStep != transitionHead.FinalizedStep)
                throw new InvalidDataException("persistence.snapshot-step-not-finalized-head");
            if (!CryptographicOperations.FixedTimeEquals(snapshot.StateContinuityToken, transitionHead.StateContinuityToken))
                throw new InvalidDataException("persistence.snapshot-continuity-mismatch");

            var context = await ReadHistoryContextAsync(transaction, cancellationToken);
            if (snapshot.HistoryAnchor.Sequence != context.Anchor.Sequence ||
                !CryptographicOperations.FixedTimeEquals(snapshot.HistoryAnchor.Digest, context.Anchor.Digest))
                throw new InvalidDataException("persistence.snapshot-history-anchor-not-current");

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
}
