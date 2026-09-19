using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

internal static class CanonicalSnapshotNegativeInvariantInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        VerifyDomainRecoveryDigestMismatchRejected();
        VerifyDomainFragmentRecordOrderRejected();
        VerifyPhysicalMappingContinuationAndNegativeCases();
    }

    private static void VerifyDomainRecoveryDigestMismatchRejected()
    {
        var resident = Qa04ReferenceWorldMaterializerV1.MaterializeResidentIdentityLifecycle(1);
        var authority = new DomainPartitionSnapshotAuthorityV1<ResidentIdentityLifecyclePayloadV1>(
            resident.Partition,
            resident.PartitionHeader,
            static payload => payload.CanonicalDigest());
        var provider = new DomainPartitionSnapshotSectionProviderV1<ResidentIdentityLifecyclePayloadV1>(
            ResidentIdentityLifecyclePayloadV1.PartitionId,
            static payload => payload.ToStandardPayload(),
            static payload => ResidentIdentityLifecyclePayloadV1.FromStandardPayload(payload),
            static payload => payload.CanonicalDigest());
        var section = provider.Create(authority);

        var badDigest = resident.PartitionHeader.CanonicalDigest.ToArray();
        badDigest[0] ^= 0x80;
        var badHeader = new PartitionStateHeaderV1(
            resident.Partition.Identity,
            resident.PartitionHeader.Revision,
            resident.PartitionHeader.BasisStep,
            resident.PartitionHeader.DetailLevel,
            resident.PartitionHeader.ItemCount,
            badDigest);

        ExpectInvalid(
            "recovered canonical partition digest mismatch",
            () => _ = provider.CreateSemanticVerifier(badHeader).Verify(section.Fragments),
            "persistence.snapshot.partition-restored-header-mismatch:");
    }

    private static void VerifyDomainFragmentRecordOrderRejected()
    {
        var resident = Qa04ReferenceWorldMaterializerV1.MaterializeResidentIdentityLifecycle(2);
        var authority = new DomainPartitionSnapshotAuthorityV1<ResidentIdentityLifecyclePayloadV1>(
            resident.Partition,
            resident.PartitionHeader,
            static payload => payload.CanonicalDigest());
        var reversed = resident.Partition.RecordsCanonical.Reverse().ToArray();

        ExpectInvalid(
            "domain fragment record ordering",
            () => _ = DomainPartitionSnapshotWireCodecV1.EncodeFragment(
                authority,
                reversed,
                static payload => payload.ToStandardPayload()),
            "persistence.snapshot.domain-wire:fragment-record-order:");
    }

    private static void VerifyPhysicalMappingContinuationAndNegativeCases()
    {
        var logical = BuildLogicalManifest();
        SnapshotManifestValidation.ValidateLogical(logical, StandardSnapshotSectionSetV1.SectionIds);
        var last = StandardSnapshotSectionSetV1.SectionIds.Count - 1;

        SnapshotManifestValidation.ValidatePhysicalMapping(
            logical,
            new[]
            {
                Descriptor(0, 0, 1),
                Descriptor(1, 1, last),
            });

        ExpectInvalid(
            "physical mapping skipped logical section",
            () => SnapshotManifestValidation.ValidatePhysicalMapping(
                logical,
                new[]
                {
                    Descriptor(0, 0, 0),
                    Descriptor(1, 2, last),
                }),
            "persistence.snapshot.chunk-section-coverage-gap");

        ExpectInvalid(
            "physical mapping backward overlap",
            () => SnapshotManifestValidation.ValidatePhysicalMapping(
                logical,
                new[]
                {
                    Descriptor(0, 0, 2),
                    Descriptor(1, 1, last),
                }),
            "persistence.snapshot.chunk-section-coverage-gap");

        ExpectInvalid(
            "physical mapping incomplete final coverage",
            () => SnapshotManifestValidation.ValidatePhysicalMapping(
                logical,
                new[] { Descriptor(0, 0, last - 1) }),
            "persistence.snapshot.chunk-section-coverage-incomplete");

        ExpectInvalid(
            "physical mapping chunk index gap",
            () => SnapshotManifestValidation.ValidatePhysicalMapping(
                logical,
                new[]
                {
                    Descriptor(0, 0, 0),
                    Descriptor(2, 1, last),
                }),
            "persistence.snapshot.chunk-index-gap");
    }

    private static LogicalSnapshotManifest BuildLogicalManifest()
    {
        var sections = StandardSnapshotSectionSetV1.SectionIds
            .Select((sectionId, index) => new LogicalSnapshotSection(
                sectionId,
                "snapshot.negative-fixture",
                SchemaMajor: 1,
                SchemaMinor: 0,
                LogicalItemCount: 0,
                LogicalContentDigest: HashByte(checked((byte)((index % 250) + 1))),
                Required: true))
            .ToArray();
        var domains = StandardDomainPartitionRegistry.Entries
            .Select(static entry => entry.OwnerDomain.Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();

        return new LogicalSnapshotManifest(
            PersistenceSchemaMajor: 1,
            PersistenceSchemaMinor: 0,
            WorldId: OpaqueId128.Parse("0000000000000000000000000000c101"),
            SnapshotId: OpaqueId128.Parse("0000000000000000000000000000c102"),
            SnapshotStep: 30,
            HistoryAnchorSequence: 1,
            HistoryAnchorDigest: HashByte(0x41),
            StateContinuityToken: HashByte(0x42),
            WorldSeed: HashByte(0x43),
            SimulationConfigGeneration: 1,
            SimulationConfigDigest: HashByte(0x44),
            MasterGeneration: 1,
            RequiredDomains: Array.AsReadOnly(domains),
            Sections: Array.AsReadOnly(sections),
            SnapshotDigest: HashByte(0x45));
    }

    private static PhysicalSnapshotChunkDescriptor Descriptor(uint chunkIndex, int firstSectionIndex, int lastSectionIndex)
        => new(
            ChunkIndex: chunkIndex,
            FirstSectionId: StandardSnapshotSectionSetV1.SectionIds[firstSectionIndex],
            LastSectionId: StandardSnapshotSectionSetV1.SectionIds[lastSectionIndex],
            UncompressedLength: 1,
            StoredLength: 1,
            Compression: SnapshotCompression.None,
            LogicalPayloadDigest: HashByte(checked((byte)(0x50 + chunkIndex))),
            StoredPayloadDigest: HashByte(checked((byte)(0x60 + chunkIndex))),
            RelativePath: SnapshotChunkFile.RelativePath(chunkIndex));

    private static byte[] HashByte(byte value)
        => Enumerable.Repeat(value, 32).ToArray();

    private static void ExpectInvalid(string name, Action action, string expectedPrefix)
    {
        try
        {
            action();
        }
        catch (InvalidDataException ex) when (ex.Message.StartsWith(expectedPrefix, StringComparison.Ordinal))
        {
            return;
        }

        throw new InvalidOperationException($"Expected InvalidDataException for {name} with prefix {expectedPrefix}.");
    }
}
