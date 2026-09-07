using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

internal static class Sim04WorldStateSmoke
{
    public static void Run()
    {
        var entries = StandardDomainPartitionRegistry.Entries;
        if (entries.Count != 97)
            throw new InvalidOperationException("SIM-04 standard partition registry must contain exactly 97 entries.");

        var expectedOwnerCounts = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["spatial"] = 8,
            ["environment"] = 13,
            ["physical_built"] = 11,
            ["participation"] = 5,
            ["resident"] = 13,
            ["society_economy"] = 16,
            ["governance_security"] = 17,
            ["infrastructure_information"] = 14
        };
        foreach (var expected in expectedOwnerCounts)
        {
            var actual = entries.Count(entry => entry.OwnerDomain.Value == expected.Key);
            if (actual != expected.Value)
                throw new InvalidOperationException($"Owner {expected.Key} partition count mismatch: {actual}.");
        }

        var partitionTokens = entries.Select(static entry => entry.PartitionId.Value).ToArray();
        var sortedTokens = partitionTokens.OrderBy(static value => value, StringComparer.Ordinal).ToArray();
        if (!partitionTokens.SequenceEqual(sortedTokens))
            throw new InvalidOperationException("Partition registry iteration must be canonical ASCII bytewise order.");
        if (partitionTokens.Distinct(StringComparer.Ordinal).Count() != 97)
            throw new InvalidOperationException("PartitionId registry contains duplicates.");

        foreach (var entry in entries)
        {
            if (entry.PartitionSchema.SchemaId.Value != "domain." + entry.PartitionId.Value ||
                entry.RecordSchema.SchemaId.Value != "domain." + entry.PartitionId.Value + ".record" ||
                entry.PartitionSchema.Version != new SchemaVersionV1(1, 0) ||
                entry.RecordSchema.Version != new SchemaVersionV1(1, 0))
                throw new InvalidOperationException($"Schema identity mismatch for {entry.PartitionId.Value}.");
            if (entry.PrimaryKeyKind != PrimaryKeyKindV1.RecordId128 ||
                entry.PersistenceClass != PersistenceClassV1.AuthoritativeAlways ||
                entry.CanonicalOrder != CanonicalOrderKindV1.RecordIdBytewiseAsc)
                throw new InvalidOperationException($"Registry invariant mismatch for {entry.PartitionId.Value}.");
        }

        var residentIdentity = StandardDomainPartitionRegistry.Get("resident.identity_lifecycle");
        if (residentIdentity.OwnerDomain.Value != "resident" || residentIdentity.OwnerDomainRank != 50)
            throw new InvalidOperationException("Resident partition owner/rank mismatch.");

        var recordHigh = new DomainRecordEnvelopeV1<string>(
            OpaqueId128.Parse("00000000000000000000000000000002"),
            residentIdentity.RecordSchema,
            revision: 1,
            createdStep: 10,
            retiredStep: null,
            detailLevel: DetailLevelV1.D0Entity,
            lineageRef: null,
            payload: "resident-b");
        var recordLow = new DomainRecordEnvelopeV1<string>(
            OpaqueId128.Parse("00000000000000000000000000000001"),
            residentIdentity.RecordSchema,
            revision: 1,
            createdStep: 10,
            retiredStep: null,
            detailLevel: DetailLevelV1.D0Entity,
            lineageRef: null,
            payload: "resident-a");

        var state = new DomainPartitionStateV1<string>(residentIdentity, [recordHigh, recordLow]);
        var recordOrder = state.RecordsCanonical.Select(static record => record.RecordId.ToString()).ToArray();
        if (!recordOrder.SequenceEqual(new[]
            {
                "00000000000000000000000000000001",
                "00000000000000000000000000000002"
            }))
            throw new InvalidOperationException("Partition records must iterate by canonical record-id byte order.");

        var revised = recordLow.Revise("resident-a-v2");
        if (revised.Revision != 2 || revised.RecordId != recordLow.RecordId || revised.Payload != "resident-a-v2")
            throw new InvalidOperationException("Record revision must preserve identity and increment exactly once.");
        var retired = revised.Retire(20);
        if (!retired.IsRetired || retired.RetiredStep != 20 || retired.Revision != 3)
            throw new InvalidOperationException("Record retirement boundary mismatch.");

        var zeroRejected = false;
        try
        {
            _ = new DomainRecordEnvelopeV1<string>(
                OpaqueId128.Zero,
                residentIdentity.RecordSchema,
                1,
                0,
                null,
                DetailLevelV1.D0Entity,
                null,
                "invalid");
        }
        catch (ArgumentException)
        {
            zeroRejected = true;
        }
        if (!zeroRejected)
            throw new InvalidOperationException("PartitionRecordId ZERO must be rejected.");

        var schemaMismatchRejected = false;
        try
        {
            var wrong = new DomainRecordEnvelopeV1<string>(
                OpaqueId128.Parse("00000000000000000000000000000003"),
                StandardDomainPartitionRegistry.Get("resident.body_health").RecordSchema,
                1,
                10,
                null,
                DetailLevelV1.D0Entity,
                null,
                "wrong-schema");
            _ = new DomainPartitionStateV1<string>(residentIdentity, [wrong]);
        }
        catch (InvalidDataException ex) when (ex.Message == "domain.record-schema-mismatch")
        {
            schemaMismatchRejected = true;
        }
        if (!schemaMismatchRejected)
            throw new InvalidOperationException("Partition state must reject a foreign record schema.");

        var digest = new byte[32];
        var partitionRefs = entries.Select(entry => new PartitionStateRefV1(
            new PartitionStateHeaderV1(
                entry,
                revision: 1,
                basisStep: 0,
                detailLevel: DetailLevelV1.D0Entity,
                itemCount: 0,
                canonicalDigest: digest)));
        var directory = new OrderedPartitionDirectoryV1(partitionRefs.Reverse());
        directory.ValidateStandardCompleteness();
        var directoryOrder = directory.CanonicalEntries.Select(static entry => entry.Header.PartitionId.Value).ToArray();
        if (!directoryOrder.SequenceEqual(sortedTokens))
            throw new InvalidOperationException("WorldState partition directory must ignore input order and iterate canonically.");

        var worldHeader = new WorldStateHeaderV1(
            OpaqueId128.Parse("00000000000000000000000000000010"),
            step: 0,
            worldSeedDigest: digest,
            configGeneration: 1,
            masterGeneration: 1,
            rateGeneration: 1);
        if (worldHeader.Schema != new SchemaRefV1("core.world-state") || worldHeader.Step != 0)
            throw new InvalidOperationException("WorldStateHeader canonical schema/state mismatch.");
    }
}
