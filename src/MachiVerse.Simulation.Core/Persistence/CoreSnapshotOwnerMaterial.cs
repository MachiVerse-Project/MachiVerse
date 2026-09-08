using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

/// <summary>
/// Authority recomputed by a Core snapshot owner from its frozen reconstructable material.
/// This is not a snapshot wire payload. Exact Core section wire schemas are tracked separately.
/// </summary>
public sealed class CoreSnapshotOwnerAuthorityV1
{
    public CoreSnapshotOwnerAuthorityV1(SchemaRefV1 schema, ReadOnlySpan<byte> canonicalDigest)
    {
        if (canonicalDigest.Length != 32)
            throw new ArgumentException("Core snapshot owner authority digest must be 32 bytes.", nameof(canonicalDigest));
        Schema = schema;
        CanonicalDigest = canonicalDigest.ToArray();
    }

    public SchemaRefV1 Schema { get; }
    public byte[] CanonicalDigest { get; }
}

/// <summary>
/// Frozen material owned by a Core subsystem whose reconstructable value is not carried directly
/// by WorldStateV1. Implementations must own an immutable/copy-on-freeze value and recompute
/// authority from that value; returning a digest copied from WorldStateV1 is not a valid provider.
/// </summary>
public interface IFrozenCoreSnapshotOwnerMaterialV1
{
    string SectionId { get; }
    ulong BasisStep { get; }
    CoreSnapshotOwnerAuthorityV1 RecomputeAuthority();
}

public static class CoreSnapshotOwnerSectionRegistryV1
{
    public const string WorldStateHeader = "core.world-state-header";
    public const string SchedulerState = "core.scheduler-state";
    public const string OperationState = "core.operation-state";
    public const string DetailDirectory = "core.detail-directory";
    public const string DomainRegistry = "core.domain-registry";
    public const string ConfigState = "core.config-state";

    private static readonly string[] SupplementalIds =
    [
        ConfigState,
        DetailDirectory,
        DomainRegistry,
    ];

    public static IReadOnlyList<string> RequiredSupplementalSectionIds { get; }
        = Array.AsReadOnly(SupplementalIds.OrderBy(static value => value, StringComparer.Ordinal).ToArray());
}

/// <summary>
/// Immutable logical owner cut captured at the same Step boundary as RunningSnapshotCutV1.
/// It deliberately carries typed/runtime owner material only; persistence serialization waits for
/// the exact P4-04 Core section wire amendment rather than inventing bytes from canonical digests.
/// </summary>
public sealed class CoreSnapshotOwnerMaterialCutV1
{
    private readonly IReadOnlyDictionary<string, IFrozenCoreSnapshotOwnerMaterialV1> _supplemental;

    private CoreSnapshotOwnerMaterialCutV1(
        WorldStateHeaderV1 header,
        IReadOnlyList<DurableOperationStateV1> durableOperations,
        IReadOnlyList<ScheduledOperationRefV1> scheduledOperations,
        IReadOnlyDictionary<string, IFrozenCoreSnapshotOwnerMaterialV1> supplemental)
    {
        Header = CloneHeader(header);
        DurableOperations = Array.AsReadOnly(durableOperations.ToArray());
        ScheduledOperations = Array.AsReadOnly(scheduledOperations.ToArray());
        _supplemental = supplemental;
    }

    public WorldStateHeaderV1 Header { get; }
    public ulong BasisStep => Header.Step;
    public IReadOnlyList<DurableOperationStateV1> DurableOperations { get; }
    public IReadOnlyList<ScheduledOperationRefV1> ScheduledOperations { get; }
    public IReadOnlyCollection<string> SupplementalSectionIds => _supplemental.Keys.OrderBy(static value => value, StringComparer.Ordinal).ToArray();

