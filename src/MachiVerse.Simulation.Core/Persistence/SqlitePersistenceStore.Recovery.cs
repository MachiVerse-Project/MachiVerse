using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.Persistence;

public sealed record SnapshotCatalogEntry(
    OpaqueId128 SnapshotId,
    ulong SnapshotStep,
    ulong HistoryAnchorSequence,
    byte[] HistoryAnchorDigest,
    byte[] StateContinuityToken,
    byte[] SnapshotDigest,
    byte[] PhysicalManifestDigest,
    string RelativeDirectory);

public sealed partial class SqlitePersistenceStore
{
    public async Task ValidateQuickCheckAsync(CancellationToken cancellationToken = default)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = "PRAGMA quick_check;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var sawRow = false;
        while (await reader.ReadAsync(cancellationToken))
        {
            sawRow = true;
            var result = reader.GetString(0);
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"persistence.sqlite.quick-check-failed:{result}");
        }
        if (!sawRow)
            throw new InvalidDataException("persistence.sqlite.quick-check-empty");
    }

    public async Task<IReadOnlyList<SnapshotCatalogEntry>> ListSnapshotCandidatesNewestFirstAsync(
        CancellationToken cancellationToken = default)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = """
SELECT snapshot_id, snapshot_step, history_anchor_sequence, history_anchor_digest,
       state_continuity_token, snapshot_digest, physical_manifest_digest, relative_directory
FROM snapshot_catalog
ORDER BY snapshot_step DESC, snapshot_id ASC;
""";

        var result = new List<SnapshotCatalogEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var snapshotId = OpaqueId128.FromBytes((byte[])reader[0]);
            var snapshotStep = U64Be.Decode((byte[])reader[1]);
            var historySequence = U64Be.Decode((byte[])reader[2]);
            var historyDigest = RequireHash((byte[])reader[3], "history_anchor_digest");
            var continuityToken = RequireHash((byte[])reader[4], "state_continuity_token");
            var snapshotDigest = RequireHash((byte[])reader[5], "snapshot_digest");
            var physicalDigest = RequireHash((byte[])reader[6], "physical_manifest_digest");
            var relativeDirectory = reader.GetString(7);
            ValidateSnapshotRelativeDirectory(relativeDirectory);

            result.Add(new SnapshotCatalogEntry(
                snapshotId,
                snapshotStep,
                historySequence,
                historyDigest,
                continuityToken,
                snapshotDigest,
                physicalDigest,
                relativeDirectory));
        }
        return result;
    }

    public async Task<bool> HistoryAnchorExistsAsync(
        ulong sequence,
        ReadOnlyMemory<byte> digest,
        CancellationToken cancellationToken = default)
    {
        if (digest.Length != 32) throw new ArgumentException("History anchor digest must be 32 bytes.", nameof(digest));

        await using var command = _connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM history_record WHERE sequence=$sequence AND record_digest=$digest LIMIT 1;";
        command.Parameters.AddWithValue("$sequence", U64Be.Encode(sequence));
        command.Parameters.AddWithValue("$digest", digest.ToArray());
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static byte[] RequireHash(byte[] bytes, string field)
    {
        if (bytes.Length != 32) throw new InvalidDataException($"persistence.invalid-hash:{field}");
        return bytes;
    }

    private static void ValidateSnapshotRelativeDirectory(string relativeDirectory)
    {
        if (string.IsNullOrWhiteSpace(relativeDirectory) ||
            Path.IsPathRooted(relativeDirectory) ||
            relativeDirectory.Contains("..", StringComparison.Ordinal) ||
            relativeDirectory.Contains('\\'))
            throw new InvalidDataException("persistence.snapshot.invalid-relative-directory");
    }
}
