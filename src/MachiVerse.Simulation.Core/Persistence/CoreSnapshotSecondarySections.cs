using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using MachiVerse.Simulation.Core.Configuration;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

internal static class CoreSnapshotSecondaryProtoV1
{
    internal static void WriteInt32(Stream stream, int field, int value)
        => WriteVarintField(stream, field, unchecked((ulong)(long)value));

    internal static void WriteSInt64(Stream stream, int field, long value)
    {
        var encoded = unchecked((ulong)((value << 1) ^ (value >> 63)));
        WriteVarintField(stream, field, encoded);
    }

    internal static void WriteBool(Stream stream, int field, bool value)
        => WriteVarintField(stream, field, value ? 1UL : 0UL);

    internal static int DecodeInt32(ulong value) => unchecked((int)value);

    internal static long DecodeSInt64(ulong value)
        => unchecked((long)(value >> 1) ^ -((long)value & 1));

    internal static bool DecodeBool(ulong value)
        => value switch
        {
            0 => false,
            1 => true,
            _ => throw new InvalidDataException("snapshot-core.config.bool-noncanonical"),
        };

    private static void WriteVarintField(Stream stream, int field, ulong value)
    {
        if (field <= 0) throw new ArgumentOutOfRangeException(nameof(field));
        WriteVarUInt64(stream, checked((ulong)field << 3));
        WriteVarUInt64(stream, value);
    }

    private static void WriteVarUInt64(Stream stream, ulong value)
    {
        while (value >= 0x80)
        {
            stream.WriteByte((byte)((value & 0x7f) | 0x80));
            value >>= 7;
        }
        stream.WriteByte((byte)value);
    }
}

public sealed class FrozenDetailDirectorySnapshotOwnerV1 : IFrozenCoreSnapshotOwnerMaterialV1
{
    private FrozenDetailDirectorySnapshotOwnerV1(ulong basisStep, DetailDirectoryV1 directory)
    {
        BasisStep = basisStep;
        Directory = Clone(directory);
    }

    public string SectionId => CoreSnapshotOwnerSectionRegistryV1.DetailDirectory;
    public ulong BasisStep { get; }
    public DetailDirectoryV1 Directory { get; }

    public static FrozenDetailDirectorySnapshotOwnerV1 Freeze(ulong basisStep, DetailDirectoryV1 directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        return new FrozenDetailDirectorySnapshotOwnerV1(basisStep, directory);
    }

    public CoreSnapshotOwnerAuthorityV1 RecomputeAuthority()
    {
        var authority = DetailDirectorySubstateV1.Canonicalize(Directory);
        return new CoreSnapshotOwnerAuthorityV1(authority.Schema, authority.CanonicalDigest);
    }

    private static DetailDirectoryV1 Clone(DetailDirectoryV1 directory)
    {
        var regions = directory.Regions.Select(region => new DetailRegionStateV1(
            region.DetailRegionId,
            region.SpatialScopeRef,
            region.LevelByDomain.Select(level => new KeyValuePair<StableToken, DetailLevelV1>(level.Key, level.Value)),
            region.LineageGeneration,
            region.LastTransitionStep,
            region.ActiveGuards.ToArray())).ToArray();

        var pending = directory.PendingTransitions.Select(candidate =>
        {
            var request = new DetailTransitionRequestV1(
                candidate.DetailRegionId,
                candidate.DomainToken,
                candidate.CurrentLevel,
                candidate.TargetLevel,
                candidate.RequiredEffectiveStep,
                candidate.SemanticPriority,
                candidate.TriggerSource,
                candidate.TriggerId,
                candidate.TriggerObservedStep,
                candidate.EstimatedRecordCount);
            return new DetailTransitionCandidateV1(request);
        }).ToArray();
        return new DetailDirectoryV1(regions, pending);
    }
}

public sealed class FrozenCoreConfigSnapshotOwnerV1 : IFrozenCoreSnapshotOwnerMaterialV1
{
    private FrozenCoreConfigSnapshotOwnerV1(ulong basisStep, EffectiveCoreConfig config)
    {
        BasisStep = basisStep;
        Config = CoreConfigSnapshotRehydrationV1.Rehydrate(config.Generation, config.Fields);
        if (!CryptographicOperations.FixedTimeEquals(Config.Digest, config.Digest))
            throw new InvalidDataException("snapshot-core.config.owner-digest-mismatch");
    }

    public string SectionId => CoreSnapshotOwnerSectionRegistryV1.ConfigState;
    public ulong BasisStep { get; }
    public EffectiveCoreConfig Config { get; }

