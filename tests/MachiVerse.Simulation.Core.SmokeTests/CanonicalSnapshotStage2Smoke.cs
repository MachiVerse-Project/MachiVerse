using MachiVerse.Simulation.Core.Configuration;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

internal static class CanonicalSnapshotStage2Smoke
{
    internal static async Task RunAsync()
    {
        VerifyActualPartitionAuthorityBinding();
        VerifyDomainWireRoundTrip();
        VerifyNestedWireRegistryRoundTrip();
        VerifyLogicalManifestWireRoundTrip();
        await VerifyProductionZstdChunkPathAsync();
    }

    private static void VerifyActualPartitionAuthorityBinding()
    {
        var resident = Qa04ReferenceWorldMaterializerV1.MaterializeResidentIdentityLifecycle(1);
        var authority = new DomainPartitionSnapshotAuthorityV1<Qa04ResidentIdentityLifecyclePayloadV1>(
            resident.Partition,
            resident.PartitionHeader,
            static payload => payload.CanonicalDigest());
        Require(authority.ActualItemCount == 1, "Stage 2 authority must expose actual record count.");
        authority.VerifyBoundAuthority();

        var badCount = new PartitionStateHeaderV1(
            resident.Partition.Identity,
            resident.PartitionHeader.Revision,
            resident.PartitionHeader.BasisStep,
            resident.PartitionHeader.DetailLevel,
            itemCount: 2,
            resident.PartitionHeader.CanonicalDigest);
        ExpectInvalid(
            "partition header item count vs actual records",
            () => _ = new DomainPartitionSnapshotAuthorityV1<Qa04ResidentIdentityLifecyclePayloadV1>(
                resident.Partition,
                badCount,
                static payload => payload.CanonicalDigest()));

        var badDigestBytes = resident.PartitionHeader.CanonicalDigest.ToArray();
        badDigestBytes[0] ^= 0x80;
        var badDigest = new PartitionStateHeaderV1(
            resident.Partition.Identity,
            resident.PartitionHeader.Revision,
            resident.PartitionHeader.BasisStep,
            resident.PartitionHeader.DetailLevel,
            resident.PartitionHeader.ItemCount,
            badDigestBytes);
        ExpectInvalid(
            "partition header digest vs actual records",
            () => _ = new DomainPartitionSnapshotAuthorityV1<Qa04ResidentIdentityLifecyclePayloadV1>(
                resident.Partition,
                badDigest,
                static payload => payload.CanonicalDigest()));

        ExpectInvalid(
            "header-only 103-section substitution",
            () => _ = new DomainPartitionSnapshotAuthoritySetV1(
                resident.WorldState,
                new IDomainPartitionSnapshotAuthorityV1[] { authority }));
    }

