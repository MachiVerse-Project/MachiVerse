using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

internal static class Sim04DiagnosticSmoke
{
    public static void Run()
    {
        static byte[] PayloadDigest(byte[] payload) => SHA256.HashData(payload);

        var partitionRefs = StandardDomainPartitionRegistry.Entries
            .Select(identity =>
            {
                var state = new DomainPartitionStateV1<byte[]>(identity, []);
                return new PartitionStateRefV1(PartitionStateHeaderV1.CreateCanonical(
                    state,
                    revision: 1,
                    basisStep: 0,
                    detailLevel: DetailLevelV1.D0Entity,
                    PayloadDigest));
            })
            .ToArray();

        var header = new WorldStateHeaderV1(
            OpaqueId128.Parse("00000000000000000000000000000040"),
            step: 10,
            worldSeedDigest: SHA256.HashData("world-seed"u8),
            configGeneration: 2,
            masterGeneration: 3,
            rateGeneration: 1);
        var scheduler = WorldStateV1.EmptySubstate("core.scheduler-state");
        var operations = WorldStateV1.EmptySubstate("core.operation-state");
        var detail = WorldStateV1.EmptySubstate("core.detail-directory");
        var registry = WorldStateV1.EmptySubstate("core.domain-registry-state");
        var configDigest = SHA256.HashData("config"u8);

        var canonicalWorld = new WorldStateV1(
            header,
            new OrderedPartitionDirectoryV1(partitionRefs),
            scheduler,
            operations,
            detail,
            registry,
            configDigest);
        var reversedWorld = new WorldStateV1(
            header,
            new OrderedPartitionDirectoryV1(partitionRefs.Reverse()),
            scheduler,
            operations,
            detail,
            registry,
            configDigest);

        Require(canonicalWorld.Diagnostic.StateDigest.AsSpan().SequenceEqual(reversedWorld.Diagnostic.StateDigest),
            "StateDiagnostic must be independent of partition insertion order.");
        Require(canonicalWorld.Diagnostic.PartitionDigests.Count == 97,
            "StateDiagnostic must contain all 97 standard partition digests.");
        var diagnosticOrder = canonicalWorld.Diagnostic.PartitionDigests.Select(static item => item.Key).ToArray();
        Require(diagnosticOrder.SequenceEqual(diagnosticOrder.OrderBy(static item => item, StringComparer.Ordinal)),
            "StateDiagnostic partition digest map must be canonical PartitionId order.");
        Require(canonicalWorld.Diagnostic.SchemaRegistryDigest.Length == 32 && canonicalWorld.Diagnostic.ConfigDigest.Length == 32,
            "StateDiagnostic schema/config digest width mismatch.");

        var identity = StandardDomainPartitionRegistry.Get("resident.identity_lifecycle");
        var recordId = OpaqueId128.Parse("00000000000000000000000000000041");
        var lineageId = OpaqueId128.Parse("00000000000000000000000000000042");
        var payload = "resident-state"u8.ToArray();
        var baseRecord = new DomainRecordEnvelopeV1<byte[]>(
            recordId,
            identity.RecordSchema,
            revision: 1,
            createdStep: 5,
            retiredStep: null,
            detailLevel: DetailLevelV1.D0Entity,
            lineageRef: null,
            payload);
        var detailRecord = new DomainRecordEnvelopeV1<byte[]>(
            recordId,
            identity.RecordSchema,
            revision: 1,
            createdStep: 5,
            retiredStep: null,
            detailLevel: DetailLevelV1.D1LocalAggregate,
            lineageRef: null,
            payload);
        var lineageRecord = new DomainRecordEnvelopeV1<byte[]>(
            recordId,
            identity.RecordSchema,
            revision: 1,
            createdStep: 5,
            retiredStep: null,
            detailLevel: DetailLevelV1.D0Entity,
            lineageRef: lineageId,
            payload);

        var baseDigest = PartitionStateHeaderV1.CreateCanonical(
            new DomainPartitionStateV1<byte[]>(identity, [baseRecord]),
            revision: 2,
            basisStep: 5,
            detailLevel: DetailLevelV1.D0Entity,
            PayloadDigest).CanonicalDigest;
        var detailDigest = PartitionStateHeaderV1.CreateCanonical(
            new DomainPartitionStateV1<byte[]>(identity, [detailRecord]),
            revision: 2,
            basisStep: 5,
            detailLevel: DetailLevelV1.D0Entity,
            PayloadDigest).CanonicalDigest;
        var lineageDigest = PartitionStateHeaderV1.CreateCanonical(
            new DomainPartitionStateV1<byte[]>(identity, [lineageRecord]),
            revision: 2,
            basisStep: 5,
            detailLevel: DetailLevelV1.D0Entity,
            PayloadDigest).CanonicalDigest;
        Require(!baseDigest.AsSpan().SequenceEqual(detailDigest),
            "Record detail_level must participate in canonical partition digest.");
        Require(!baseDigest.AsSpan().SequenceEqual(lineageDigest),
            "Record lineage_ref must participate in canonical partition digest.");

        var low = new DomainRecordEnvelopeV1<byte[]>(
            OpaqueId128.Parse("00000000000000000000000000000043"),
            identity.RecordSchema,
            1,
            5,
            null,
            DetailLevelV1.D0Entity,
            null,
            [1]);
        var high = new DomainRecordEnvelopeV1<byte[]>(
            OpaqueId128.Parse("00000000000000000000000000000044"),
            identity.RecordSchema,
            1,
            5,
            null,
            DetailLevelV1.D0Entity,
            null,
            [2]);
        var orderedDigest = PartitionStateHeaderV1.CreateCanonical(
            new DomainPartitionStateV1<byte[]>(identity, [low, high]),
            3,
            5,
            DetailLevelV1.D0Entity,
            PayloadDigest).CanonicalDigest;
        var permutedDigest = PartitionStateHeaderV1.CreateCanonical(
            new DomainPartitionStateV1<byte[]>(identity, [high, low]),
            3,
            5,
            DetailLevelV1.D0Entity,
            PayloadDigest).CanonicalDigest;
        Require(orderedDigest.AsSpan().SequenceEqual(permutedDigest),
            "Partition digest must be independent of runtime record insertion order.");

        RunHierarchySmoke();
    }