    public static FrozenCoreConfigSnapshotOwnerV1 Freeze(ulong basisStep, EffectiveCoreConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return new FrozenCoreConfigSnapshotOwnerV1(basisStep, config);
    }

    public CoreSnapshotOwnerAuthorityV1 RecomputeAuthority()
    {
        var recomputed = CoreConfigSnapshotRehydrationV1.Rehydrate(Config.Generation, Config.Fields);
        return new CoreSnapshotOwnerAuthorityV1(new SchemaRefV1("config.simulation-core"), recomputed.Digest);
    }
}

public static class CoreConfigSnapshotRehydrationV1
{
    public static EffectiveCoreConfig Rehydrate(ulong generation, IReadOnlyDictionary<string, object> fields)
    {
        if (generation == 0) throw new InvalidDataException("snapshot-core.config.generation-zero");
        ArgumentNullException.ThrowIfNull(fields);
        if (fields.Count != CoreConfigSchema.Fields.Count ||
            !fields.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(CoreConfigSchema.Fields.Keys))
            throw new InvalidDataException("snapshot-core.config.field-set-mismatch");

        EffectiveCoreConfig loaded;
        try
        {
            loaded = new CoreConfigCoordinator().LoadStartup(BuildToml(fields));
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
        {
            throw new InvalidDataException("snapshot-core.config.invalid-fields", ex);
        }

        foreach (var path in CoreConfigSchema.Fields.Keys)
        {
            if (!Equals(fields[path], loaded.Fields[path]))
                throw new InvalidDataException("snapshot-core.config.noncanonical-normalization");
        }

        var frozen = new ReadOnlyDictionary<string, object>(
            loaded.Fields.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal));
        return new EffectiveCoreConfig(
            generation,
            frozen,
            loaded.Digest.ToArray(),
            loaded.NormalizedToml);
    }

    private static string BuildToml(IReadOnlyDictionary<string, object> fields)
    {
        var builder = new StringBuilder();
        builder.AppendLine("meta.format = \"machiverse-config\"");
        builder.Append("meta.schema_version = \"").Append(CoreConfigSchema.SchemaVersion).AppendLine("\"");
        builder.Append("meta.component = \"").Append(CoreConfigSchema.Component).AppendLine("\"");
        foreach (var (path, value) in fields.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            _ = new StableToken(path);
            builder.Append(path).Append(" = ").Append(Format(value)).Append('\n');
        }
        return builder.ToString();
    }

    private static string Format(object value) => value switch
    {
        long number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
        bool boolean => boolean ? "true" : "false",
        string text => $"\"{text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"",
        _ => throw new InvalidDataException("snapshot-core.config.value-kind-invalid"),
    };
}

internal sealed record CoreDetailSnapshotFragmentV1(ulong BasisStep, IReadOnlyList<object> Items);

public static class CoreDetailDirectorySnapshotWireCodecV1
{
    public static byte[] Encode(ulong basisStep, IReadOnlyList<object> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        ValidateCanonical(items);
        using var stream = new MemoryStream();
        CoreSnapshotProtoV1.WriteUInt64(stream, 1, basisStep);
        foreach (var item in items)
        {
            var encoded = item switch
            {
                DetailRegionStateV1 region => EncodeItemRegion(region),
                DetailTransitionCandidateV1 transition => EncodeItemTransition(transition),
                _ => throw new InvalidDataException("snapshot-core.detail.item-kind-invalid"),
            };
            CoreSnapshotProtoV1.WriteMessage(stream, 2, encoded);
        }
        return stream.ToArray();
    }

