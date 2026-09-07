using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.State;

internal static class Sim04PartitionRegistrySmoke
{
    [ModuleInitializer]
    internal static void Initialize() => Run();

    internal static void Run()
    {
        var registry = StandardDomainPartitionRegistry.Create();
        StandardDomainPartitionRegistry.ValidateStandardProfile(registry);

        if (registry.Entries.Count != 97)
            throw new InvalidOperationException("SIM-04 standard partition registry must contain exactly 97 entries.");

        var orderedIds = registry.Entries.Select(static entry => entry.PartitionId.Value).ToArray();
        var sortedIds = orderedIds.OrderBy(static value => value, StringComparer.Ordinal).ToArray();
        if (!orderedIds.SequenceEqual(sortedIds, StringComparer.Ordinal))
            throw new InvalidOperationException("Partition registry iteration must be ASCII/ordinal ascending.");
        if (orderedIds.Distinct(StringComparer.Ordinal).Count() != 97)
            throw new InvalidOperationException("PartitionId must be unique across the standard registry.");

        var families = StandardDomainPartitionRegistry.CreateDomainFamilies(registry);
        var expected = new (string Domain, ushort Rank, int Count)[]
        {
            ("spatial", 10, 8),
            ("environment", 20, 13),
            ("physical_built", 30, 11),
            ("participation", 40, 5),
            ("resident", 50, 13),
            ("society_economy", 60, 16),
            ("governance_security", 70, 17),
            ("infrastructure_information", 80, 14),
        };

        if (families.Count != expected.Length)
            throw new InvalidOperationException("SIM-04 standard domain family count must be exactly 8.");

        for (var i = 0; i < expected.Length; i++)
        {
            var family = families[i];
            if (family.DomainToken.Value != expected[i].Domain ||
                family.DomainRank != expected[i].Rank ||
                family.OwnedPartitions.Count != expected[i].Count)
                throw new InvalidOperationException($"Domain family registry mismatch at index {i}.");
        }

        foreach (var entry in registry.Entries)
        {
            var expectedPartitionSchema = $"domain.{entry.PartitionId.Value}";
            var expectedRecordSchema = expectedPartitionSchema + ".record";
            if (entry.PartitionSchema.SchemaId.Value != expectedPartitionSchema ||
                entry.RecordSchema.SchemaId.Value != expectedRecordSchema)
                throw new InvalidOperationException($"Partition schema identity mismatch: {entry.PartitionId.Value}");
            if (entry.PersistenceClass != PersistenceClassV1.AuthoritativeAlways)
                throw new InvalidOperationException($"Standard partition must be AUTHORITATIVE_ALWAYS: {entry.PartitionId.Value}");
        }

        var registration = registry.GetRequired(new StableToken("resident.identity_lifecycle"));
        var recordA = new DomainRecordEnvelopeV1(
            OpaqueId128.Parse("00000000000000000000000000000011"),
            registration.RecordSchema,
            revision: 1,
            createdStep: 0,
            retiredStep: null,
            DetailLevelV1.D0Entity,
            lineageRef: null,
            payload: new byte[] { 1 });
        var recordB = new DomainRecordEnvelopeV1(
            OpaqueId128.Parse("00000000000000000000000000000010"),
            registration.RecordSchema,
            revision: 1,
            createdStep: 0,
            retiredStep: null,
            DetailLevelV1.D0Entity,
            lineageRef: null,
            payload: new byte[] { 2 });
        var header = new PartitionStateHeaderV1(
            registration.PartitionId,
            registration.OwnerDomain,
            registration.PartitionSchema,
            Revision: 1,
            BasisStep: 0,
            DetailLevelV1.D0Entity,
            ItemCount: 2,
            new Hash256(Enumerable.Repeat((byte)0x42, 32).ToArray()));

        var partition = DomainPartitionStateV1.CreateOwned(
            registration,
            registration.OwnerDomain,
            header,
            new[] { recordA, recordB });
        if (partition.Records[0].RecordId != recordB.RecordId || partition.Records[1].RecordId != recordA.RecordId)
            throw new InvalidOperationException("Partition records must iterate by PartitionRecordId bytewise ascending.");

        var foreignRejected = false;
        try
        {
            _ = DomainPartitionStateV1.CreateOwned(
                registration,
                new StableToken("society_economy"),
                header,
                new[] { recordA, recordB });
        }
        catch (InvalidOperationException ex) when (ex.Message == "state.partition.foreign-owner-mutation-rejected")
        {
            foreignRejected = true;
        }
        if (!foreignRejected)
            throw new InvalidOperationException("Foreign owner direct mutation must be rejected.");

        var revisionRejected = false;
        try
        {
            _ = new DomainRecordEnvelopeV1(
                OpaqueId128.Parse("00000000000000000000000000000012"),
                registration.RecordSchema,
                revision: 0,
                createdStep: 0,
                retiredStep: null,
                DetailLevelV1.D0Entity,
                lineageRef: null,
                payload: ReadOnlySpan<byte>.Empty);
        }
        catch (ArgumentOutOfRangeException)
        {
            revisionRejected = true;
        }
        if (!revisionRejected)
            throw new InvalidOperationException("DomainRecordEnvelope revision 0 must be rejected.");
    }
}