    private static void RunHierarchySmoke()
    {
        static byte[] CanonicalSliceValue(ulong value)
        {
            var writer = new MvDcborWriter();
            writer.WriteMapStart(1);
            writer.WriteUnsigned(0);
            writer.WriteUnsigned(value);
            return writer.ToArray();
        }

        var worldId = OpaqueId128.Parse("00000000000000000000000000000050");
        const ulong step = 77;
        var residentDomain = new StableToken("resident");
        var physicalDomain = new StableToken("physical-built");
        var residentSliceA = StateDiagnosticHierarchyV1.CreateSliceHash(
            worldId,
            step,
            residentDomain,
            partitionVersion: 1,
            new StableToken("resident.slice-a"),
            CanonicalSliceValue(10));
        var residentSliceB = StateDiagnosticHierarchyV1.CreateSliceHash(
            worldId,
            step,
            residentDomain,
            partitionVersion: 1,
            new StableToken("resident.slice-b"),
            CanonicalSliceValue(20));

        var residentCanonical = StateDiagnosticHierarchyV1.CreateDomainHash(
            worldId,
            step,
            residentDomain,
            partitionVersion: 1,
            [residentSliceA, residentSliceB]);
        var residentReversed = StateDiagnosticHierarchyV1.CreateDomainHash(
            worldId,
            step,
            residentDomain,
            partitionVersion: 1,
            [residentSliceB, residentSliceA]);
        Require(residentCanonical.Hash.AsSpan().SequenceEqual(residentReversed.Hash),
            "Domain diagnostic hash must use canonical DiagnosticSliceKey order.");

        var changedResidentSlice = StateDiagnosticHierarchyV1.CreateSliceHash(
            worldId,
            step,
            residentDomain,
            partitionVersion: 1,
            new StableToken("resident.slice-b"),
            CanonicalSliceValue(21));
        var changedResident = StateDiagnosticHierarchyV1.CreateDomainHash(
            worldId,
            step,
            residentDomain,
            partitionVersion: 1,
            [residentSliceA, changedResidentSlice]);
        Require(!residentCanonical.Hash.AsSpan().SequenceEqual(changedResident.Hash),
            "Authoritative slice content must participate in the domain diagnostic hash.");

        var physicalSlice = StateDiagnosticHierarchyV1.CreateSliceHash(
            worldId,
            step,
            physicalDomain,
            partitionVersion: 1,
            new StableToken("physical.slice-a"),
            CanonicalSliceValue(30));
        var physical = StateDiagnosticHierarchyV1.CreateDomainHash(
            worldId,
            step,
            physicalDomain,
            partitionVersion: 1,
            [physicalSlice]);

        var canonicalRoot = StateDiagnosticHierarchyV1.CreateRootHash(
            worldId,
            step,
            [residentCanonical, physical]);
        var reversedRoot = StateDiagnosticHierarchyV1.CreateRootHash(
            worldId,
            step,
            [physical, residentCanonical]);
        Require(canonicalRoot.Hash.AsSpan().SequenceEqual(reversedRoot.Hash),
            "State diagnostic root must use canonical DomainToken order.");
        Require(canonicalRoot.Domains.Count == 2 &&
                canonicalRoot.Domains[0].DomainToken.Value == "physical-built" &&
                canonicalRoot.Domains[1].DomainToken.Value == "resident",
            "State diagnostic root must expose canonical DomainToken order.");

        var changedRoot = StateDiagnosticHierarchyV1.CreateRootHash(
            worldId,
            step,
            [changedResident, physical]);
        Require(!canonicalRoot.Hash.AsSpan().SequenceEqual(changedRoot.Hash),
            "Domain diagnostic changes must propagate to the state diagnostic root.");

        var duplicateSliceRejected = false;
        try
        {
            StateDiagnosticHierarchyV1.CreateDomainHash(
                worldId,
                step,
                residentDomain,
                partitionVersion: 1,
                [residentSliceA, residentSliceA]);
        }
        catch (InvalidDataException ex) when (ex.Message == "world-state.duplicate-diagnostic-slice-key")
        {
            duplicateSliceRejected = true;
        }
        Require(duplicateSliceRejected, "Duplicate DiagnosticSliceKey must fail closed.");

        var duplicateDomainRejected = false;
        try
        {
            StateDiagnosticHierarchyV1.CreateRootHash(
                worldId,
                step,
                [residentCanonical, residentCanonical]);
        }
        catch (InvalidDataException ex) when (ex.Message == "world-state.duplicate-diagnostic-domain")
        {
            duplicateDomainRejected = true;
        }
        Require(duplicateDomainRejected, "Duplicate DomainToken must fail closed.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