    internal static CoreDetailSnapshotFragmentV1 Decode(ReadOnlySpan<byte> encoded)
    {
        var reader = new CoreSnapshotProtoV1.Reader(encoded);
        var seenBasis = false;
        ulong basisStep = 0;
        var items = new List<object>();
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag("snapshot-core.detail.field");
            switch (field)
            {
                case 1:
                    if (seenBasis) throw new InvalidDataException("snapshot-core.detail.duplicate-field");
                    seenBasis = true;
                    basisStep = reader.ReadUInt64(wire, "snapshot-core.detail.basis-step");
                    break;
                case 2:
                    items.Add(DecodeItem(reader.ReadBytes(wire, "snapshot-core.detail.item")));
                    break;
                default:
                    throw new InvalidDataException("snapshot-core.detail.unknown-field");
            }
        }
        if (!seenBasis) throw new InvalidDataException("snapshot-core.detail.required-field-missing");
        ValidateCanonical(items);
        return new CoreDetailSnapshotFragmentV1(basisStep, Array.AsReadOnly(items.ToArray()));
    }

    internal static int EncodedItemFieldLength(object item)
    {
        var encoded = item switch
        {
            DetailRegionStateV1 region => EncodeItemRegion(region),
            DetailTransitionCandidateV1 transition => EncodeItemTransition(transition),
            _ => throw new InvalidDataException("snapshot-core.detail.item-kind-invalid"),
        };
        return CoreSnapshotProtoV1.LengthDelimitedFieldLength(2, encoded.Length);
    }

    internal static void ValidateCanonical(IReadOnlyList<object> items)
    {
        var regions = items.TakeWhile(static item => item is DetailRegionStateV1).Cast<DetailRegionStateV1>().ToArray();
        if (items.Skip(regions.Length).Any(static item => item is not DetailTransitionCandidateV1))
            throw new InvalidDataException("snapshot-core.detail.item-kind-order");
        var transitions = items.Skip(regions.Length).Cast<DetailTransitionCandidateV1>().ToArray();
        for (var i = 1; i < regions.Length; i++)
        {
            if (regions[i - 1].DetailRegionId.CompareTo(regions[i].DetailRegionId) >= 0)
                throw new InvalidDataException("snapshot-core.detail.noncanonical-order");
        }
        var expected = DetailTransitionCanonicalOrderV1.Order(transitions).ToArray();
        for (var i = 0; i < transitions.Length; i++)
        {
            if (!ReferenceEquals(expected[i], transitions[i]))
                throw new InvalidDataException("snapshot-core.detail.noncanonical-order");
        }
    }

    private static byte[] EncodeItemRegion(DetailRegionStateV1 region)
    {
        using var nested = new MemoryStream();
        CoreSnapshotProtoV1.WriteMessage(nested, 1, EncodeRegion(region));
        return nested.ToArray();
    }

    private static byte[] EncodeItemTransition(DetailTransitionCandidateV1 transition)
    {
        using var nested = new MemoryStream();
        CoreSnapshotProtoV1.WriteMessage(nested, 2, EncodeTransition(transition));
        return nested.ToArray();
    }

    private static object DecodeItem(ReadOnlySpan<byte> encoded)
    {
        var reader = new CoreSnapshotProtoV1.Reader(encoded);
        object? value = null;
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag("snapshot-core.detail.item-field");
            if (value is not null) throw new InvalidDataException("snapshot-core.detail.item-oneof");
            value = field switch
            {
                1 => DecodeRegion(reader.ReadBytes(wire, "snapshot-core.detail.region")),
                2 => DecodeTransition(reader.ReadBytes(wire, "snapshot-core.detail.transition")),
                _ => throw new InvalidDataException("snapshot-core.detail.item-unknown-field"),
            };
        }
        return value ?? throw new InvalidDataException("snapshot-core.detail.item-empty");
    }

    private static byte[] EncodeRegion(DetailRegionStateV1 region)
    {
        using var stream = new MemoryStream();
        CoreSnapshotProtoV1.WriteBytes(stream, 1, region.DetailRegionId.ToBytes());
        CoreSnapshotProtoV1.WriteBytes(stream, 2, region.SpatialScopeRef.ToBytes());
        CoreSnapshotProtoV1.WriteUInt32(stream, 3, region.LineageGeneration);
        CoreSnapshotProtoV1.WriteUInt64(stream, 4, region.LastTransitionStep);
        foreach (var level in region.LevelByDomain)
        {
            using var nested = new MemoryStream();
            CoreSnapshotProtoV1.WriteString(nested, 1, level.Key.Value);
            CoreSnapshotProtoV1.WriteUInt32(nested, 2, checked((uint)level.Value + 1));
            CoreSnapshotProtoV1.WriteMessage(stream, 5, nested.ToArray());
        }
        foreach (var guard in region.ActiveGuards)
            CoreSnapshotProtoV1.WriteString(stream, 6, guard.Value);
        return stream.ToArray();
    }

    private static DetailRegionStateV1 DecodeRegion(ReadOnlySpan<byte> encoded)
    {
        var reader = new CoreSnapshotProtoV1.Reader(encoded);
        byte[]? regionId = null;
        byte[]? scopeId = null;
        uint lineage = 0;
        ulong lastStep = 0;
        var levels = new List<KeyValuePair<StableToken, DetailLevelV1>>();
        var guards = new List<StableToken>();
        var seen = 0u;
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag("snapshot-core.detail.region-field");
            switch (field)
            {
                case 1: RequireOnce(ref seen, 1, "snapshot-core.detail.region-duplicate-field"); regionId = reader.ReadBytes(wire, "snapshot-core.detail.region-id"); break;
                case 2: RequireOnce(ref seen, 2, "snapshot-core.detail.region-duplicate-field"); scopeId = reader.ReadBytes(wire, "snapshot-core.detail.scope-id"); break;
                case 3: RequireOnce(ref seen, 3, "snapshot-core.detail.region-duplicate-field"); lineage = reader.ReadUInt32(wire, "snapshot-core.detail.lineage"); break;
                case 4: RequireOnce(ref seen, 4, "snapshot-core.detail.region-duplicate-field"); lastStep = reader.ReadUInt64(wire, "snapshot-core.detail.last-step"); break;
                case 5: levels.Add(DecodeLevel(reader.ReadBytes(wire, "snapshot-core.detail.level"))); break;
                case 6: guards.Add(new StableToken(reader.ReadString(wire, "snapshot-core.detail.guard"))); break;
                default: throw new InvalidDataException("snapshot-core.detail.region-unknown-field");
            }
        }
        if ((seen & 0b1111) != 0b1111 || regionId is null || regionId.Length != 16 || scopeId is null || scopeId.Length != 16)
            throw new InvalidDataException("snapshot-core.detail.region-shape");
        RequireAsciiAscending(levels.Select(static value => value.Key.Value), "snapshot-core.detail.level-order");
        RequireAsciiAscending(guards.Select(static value => value.Value), "snapshot-core.detail.guard-order");
        return new DetailRegionStateV1(
            OpaqueId128.FromBytes(regionId),
            OpaqueId128.FromBytes(scopeId),
            levels,
            lineage,
            lastStep,
            guards);
    }

    private static KeyValuePair<StableToken, DetailLevelV1> DecodeLevel(ReadOnlySpan<byte> encoded)
    {
        var reader = new CoreSnapshotProtoV1.Reader(encoded);
        string? domain = null;
        uint level = 0;
        var seen = 0u;
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag("snapshot-core.detail.level-field");
            switch (field)
            {
                case 1: RequireOnce(ref seen, 1, "snapshot-core.detail.level-duplicate-field"); domain = reader.ReadString(wire, "snapshot-core.detail.level-domain"); break;
                case 2: RequireOnce(ref seen, 2, "snapshot-core.detail.level-duplicate-field"); level = reader.ReadUInt32(wire, "snapshot-core.detail.level-value"); break;
                default: throw new InvalidDataException("snapshot-core.detail.level-unknown-field");
            }
        }
        if (seen != 0b11 || domain is null || level is < 1 or > 4)
            throw new InvalidDataException("snapshot-core.detail.level-shape");
        return new KeyValuePair<StableToken, DetailLevelV1>(new StableToken(domain), (DetailLevelV1)(level - 1));
    }

    private static byte[] EncodeTransition(DetailTransitionCandidateV1 value)
    {
        using var stream = new MemoryStream();
        CoreSnapshotProtoV1.WriteBytes(stream, 1, value.DetailRegionId.ToBytes());
        CoreSnapshotProtoV1.WriteString(stream, 2, value.DomainToken.Value);
        CoreSnapshotProtoV1.WriteUInt32(stream, 3, checked((uint)value.CurrentLevel + 1));
        CoreSnapshotProtoV1.WriteUInt32(stream, 4, checked((uint)value.TargetLevel + 1));
        CoreSnapshotProtoV1.WriteUInt64(stream, 5, value.RequiredEffectiveStep);
        CoreSnapshotSecondaryProtoV1.WriteInt32(stream, 6, value.SemanticPriority);
        CoreSnapshotProtoV1.WriteUInt32(stream, 7, (uint)value.TriggerSource);
        CoreSnapshotProtoV1.WriteBytes(stream, 8, value.TriggerId.ToBytes());
        CoreSnapshotProtoV1.WriteUInt64(stream, 9, value.TriggerObservedStep);
        CoreSnapshotProtoV1.WriteUInt32(stream, 10, value.EstimatedRecordCount);
        return stream.ToArray();
    }

    private static DetailTransitionCandidateV1 DecodeTransition(ReadOnlySpan<byte> encoded)
    {
        var reader = new CoreSnapshotProtoV1.Reader(encoded);
        byte[]? regionId = null;
        string? domain = null;
        uint current = 0, target = 0, source = 0, estimated = 0;
        ulong required = 0, observed = 0;
        int priority = 0;
        byte[]? triggerId = null;
        uint seen = 0;
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag("snapshot-core.detail.transition-field");
            if (field is < 1 or > 10) throw new InvalidDataException("snapshot-core.detail.transition-unknown-field");
            RequireOnce(ref seen, field, "snapshot-core.detail.transition-duplicate-field");
            switch (field)
            {
                case 1: regionId = reader.ReadBytes(wire, "snapshot-core.detail.transition-region-id"); break;
                case 2: domain = reader.ReadString(wire, "snapshot-core.detail.transition-domain"); break;
                case 3: current = reader.ReadUInt32(wire, "snapshot-core.detail.transition-current"); break;
                case 4: target = reader.ReadUInt32(wire, "snapshot-core.detail.transition-target"); break;
                case 5: required = reader.ReadUInt64(wire, "snapshot-core.detail.transition-required-step"); break;
                case 6: priority = CoreSnapshotSecondaryProtoV1.DecodeInt32(reader.ReadUInt64(wire, "snapshot-core.detail.transition-priority")); break;
                case 7: source = reader.ReadUInt32(wire, "snapshot-core.detail.transition-source"); break;
                case 8: triggerId = reader.ReadBytes(wire, "snapshot-core.detail.transition-trigger-id"); break;
                case 9: observed = reader.ReadUInt64(wire, "snapshot-core.detail.transition-observed-step"); break;
                case 10: estimated = reader.ReadUInt32(wire, "snapshot-core.detail.transition-estimated-count"); break;
            }
        }
        if (seen != 0x3ff || regionId is null || regionId.Length != 16 || triggerId is null || triggerId.Length != 16 ||
            domain is null || current is < 1 or > 4 || target is < 1 or > 4 || source is < 1 or > 5 || estimated == 0)
            throw new InvalidDataException("snapshot-core.detail.transition-shape");
        var request = new DetailTransitionRequestV1(
            OpaqueId128.FromBytes(regionId),
            new StableToken(domain),
            (DetailLevelV1)(current - 1),
            (DetailLevelV1)(target - 1),
            required,
            priority,
            (DetailTransitionTriggerSourceV1)source,
            OpaqueId128.FromBytes(triggerId),
            observed,
            estimated);
        return new DetailTransitionCandidateV1(request);
    }

    private static void RequireOnce(ref uint seen, int field, string error)
    {
        var bit = 1u << (field - 1);
        if ((seen & bit) != 0) throw new InvalidDataException(error);
        seen |= bit;
    }

    private static void RequireAsciiAscending(IEnumerable<string> values, string error)
    {
        string? previous = null;
        foreach (var value in values)
        {
            _ = new StableToken(value);
            if (previous is not null && string.CompareOrdinal(previous, value) >= 0)
                throw new InvalidDataException(error);
            previous = value;
        }
    }
}

