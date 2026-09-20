using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.WorldState;

/// <summary>
/// Phase 1 authoritative large-world diagnostic slice hash.
/// This type is intentionally independent from the legacy PartitionStateHeaderV1 canonical digest.
/// </summary>
public sealed class StateDiagnosticSliceHashV1
{
    internal StateDiagnosticSliceHashV1(
        OpaqueId128 worldId,
        ulong step,
        StableToken domainToken,
        uint partitionVersion,
        StableToken sliceKey,
        byte[] hash)
    {
        if (worldId.IsZero) throw new ArgumentException("WorldId ZERO is invalid.", nameof(worldId));
        RequireToken(domainToken, nameof(domainToken));
        if (partitionVersion == 0) throw new ArgumentOutOfRangeException(nameof(partitionVersion));
        RequireToken(sliceKey, nameof(sliceKey));
        RequireHash(hash, nameof(hash));

        WorldId = worldId;
        Step = step;
        DomainToken = domainToken;
        PartitionVersion = partitionVersion;
        SliceKey = sliceKey;
        Hash = hash.ToArray();
    }

    public OpaqueId128 WorldId { get; }
    public ulong Step { get; }
    public StableToken DomainToken { get; }
    public uint PartitionVersion { get; }
    public StableToken SliceKey { get; }
    public byte[] Hash { get; }

    private static void RequireToken(StableToken token, string field)
    {
        if (string.IsNullOrEmpty(token.Value))
            throw new ArgumentException("StableToken cannot be default.", field);
    }

    private static void RequireHash(byte[] hash, string field)
    {
        ArgumentNullException.ThrowIfNull(hash, field);
        if (hash.Length != 32)
            throw new ArgumentException("Diagnostic hash must be exactly 32 bytes.", field);
    }
}

/// <summary>
/// Phase 1 authoritative domain diagnostic hash over stable logical slices.
/// </summary>
public sealed class DomainDiagnosticHashV1
{
    internal DomainDiagnosticHashV1(
        OpaqueId128 worldId,
        ulong step,
        StableToken domainToken,
        uint partitionVersion,
        IReadOnlyList<StateDiagnosticSliceHashV1> slices,
        byte[] hash)
    {
        if (worldId.IsZero) throw new ArgumentException("WorldId ZERO is invalid.", nameof(worldId));
        if (string.IsNullOrEmpty(domainToken.Value))
            throw new ArgumentException("StableToken cannot be default.", nameof(domainToken));
        if (partitionVersion == 0) throw new ArgumentOutOfRangeException(nameof(partitionVersion));
        ArgumentNullException.ThrowIfNull(slices);
        ArgumentNullException.ThrowIfNull(hash);
        if (hash.Length != 32)
            throw new ArgumentException("Diagnostic hash must be exactly 32 bytes.", nameof(hash));

        WorldId = worldId;
        Step = step;
        DomainToken = domainToken;
        PartitionVersion = partitionVersion;
        Slices = slices;
        Hash = hash.ToArray();
    }

    public OpaqueId128 WorldId { get; }
    public ulong Step { get; }
    public StableToken DomainToken { get; }
    public uint PartitionVersion { get; }
    public IReadOnlyList<StateDiagnosticSliceHashV1> Slices { get; }
    public byte[] Hash { get; }
}

/// <summary>
/// Phase 1 authoritative cross-domain diagnostic root.
/// </summary>
public sealed class StateDiagnosticRootV1
{
    internal StateDiagnosticRootV1(
        OpaqueId128 worldId,
        ulong step,
        IReadOnlyList<DomainDiagnosticHashV1> domains,
        byte[] hash)
    {
        if (worldId.IsZero) throw new ArgumentException("WorldId ZERO is invalid.", nameof(worldId));
        ArgumentNullException.ThrowIfNull(domains);
        ArgumentNullException.ThrowIfNull(hash);
        if (hash.Length != 32)
            throw new ArgumentException("Diagnostic hash must be exactly 32 bytes.", nameof(hash));

        WorldId = worldId;
        Step = step;
        Domains = domains;
        Hash = hash.ToArray();
    }

    public OpaqueId128 WorldId { get; }
    public ulong Step { get; }
    public IReadOnlyList<DomainDiagnosticHashV1> Domains { get; }
    public byte[] Hash { get; }
}

/// <summary>
/// Implements the P1-07 logical slice -> domain -> root diagnostic hierarchy.
///
/// The concrete slice partition schema remains owned by each domain. Callers provide one canonical
/// authoritative slice value; this class only owns the cross-cutting hash envelope and ordering.
/// </summary>
public static class StateDiagnosticHierarchyV1
{
    private const string SliceHashLabel = "mv.state-diagnostic-slice.v1";
    private const string DomainHashLabel = "mv.state-diagnostic-domain.v1";
    private const string RootHashLabel = "mv.state-diagnostic-root.v1";

