using System.Runtime.CompilerServices;
using MachiVerse.Gateway.Audit;
using MachiVerse.Gateway.Configuration;
using Microsoft.Data.Sqlite;

internal static class Gw07StorageIntegritySmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        var config = GatewayConfigLoader.LoadFile("config/gateway.toml");
        var temp = Path.Combine(Path.GetTempPath(), "machiverse-gw07-integrity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            RunAsync(temp, config).GetAwaiter().GetResult();
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    private static async Task RunAsync(string temp, GatewayConfig config)
    {
        var id = Enumerable.Repeat((byte)7, 16).ToArray();
        await using (var store = new GatewayAuditStoreV1(temp, config.Audit.QueryMaxPageSize))
        {
            await store.InitializeAsync();
            await store.AppendAsync(new AuditRecordDraftV1(
                "audit.admin.config-read", 1, "gateway", id, null, null, null, null, "gateway", null,
                null, null, null, "success", "ok", null, new Dictionary<string, string>()));
            await AuditStorageIntegrityV1.ValidateRowsAsync(store.DatabasePath);
        }

        var path = Path.Combine(temp, "audit", "audit.sqlite");
        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ToString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE audit_record SET kind='audit.admin.audit-query' WHERE sequence=x'0000000000000001';";
            await command.ExecuteNonQueryAsync();
        }

        try
        {
            await AuditStorageIntegrityV1.ValidateRowsAsync(path);
            throw new InvalidOperationException("Audit storage-column tampering was not detected.");
        }
        catch (InvalidDataException ex) when (ex.Message == "audit.storage-row-mismatch")
        {
        }
    }
}
