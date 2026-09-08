using System.Buffers.Binary;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace MachiVerse.Gateway.Audit;

public static class AuditStorageIntegrityV1
{
    public static async ValueTask ValidateRowsAsync(
        string databasePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT sequence, digest, previous_digest, kind, payload FROM audit_record ORDER BY sequence;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var sequenceBytes = (byte[])reader[0];
            if (sequenceBytes.Length != 8) throw new InvalidDataException("audit.storage-sequence-length");
            var sequence = BinaryPrimitives.ReadUInt64BigEndian(sequenceBytes);
            var digest = (byte[])reader[1];
            var previous = (byte[])reader[2];
            var kind = reader.GetString(3);
            var payload = (byte[])reader[4];
            var record = JsonSerializer.Deserialize<AuditRecordV1>(payload, json)
                ?? throw new InvalidDataException("audit.payload-invalid");
            AuditRecordCodecV1.ValidateDigest(record);

            if (sequence != record.AuditSequence ||
                !digest.AsSpan().SequenceEqual(record.RecordDigest) ||
                !previous.AsSpan().SequenceEqual(record.PreviousDigest) ||
                !string.Equals(kind, record.AuditKind, StringComparison.Ordinal))
                throw new InvalidDataException("audit.storage-row-mismatch");
        }
    }
}
