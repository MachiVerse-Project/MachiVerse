using System.Security.Cryptography;
using System.Text;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

internal static class CoreSnapshotPrimarySectionsSmoke
{
    internal static void Run()
    {
        var worldId = OpaqueId128.Parse("000000000000000000000000000000c1");
        var opA = OpaqueId128.Parse("000000000000000000000000000000d1");
        var opB = OpaqueId128.Parse("000000000000000000000000000000d2");
        var intentA = OpaqueId128.Parse("000000000000000000000000000000e1");
        var intentB = OpaqueId128.Parse("000000000000000000000000000000e2");
        var orderA = new SameStepOrderKey(1, 10, SHA256.HashData("scope-a"u8), 0, intentA);
        var orderB = new SameStepOrderKey(1, 10, SHA256.HashData("scope-b"u8), 1, intentB);
        var scheduled = new[]
        {
            new ScheduledOperationRefV1(opA, 31, orderA),
            new ScheduledOperationRefV1(opB, 31, orderB),
        }
        .OrderBy(static item => item.OrderKey)
        .ThenBy(static item => item.OperationId)
        .ToArray();
        var payloadA = SHA256.HashData("operation-a"u8);
        var payloadB = SHA256.HashData("operation-b"u8);
        var durable = new[]
        {
            new DurableOperationStateV1(
                opA,
                payloadA,
                DurableOperationLifecycleV1.ScheduledDurable,
                2,
                3,
                31,
                null,
                null,
                null,
                null),
            new DurableOperationStateV1(
                opB,
                payloadB,
                DurableOperationLifecycleV1.AcceptedDurable,
                4,
                null,
                null,
                null,
                null,
                null,
                null),
        };
        var detailDigest = HashSuite.DomainHash("mv.test-detail-owner.v1", writer =>
        {
            writer.WriteArrayStart(1); writer.WriteUnsigned(30);
        });
        var registryDigest = HashSuite.DomainHash("mv.test-registry-owner.v1", writer =>
        {
            writer.WriteArrayStart(1); writer.WriteUnsigned(30);
        });
        var configDigest = HashSuite.DomainHash("mv.config.v1", writer =>
        {
            writer.WriteArrayStart(3);
            writer.WriteAsciiText("1.0");
            writer.WriteAsciiText("simulation-core");
            writer.WriteArrayStart(0);
        });
        var schedulerState = OperationSchedulerSubstateV1.Canonicalize(
            new OperationSchedulerStateV1(30, null, scheduled),
            30);
        var operationState = DurableOperationSubstateV1.Canonicalize(durable);
        var state = new WorldStateV1(
            new WorldStateHeaderV1(
                worldId,
                30,
                SHA256.HashData("world-seed"u8),
                1,
                1,
                1,
                SHA256.HashData("state-29"u8)),
            EmptyPartitions(),
            schedulerState,
            operationState,
            new WorldSubstateRefV1(new SchemaRefV1("core.detail-state"), detailDigest),
            new WorldSubstateRefV1(new SchemaRefV1("core.domain-registry-state"), registryDigest),
            configDigest);
        var supplemental = new IFrozenCoreSnapshotOwnerMaterialV1[]
        {
            new FixedOwnerMaterial(
                CoreSnapshotOwnerSectionRegistryV1.DetailDirectory,
                30,
                new SchemaRefV1("core.detail-state"),
                detailDigest),
            new FixedOwnerMaterial(
                CoreSnapshotOwnerSectionRegistryV1.DomainRegistry,
                30,
                new SchemaRefV1("core.domain-registry-state"),
                registryDigest),
            new FixedOwnerMaterial(
                CoreSnapshotOwnerSectionRegistryV1.ConfigState,
                30,
                new SchemaRefV1("config.simulation-core"),
                configDigest),
        };

        var cut = CoreSnapshotOwnerMaterialCutV1.Create(state, durable, scheduled, supplemental);

        // The frozen cut must remain authoritative after callers mutate source arrays.
        Array.Fill(payloadA, (byte)0xff);
        Array.Fill(payloadB, (byte)0xee);
        if (!cut.RecomputeOperationAuthority().CanonicalDigest.AsSpan().SequenceEqual(operationState.CanonicalDigest))
            throw new InvalidOperationException("Durable Operation payload digest was not deep-frozen.");

        var sections = CoreSnapshotPrimarySectionProviderV1.Create(cut);
        if (sections.Count != 3 || sections.Any(static section =>
                section.Fragments.Any(static fragment => fragment.FirstRecordId is not null || fragment.LastRecordId is not null)))
            throw new InvalidOperationException("Core primary section provider shape mismatch.");

        VerifySection(
            sections.Single(static section => section.SectionId == CoreSnapshotOwnerSectionRegistryV1.WorldStateHeader),
            CoreSnapshotPrimarySemanticVerifierV1.WorldStateHeader(30));
        VerifySection(
            sections.Single(static section => section.SectionId == CoreSnapshotOwnerSectionRegistryV1.SchedulerState),
            CoreSnapshotPrimarySemanticVerifierV1.Scheduler(30));
        VerifySection(
            sections.Single(static section => section.SectionId == CoreSnapshotOwnerSectionRegistryV1.OperationState),
            CoreSnapshotPrimarySemanticVerifierV1.Operation(30));

        var headerSection = sections.Single(static section => section.SectionId == CoreSnapshotOwnerSectionRegistryV1.WorldStateHeader);
        var decodedHeader = CoreWorldStateHeaderSnapshotWireCodecV1.Decode(headerSection.Fragments[0].FragmentPayload);
        if (decodedHeader.WorldId != worldId || decodedHeader.Step != 30 || decodedHeader.ConfigGeneration != 1)
            throw new InvalidOperationException("Core world-state header wire round-trip mismatch.");

        var unknownHeader = headerSection.Fragments[0].FragmentPayload.Concat(new byte[] { 0x78, 0x01 }).ToArray();
        RequireRejected(
            () => CoreWorldStateHeaderSnapshotWireCodecV1.Decode(unknownHeader),
            "snapshot-core.header.unknown-field",
            "Unknown authoritative header field must be rejected.");

        var duplicateStep = headerSection.Fragments[0].FragmentPayload.Concat(new byte[] { 0x10, 0x1e }).ToArray();
        RequireRejected(
            () => CoreWorldStateHeaderSnapshotWireCodecV1.Decode(duplicateStep),
            "snapshot-core.header.duplicate-field",
            "Duplicate singular header field must be rejected.");

        var staleVerifier = CoreSnapshotPrimarySemanticVerifierV1.WorldStateHeader(31);
        RequireRejected(
            () => staleVerifier.Verify(headerSection.Fragments),
            "snapshot-core.header.step-mismatch",
            "Core header from a stale Snapshot Step must be rejected.");

        var unsortedScheduledCut = CoreSnapshotOwnerMaterialCutV1.Create(
            state,
            cut.DurableOperations,
            cut.ScheduledOperations.Reverse().ToArray(),
            supplemental);
        RequireRejected(
            () => CoreSnapshotPrimarySectionProviderV1.CreateScheduler(unsortedScheduledCut),
            "snapshot-core.scheduler.noncanonical-order",
            "Noncanonical scheduler owner ordering must be rejected rather than silently sorted by the provider.");

        var unsortedDurableCut = CoreSnapshotOwnerMaterialCutV1.Create(
            state,
            cut.DurableOperations.Reverse().ToArray(),
            cut.ScheduledOperations,
            supplemental);
        RequireRejected(
            () => CoreSnapshotPrimarySectionProviderV1.CreateOperation(unsortedDurableCut),
            "snapshot-core.operation.noncanonical-order",
            "Noncanonical Operation owner ordering must be rejected rather than silently sorted by the provider.");

        var emptySchedulerState = OperationSchedulerSubstateV1.Canonicalize(
            new OperationSchedulerStateV1(30, null, Array.Empty<ScheduledOperationRefV1>()),
            30);
        var emptyOperationState = DurableOperationSubstateV1.Canonicalize(Array.Empty<DurableOperationStateV1>());
        var emptyState = new WorldStateV1(
            state.Header,
            EmptyPartitions(),
            emptySchedulerState,
            emptyOperationState,
            state.DetailState,
            state.DomainRegistryState,
            configDigest);
        var emptyCut = CoreSnapshotOwnerMaterialCutV1.Create(
            emptyState,
            Array.Empty<DurableOperationStateV1>(),
            Array.Empty<ScheduledOperationRefV1>(),
            supplemental);
        var emptyScheduler = CoreSnapshotPrimarySectionProviderV1.CreateScheduler(emptyCut);
        var emptyOperation = CoreSnapshotPrimarySectionProviderV1.CreateOperation(emptyCut);
        if (emptyScheduler.Fragments.Count != 1 || emptyScheduler.Fragments[0].ItemCount != 0 ||
            emptyOperation.Fragments.Count != 1 || emptyOperation.Fragments[0].ItemCount != 0)
            throw new InvalidOperationException("Zero-item Core sections must emit one metadata fragment.");
        VerifySection(emptyScheduler, CoreSnapshotPrimarySemanticVerifierV1.Scheduler(30));
        VerifySection(emptyOperation, CoreSnapshotPrimarySemanticVerifierV1.Operation(30));
    }

