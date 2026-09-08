using System.Text.RegularExpressions;

namespace MachiVerse.Simulation.Core.Determinism;

public readonly partial record struct StableToken
{
    public StableToken(string value)
    {
        if (!TokenPattern().IsMatch(value))
        {
            throw new ArgumentException("StableToken must match [a-z0-9][a-z0-9._/-]{0,63}.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;

    [GeneratedRegex("^[a-z0-9][a-z0-9._/-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();
}
