using System.Text;
using Google.Protobuf;
using MachiVerse.Protocol.V1;

namespace MachiVerse.Administration.View.Modules.Monitoring;

public sealed record LogQueryOptions(
    IReadOnlyList<ComponentTargetV1> Targets,
    ulong? FromUnixMillis,
    ulong? ToUnixMillis,
    IReadOnlyList<string> EventKinds,
    ByteString? CorrelationId,
    ByteString? OperationId,
    ulong? BasisStep,
    uint PageSize,
    ByteString? Cursor);

public sealed record AuditQueryOptions(
    ulong? FromUnixMillis,
    ulong? ToUnixMillis,
    IReadOnlyList<string> AuditEventKinds,
    ByteString? OperationId,
    ulong? SimulationStep,
    uint PageSize,
    ByteString? Cursor);

public static class MonitoringQueryBuilder
{
    public const uint MaxPageSize = 1000;
    public const int MaxCursorBytes = 256;

    public static HealthQueryV1 BuildHealth(
        IEnumerable<ComponentTargetV1> targets,
        IEnumerable<string>? metricNames = null)
    {
        var query = new HealthQueryV1();
        query.Targets.Add(targets.Select(static target => target.Clone()));
        if (metricNames is not null)
        {
            query.MetricNames.Add(metricNames
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static name => name, StringComparer.Ordinal));
        }

        return query;
    }

    public static LogQueryV1 BuildLog(LogQueryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateRange(options.FromUnixMillis, options.ToUnixMillis);
        ValidatePage(options.PageSize);
        ValidateOptionalId(options.CorrelationId, nameof(options.CorrelationId));
        ValidateOptionalId(options.OperationId, nameof(options.OperationId));
        ValidateCursor(options.Cursor);

        var query = new LogQueryV1
        {
            PageSize = options.PageSize,
        };
        query.Targets.Add(options.Targets.Select(static target => target.Clone()));
        query.EventKinds.Add(options.EventKinds
            .Where(static kind => !string.IsNullOrWhiteSpace(kind))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static kind => kind, StringComparer.Ordinal));

        if (options.FromUnixMillis is { } from) query.FromUnixMillis = from;
        if (options.ToUnixMillis is { } to) query.ToUnixMillis = to;
        if (options.CorrelationId is { } correlation) query.CorrelationId = correlation;
        if (options.OperationId is { } operation) query.OperationId = operation;
        if (options.BasisStep is { } step) query.BasisStep = step;
        if (options.Cursor is { } cursor) query.Cursor = cursor;
        return query;
    }

    public static AuditQueryV1 BuildAudit(AuditQueryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateRange(options.FromUnixMillis, options.ToUnixMillis);
        ValidatePage(options.PageSize);
        ValidateOptionalId(options.OperationId, nameof(options.OperationId));
        ValidateCursor(options.Cursor);

        var query = new AuditQueryV1
        {
            PageSize = options.PageSize,
        };
        query.AuditEventKinds.Add(options.AuditEventKinds
            .Where(static kind => !string.IsNullOrWhiteSpace(kind))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static kind => kind, StringComparer.Ordinal));

        if (options.FromUnixMillis is { } from) query.FromUnixMillis = from;
        if (options.ToUnixMillis is { } to) query.ToUnixMillis = to;
        if (options.OperationId is { } operation) query.OperationId = operation;
        if (options.SimulationStep is { } step) query.SimulationStep = step;
        if (options.Cursor is { } cursor) query.Cursor = cursor;
        return query;
    }

    private static void ValidateRange(ulong? from, ulong? to)
    {
        if (from is { } fromValue && to is { } toValue && fromValue > toValue)
        {
            throw new ArgumentException("Query from timestamp must not be later than to timestamp.");
        }
    }

    private static void ValidatePage(uint pageSize)
    {
        if (pageSize is < 1 or > MaxPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), $"Page size must be in range 1..{MaxPageSize}.");
        }
    }

    private static void ValidateOptionalId(ByteString? value, string name)
    {
        if (value is null) return;
        if (value.Length != 16 || value.ToByteArray().All(static b => b == 0))
        {
            throw new ArgumentException($"{name} must be a non-zero Id128.", name);
        }
    }

    private static void ValidateCursor(ByteString? cursor)
    {
        if (cursor is { Length: > MaxCursorBytes })
        {
            throw new ArgumentException($"Cursor must not exceed {MaxCursorBytes} bytes.", nameof(cursor));
        }
    }
}

public static class AuditExportFormatter
{
    public static string ToCsv(IEnumerable<AuditRecordProjection> records)
    {
        var builder = new StringBuilder();
        builder.AppendLine("audit_record_id,timestamp_unix_millis,event_kind,actor_account_ref,operation_id,simulation_step,target_kind,result_code");

        foreach (var record in records)
        {
            Append(builder, record.AuditRecordId);
            Append(builder, record.TimestampUnixMillis.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Append(builder, record.AuditEventKind);
            Append(builder, record.ActorAccountRef);
            Append(builder, record.OperationId ?? string.Empty);
            Append(builder, record.SimulationStep?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
            Append(builder, record.TargetKind);
            Append(builder, record.ResultCode, last: true);
        }

        return builder.ToString();
    }

    private static void Append(StringBuilder builder, string value, bool last = false)
    {
        builder.Append('"').Append(value.Replace("\"", "\"\"", StringComparison.Ordinal)).Append('"');
        builder.Append(last ? '\n' : ',');
    }
}
