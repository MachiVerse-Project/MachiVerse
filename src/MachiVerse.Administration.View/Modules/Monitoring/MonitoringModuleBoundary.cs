using MachiVerse.Protocol.V1;

namespace MachiVerse.Administration.View.Modules.Monitoring;

// ADMIN-02 boundary. Only canonical Gateway/Admin protocol payloads enter here;
// target component internal types and direct fallback access are forbidden.
public interface IMonitoringModuleBoundary
{
    event Action? Changed;

    MonitoringSnapshot Snapshot { get; }

    bool TryApply(WireEnvelopeV1 envelope);

    void SetChannelAccess(MonitoringChannel channel, MonitoringAccessState state, string? reasonCode = null);
}
