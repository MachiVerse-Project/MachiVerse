using System.Text.Json;

static Qa04SoakSchedule Parse(int hours, int minutes)
{
    using var json = JsonDocument.Parse(JsonSerializer.Serialize(new
    {
        durationHours = hours,
        gatewayReconnectFailoverIntervalMinutes = minutes,
    }));
    return Qa04SoakSchedule.FromProfile(json.RootElement);
}

foreach (var (hours, minutes, count) in new[] { (12, 30, 24), (24, 30, 48), (12, 15, 48), (12, 50, 15), (1, 120, 1) })
{
    var schedule = Parse(hours, minutes);
    if (schedule.DurationSeconds != hours * 3600L || schedule.GatewayCycleInterval != TimeSpan.FromMinutes(minutes) ||
        schedule.MinimumGatewayCycles != count)
        throw new Exception($"soak schedule mismatch: {hours}h / {minutes}min");
    var actual = Enumerable.Range(0, count).Count(i => i * schedule.GatewayCycleInterval < TimeSpan.FromHours(hours));
    if (actual != count || count * schedule.GatewayCycleInterval < TimeSpan.FromHours(hours))
        throw new Exception("cycle schedule requires a deadline cycle or omits an in-duration cycle");
}
foreach (var (hours, minutes) in new[] { (0, 30), (-1, 30), (12, 0), (12, -1) })
{
    try { Parse(hours, minutes); }
    catch (InvalidDataException) { continue; }
    throw new Exception("invalid soak duration/interval accepted");
}
Console.WriteLine("QA-04 soak schedule boundary tests passed.");
