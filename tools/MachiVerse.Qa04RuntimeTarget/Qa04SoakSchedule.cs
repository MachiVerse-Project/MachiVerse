using System.Text.Json;

internal sealed record Qa04SoakSchedule(long DurationSeconds, TimeSpan GatewayCycleInterval, int MinimumGatewayCycles)
{
    internal static Qa04SoakSchedule FromProfile(JsonElement profile)
    {
        var hours = profile.GetProperty("durationHours").GetInt32();
        var minutes = profile.GetProperty("gatewayReconnectFailoverIntervalMinutes").GetInt32();
        if (hours <= 0 || minutes <= 0)
            throw new InvalidDataException("qa04.soak-duration-or-cycle-interval-invalid");
        var duration = TimeSpan.FromHours(hours);
        var interval = TimeSpan.FromMinutes(minutes);
        // 開始時の1回を含む。終了時刻と同時のcycleを必須にしない。
        var cycles = checked((int)((duration.Ticks - 1) / interval.Ticks + 1));
        return new Qa04SoakSchedule(checked((long)duration.TotalSeconds), interval, cycles);
    }
}
