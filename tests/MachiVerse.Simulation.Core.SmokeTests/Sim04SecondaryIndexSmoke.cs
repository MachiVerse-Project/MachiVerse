using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.State;

internal static class Sim04SecondaryIndexSmoke
{
    [ModuleInitializer]
    internal static void Initialize() => Run();

    internal static void Run()
    {
        var registry = StandardDomainPartitionRegistry.Create();
        var registration = registry.GetRequired(new StableToken("resident.identity_lifecycle"));
        var recordA = Record("00000000000000000000000000000002", 0x02);
        var recordB = Record("00000000000000000000000000000001", 0x01);
        var digest = new Hash256(Enumerable.Repeat((byte)0x77, 32).ToArray());
        var partition = DomainPartitionStateV1.RestoreValidated(
            registration,
            new PartitionStateHeaderV1(
                registration.PartitionId,
                registration.OwnerDomain,
                registration.PartitionSchema,
                Revision: 1,
                BasisStep: 0,
                DetailLevelV1.D0Entity,
                ItemCount: 2,
                digest),
            new[] { recordA, recordB });

        var descriptor = new PartitionIndexDescriptorV1(
            new StableToken("resident.lifecycle-by-status"),
            registration.PartitionId,
            IndexAuthorityV1.DerivedRebuildable,
            new SchemaRefV1(new StableToken("index.key.lifecycle-status"), new SchemaVersionV1(1, 0)),
            new SchemaRefV1(new StableToken("index.value.record-ref"), new SchemaVersionV1(1, 0)),
            IndexUniquenessV1.MultiValue,
            CanonicalOrderKindV1.RecordIdBytewiseAscending,
            new SchemaRefV1(new StableToken("index.recipe.lifecycle-status"), new SchemaVersionV1(1, 0)));

        var rebuiltA = PartitionSecondaryIndexBuilder.Rebuild(
            partition,
            descriptor,
            static record => new[] { new CanonicalIndexKeyV1(record.Payload.Span) });
        var rebuiltB = PartitionSecondaryIndexBuilder.Rebuild(
            DomainPartitionStateV1.RestoreValidated(registration, partition.Header, partition.Records.Reverse()),
            descriptor,
            static record => new[] { new CanonicalIndexKeyV1(record.Payload.Span) });

        if (!rebuiltA.CanonicallyEquivalentTo(rebuiltB))
            throw new InvalidOperationException("Derived index rebuild must be independent of source collection order.");
        if (rebuiltA.Buckets.Count != 2 || rebuiltA.Buckets[0].Key.Bytes.Span[0] != 0x01)
            throw new InvalidOperationException("Derived index keys must be bytewise canonical ascending.");

        var uniqueDescriptor = descriptor with { Uniqueness = IndexUniquenessV1.Unique };
        var uniquenessRejected = false;
        try
        {
            _ = PartitionSecondaryIndexBuilder.Rebuild(
                partition,
                uniqueDescriptor,
                static _ => new[] { new CanonicalIndexKeyV1(new byte[] { 0x7f }) });
        }
        catch (InvalidDataException ex) when (ex.Message == "state.index.uniqueness-violation")
        {
            uniquenessRejected = true;
        }
        if (!uniquenessRejected)
            throw new InvalidOperationException("Unique derived index must reject multiple records for one key.");

        DomainRecordEnvelopeV1 Record(string id, byte key)
            => new(
                OpaqueId128.Parse(id),
                registration.RecordSchema,
                revision: 1,
                createdStep: 0,
                retiredStep: null,
                DetailLevelV1.D0Entity,
                lineageRef: null,
                payload: new byte[] { key });
    }
}
