using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Domains.ResidentParticipation;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

internal static class CanonicalResidentPerceivedFactNestedSnapshotInitializer
{
    private const string PartitionId = "resident.perception";
    private const string FieldName = "perceived_facts";

    [ModuleInitializer]
    internal static void Initialize()
    {
        var residentId = Id("00000000000000000000000000034001");
        var firstObservation = new ResidentPerceptionObservationV1(
            Id("00000000000000000000000000034101"),
            residentId,
            Id("00000000000000000000000000034201"),
            new StableToken("fact.first"),
            Id("00000000000000000000000000034301"),
            750_000,
            12);
        var secondObservation = new ResidentPerceptionObservationV1(
            Id("00000000000000000000000000034102"),
            residentId,
            Id("00000000000000000000000000034202"),
            new StableToken("fact.second"),
            Id("00000000000000000000000000034302"),
            500_000,
            12);

        var first = ResidentPerceivedFactNestedValueV1.FromObservation(firstObservation);
        var second = ResidentPerceivedFactNestedValueV1.FromObservation(secondObservation);
        var registry = StandardDomainNestedSnapshotCodecRegistryV1.Create();
        var codec = registry.GetForBinding(PartitionId, FieldName);
        Require(codec.Descriptor.Schema == new SchemaRefV1("domain.resident.perceived-fact"),
            "Perceived fact nested schema id/version drifted.");
        Require(codec.Descriptor.Fields.Count == 7,
            "Perceived fact nested schema must retain the exact seven-field runtime projection.");

        var encodedValue = DomainNestedSnapshotWireCodecV1.EncodeValue(
            PartitionId,
            FieldName,
            first,
            registry);
        var decodedValue = DomainNestedSnapshotWireCodecV1.DecodeValue(
            PartitionId,
            FieldName,
            encodedValue,
            registry);
        Require(decodedValue is ResidentPerceivedFactNestedValueV1 decodedFirst && decodedFirst == first,
            "Perceived fact nested value must roundtrip exactly.");
        Require(decodedFirst.ToObservation() == firstObservation,
            "Perceived fact nested value must reconstruct the runtime observation losslessly.");

        var payload = new ResidentPerceptionPayloadV1(
            new PartitionRecordRefV1("resident.identity_lifecycle", residentId),
            Array.Empty<PartitionRecordRefV1>(),
            new ICanonicalDomainNestedValueV1[] { first, second },
            900_000,
            12);
        var encodedPayload = DomainPartitionSnapshotWireCodecV1.EncodePayload(
            PartitionId,
            payload.ToStandardPayload(),
            registry);
        var decodedPayload = ResidentPerceptionPayloadV1.FromStandardPayload(
            DomainPartitionSnapshotWireCodecV1.DecodePayload(
                PartitionId,
                encodedPayload,
                registry));
        Require(decodedPayload.PerceivedFacts.Count == 2 &&
                decodedPayload.PerceivedFacts[0] is ResidentPerceivedFactNestedValueV1 firstRoundtrip && firstRoundtrip == first &&
                decodedPayload.PerceivedFacts[1] is ResidentPerceivedFactNestedValueV1 secondRoundtrip && secondRoundtrip == second,
            "Resident perception payload wire must preserve perceived facts through the standard nested registry.");

        ExpectInvalid(
            () => _ = DomainNestedSnapshotWireCodecV1.EncodeList(
                PartitionId,
                FieldName,
                new ICanonicalDomainNestedValueV1[] { second, first },
                registry),
            "persistence.snapshot.nested-list-order:");
    }

    private static OpaqueId128 Id(string value) => OpaqueId128.Parse(value);

    private static void ExpectInvalid(Action action, string expectedPrefix)
    {
        try
        {
            action();
        }
        catch (InvalidDataException ex) when (ex.Message.StartsWith(expectedPrefix, StringComparison.Ordinal))
        {
            return;
        }

        throw new InvalidOperationException($"Expected InvalidDataException with prefix {expectedPrefix}.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
