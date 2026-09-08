using System.Security.Cryptography;

namespace MachiVerse.Simulation.Core.Persistence;

public sealed record RecoveryHistoryIntegrity(
    ulong FirstSequence,
    ulong LastSequence,
    byte[] LastRecordDigest,
    int RecordCount);

public sealed partial class SqlitePersistenceStore
{
    public async Task<RecoveryHistoryIntegrity> ValidateHistoryLinkChainAsync(
        IReadOnlySet<string> registeredAuthoritativeRecordTypes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registeredAuthoritativeRecordTypes);
        if (registeredAuthoritativeRecordTypes.Count == 0)
            throw new ArgumentException("At least one authoritative history record type must be registered.", nameof(registeredAuthoritativeRecordTypes));

        await using var command = _connection.CreateCommand();
        command.CommandText = """
SELECT sequence, previous_record_digest, record_type, record_digest
FROM history_record
ORDER BY sequence ASC;
""";

        ulong expectedSequence = 1;
        var expectedPreviousDigest = new byte[32];
        var count = 0;
        byte[]? lastDigest = null;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var sequence = U64Be.Decode((byte[])reader[0]);
            var previousDigest = (byte[])reader[1];
            var recordType = reader.GetString(2);
            var recordDigest = (byte[])reader[3];
            if (previousDigest.Length != 32 || recordDigest.Length != 32)
                throw new InvalidDataException("persistence.history-invalid-digest-width");
            if (sequence != expectedSequence)
                throw new InvalidDataException("persistence.history-sequence-gap");
            if (!CryptographicOperations.FixedTimeEquals(previousDigest, expectedPreviousDigest))
                throw new InvalidDataException("persistence.history-link-mismatch");
            if (!registeredAuthoritativeRecordTypes.Contains(recordType))
                throw new InvalidDataException($"persistence.history-unknown-authoritative-type:{recordType}");

            lastDigest = recordDigest;
            expectedPreviousDigest = recordDigest;
            count++;
            if (expectedSequence == ulong.MaxValue)
                throw new OverflowException("HistorySequence cannot advance beyond uint64 max.");
            expectedSequence++;
        }

        if (count == 0 || lastDigest is null)
            throw new InvalidDataException("persistence.history-empty");

        var metadataAnchor = await ReadHistoryAnchorAsync(cancellationToken);
        var lastSequence = expectedSequence - 1;
        if (metadataAnchor.Sequence != lastSequence ||
            !CryptographicOperations.FixedTimeEquals(metadataAnchor.Digest, lastDigest))
            throw new InvalidDataException("persistence.history-metadata-head-mismatch");

        return new RecoveryHistoryIntegrity(1, lastSequence, lastDigest, count);
    }

    public async Task<SnapshotCatalogEntry?> SelectRecoverySnapshotAsync(
        WorldPersistencePaths paths,
        Func<string, SnapshotCatalogEntry, CancellationToken, Task> validateSnapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(validateSnapshot);
        await ValidateQuickCheckAsync(cancellationToken);

        var candidates = await ListSnapshotCandidatesNewestFirstAsync(cancellationToken);
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await HistoryAnchorExistsAsync(candidate.HistoryAnchorSequence, candidate.HistoryAnchorDigest, cancellationToken))
                continue;

            var directory = Path.GetFullPath(Path.Combine(paths.GenerationDirectory, candidate.RelativeDirectory.Replace('/', Path.DirectorySeparatorChar)));
            var generationRoot = Path.GetFullPath(paths.GenerationDirectory) + Path.DirectorySeparatorChar;
            if (!directory.StartsWith(generationRoot, StringComparison.Ordinal) || !Directory.Exists(directory))
                continue;

            try
            {
                await validateSnapshot(directory, candidate, cancellationToken);
                return candidate;
            }
            catch (InvalidDataException)
            {
                // A cataloged but unusable candidate is skipped. The caller must only accept an
                // older candidate when its history anchor can be replayed through the validated
                // durable history head; link-chain validation is a separate required startup gate.
            }
        }

        return null;
    }
}