internal sealed record CoreConfigSnapshotFragmentV1(
    ulong BasisStep,
    ulong Generation,
    IReadOnlyList<KeyValuePair<string, object>> Fields);

public static class CoreConfigStateSnapshotWireCodecV1
{
    public static byte[] Encode(ulong basisStep, EffectiveCoreConfig config, IReadOnlyList<KeyValuePair<string, object>> fields)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(fields);
        ValidateCanonical(fields);
        using var stream = new MemoryStream();
        CoreSnapshotProtoV1.WriteUInt64(stream, 1, basisStep);
        CoreSnapshotProtoV1.WriteUInt64(stream, 2, config.Generation);
        CoreSnapshotProtoV1.WriteString(stream, 3, CoreConfigSchema.SchemaVersion);
        CoreSnapshotProtoV1.WriteString(stream, 4, CoreConfigSchema.Component);
        foreach (var field in fields)
            CoreSnapshotProtoV1.WriteMessage(stream, 5, EncodeField(field));
        return stream.ToArray();
    }

    internal static CoreConfigSnapshotFragmentV1 Decode(ReadOnlySpan<byte> encoded)
    {
        var reader = new CoreSnapshotProtoV1.Reader(encoded);
        ulong basis = 0, generation = 0;
        string? schema = null, component = null;
        var fields = new List<KeyValuePair<string, object>>();
        uint seen = 0;
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag("snapshot-core.config.field");
            switch (field)
            {
                case 1: RequireOnce(ref seen, 1); basis = reader.ReadUInt64(wire, "snapshot-core.config.basis-step"); break;
                case 2: RequireOnce(ref seen, 2); generation = reader.ReadUInt64(wire, "snapshot-core.config.generation"); break;
                case 3: RequireOnce(ref seen, 3); schema = reader.ReadString(wire, "snapshot-core.config.schema-version"); break;
                case 4: RequireOnce(ref seen, 4); component = reader.ReadString(wire, "snapshot-core.config.component"); break;
                case 5: fields.Add(DecodeField(reader.ReadBytes(wire, "snapshot-core.config.entry"))); break;
                default: throw new InvalidDataException("snapshot-core.config.unknown-field");
            }
        }
        if ((seen & 0b1111) != 0b1111 || generation == 0 || schema != CoreConfigSchema.SchemaVersion || component != CoreConfigSchema.Component)
            throw new InvalidDataException("snapshot-core.config.metadata-invalid");
        ValidateCanonical(fields);
        return new CoreConfigSnapshotFragmentV1(basis, generation, Array.AsReadOnly(fields.ToArray()));
    }

    internal static int EncodedFieldLength(KeyValuePair<string, object> field)
        => CoreSnapshotProtoV1.LengthDelimitedFieldLength(5, EncodeField(field).Length);

    internal static void ValidateCanonical(IReadOnlyList<KeyValuePair<string, object>> fields)
    {
        string? previous = null;
        foreach (var field in fields)
        {
            _ = new StableToken(field.Key);
            if (previous is not null && string.CompareOrdinal(previous, field.Key) >= 0)
                throw new InvalidDataException("snapshot-core.config.noncanonical-order");
            if (!CoreConfigSchema.Fields.TryGetValue(field.Key, out var spec) || !spec.Validate(field.Value))
                throw new InvalidDataException("snapshot-core.config.field-invalid");
            previous = field.Key;
        }
    }

    private static byte[] EncodeField(KeyValuePair<string, object> field)
    {
        using var stream = new MemoryStream();
        CoreSnapshotProtoV1.WriteString(stream, 1, field.Key);
        switch (field.Value)
        {
            case long number: CoreSnapshotSecondaryProtoV1.WriteSInt64(stream, 2, number); break;
            case bool boolean: CoreSnapshotSecondaryProtoV1.WriteBool(stream, 3, boolean); break;
            case string text: CoreSnapshotProtoV1.WriteString(stream, 4, text); break;
            default: throw new InvalidDataException("snapshot-core.config.value-kind-invalid");
        }
        return stream.ToArray();
    }

    private static KeyValuePair<string, object> DecodeField(ReadOnlySpan<byte> encoded)
    {
        var reader = new CoreSnapshotProtoV1.Reader(encoded);
        string? path = null;
        object? value = null;
        var seenPath = false;
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag("snapshot-core.config.entry-field");
            switch (field)
            {
                case 1:
                    if (seenPath) throw new InvalidDataException("snapshot-core.config.entry-duplicate-field");
                    seenPath = true;
                    path = reader.ReadString(wire, "snapshot-core.config.path");
                    break;
                case 2:
                    if (value is not null) throw new InvalidDataException("snapshot-core.config.entry-oneof");
                    value = CoreSnapshotSecondaryProtoV1.DecodeSInt64(reader.ReadUInt64(wire, "snapshot-core.config.int64"));
                    break;
                case 3:
                    if (value is not null) throw new InvalidDataException("snapshot-core.config.entry-oneof");
                    value = CoreSnapshotSecondaryProtoV1.DecodeBool(reader.ReadUInt64(wire, "snapshot-core.config.bool"));
                    break;
                case 4:
                    if (value is not null) throw new InvalidDataException("snapshot-core.config.entry-oneof");
                    value = reader.ReadString(wire, "snapshot-core.config.string");
                    break;
                default:
                    throw new InvalidDataException("snapshot-core.config.entry-unknown-field");
            }
        }
        if (!seenPath || path is null || value is null)
            throw new InvalidDataException("snapshot-core.config.entry-shape");
        return new KeyValuePair<string, object>(path, value);
    }

    private static void RequireOnce(ref uint seen, int field)
    {
        var bit = 1u << (field - 1);
        if ((seen & bit) != 0) throw new InvalidDataException("snapshot-core.config.duplicate-field");
        seen |= bit;
    }
}

