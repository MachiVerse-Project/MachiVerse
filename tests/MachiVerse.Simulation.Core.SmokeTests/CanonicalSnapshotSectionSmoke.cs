using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

internal static class CanonicalSnapshotSectionSmoke
{
    internal static void Run()
    {
        Require(StandardSnapshotSectionSetV1.SectionIds.Count == 103,
            "Standard snapshot registry must contain exactly 103 sections.");
        Require(StandardSnapshotSectionSetV1.SectionIds.SequenceEqual(
                StandardSnapshotSectionSetV1.SectionIds.OrderBy(static id => id, StringComparer.Ordinal)),
            "Standard snapshot section registry must be ASCII ascending.");

        var worldId = OpaqueId128.Parse("00000000000000000000000000000081");
        var configDigest = SHA256.HashData("snapshot-section-config"u8);
        var state = CreateState(worldId, configDigest);
        var sections = BuildSections(state);
        var validated = CanonicalSnapshotSectionValidationV1.ValidateStandard(sections, state);
        Require(validated.Count == 103, "Validated canonical snapshot section count mismatch.");

        var logical = CanonicalSnapshotSectionValidationV1.ToLogicalSections(sections, state);
        var requiredDomains = StandardDomainPartitionRegistry.Entries
            .Select(static entry => entry.OwnerDomain.Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        var manifest = new LogicalSnapshotManifest(
            PersistenceSchemaMajor: 1,
            PersistenceSchemaMinor: 0,
            worldId,
            OpaqueId128.Parse("00000000000000000000000000000082"),
            SnapshotStep: state.Header.Step,
            HistoryAnchorSequence: 1,
            HistoryAnchorDigest: SHA256.HashData("snapshot-section-history"u8),
            StateContinuityToken: SHA256.HashData("snapshot-section-continuity"u8),
            WorldSeed: SHA256.HashData("snapshot-section-world-seed"u8),
            SimulationConfigGeneration: state.Header.ConfigGeneration,
            SimulationConfigDigest: configDigest,
            MasterGeneration: state.Header.MasterGeneration,
            RequiredDomains: requiredDomains,
            Sections: logical,
            SnapshotDigest: SHA256.HashData("snapshot-section-logical-manifest"u8));
        SnapshotManifestValidation.ValidateLogical(manifest, StandardSnapshotSectionSetV1.SectionIds);

        var fragment = new SnapshotSectionFragmentMaterialV1(
            "resident.identity_lifecycle",
            FragmentIndex: 0,
            FragmentCount: 1,
            FirstRecordId: OpaqueId128.Parse("00000000000000000000000000000010").ToBytes(),
            LastRecordId: OpaqueId128.Parse("00000000000000000000000000000011").ToBytes(),
            ItemCount: 2,
            FragmentPayload: [1, 2, 3, 4, 5]);
        var encoded = SnapshotSectionFragmentWireCodecV1.Encode(fragment);
        var decoded = SnapshotSectionFragmentWireCodecV1.Decode(encoded);
        Require(decoded.SectionId == fragment.SectionId &&
                decoded.FragmentIndex == 0 && decoded.FragmentCount == 1 && decoded.ItemCount == 2 &&
                decoded.FirstRecordId!.SequenceEqual(fragment.FirstRecordId!) &&
                decoded.LastRecordId!.SequenceEqual(fragment.LastRecordId!) &&
                decoded.FragmentPayload.SequenceEqual(fragment.FragmentPayload),
            "SnapshotSectionFragmentV1 protobuf round-trip mismatch.");
        Require(SnapshotSectionFragmentWireCodecV1.Encode(decoded).SequenceEqual(encoded),
            "SnapshotSectionFragmentV1 deterministic re-encode mismatch.");

        var missingRejected = false;
        try
        {
            CanonicalSnapshotSectionValidationV1.ValidateStandard(sections.Skip(1), state);
        }
        catch (InvalidDataException ex) when (ex.Message == "persistence.snapshot.section-count-mismatch")
        {
            missingRejected = true;
        }
        Require(missingRejected, "Missing required snapshot section must be rejected.");

        var residentIndex = sections.FindIndex(static section => section.SectionId == "resident.identity_lifecycle");
        Require(residentIndex >= 0, "Resident snapshot section fixture missing.");
        var resident = sections[residentIndex];

        var schemaMismatch = sections.ToArray();
        schemaMismatch[residentIndex] = resident with { SectionSchema = new SchemaRefV1("domain.environment.soil") };
        RequireRejected(
            () => CanonicalSnapshotSectionValidationV1.ValidateStandard(schemaMismatch, state),
            "persistence.snapshot.partition-schema-mismatch:resident.identity_lifecycle",
            "Domain snapshot section schema mismatch must fail closed.");

        var digestMismatch = sections.ToArray();
        digestMismatch[residentIndex] = resident with { LogicalContentDigest = SHA256.HashData("wrong-resident-digest"u8) };
        RequireRejected(
            () => CanonicalSnapshotSectionValidationV1.ValidateStandard(digestMismatch, state),
            "persistence.snapshot.partition-digest-mismatch:resident.identity_lifecycle",
            "Domain snapshot section digest mismatch must fail closed.");

        var itemMismatch = sections.ToArray();
        itemMismatch[residentIndex] = resident with
        {
            LogicalItemCount = 1,
            Fragments = [resident.Fragments[0] with { ItemCount = 1 }]
        };
        RequireRejected(
            () => CanonicalSnapshotSectionValidationV1.ValidateStandard(itemMismatch, state),
            "persistence.snapshot.partition-item-count-mismatch:resident.identity_lifecycle",
            "Domain snapshot section item count mismatch must fail closed.");

        var overlap = resident with
        {
            LogicalItemCount = 2,
            Fragments =
            [
                new SnapshotSectionFragmentMaterialV1(
                    resident.SectionId, 0, 2,
                    OpaqueId128.Parse("00000000000000000000000000000020").ToBytes(),
                    OpaqueId128.Parse("00000000000000000000000000000021").ToBytes(),
                    1, [1]),
                new SnapshotSectionFragmentMaterialV1(
                    resident.SectionId, 1, 2,
                    OpaqueId128.Parse("00000000000000000000000000000021").ToBytes(),
                    OpaqueId128.Parse("00000000000000000000000000000022").ToBytes(),
                    1, [2]),
            ]
        };
        RequireRejected(
            () => CanonicalSnapshotSectionValidationV1.ValidateStandard(
                sections.Select(section => section.SectionId == resident.SectionId ? overlap : section),
                frozenState: null),
            "persistence.snapshot.fragment-record-range-overlap:resident.identity_lifecycle",
            "Overlapping snapshot fragment record ranges must be rejected.");

        var truncatedRejected = false;
        try
        {
            SnapshotSectionFragmentWireCodecV1.Decode(encoded.AsSpan(0, encoded.Length - 1));
        }
        catch (InvalidDataException)
        {
            truncatedRejected = true;
        }
        Require(truncatedRejected, "Truncated SnapshotSectionFragmentV1 must be rejected.");
    }

    private static List<CanonicalSnapshotSectionMaterialV1> BuildSections(WorldStateV1 state)
    {
        var result = new List<CanonicalSnapshotSectionMaterialV1>(103);
        foreach (var sectionId in StandardSnapshotSectionSetV1.SectionIds)
        {
            if (StandardDomainPartitionRegistry.TryGet(sectionId, out var identity) && identity is not null)
            {
                var header = state.Partitions.Get(sectionId).Header;
                result.Add(new CanonicalSnapshotSectionMaterialV1(
                    sectionId,
                    identity.PartitionSchema,
                    header.ItemCount,
                    header.CanonicalDigest.ToArray(),
                    [new SnapshotSectionFragmentMaterialV1(sectionId, 0, 1, null, null, header.ItemCount, [0x01])]));
            }
            else
            {
                var digest = SHA256.HashData(System.Text.Encoding.ASCII.GetBytes("fixture:" + sectionId));
                result.Add(new CanonicalSnapshotSectionMaterialV1(
                    sectionId,
                    new SchemaRefV1("fixture.snapshot-core-section"),
                    LogicalItemCount: 1,
                    LogicalContentDigest: digest,
                    Fragments: [new SnapshotSectionFragmentMaterialV1(sectionId, 0, 1, null, null, 1, [0x01])]));
            }
        }
        return result;
    }

    private static WorldStateV1 CreateState(OpaqueId128 worldId, byte[] configDigest)
    {
        var partitions = StandardDomainPartitionRegistry.Entries.Select(identity => new PartitionStateRefV1(
            new PartitionStateHeaderV1(
                identity,
                revision: 1,
                basisStep: 7,
                detailLevel: DetailLevelV1.D0Entity,
                itemCount: 0,
                canonicalDigest: SHA256.HashData(System.Text.Encoding.ASCII.GetBytes("partition:" + identity.PartitionId.Value)))));
        return new WorldStateV1(
            new WorldStateHeaderV1(
                worldId,
                step: 7,
                worldSeedDigest: SHA256.HashData("snapshot-section-seed"u8),
                configGeneration: 1,
                masterGeneration: 1,
                rateGeneration: 1),
            new OrderedPartitionDirectoryV1(partitions),
            WorldStateV1.EmptySubstate("core.scheduler-state"),
            WorldStateV1.EmptySubstate("core.operation-state"),
            WorldStateV1.EmptySubstate("core.detail-state"),
            WorldStateV1.EmptySubstate("core.domain-registry-state"),
            configDigest);
    }

    private static void RequireRejected(Action action, string expectedMessage, string failureMessage)
    {
        var rejected = false;
        try
        {
            action();
        }
        catch (InvalidDataException ex) when (ex.Message == expectedMessage)
        {
            rejected = true;
        }
        Require(rejected, failureMessage);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static int FindIndex<T>(this IReadOnlyList<T> values, Predicate<T> predicate)
    {
        for (var i = 0; i < values.Count; i++)
            if (predicate(values[i])) return i;
        return -1;
    }
}
