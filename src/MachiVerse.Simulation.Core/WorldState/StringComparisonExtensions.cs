namespace MachiVerse.Simulation.Core.WorldState;

internal static class StringComparisonExtensions
{
    public static bool EndsWith(this string value, char suffix, StringComparison comparisonType)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length == 0) return false;
        return string.Equals(value[^1].ToString(), suffix.ToString(), comparisonType);
    }
}