public static class CoreSnapshotSecondarySectionProviderV1
{
    public static CanonicalSnapshotSectionMaterialV1 CreateDetail(CoreSnapshotOwnerMaterialCutV1 cut)
    {
        ArgumentNullException.ThrowIfNull(cut);
        if (cut.GetSupplemental(CoreSnapshotOwnerSectionRegistryV1.DetailDirectory) is not FrozenDetailDirectorySnapshotOwnerV1 owner)
            throw new InvalidDataException("snapshot-core.detail.production-owner-required");
        if (owner.BasisStep != cut.BasisStep)
            throw new InvalidDataException("snapshot-core.detail.step-mismatch");

        var directory = owner.Directory;
        var items = directory.Regions.Cast<object>().Concat(directory.PendingTransitions).ToArray();
        CoreDetailDirectorySnapshotWireCodecV1.ValidateCanonical(items);
        var fragments = FragmentDetail(cut.BasisStep, items);
        var authority = owner.RecomputeAuthority();
        return new CanonicalSnapshotSectionMaterialV1(
            CoreSnapshotOwnerSectionRegistryV1.DetailDirectory,
            authority.Schema,
            checked((ulong)items.Length),
            authority.CanonicalDigest.ToArray(),
            fragments);
    }

    public static CanonicalSnapshotSectionMaterialV1 CreateConfig(CoreSnapshotOwnerMaterialCutV1 cut)
    {
        ArgumentNullException.ThrowIfNull(cut);
        if (cut.GetSupplemental(CoreSnapshotOwnerSectionRegistryV1.ConfigState) is not FrozenCoreConfigSnapshotOwnerV1 owner)
            throw new InvalidDataException("snapshot-core.config.production-owner-required");
        if (owner.BasisStep != cut.BasisStep)
            throw new InvalidDataException("snapshot-core.config.step-mismatch");

        var fields = owner.Config.Fields.OrderBy(static pair => pair.Key, StringComparer.Ordinal).ToArray();
        CoreConfigStateSnapshotWireCodecV1.ValidateCanonical(fields);
        var fragments = FragmentConfig(cut.BasisStep, owner.Config, fields);
        var authority = owner.RecomputeAuthority();
        return new CanonicalSnapshotSectionMaterialV1(
            CoreSnapshotOwnerSectionRegistryV1.ConfigState,
            authority.Schema,
            checked((ulong)fields.Length),
            authority.CanonicalDigest.ToArray(),
            fragments);
    }

