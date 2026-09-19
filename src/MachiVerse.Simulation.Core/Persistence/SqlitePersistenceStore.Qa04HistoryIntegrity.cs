using System.Security.Cryptography;

namespace MachiVerse.Simulation.Core.Persistence;

public sealed partial class SqlitePersistenceStore
{
    public async Task<RecoveryHistoryIntegrity> ValidateHistoryHashChainStructureAsync(
        CancellationToken cancellationToken = default)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = """
SELECT sequence, previous_record_digest, record_digest
FROM history_record
ORDER BY sequence ASC;
""";

        ulong expectedSequence = 1;
        var expectedPreviousDigest = new byte[32];
        var count = 0;
        byte[]? lastDigest = null;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var sequence = U64Be.Decode((byte[])reader[0]);
            var previousDigest = (byte[])reader[1];
            var recordDigest = (byte[])reader[2];
            if (previousDigest.Length != 32 || recordDigest.Length != 32)
                throw new InvalidDataException("persistence.history-invalid-digest-width");
            if (sequence != expectedSequence)
                throw new InvalidDataException("persistence.history-sequence-gap");
            if (!CryptographicOperations.FixedTimeEquals(previousDigest, expectedPreviousDigest))
                throw new InvalidDataException("persistence.history-link-mismatch");

            lastDigest = recordDigest;
            expectedPreviousDigest = recordDigest;
            count++;
            if (expectedSequence == ulong.MaxValue)
                throw new OverflowException("HistorySequence cannot advance beyond uint64 max.");
            expectedSequence++;
        }

        if (count == 0 || lastDigest is null)
            throw new InvalidDataException("persistence.history-empty");

        var metadataAnchor = await ReadHistoryAnchorAsync(cancellationToken).ConfigureAwait(false);
        var lastSequence = expectedSequence - 1;
        if (metadataAnchor.Sequence != lastSequence ||
            !CryptographicOperations.FixedTimeEquals(metadataAnchor.Digest, lastDigest))
            throw new InvalidDataException("persistence.history-metadata-head-mismatch");

        return new RecoveryHistoryIntegrity(1, lastSequence, lastDigest, count);
    }
}