    private static void VerifyDomainWireRoundTrip()
    {
        const string partitionId = "resident.identity_lifecycle";
        var resident = Qa04ReferenceWorldMaterializerV1.MaterializeResidentIdentityLifecycle(1);
        var authority = new DomainPartitionSnapshotAuthorityV1<Qa04ResidentIdentityLifecyclePayloadV1>(
            resident.Partition,
            resident.PartitionHeader,
            static payload => payload.CanonicalDigest());
        var records = resident.Partition.RecordsCanonical.ToArray();
        var encoded = DomainPartitionSnapshotWireCodecV1.EncodeFragment(
            authority,
            records,
            static payload => StandardResidentPayload(payload));
        var decoded = DomainPartitionSnapshotWireCodecV1.DecodeFragment(partitionId, encoded);

        Require(decoded.Header.PartitionId == resident.PartitionHeader.PartitionId &&
                decoded.Header.OwnerDomain == resident.PartitionHeader.OwnerDomain &&
                decoded.Header.Schema == resident.PartitionHeader.Schema &&
                decoded.Header.Revision == resident.PartitionHeader.Revision &&
                decoded.Header.BasisStep == resident.PartitionHeader.BasisStep &&
                decoded.Header.DetailLevel == resident.PartitionHeader.DetailLevel &&
                decoded.Header.ItemCount == resident.PartitionHeader.ItemCount &&
                decoded.Header.CanonicalDigest.SequenceEqual(resident.PartitionHeader.CanonicalDigest),
            "Domain partition header wire round-trip must preserve frozen authority.");
        Require(decoded.Records.Count == 1 && decoded.Records[0].RecordId == records[0].RecordId,
            "Domain record wire round-trip must preserve canonical record identity.");
        Require(decoded.Records[0].Payload["resident_id"] is OpaqueId128 residentId && residentId == records[0].Payload.ResidentId,
            "Domain payload Id128 field must round-trip through the P4-05 field-number wire.");
        Require(decoded.Records[0].Payload["lifecycle"] is string lifecycle && lifecycle == records[0].Payload.Lifecycle.Value,
            "Domain payload Token field must round-trip through the P4-05 field-number wire.");
        Require(decoded.Records[0].Payload["lineage_generation"] is uint generation && generation == records[0].Payload.LineageGeneration,
            "Domain payload UInt32 field must preserve its exact scalar family.");

        var provider = new DomainPartitionSnapshotSectionProviderV1<Qa04ResidentIdentityLifecyclePayloadV1>(
            partitionId,
            static payload => StandardResidentPayload(payload),
            static payload => ResidentFromStandardPayload(payload),
            static payload => payload.CanonicalDigest());
        var section = provider.Create(authority);
        Require(section.SectionId == partitionId &&
                section.LogicalItemCount == 1 &&
                section.LogicalContentDigest.SequenceEqual(resident.PartitionHeader.CanonicalDigest) &&
                section.Fragments.Count == 1,
            "Domain section provider must emit actual one-record material with frozen canonical digest.");
        var semantic = provider.CreateSemanticVerifier(resident.PartitionHeader).Verify(section.Fragments);
        Require(semantic.LogicalItemCount == 1 &&
                semantic.LogicalContentDigest.SequenceEqual(resident.PartitionHeader.CanonicalDigest),
            "Domain section recovery verifier must reconstruct material and recompute the frozen canonical digest.");

        var badType = StandardResidentPayload(records[0].Payload)
            .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
        badType["lineage_generation"] = (ulong)records[0].Payload.LineageGeneration;
        ExpectInvalid(
            "P4-05 payload scalar kind substitution",
            () => _ = DomainPartitionSnapshotWireCodecV1.EncodePayload(partitionId, badType));

        var missingRequired = StandardResidentPayload(records[0].Payload)
            .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
        missingRequired.Remove("lifecycle");
        ExpectInvalid(
            "P4-05 required payload field omission",
            () => _ = DomainPartitionSnapshotWireCodecV1.EncodePayload(partitionId, missingRequired));

        var tamperedFragment = section.Fragments[0] with
        {
            FirstRecordId = Enumerable.Repeat((byte)0x7f, 16).ToArray(),
        };
        ExpectInvalid(
            "outer fragment record range vs decoded records",
            () => _ = provider.CreateSemanticVerifier(resident.PartitionHeader).Verify(new[] { tamperedFragment }));
    }