    private static IReadOnlyList<SnapshotSectionFragmentMaterialV1> FragmentDetail(ulong basisStep, IReadOnlyList<object> items)
    {
        var groups = SplitItems(items, CoreDetailDirectorySnapshotWireCodecV1.EncodedItemFieldLength, 16);
        if (groups.Count == 0) groups.Add(Array.Empty<object>());
        var result = groups.Select((group, index) =>
        {
            var payload = CoreDetailDirectorySnapshotWireCodecV1.Encode(basisStep, group);
            RequirePayloadLimit(payload);
            return new SnapshotSectionFragmentMaterialV1(
                CoreSnapshotOwnerSectionRegistryV1.DetailDirectory,
                (uint)index,
                (uint)groups.Count,
                null,
                null,
                (ulong)group.Count,
                payload);
        }).ToArray();
        return Array.AsReadOnly(result);
    }

    private static IReadOnlyList<SnapshotSectionFragmentMaterialV1> FragmentConfig(
        ulong basisStep,
        EffectiveCoreConfig config,
        IReadOnlyList<KeyValuePair<string, object>> fields)
    {
        var groups = SplitItems(fields, CoreConfigStateSnapshotWireCodecV1.EncodedFieldLength, 64);
        var result = groups.Select((group, index) =>
        {
            var payload = CoreConfigStateSnapshotWireCodecV1.Encode(basisStep, config, group);
            RequirePayloadLimit(payload);
            return new SnapshotSectionFragmentMaterialV1(
                CoreSnapshotOwnerSectionRegistryV1.ConfigState,
                (uint)index,
                (uint)groups.Count,
                null,
                null,
                (ulong)group.Count,
                payload);
        }).ToArray();
        return Array.AsReadOnly(result);
    }