    public static StateDiagnosticSliceHashV1 CreateSliceHash(
        OpaqueId128 worldId,
        ulong step,
        StableToken domainToken,
        uint partitionVersion,
        StableToken sliceKey,
        ReadOnlySpan<byte> canonicalAuthoritativeSlice)
    {
        RequireContext(worldId, domainToken, partitionVersion);
        if (string.IsNullOrEmpty(sliceKey.Value))
            throw new ArgumentException("StableToken cannot be default.", nameof(sliceKey));
        if (canonicalAuthoritativeSlice.IsEmpty)
            throw new ArgumentException("Canonical authoritative slice cannot be empty.", nameof(canonicalAuthoritativeSlice));

        var writer = new MvDcborWriter();
        writer.WriteMapStart(6);
        writer.WriteUnsigned(0); writer.WriteBytes(worldId.ToBytes());
        writer.WriteUnsigned(1); writer.WriteUnsigned(step);
        writer.WriteUnsigned(2); writer.WriteAsciiText(domainToken.Value);
        writer.WriteUnsigned(3); writer.WriteUnsigned(partitionVersion);
        writer.WriteUnsigned(4); writer.WriteAsciiText(sliceKey.Value);
        writer.WriteUnsigned(5); writer.WriteCanonicalValue(canonicalAuthoritativeSlice);

        return new StateDiagnosticSliceHashV1(
            worldId,
            step,
            domainToken,
            partitionVersion,
            sliceKey,
            HashSuite.DomainHashCanonicalValue(SliceHashLabel, writer.ToArray()));
    }

    public static DomainDiagnosticHashV1 CreateDomainHash(
        OpaqueId128 worldId,
        ulong step,
        StableToken domainToken,
        uint partitionVersion,
        IEnumerable<StateDiagnosticSliceHashV1> slices)
    {
        RequireContext(worldId, domainToken, partitionVersion);
        ArgumentNullException.ThrowIfNull(slices);

        var ordered = slices
            .OrderBy(static slice => slice.SliceKey.Value, StringComparer.Ordinal)
            .ToArray();

        for (var index = 0; index < ordered.Length; index++)
        {
            var slice = ordered[index] ?? throw new InvalidDataException("world-state.diagnostic-slice-null");
            if (slice.WorldId != worldId ||
                slice.Step != step ||
                slice.DomainToken != domainToken ||
                slice.PartitionVersion != partitionVersion)
            {
                throw new InvalidDataException("world-state.diagnostic-slice-context-drift");
            }

            if (index != 0 && ordered[index - 1].SliceKey == slice.SliceKey)
                throw new InvalidDataException("world-state.duplicate-diagnostic-slice-key");
        }

        var hash = HashSuite.DomainHash(DomainHashLabel, writer =>
        {
            writer.WriteMapStart(5);
            writer.WriteUnsigned(0); writer.WriteBytes(worldId.ToBytes());
            writer.WriteUnsigned(1); writer.WriteUnsigned(step);
            writer.WriteUnsigned(2); writer.WriteAsciiText(domainToken.Value);
            writer.WriteUnsigned(3); writer.WriteUnsigned(partitionVersion);
            writer.WriteUnsigned(4);
            writer.WriteArrayStart((ulong)ordered.Length);
            foreach (var slice in ordered)
            {
                writer.WriteMapStart(2);
                writer.WriteUnsigned(0); writer.WriteAsciiText(slice.SliceKey.Value);
                writer.WriteUnsigned(1); writer.WriteBytes(slice.Hash);
            }
        });

        return new DomainDiagnosticHashV1(
            worldId,
            step,
            domainToken,
            partitionVersion,
            Array.AsReadOnly(ordered),
            hash);
    }

    public static StateDiagnosticRootV1 CreateRootHash(
        OpaqueId128 worldId,
        ulong step,
        IEnumerable<DomainDiagnosticHashV1> domains)
    {
        if (worldId.IsZero) throw new ArgumentException("WorldId ZERO is invalid.", nameof(worldId));
        ArgumentNullException.ThrowIfNull(domains);

        var ordered = domains
            .OrderBy(static domain => domain.DomainToken.Value, StringComparer.Ordinal)
            .ToArray();

        for (var index = 0; index < ordered.Length; index++)
        {
            var domain = ordered[index] ?? throw new InvalidDataException("world-state.diagnostic-domain-null");
            if (domain.WorldId != worldId || domain.Step != step)
                throw new InvalidDataException("world-state.diagnostic-domain-context-drift");
            if (index != 0 && ordered[index - 1].DomainToken == domain.DomainToken)
                throw new InvalidDataException("world-state.duplicate-diagnostic-domain");
        }

        var hash = HashSuite.DomainHash(RootHashLabel, writer =>
        {
            writer.WriteMapStart(3);
            writer.WriteUnsigned(0); writer.WriteBytes(worldId.ToBytes());
            writer.WriteUnsigned(1); writer.WriteUnsigned(step);
            writer.WriteUnsigned(2);
            writer.WriteArrayStart((ulong)ordered.Length);
            foreach (var domain in ordered)
            {
                writer.WriteMapStart(3);
                writer.WriteUnsigned(0); writer.WriteAsciiText(domain.DomainToken.Value);
                writer.WriteUnsigned(1); writer.WriteUnsigned(domain.PartitionVersion);
                writer.WriteUnsigned(2); writer.WriteBytes(domain.Hash);
            }
        });

        return new StateDiagnosticRootV1(
            worldId,
            step,
            Array.AsReadOnly(ordered),
            hash);
    }

    private static void RequireContext(
        OpaqueId128 worldId,
        StableToken domainToken,
        uint partitionVersion)
    {
        if (worldId.IsZero) throw new ArgumentException("WorldId ZERO is invalid.", nameof(worldId));
        if (string.IsNullOrEmpty(domainToken.Value))
            throw new ArgumentException("StableToken cannot be default.", nameof(domainToken));
        if (partitionVersion == 0) throw new ArgumentOutOfRangeException(nameof(partitionVersion));
    }
}