    private static void VerifyNestedWireRegistryRoundTrip()
    {
        const string partitionId = "participation.absence_policy";
        const string fieldName = "priority_rules";
        var codec = new DomainNestedSnapshotCodecV1<ParticipationPolicyRuleProbe>(
            partitionId,
            fieldName,
            StandardDomainNestedSnapshotSchemaV1.ParticipationPolicyRule,
            static value => new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["priority"] = value.Priority,
                ["rule_id"] = value.RuleId,
            },
            static fields => new ParticipationPolicyRuleProbe(
                (int)fields["priority"]!,
                (string)fields["rule_id"]!),
            static (left, right) =>
            {
                var priority = left.Priority.CompareTo(right.Priority);
                return priority != 0 ? priority : string.CompareOrdinal(left.RuleId, right.RuleId);
            });
        var registry = new DomainNestedSnapshotCodecRegistryV1(new IDomainNestedSnapshotCodecV1[] { codec });
        ICanonicalDomainNestedValueV1[] source =
        [
            new ParticipationPolicyRuleProbe(-10, "safety"),
            new ParticipationPolicyRuleProbe(20, "routine"),
        ];
        var encoded = DomainNestedSnapshotWireCodecV1.EncodeList(
            partitionId,
            fieldName,
            source,
            registry);
        var decoded = DomainNestedSnapshotWireCodecV1.DecodeList(
            partitionId,
            fieldName,
            encoded,
            registry);

        Require(decoded.Count == 2 &&
                decoded[0] is ParticipationPolicyRuleProbe first && first.Priority == -10 && first.RuleId == "safety" &&
                decoded[1] is ParticipationPolicyRuleProbe second && second.Priority == 20 && second.RuleId == "routine",
            "Explicit nested Snapshot codec must preserve registered participation policy rule fields.");