    private static List<IReadOnlyList<T>> SplitItems<T>(IReadOnlyList<T> items, Func<T, int> encodedLength, int metadataAllowance)
    {
        var groups = new List<IReadOnlyList<T>>();
        var current = new List<T>();
        var size = metadataAllowance;
        foreach (var item in items)
        {
            var length = encodedLength(item);
            if (length > CanonicalSnapshotSectionValidationV1.HardMaxUncompressedBytes)
                throw new InvalidDataException("persistence.snapshot-item-too-large");
            if (current.Count > 0 && checked(size + length) > CanonicalSnapshotSectionValidationV1.TargetUncompressedBytes)
            {
                groups.Add(Array.AsReadOnly(current.ToArray()));
                current = [];
                size = metadataAllowance;
            }
            current.Add(item);
            size = checked(size + length);
            if (size > CanonicalSnapshotSectionValidationV1.HardMaxUncompressedBytes)
                throw new InvalidDataException("persistence.snapshot-item-too-large");
        }
        if (current.Count > 0) groups.Add(Array.AsReadOnly(current.ToArray()));
        return groups;
    }

    private static void RequirePayloadLimit(byte[] payload)
    {
        if (payload.Length > CanonicalSnapshotSectionValidationV1.HardMaxUncompressedBytes)
            throw new InvalidDataException("persistence.snapshot-item-too-large");
    }
}

