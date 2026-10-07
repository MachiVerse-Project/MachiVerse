using System.Text.Json;

namespace MachiVerse.Simulation.Core.Observability;

/// <summary>
/// Minimal component-local structured log sink for the Core executable. It emits one sanitized
/// JSON object per line to stderr and deliberately owns no retry/backpressure authority.
/// </summary>
public sealed class JsonConsoleCoreStructuredLogSinkV1 : ICoreStructuredLogSinkV1
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    private readonly TextWriter _writer;

    public JsonConsoleCoreStructuredLogSinkV1(TextWriter? writer = null)
    {
        _writer = writer ?? Console.Error;
    }

    public void Emit(CoreStructuredLogEventV1 logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        _writer.WriteLine(JsonSerializer.Serialize(logEvent, SerializerOptions));
    }
}