    public IFrozenCoreSnapshotOwnerMaterialV1 GetSupplemental(string sectionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionId);
        return _supplemental.TryGetValue(sectionId, out var material)
            ? material
            : throw new KeyNotFoundException($"Frozen Core snapshot owner material is missing: {sectionId}");
    }

    public WorldSubstateRefV1 RecomputeSchedulerAuthority()
    {
        var scheduler = new OperationSchedulerStateV1(
            nextSchedulableStep: BasisStep,
            freezeStep: null,
            scheduled: ScheduledOperations);
        return OperationSchedulerSubstateV1.Canonicalize(scheduler, BasisStep);
    }

    public WorldSubstateRefV1 RecomputeOperationAuthority()
        => DurableOperationSubstateV1.Canonicalize(DurableOperations);

    public static CoreSnapshotOwnerMaterialCutV1 Create(
        WorldStateV1 frozenState,
        IReadOnlyList<DurableOperationStateV1> durableOperations,
        IReadOnlyList<ScheduledOperationRefV1> scheduledOperations,
        IEnumerable<IFrozenCoreSnapshotOwnerMaterialV1> supplementalOwnerMaterial)
    {
        ArgumentNullException.ThrowIfNull(frozenState);
        ArgumentNullException.ThrowIfNull(durableOperations);
        ArgumentNullException.ThrowIfNull(scheduledOperations);
        ArgumentNullException.ThrowIfNull(supplementalOwnerMaterial);

        var supplemental = supplementalOwnerMaterial
            .Select(static material => material ?? throw new ArgumentNullException(nameof(supplementalOwnerMaterial)))
            .ToArray();
        var required = CoreSnapshotOwnerSectionRegistryV1.RequiredSupplementalSectionIds;
        if (supplemental.Length != required.Count)
            throw new InvalidDataException("snapshot-running.core-owner-material-count-mismatch");
        if (supplemental.Select(static material => material.SectionId).Distinct(StringComparer.Ordinal).Count() != supplemental.Length)
            throw new InvalidDataException("snapshot-running.core-owner-material-duplicate");

        var byId = new Dictionary<string, IFrozenCoreSnapshotOwnerMaterialV1>(StringComparer.Ordinal);
        foreach (var material in supplemental)
        {
            if (!required.Contains(material.SectionId, StringComparer.Ordinal))
                throw new InvalidDataException($"snapshot-running.core-owner-material-unknown:{material.SectionId}");
            if (material.BasisStep != frozenState.Header.Step)
                throw new InvalidDataException($"snapshot-running.core-owner-material-step-mismatch:{material.SectionId}");
            byId.Add(material.SectionId, material);
        }
        foreach (var sectionId in required)
        {
            if (!byId.ContainsKey(sectionId))
                throw new InvalidDataException($"snapshot-running.core-owner-material-missing:{sectionId}");
        }

        var cut = new CoreSnapshotOwnerMaterialCutV1(
            frozenState.Header,
            durableOperations,
            scheduledOperations,
            byId);
        RequireSubstate(
            cut.RecomputeSchedulerAuthority(),
            frozenState.SchedulerState,
            "snapshot-running.scheduler-owner-material-mismatch");
        RequireSubstate(
            cut.RecomputeOperationAuthority(),
            frozenState.OperationState,
            "snapshot-running.operation-owner-material-mismatch");

        foreach (var sectionId in required)
        {
            var authority = byId[sectionId].RecomputeAuthority()
                ?? throw new InvalidDataException($"snapshot-running.core-owner-material-authority-null:{sectionId}");
            var expected = ExpectedAuthority(frozenState, sectionId);
            if (authority.Schema != expected.Schema ||
                !CryptographicOperations.FixedTimeEquals(authority.CanonicalDigest, expected.CanonicalDigest))
                throw new InvalidDataException($"snapshot-running.core-owner-material-authority-mismatch:{sectionId}");
        }

        return cut;
    }

    private static CoreSnapshotOwnerAuthorityV1 ExpectedAuthority(WorldStateV1 state, string sectionId)
        => sectionId switch
        {
            CoreSnapshotOwnerSectionRegistryV1.DetailDirectory =>
                new CoreSnapshotOwnerAuthorityV1(state.DetailState.Schema, state.DetailState.CanonicalDigest),
            CoreSnapshotOwnerSectionRegistryV1.DomainRegistry =>
                new CoreSnapshotOwnerAuthorityV1(state.DomainRegistryState.Schema, state.DomainRegistryState.CanonicalDigest),
            CoreSnapshotOwnerSectionRegistryV1.ConfigState =>
                new CoreSnapshotOwnerAuthorityV1(new SchemaRefV1("config.simulation-core"), state.Diagnostic.ConfigDigest),
            _ => throw new InvalidDataException($"snapshot-running.core-owner-material-unknown:{sectionId}"),
        };

    private static void RequireSubstate(WorldSubstateRefV1 actual, WorldSubstateRefV1 expected, string message)
    {
        if (actual.Schema != expected.Schema ||
            !CryptographicOperations.FixedTimeEquals(actual.CanonicalDigest, expected.CanonicalDigest))
            throw new InvalidDataException(message);
    }

    private static WorldStateHeaderV1 CloneHeader(WorldStateHeaderV1 header)
        => new(
            header.WorldId,
            header.Step,
            header.WorldSeedDigest,
            header.ConfigGeneration,
            header.MasterGeneration,
            header.RateGeneration,
            header.PreviousStateDigest);
}
