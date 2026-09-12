using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.Persistence;

public sealed partial class SqlitePersistenceStore
{
    /// <summary>
    /// Returns the complete durable Operation authority ordered by OperationId. This is used when
    /// deriving the WorldState core.operation-state digest; callers must not substitute a cache or
    /// admission-side approximation for the committed SQLite authority.
    /// </summary>
    public async Task<IReadOnlyList<DurableOperationStateV1>> ListOperationStatesCanonicalAsync(
        CancellationToken cancellationToken = default)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = """
SELECT operation_id, payload_digest, lifecycle,
       accepted_sequence, scheduled_sequence, effective_step,
       terminal_sequence, terminal_status, result_code, rich_result_payload
FROM operation_state
ORDER BY operation_id ASC;
""";

        var result = new List<DurableOperationStateV1>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(ReadDurableOperationState(reader));

        var ordered = result.OrderBy(static item => item.OperationId).ToArray();
        if (ordered.Select(static item => item.OperationId).Distinct().Count() != ordered.Length)
            throw new InvalidDataException("persistence.operation-state-catalog-duplicate-id");
        return Array.AsReadOnly(ordered);
    }
}
