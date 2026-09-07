using System.Text.Json;
using MachiVerse.Gateway.Audit;
using MachiVerse.Gateway.Configuration;
using MachiVerse.Gateway.Observability;
using Microsoft.Data.Sqlite;

internal static class Gw07CardinalityAndChainSmoke
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Run()
    {
        VerifyMetricCardinalityGuard();
        VerifyChainCorruptionDetectionAsync().GetAwaiter().GetResult();
    }

    private static void VerifyMetricCardinalityGuard()
    {
        var guard = new GatewayMetricCardinalityGuardV1();
        GatewayMetricSeriesRegistrationV1 last = default;
        for (var index = 0; index < GatewayMetricCardinalityGuardV1.HardActiveSeriesLimit; index++)
        {
            last = guard.Register(
                "machiverse.gateway.authorization.denied",
                new Dictionary<string, string>
                {
                    ["permission_class"] = $"p{index:D5}",
                });
            Require(last.WasRegistered, "Metric series below the hard limit must register.");
        }

        Require(guard.ActiveSeriesCount == GatewayMetricCardinalityGuardV1.HardActiveSeriesLimit,
            "Metric cardinality guard must stop at the configured hard active-series bound.");
        Require(last.Decision == GatewayMetricSeriesDecisionV1.TargetExceeded,
            "Series above the standard target must surface an operational warning decision.");

        var rejected = guard.Register(
            "machiverse.gateway.authorization.denied",
            new Dictionary<string, string> { ["permission_class"] = "overflow-series" });
        Require(rejected.Decision == GatewayMetricSeriesDecisionV1.HardLimitRejected &&
                rejected.ActiveSeriesCount == GatewayMetricCardinalityGuardV1.HardActiveSeriesLimit,
            "The 10,001st active series must be rejected without changing world semantics.");
    }

    private static async Task VerifyChainCorruptionDetectionAsync()
    {
        var config = GatewayConfigLoader.LoadFile("config/gateway.toml");
        var temp = Path.Combine(Path.GetTempPath(), "machiverse-gw07-chain-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var componentId = Enumerable.Repeat((byte)0x2a, 16).ToArray();
            await using var store = new GatewayAuditStoreV1(temp, config.Audit.QueryMaxPageSize);
            await store.InitializeAsync();
            await store.AppendAsync(Draft("audit.admin.config-read", 1, componentId));
            await store.AppendAsync(Draft("audit.admin.config-change-requested", 2, componentId));
            await store.AppendAsync(Draft("audit.admin.config-change-applied", 3, componentId));
            await store.ValidateChainAsync();

            await DeleteSequenceAsync(store.DatabasePath, 2);
            await RequireInvalidDataAsync(store.ValidateChainAsync, "audit.sequence-gap");
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }

        var mismatchTemp = Path.Combine(Path.GetTempPath(), "machiverse-gw07-prev-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(mismatchTemp);
        try
        {
            var componentId = Enumerable.Repeat((byte)0x2b, 16).ToArray();
            await using var store = new GatewayAuditStoreV1(mismatchTemp, config.Audit.QueryMaxPageSize);
            await store.InitializeAsync();
            await store.AppendAsync(Draft("audit.admin.config-read", 1, componentId));
            await store.AppendAsync(Draft("audit.admin.config-change-requested", 2, componentId));
            var records = await store.QueryAsync(null, 10);
            var second = records[1];
            var corrupted = second with
            {
                PreviousDigest = new byte[32],
                RecordDigest = second.RecordDigest.ToArray(),
            };
            corrupted = corrupted with
            {
                RecordDigest = AuditRecordCodecV1.DomainHash(
                    "mv.audit-record.v1",
                    AuditRecordCodecV1.NormalizeWithoutDigest(corrupted)),
            };
            await RewriteSecondRecordAsync(store.DatabasePath, corrupted);
            await RequireInvalidDataAsync(store.ValidateChainAsync, "audit.previous-digest-mismatch");
        }
        finally
        {
            Directory.Delete(mismatchTemp, recursive: true);
        }
    }

    private static AuditRecordDraftV1 Draft(string kind, long observedAt, byte[] componentId)
        => new(
            kind,
            observedAt,
            "gateway",
            componentId,
            ActorRef: null,
            SessionRefDigest: null,
            OperationId: null,
            CorrelationId: null,
            TargetRef: "gateway",
            WorldId: null,
            SimulationStep: null,
            ConfigGeneration: null,
            RequestDigest: null,
            ResultStatus: "success",
            ResultCode: "ok",
            ApprovalEvidenceDigest: null,
            SummaryFields: new Dictionary<string, string>());

    private static async Task DeleteSequenceAsync(string path, ulong sequence)
    {
        await using var connection = Open(path);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM audit_record WHERE sequence=$sequence;";
        command.Parameters.AddWithValue("$sequence", U64Be(sequence));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task RewriteSecondRecordAsync(string path, AuditRecordV1 record)
    {
        await using var connection = Open(path);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE audit_record
            SET digest=$digest, previous_digest=$previous, kind=$kind, payload=$payload
            WHERE sequence=$sequence;
            """;
        command.Parameters.AddWithValue("$digest", record.RecordDigest);
        command.Parameters.AddWithValue("$previous", record.PreviousDigest);
        command.Parameters.AddWithValue("$kind", record.AuditKind);
        command.Parameters.AddWithValue("$payload", JsonSerializer.SerializeToUtf8Bytes(record, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        command.Parameters.AddWithValue("$sequence", U64Be(record.AuditSequence));
        await command.ExecuteNonQueryAsync();
    }

    private static SqliteConnection Open(string path)
        => new(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ToString());

    private static byte[] U64Be(ulong value)
    {
        var bytes = new byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        return bytes;
    }

    private static async Task RequireInvalidDataAsync(Func<CancellationToken, ValueTask> action, string expected)
    {
        try
        {
            await action(CancellationToken.None);
        }
        catch (InvalidDataException ex) when (ex.Message == expected)
        {
            return;
        }
        throw new InvalidOperationException($"Expected audit corruption rejection: {expected}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
