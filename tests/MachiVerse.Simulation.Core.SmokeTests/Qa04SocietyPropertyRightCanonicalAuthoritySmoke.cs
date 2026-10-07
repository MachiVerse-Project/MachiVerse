using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04SocietyPropertyRightCanonicalAuthoritySmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        Qa04SocietyPropertyRightCanonicalAuthorityV1.ValidateCanonicalContract();
        var materialization = Qa04SocietyPropertyRightCanonicalAuthorityV1.MaterializeCanonical();
        var support = materialization.Support;
        var rights = materialization.RecordsByOrdinal;

        Require(materialization.MaterializedRecordCount == 50_000 && rights.Count == 50_000,
            "Canonical society.property_right authority must materialize exactly 50,000 records.");
        Require(support.TileFrames.ItemCount == 4_096 && support.Presences.ItemCount == 50_000,
            "PropertyRight support authority must materialize 4,096 TileFrames and 50,000 actual Presence records.");
        Require(rights.Select(static value => value.Payload.AssetRef).Distinct().Count() == 50_000 &&
                rights.Select(static value => value.Payload.HolderRef).Distinct().Count() == 50_000,
            "PropertyRight must retain 50,000 distinct actual assets and holders.");
        Require(rights.All(static value =>
                value.Payload.RightKind.Value == "perf.ownership" && value.Payload.SharePpm == 1_000_000 &&
                value.Payload.EffectiveFrom == 0 && value.Payload.EffectiveUntil is null && value.Payload.ClaimRef is null),
            "PropertyRight approved genesis payload drifted.");

        Qa04SocietyPropertyRightCanonicalAuthorityV1.ValidateSecondaryIndexes(materialization.PropertyRights);
        var recovered = Qa04SocietyPropertyRightSnapshotRecoveryEvidenceV1.Verify(materialization);
        Require(recovered == 50_000,
            "PropertyRight Snapshot/recovery must semantically recover all 50,000 records.");

        var first = rights[0];
        var second = rights[1];
        ExpectInvalid(() => new StandardDomainPayloadCodecValidatorV1().Validate(
                SocietyPropertyRightPayloadV1.PartitionId,
                first.Payload.ToStandardPayload(),
                new EmptyReferenceResolver()),
            "PropertyRight must fail closed without upstream Physical/Resident authority.");
        ExpectInvalid(() => Qa04SocietyPropertyRightCanonicalAuthorityV1.ValidateCanonicalRecord(
                0,
                CopyWithPayload(first, first.Payload with { AssetRef = second.Payload.AssetRef }),
                support,
                materialization.References),
            "Wrong PropertyRight asset mapping must fail closed.");
        ExpectInvalid(() => Qa04SocietyPropertyRightCanonicalAuthorityV1.ValidateCanonicalRecord(
                0,
                CopyWithPayload(first, first.Payload with { SharePpm = 999_999 }),
                support,
                materialization.References),
            "PropertyRight share drift must fail closed.");

        var firstPhysical = support.PhysicalRecordsByOrdinal[0];
        var badPresence = new DomainRecordEnvelopeV1<PhysicalPresencePayloadV1>(
            firstPhysical.Presence.RecordId,
            firstPhysical.Presence.RecordSchema,
            firstPhysical.Presence.Revision,
            firstPhysical.Presence.CreatedStep,
            firstPhysical.Presence.RetiredStep,
            firstPhysical.Presence.DetailLevel,
            firstPhysical.Presence.LineageRef,
            firstPhysical.Presence.Payload with
            {
                Position = firstPhysical.Presence.Payload.Position with { X = firstPhysical.Presence.Payload.Position.X + 1 },
            });
        var badPhysical = new Qa04PhysicalD0RecordMaterialV1(
            firstPhysical.PhysicalOrdinal,
            badPresence,
            firstPhysical.Occupancy,
            firstPhysical.CollisionShape);
        ExpectInvalid(() => Qa04PhysicalD0PropertyAssetSupportCanonicalAuthorityV1.ValidateCanonicalPhysicalRecord(
                0,
                badPhysical,
                materialization.References,
                support.TerrainBindingsByTile),
            "Physical PropertyRight asset position drift must fail closed.");
    }

    private static DomainRecordEnvelopeV1<SocietyPropertyRightPayloadV1> CopyWithPayload(
        DomainRecordEnvelopeV1<SocietyPropertyRightPayloadV1> record,
        SocietyPropertyRightPayloadV1 payload)
        => new(record.RecordId, record.RecordSchema, record.Revision, record.CreatedStep, record.RetiredStep,
            record.DetailLevel, record.LineageRef, payload);

    private static void ExpectInvalid(Action action, string message)
    {
        try
        {
            action();
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or ArgumentOutOfRangeException)
        {
            return;
        }
        throw new InvalidOperationException(message);
    }

    private sealed class EmptyReferenceResolver : IDomainRecordSchemaResolverV1
    {
        public bool Exists(PartitionRecordRefV1 reference) => false;

        public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
        {
            schema = default;
            return false;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