    private static OrderedPartitionDirectoryV1 EmptyPartitions()
        => new(StandardDomainPartitionRegistry.Entries.Select(identity => new PartitionStateRefV1(
            new PartitionStateHeaderV1(
                identity,
                1,
                0,
                DetailLevelV1.D0Entity,
                0,
                SHA256.HashData(Encoding.ASCII.GetBytes(identity.PartitionId.Value))))));

    private static void VerifySection(
        CanonicalSnapshotSectionMaterialV1 section,
        SnapshotSectionSemanticVerifierV1 verifier)
    {
        var verified = verifier.Verify(section.Fragments);
        if (verified.LogicalItemCount != section.LogicalItemCount ||
            !CryptographicOperations.FixedTimeEquals(verified.LogicalContentDigest, section.LogicalContentDigest))
            throw new InvalidOperationException($"Semantic verifier mismatch: {section.SectionId}");
    }

    private static void RequireRejected<T>(Func<T> action, string expectedMessage, string failureMessage)
    {
        var rejected = false;
        try { _ = action(); }
        catch (InvalidDataException ex) when (ex.Message == expectedMessage) { rejected = true; }
        if (!rejected) throw new InvalidOperationException(failureMessage);
    }

    private sealed class FixedOwnerMaterial : IFrozenCoreSnapshotOwnerMaterialV1
    {
        private readonly CoreSnapshotOwnerAuthorityV1 _authority;

        internal FixedOwnerMaterial(string sectionId, ulong basisStep, SchemaRefV1 schema, byte[] digest)
        {
            SectionId = sectionId;
            BasisStep = basisStep;
            _authority = new CoreSnapshotOwnerAuthorityV1(schema, digest);
        }

        public string SectionId { get; }
        public ulong BasisStep { get; }

        public CoreSnapshotOwnerAuthorityV1 RecomputeAuthority()
            => new(_authority.Schema, _authority.CanonicalDigest);
    }
}