public static class CoreSnapshotSecondarySemanticVerifierV1
{
    public static SnapshotSectionSemanticVerifierV1 Detail(ulong snapshotStep)
        => new(
            CoreSnapshotOwnerSectionRegistryV1.DetailDirectory,
            new SchemaRefV1("core.detail-state"),
            fragments => VerifyDetail(snapshotStep, fragments));

    public static SnapshotSectionSemanticVerifierV1 Config(ulong snapshotStep)
        => new(
            CoreSnapshotOwnerSectionRegistryV1.ConfigState,
            new SchemaRefV1("config.simulation-core"),
            fragments => VerifyConfig(snapshotStep, fragments));

    private static SnapshotSectionSemanticVerificationV1 VerifyDetail(
        ulong snapshotStep,
        IReadOnlyList<SnapshotSectionFragmentMaterialV1> fragments)
    {
        var allItems = new List<object>();
        var total = 0UL;
        foreach (var fragment in fragments)
        {
            var decoded = CoreDetailDirectorySnapshotWireCodecV1.Decode(fragment.FragmentPayload);
            if (decoded.BasisStep != snapshotStep) throw new InvalidDataException("snapshot-core.detail.step-mismatch");
            if ((ulong)decoded.Items.Count != fragment.ItemCount) throw new InvalidDataException("snapshot-core.detail.fragment-item-count");
            total = checked(total + fragment.ItemCount);
            allItems.AddRange(decoded.Items);
        }
        CoreDetailDirectorySnapshotWireCodecV1.ValidateCanonical(allItems);
        var regions = allItems.TakeWhile(static item => item is DetailRegionStateV1).Cast<DetailRegionStateV1>().ToArray();
        var pending = allItems.Skip(regions.Length).Cast<DetailTransitionCandidateV1>().ToArray();
        var authority = DetailDirectorySubstateV1.Canonicalize(new DetailDirectoryV1(regions, pending));
        return new SnapshotSectionSemanticVerificationV1(total, authority.CanonicalDigest);
    }

    private static SnapshotSectionSemanticVerificationV1 VerifyConfig(
        ulong snapshotStep,
        IReadOnlyList<SnapshotSectionFragmentMaterialV1> fragments)
    {
        ulong? generation = null;
        var fields = new List<KeyValuePair<string, object>>();
        var total = 0UL;
        foreach (var fragment in fragments)
        {
            var decoded = CoreConfigStateSnapshotWireCodecV1.Decode(fragment.FragmentPayload);
            if (decoded.BasisStep != snapshotStep) throw new InvalidDataException("snapshot-core.config.step-mismatch");
            if (generation is null) generation = decoded.Generation;
            else if (generation.Value != decoded.Generation) throw new InvalidDataException("snapshot-core.config.metadata-mismatch");
            if ((ulong)decoded.Fields.Count != fragment.ItemCount) throw new InvalidDataException("snapshot-core.config.fragment-item-count");
            total = checked(total + fragment.ItemCount);
            fields.AddRange(decoded.Fields);
        }
        CoreConfigStateSnapshotWireCodecV1.ValidateCanonical(fields);
        var map = new ReadOnlyDictionary<string, object>(
            fields.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal));
        var config = CoreConfigSnapshotRehydrationV1.Rehydrate(
            generation ?? throw new InvalidDataException("snapshot-core.config.fragment-missing"),
            map);
        return new SnapshotSectionSemanticVerificationV1(total, config.Digest);
    }
}
