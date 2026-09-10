using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Performance;

internal static class Qa04ReferenceGenesisValueSourceSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        var descriptor = Qa04ReferenceLoadV1.Record(new StableToken("environment.d0-cell-cohort"), 0);
        var repeat = Qa04ReferenceGenesisValueSourceV1.Hash(descriptor.RecordId, "porosity_ppm");
        var same = Qa04ReferenceGenesisValueSourceV1.Hash(descriptor.RecordId, "porosity_ppm");
        var otherField = Qa04ReferenceGenesisValueSourceV1.Hash(descriptor.RecordId, "stability_ppm");
        var otherRecord = Qa04ReferenceGenesisValueSourceV1.Hash(
            Qa04ReferenceLoadV1.Record(new StableToken("environment.d0-cell-cohort"), 1).RecordId,
            "porosity_ppm");

        Require(repeat.Length == 32 && repeat.SequenceEqual(same),
            "QA-04 genesis value hash must be deterministic SHA-256 material.");
        Require(!repeat.SequenceEqual(otherField),
            "QA-04 genesis value hash must domain-separate field tags.");
        Require(!repeat.SequenceEqual(otherRecord),
            "QA-04 genesis value hash must bind the authoritative record id.");

        var ppm = Qa04ReferenceGenesisValueSourceV1.BoundedPpm(descriptor.RecordId, "porosity_ppm");
        Require(ppm is >= 500_000 and <= 900_000,
            "QA-04 generic bounded ppm conversion must stay inside the normative range.");
        var count = Qa04ReferenceGenesisValueSourceV1.PositiveCount(descriptor.RecordId, "sample_count");
        Require(count is >= 1 and <= 1_000,
            "QA-04 generic positive-count conversion must stay inside the normative range.");
        var money = Qa04ReferenceGenesisValueSourceV1.Money(descriptor.RecordId, "money");
        Require(money is >= 1_000 and <= 1_000_999,
            "QA-04 generic money conversion must stay inside the normative range.");
        var signed = Qa04ReferenceGenesisValueSourceV1.SmallSignedValue(descriptor.RecordId, "signed");
        Require(signed is >= -1_000 and <= 1_000,
            "QA-04 generic signed conversion must stay inside the normative range.");

        ExpectReject(() => Qa04ReferenceGenesisValueSourceV1.Hash(OpaqueId128.Zero, "field"));
        ExpectReject(() => Qa04ReferenceGenesisValueSourceV1.Hash(descriptor.RecordId, ""));
    }

    private static void ExpectReject(Action action)
    {
        try
        {
            action();
        }
        catch (ArgumentException)
        {
            return;
        }
        throw new InvalidOperationException("Expected QA-04 genesis source validation failure.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