        var topLevel = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["diver_ref"] = OpaqueId128.Parse("0000000000000000000000000001a001"),
            ["policy_generation"] = 1u,
            [fieldName] = source,
            ["effective_from"] = 10UL,
        };
        var payloadWire = DomainPartitionSnapshotWireCodecV1.EncodePayload(partitionId, topLevel, registry);
        var restoredPayload = DomainPartitionSnapshotWireCodecV1.DecodePayload(partitionId, payloadWire, registry);
        Require(restoredPayload[fieldName] is IReadOnlyList<ICanonicalDomainNestedValueV1> restoredRules &&
                restoredRules.Count == 2 &&
                restoredRules[0] is ParticipationPolicyRuleProbe restoredFirst && restoredFirst.RuleId == "safety" &&
                restoredRules[1] is ParticipationPolicyRuleProbe restoredSecond && restoredSecond.RuleId == "routine",
            "Top-level Domain payload wire must route OrderedNestedList through the registered nested codec.");

        ExpectInvalid(
            "top-level nested payload without codec registry",
            () => _ = DomainPartitionSnapshotWireCodecV1.EncodePayload(partitionId, topLevel));
        ExpectInvalid(
            "top-level nested recovery without codec registry",
            () => _ = DomainPartitionSnapshotWireCodecV1.DecodePayload(partitionId, payloadWire));
        ExpectInvalid(
            "nested ordered list semantic order",
            () => _ = DomainNestedSnapshotWireCodecV1.EncodeList(
                partitionId,
                fieldName,
                source.Reverse().ToArray(),
                registry));
        ExpectInvalid(
            "nested codec unavailable",
            () => _ = DomainNestedSnapshotWireCodecV1.EncodeList(
                partitionId,
                fieldName,
                source,
                new DomainNestedSnapshotCodecRegistryV1(Array.Empty<IDomainNestedSnapshotCodecV1>())));
    }

    private static void VerifyLogicalManifestWireRoundTrip()
    {
        var sections = StandardSnapshotSectionSetV1.SectionIds
            .Select((sectionId, index) => new LogicalSnapshotSection(
                sectionId,
                "snapshot.section-fixture",
                SchemaMajor: 1,
                SchemaMinor: 0,
                LogicalItemCount: 0,
                LogicalContentDigest: HashByte((byte)((index % 250) + 1)),
                Required: true))
            .ToArray();
        var requiredDomains = StandardDomainPartitionRegistry.Entries
            .Select(static entry => entry.OwnerDomain.Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();

        var draft = new LogicalSnapshotManifest(
            PersistenceSchemaMajor: 1,
            PersistenceSchemaMinor: 0,
            WorldId: OpaqueId128.Parse("00000000000000000000000000019001"),
            SnapshotId: OpaqueId128.Parse("00000000000000000000000000019002"),
            SnapshotStep: 42,
            HistoryAnchorSequence: 7,
            HistoryAnchorDigest: HashByte(0x41),
            StateContinuityToken: HashByte(0x42),
            WorldSeed: HashByte(0x43),
            SimulationConfigGeneration: 3,
            SimulationConfigDigest: HashByte(0x44),
            MasterGeneration: 2,
            RequiredDomains: Array.AsReadOnly(requiredDomains),
            Sections: Array.AsReadOnly(sections),
            SnapshotDigest: new byte[32]);
        var manifest = LogicalSnapshotManifestWireCodecV1.WithComputedSnapshotDigest(draft);
        var encoded = LogicalSnapshotManifestWireCodecV1.Encode(manifest);
        var decoded = LogicalSnapshotManifestWireCodecV1.Decode(encoded);

        Require(decoded.WorldId == manifest.WorldId &&
                decoded.SnapshotId == manifest.SnapshotId &&
                decoded.SnapshotStep == manifest.SnapshotStep &&
                decoded.Sections.Count == SnapshotManifestValidation.StandardRequiredSectionCount &&
                decoded.SnapshotDigest.SequenceEqual(manifest.SnapshotDigest),
            "Logical manifest protobuf round-trip must preserve the semantic Snapshot authority.");
        Require(LogicalSnapshotManifestWireCodecV1.ComputeSnapshotDigest(decoded).SequenceEqual(manifest.SnapshotDigest),
            "Decoded logical manifest must recompute the exact mv.snapshot.v1 semantic digest.");

        var tampered = encoded.ToArray();
        tampered[^1] ^= 0x01;
        ExpectInvalid(
            "logical manifest SnapshotDigest tamper",
            () => _ = LogicalSnapshotManifestWireCodecV1.Decode(tampered));

        var addonDraft = draft with
        {
            RequiredAddons = new[]
            {
                new RequiredAddonSnapshotMetadataV1(
                    "addon.stage2-probe",
                    "addon.stage2-metadata",
                    1,
                    0,
                    new byte[] { 1, 2, 3, 4 },
                    HashByte(0x55)),
            },
        };
        var addonManifest = LogicalSnapshotManifestWireCodecV1.WithComputedSnapshotDigest(addonDraft);
        ExpectInvalid(
            "required addon without registered metadata codec",
            () => _ = LogicalSnapshotManifestWireCodecV1.Encode(addonManifest));
    }

    private static IReadOnlyDictionary<string, object?> StandardResidentPayload(Qa04ResidentIdentityLifecyclePayloadV1 payload)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["resident_id"] = payload.ResidentId,
            ["lifecycle"] = payload.Lifecycle.Value,
            ["parent_refs"] = payload.ParentRefs,
            ["lineage_generation"] = payload.LineageGeneration,
            ["profile_token"] = payload.ProfileToken.Value,
        };
        if (payload.BirthStep is { } birthStep) values["birth_step"] = birthStep;
        if (payload.DeathStep is { } deathStep) values["death_step"] = deathStep;
        return values;
    }

    private static Qa04ResidentIdentityLifecyclePayloadV1 ResidentFromStandardPayload(
        IReadOnlyDictionary<string, object?> payload)
    {
        return new Qa04ResidentIdentityLifecyclePayloadV1(
            (OpaqueId128)payload["resident_id"]!,
            new StableToken((string)payload["lifecycle"]!),
            payload.TryGetValue("birth_step", out var birth) ? (ulong?)birth : null,
            payload.TryGetValue("death_step", out var death) ? (ulong?)death : null,
            (IReadOnlyList<PartitionRecordRefV1>)payload["parent_refs"]!,
            (uint)payload["lineage_generation"]!,
            new StableToken((string)payload["profile_token"]!));
    }

    private static async Task VerifyProductionZstdChunkPathAsync()
    {
        var config = new CoreConfigCoordinator().LoadStartup(
            """
            [meta]
            format = "machiverse-config"
            schema_version = "1.0"
            component = "simulation-core"
            """);
        var policy = SnapshotCanonicalCompressionPolicyV1.FromConfig(config);
        Require(policy.Compression == SnapshotCompression.Zstd && policy.ZstdLevel == 3,
            "Canonical Core Config must select Zstd level 3.");

        var fragment = new SnapshotSectionFragmentMaterialV1(
            "resident.identity_lifecycle",
            FragmentIndex: 0,
            FragmentCount: 1,
            FirstRecordId: Enumerable.Repeat((byte)0x01, 16).ToArray(),
            LastRecordId: Enumerable.Repeat((byte)0x01, 16).ToArray(),
            ItemCount: 1,
            FragmentPayload: Enumerable.Range(0, 8192).Select(static value => (byte)(value % 17)).ToArray());
        var payload = new SnapshotChunkFragmentPayloadV1(new[] { fragment });
        var codec = new ZstdSnapshotChunkCompressionCodecV1();
        var root = Path.Combine(Path.GetTempPath(), "machiverse-stage2-zstd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "valid.mvchunk");
            var written = await CanonicalSnapshotProductionChunkFileV1.WriteAsync(path, payload, config, codec);
            Require(written.Compression == SnapshotCompression.Zstd,
                "Production Snapshot chunk writer must honor canonical Zstd policy.");
            Require(written.StoredLength < written.UncompressedLength,
                "Compressible Stage 2 probe should be physically Zstd-compressed.");

            var read = await CanonicalSnapshotChunkFileV1.ReadValidatedAsync(
                path,
                CanonicalSnapshotProductionPhysicalDrainV1.ProductionDecoders(codec));
            Require(read.Header.Compression == SnapshotCompression.Zstd,
                "Validated readback must preserve Zstd framing.");
            Require(read.Payload.Fragments.Count == 1 &&
                    read.Payload.Fragments[0].FragmentPayload.SequenceEqual(fragment.FragmentPayload),
                "Zstd readback must reconstruct exact fragment material.");

            var noCodecRejected = false;
            try
            {
                _ = await CanonicalSnapshotChunkFileV1.ReadValidatedAsync(
                    path,
                    Array.Empty<ISnapshotChunkCompressionDecoderV1>());
            }
            catch (InvalidDataException ex) when (ex.Message == "persistence.snapshot.compression-codec-unavailable:zstd")
            {
                noCodecRejected = true;
            }
            Require(noCodecRejected, "Zstd Snapshot readback must fail closed when the production codec is unavailable.");

            var tampered = Path.Combine(root, "tampered.mvchunk");
            File.Copy(path, tampered);
            var bytes = await File.ReadAllBytesAsync(tampered);
            bytes[^1] ^= 0x01;
            await File.WriteAllBytesAsync(tampered, bytes);
            var tamperRejected = false;
            try
            {
                _ = await CanonicalSnapshotChunkFileV1.ReadValidatedAsync(
                    tampered,
                    CanonicalSnapshotProductionPhysicalDrainV1.ProductionDecoders(codec));
            }
            catch (InvalidDataException ex) when (ex.Message == "persistence.snapshot.stored-digest-mismatch")
            {
                tamperRejected = true;
            }
            Require(tamperRejected, "Physical chunk tamper must be rejected before decompression authority is used.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static byte[] HashByte(byte value) => Enumerable.Repeat(value, 32).ToArray();

    private static void ExpectInvalid(string name, Action action)
    {
        var rejected = false;
        try { action(); }
        catch (InvalidDataException) { rejected = true; }
        Require(rejected, $"Negative test must reject: {name}.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed record ParticipationPolicyRuleProbe(int Priority, string RuleId) : ICanonicalDomainNestedValueV1
    {
        public void ValidateCanonical()
        {
            _ = new StableToken(RuleId);
        }
    }
}
