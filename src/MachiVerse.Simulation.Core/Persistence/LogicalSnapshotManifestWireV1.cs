using System.Security.Cryptography;
using System.Text;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

public interface IRequiredAddonSnapshotMetadataCodecV1
{
    string AddonId { get; }
    SchemaRefV1 MetadataSchema { get; }
    byte[] ComputeSemanticDigest(ReadOnlyMemory<byte> metadataPayload);
}

public sealed class RequiredAddonSnapshotMetadataCodecRegistryV1
{
    private readonly IReadOnlyDictionary<(string AddonId, string SchemaId, ushort Major, ushort Minor), IRequiredAddonSnapshotMetadataCodecV1> _codecs;

    public RequiredAddonSnapshotMetadataCodecRegistryV1(IEnumerable<IRequiredAddonSnapshotMetadataCodecV1> codecs)
    {
        ArgumentNullException.ThrowIfNull(codecs);
        var map = new Dictionary<(string, string, ushort, ushort), IRequiredAddonSnapshotMetadataCodecV1>();
        foreach (var codec in codecs)
        {
            ArgumentNullException.ThrowIfNull(codec);
            var addonId = new StableToken(codec.AddonId).Value;
            var schemaId = codec.MetadataSchema.SchemaId.Value;
            if (codec.MetadataSchema.Version.Major == 0)
                throw new InvalidDataException($"persistence.snapshot.addon-codec-schema-version-invalid:{addonId}");
            var key = (addonId, schemaId, codec.MetadataSchema.Version.Major, codec.MetadataSchema.Version.Minor);
            if (!map.TryAdd(key, codec))
                throw new InvalidDataException($"persistence.snapshot.addon-codec-duplicate:{addonId}");
        }
        _codecs = map;
    }

    public void Verify(RequiredAddonSnapshotMetadataV1 addon)
    {
        ArgumentNullException.ThrowIfNull(addon);
        var addonId = new StableToken(addon.AddonId).Value;
        var schemaId = new StableToken(addon.MetadataSchemaId).Value;
        var key = (addonId, schemaId, addon.MetadataSchemaMajor, addon.MetadataSchemaMinor);
        if (!_codecs.TryGetValue(key, out var codec))
            throw new InvalidDataException($"persistence.snapshot.addon-codec-unavailable:{addonId}");
        var recomputed = codec.ComputeSemanticDigest(addon.MetadataPayload)
            ?? throw new InvalidDataException($"persistence.snapshot.addon-digest-null:{addonId}");
        if (recomputed.Length != 32 || addon.MetadataSemanticDigest.Length != 32 ||
            !CryptographicOperations.FixedTimeEquals(recomputed, addon.MetadataSemanticDigest))
            throw new InvalidDataException($"persistence.snapshot.addon-digest-mismatch:{addonId}");
    }
}

/// <summary>
/// Exact protobuf wire plus MV-DCBOR semantic digest authority for LogicalSnapshotManifestWireV1.
/// Protobuf bytes are a persistence container. SnapshotDigest is always recomputed from the
/// decoded semantic manifest under the existing mv.snapshot.v1 domain label.
/// </summary>
public static class LogicalSnapshotManifestWireCodecV1
{
    public static LogicalSnapshotManifest WithComputedSnapshotDigest(LogicalSnapshotManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var digest = ComputeSnapshotDigest(manifest);
        return manifest with { SnapshotDigest = digest };
    }

    public static byte[] ComputeSnapshotDigest(LogicalSnapshotManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var shape = manifest with { SnapshotDigest = new byte[32] };
        SnapshotManifestValidation.ValidateLogical(shape, StandardSnapshotSectionSetV1.SectionIds);

        return HashSuite.DomainHash("mv.snapshot.v1", writer =>
        {
            writer.WriteMapStart(15);
            writer.WriteUnsigned(0); writer.WriteUnsigned(manifest.PersistenceSchemaMajor);
            writer.WriteUnsigned(1); writer.WriteUnsigned(manifest.PersistenceSchemaMinor);
            writer.WriteUnsigned(2); writer.WriteBytes(manifest.WorldId.ToBytes());
            writer.WriteUnsigned(3); writer.WriteBytes(manifest.SnapshotId.ToBytes());
            writer.WriteUnsigned(4); writer.WriteUnsigned(manifest.SnapshotStep);
            writer.WriteUnsigned(5); writer.WriteUnsigned(manifest.HistoryAnchorSequence);
            writer.WriteUnsigned(6); writer.WriteBytes(manifest.HistoryAnchorDigest);
            writer.WriteUnsigned(7); writer.WriteBytes(manifest.StateContinuityToken);
            writer.WriteUnsigned(8); writer.WriteBytes(manifest.WorldSeed);
            writer.WriteUnsigned(9); writer.WriteUnsigned(manifest.SimulationConfigGeneration);
            writer.WriteUnsigned(10); writer.WriteBytes(manifest.SimulationConfigDigest);
            writer.WriteUnsigned(11); writer.WriteUnsigned(manifest.MasterGeneration);
            writer.WriteUnsigned(12);
            writer.WriteArrayStart(checked((ulong)manifest.RequiredDomains.Count));
            foreach (var domain in manifest.RequiredDomains)
                writer.WriteAsciiText(new StableToken(domain).Value);
            writer.WriteUnsigned(13);
            writer.WriteArrayStart(checked((ulong)manifest.RequiredAddons.Count));
            foreach (var addon in manifest.RequiredAddons)
            {
                writer.WriteMapStart(5);
                writer.WriteUnsigned(0); writer.WriteAsciiText(new StableToken(addon.AddonId).Value);
                writer.WriteUnsigned(1); writer.WriteAsciiText(new StableToken(addon.MetadataSchemaId).Value);
                writer.WriteUnsigned(2); writer.WriteUnsigned(addon.MetadataSchemaMajor);
                writer.WriteUnsigned(3); writer.WriteUnsigned(addon.MetadataSchemaMinor);
                writer.WriteUnsigned(4); writer.WriteBytes(addon.MetadataSemanticDigest);
            }
            writer.WriteUnsigned(14);
            writer.WriteArrayStart(checked((ulong)manifest.Sections.Count));
            foreach (var section in manifest.Sections)
            {
                writer.WriteMapStart(7);
                writer.WriteUnsigned(0); writer.WriteAsciiText(new StableToken(section.SectionId).Value);
                writer.WriteUnsigned(1); writer.WriteAsciiText(new StableToken(section.SchemaId).Value);
                writer.WriteUnsigned(2); writer.WriteUnsigned(section.SchemaMajor);
                writer.WriteUnsigned(3); writer.WriteUnsigned(section.SchemaMinor);
                writer.WriteUnsigned(4); writer.WriteUnsigned(section.LogicalItemCount);
                writer.WriteUnsigned(5); writer.WriteBytes(section.LogicalContentDigest);
                writer.WriteUnsigned(6); writer.WriteBoolean(section.Required);
            }
        });
    }

    public static byte[] Encode(
        LogicalSnapshotManifest manifest,
        RequiredAddonSnapshotMetadataCodecRegistryV1? addonCodecs = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        SnapshotManifestValidation.ValidateLogical(manifest, StandardSnapshotSectionSetV1.SectionIds);
        VerifyAddonCodecs(manifest.RequiredAddons, addonCodecs);
        var expectedDigest = ComputeSnapshotDigest(manifest);
        if (!CryptographicOperations.FixedTimeEquals(expectedDigest, manifest.SnapshotDigest))
            throw new InvalidDataException("persistence.snapshot-digest-mismatch");

        return Proto.Encode(stream =>
        {
            Proto.WriteUInt32(stream, 1, manifest.PersistenceSchemaMajor);
            if (manifest.PersistenceSchemaMinor != 0) Proto.WriteUInt32(stream, 2, manifest.PersistenceSchemaMinor);
            Proto.WriteBytes(stream, 3, manifest.WorldId.ToBytes());
            Proto.WriteBytes(stream, 4, manifest.SnapshotId.ToBytes());
            if (manifest.SnapshotStep != 0) Proto.WriteUInt64(stream, 5, manifest.SnapshotStep);
            if (manifest.HistoryAnchorSequence != 0) Proto.WriteUInt64(stream, 6, manifest.HistoryAnchorSequence);
            Proto.WriteBytes(stream, 7, manifest.HistoryAnchorDigest);
            Proto.WriteBytes(stream, 8, manifest.StateContinuityToken);
            Proto.WriteBytes(stream, 9, manifest.WorldSeed);
            Proto.WriteUInt64(stream, 10, manifest.SimulationConfigGeneration);
            Proto.WriteBytes(stream, 11, manifest.SimulationConfigDigest);
            if (manifest.MasterGeneration != 0) Proto.WriteUInt64(stream, 12, manifest.MasterGeneration);
            foreach (var domain in manifest.RequiredDomains)
                Proto.WriteString(stream, 13, new StableToken(domain).Value);
            foreach (var addon in manifest.RequiredAddons)
                Proto.WriteMessage(stream, 14, EncodeAddon(addon));
            foreach (var section in manifest.Sections)
                Proto.WriteMessage(stream, 15, EncodeSection(section));
            Proto.WriteBytes(stream, 16, manifest.SnapshotDigest);
        });
    }

    public static LogicalSnapshotManifest Decode(
        ReadOnlySpan<byte> encoded,
        RequiredAddonSnapshotMetadataCodecRegistryV1? addonCodecs = null)
    {
        if (encoded.IsEmpty) throw new InvalidDataException("persistence.snapshot.manifest-empty");
        var reader = new Proto.Reader(encoded);
        uint persistenceMajor = 0;
        uint persistenceMinor = 0;
        byte[]? worldIdBytes = null;
        byte[]? snapshotIdBytes = null;
        ulong snapshotStep = 0;
        ulong historySequence = 0;
        byte[]? historyDigest = null;
        byte[]? continuity = null;
        byte[]? worldSeed = null;
        ulong configGeneration = 0;
        byte[]? configDigest = null;
        ulong masterGeneration = 0;
        var domains = new List<string>();
        var addons = new List<RequiredAddonSnapshotMetadataV1>();
        var sections = new List<LogicalSnapshotSection>();
        byte[]? snapshotDigest = null;
        var singular = new HashSet<int>();

        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            if (field is not (13 or 14 or 15) && !singular.Add(field))
                throw new InvalidDataException("persistence.snapshot.manifest-duplicate-field");
            switch (field)
            {
                case 1: reader.RequireWire(wire, 0); persistenceMajor = reader.ReadUInt32(); break;
                case 2: reader.RequireWire(wire, 0); persistenceMinor = reader.ReadUInt32(); break;
                case 3: reader.RequireWire(wire, 2); worldIdBytes = reader.ReadBytes(); break;
                case 4: reader.RequireWire(wire, 2); snapshotIdBytes = reader.ReadBytes(); break;
                case 5: reader.RequireWire(wire, 0); snapshotStep = reader.ReadVarUInt64(); break;
                case 6: reader.RequireWire(wire, 0); historySequence = reader.ReadVarUInt64(); break;
                case 7: reader.RequireWire(wire, 2); historyDigest = reader.ReadBytes(); break;
                case 8: reader.RequireWire(wire, 2); continuity = reader.ReadBytes(); break;
                case 9: reader.RequireWire(wire, 2); worldSeed = reader.ReadBytes(); break;
                case 10: reader.RequireWire(wire, 0); configGeneration = reader.ReadVarUInt64(); break;
                case 11: reader.RequireWire(wire, 2); configDigest = reader.ReadBytes(); break;
                case 12: reader.RequireWire(wire, 0); masterGeneration = reader.ReadVarUInt64(); break;
                case 13: reader.RequireWire(wire, 2); domains.Add(new StableToken(reader.ReadString()).Value); break;
                case 14: reader.RequireWire(wire, 2); addons.Add(DecodeAddon(reader.ReadBytes())); break;
                case 15: reader.RequireWire(wire, 2); sections.Add(DecodeSection(reader.ReadBytes())); break;
                case 16: reader.RequireWire(wire, 2); snapshotDigest = reader.ReadBytes(); break;
                default: throw new InvalidDataException("persistence.snapshot.manifest-unknown-field");
            }
        }

        if (persistenceMajor is 0 or > ushort.MaxValue || persistenceMinor > ushort.MaxValue ||
            worldIdBytes is null || snapshotIdBytes is null || historyDigest is null || continuity is null ||
            worldSeed is null || configGeneration == 0 || configDigest is null || snapshotDigest is null)
            throw new InvalidDataException("persistence.snapshot.manifest-required-field");
        if (worldIdBytes.Length != 16 || snapshotIdBytes.Length != 16)
            throw new InvalidDataException("persistence.snapshot.invalid-id");
        var worldId = OpaqueId128.FromBytes(worldIdBytes);
        var snapshotId = OpaqueId128.FromBytes(snapshotIdBytes);
        if (worldId.IsZero || snapshotId.IsZero)
            throw new InvalidDataException("persistence.snapshot.invalid-id");

        var manifest = new LogicalSnapshotManifest(
            (ushort)persistenceMajor,
            (ushort)persistenceMinor,
            worldId,
            snapshotId,
            snapshotStep,
            historySequence,
            historyDigest,
            continuity,
            worldSeed,
            configGeneration,
            configDigest,
            masterGeneration,
            Array.AsReadOnly(domains.ToArray()),
            Array.AsReadOnly(sections.ToArray()),
            snapshotDigest)
        {
            RequiredAddons = Array.AsReadOnly(addons.ToArray()),
        };

        SnapshotManifestValidation.ValidateLogical(manifest, StandardSnapshotSectionSetV1.SectionIds);
        VerifyAddonCodecs(manifest.RequiredAddons, addonCodecs);
        var recomputed = ComputeSnapshotDigest(manifest);
        if (!CryptographicOperations.FixedTimeEquals(recomputed, manifest.SnapshotDigest))
            throw new InvalidDataException("persistence.snapshot-digest-mismatch");
        return manifest;
    }

    private static void VerifyAddonCodecs(
        IReadOnlyList<RequiredAddonSnapshotMetadataV1> addons,
        RequiredAddonSnapshotMetadataCodecRegistryV1? registry)
    {
        if (addons.Count == 0) return;
        if (registry is null)
            throw new InvalidDataException($"persistence.snapshot.addon-codec-unavailable:{addons[0].AddonId}");
        foreach (var addon in addons) registry.Verify(addon);
    }

    private static byte[] EncodeSection(LogicalSnapshotSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        return Proto.Encode(stream =>
        {
            Proto.WriteString(stream, 1, new StableToken(section.SectionId).Value);
            Proto.WriteString(stream, 2, new StableToken(section.SchemaId).Value);
            Proto.WriteMessage(stream, 3, EncodeSchemaVersion(section.SchemaMajor, section.SchemaMinor));
            if (section.LogicalItemCount != 0) Proto.WriteUInt64(stream, 4, section.LogicalItemCount);
            Proto.WriteBytes(stream, 5, section.LogicalContentDigest);
            if (section.Required) Proto.WriteBool(stream, 6, true);
        });
    }

    private static LogicalSnapshotSection DecodeSection(ReadOnlySpan<byte> encoded)
    {
        var reader = new Proto.Reader(encoded);
        string? sectionId = null;
        string? schemaId = null;
        (ushort Major, ushort Minor)? version = null;
        ulong itemCount = 0;
        byte[]? digest = null;
        var required = false;
        var seen = new HashSet<int>();
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            if (!seen.Add(field)) throw new InvalidDataException("persistence.snapshot.section-wire-duplicate-field");
            switch (field)
            {
                case 1: reader.RequireWire(wire, 2); sectionId = new StableToken(reader.ReadString()).Value; break;
                case 2: reader.RequireWire(wire, 2); schemaId = new StableToken(reader.ReadString()).Value; break;
                case 3: reader.RequireWire(wire, 2); version = DecodeSchemaVersion(reader.ReadBytes()); break;
                case 4: reader.RequireWire(wire, 0); itemCount = reader.ReadVarUInt64(); break;
                case 5: reader.RequireWire(wire, 2); digest = reader.ReadBytes(); break;
                case 6: reader.RequireWire(wire, 0); required = reader.ReadBool(); break;
                default: throw new InvalidDataException("persistence.snapshot.section-wire-unknown-field");
            }
        }
        if (sectionId is null || schemaId is null || version is null || digest is null)
            throw new InvalidDataException("persistence.snapshot.section-wire-required-field");
        return new LogicalSnapshotSection(sectionId, schemaId, version.Value.Major, version.Value.Minor, itemCount, digest, required);
    }

    private static byte[] EncodeAddon(RequiredAddonSnapshotMetadataV1 addon)
    {
        ArgumentNullException.ThrowIfNull(addon);
        return Proto.Encode(stream =>
        {
            Proto.WriteString(stream, 1, new StableToken(addon.AddonId).Value);
            Proto.WriteString(stream, 2, new StableToken(addon.MetadataSchemaId).Value);
            Proto.WriteMessage(stream, 3, EncodeSchemaVersion(addon.MetadataSchemaMajor, addon.MetadataSchemaMinor));
            Proto.WriteBytes(stream, 4, addon.MetadataPayload);
            Proto.WriteBytes(stream, 5, addon.MetadataSemanticDigest);
        });
    }

    private static RequiredAddonSnapshotMetadataV1 DecodeAddon(ReadOnlySpan<byte> encoded)
    {
        var reader = new Proto.Reader(encoded);
        string? addonId = null;
        string? schemaId = null;
        (ushort Major, ushort Minor)? version = null;
        byte[]? payload = null;
        byte[]? digest = null;
        var seen = new HashSet<int>();
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            if (!seen.Add(field)) throw new InvalidDataException("persistence.snapshot.addon-wire-duplicate-field");
            switch (field)
            {
                case 1: reader.RequireWire(wire, 2); addonId = new StableToken(reader.ReadString()).Value; break;
                case 2: reader.RequireWire(wire, 2); schemaId = new StableToken(reader.ReadString()).Value; break;
                case 3: reader.RequireWire(wire, 2); version = DecodeSchemaVersion(reader.ReadBytes()); break;
                case 4: reader.RequireWire(wire, 2); payload = reader.ReadBytes(); break;
                case 5: reader.RequireWire(wire, 2); digest = reader.ReadBytes(); break;
                default: throw new InvalidDataException("persistence.snapshot.addon-wire-unknown-field");
            }
        }
        if (addonId is null || schemaId is null || version is null || payload is null || digest is null)
            throw new InvalidDataException("persistence.snapshot.addon-wire-required-field");
        return new RequiredAddonSnapshotMetadataV1(addonId, schemaId, version.Value.Major, version.Value.Minor, payload, digest);
    }

    private static byte[] EncodeSchemaVersion(ushort major, ushort minor)
    {
        if (major == 0) throw new InvalidDataException("persistence.snapshot.invalid-schema-version");
        return Proto.Encode(stream =>
        {
            Proto.WriteUInt32(stream, 1, major);
            if (minor != 0) Proto.WriteUInt32(stream, 2, minor);
        });
    }

    private static (ushort Major, ushort Minor) DecodeSchemaVersion(ReadOnlySpan<byte> encoded)
    {
        var reader = new Proto.Reader(encoded);
        uint major = 0, minor = 0;
        var seen = new HashSet<int>();
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            if (!seen.Add(field)) throw new InvalidDataException("persistence.snapshot.schema-version-duplicate-field");
            reader.RequireWire(wire, 0);
            if (field == 1) major = reader.ReadUInt32();
            else if (field == 2) minor = reader.ReadUInt32();
            else throw new InvalidDataException("persistence.snapshot.schema-version-unknown-field");
        }
        if (major is 0 or > ushort.MaxValue || minor > ushort.MaxValue)
            throw new InvalidDataException("persistence.snapshot.invalid-schema-version");
        return ((ushort)major, (ushort)minor);
    }

    private static class Proto
    {
        public static byte[] Encode(Action<Stream> write)
        {
            using var stream = new MemoryStream();
            write(stream);
            return stream.ToArray();
        }

        public static void WriteMessage(Stream stream, int field, ReadOnlySpan<byte> value) => WriteBytes(stream, field, value);
        public static void WriteString(Stream stream, int field, string value) => WriteBytes(stream, field, Encoding.UTF8.GetBytes(value));
        public static void WriteBytes(Stream stream, int field, ReadOnlySpan<byte> value)
        {
            WriteVarUInt64(stream, ((ulong)field << 3) | 2UL);
            WriteVarUInt64(stream, checked((ulong)value.Length));
            stream.Write(value);
        }
        public static void WriteUInt32(Stream stream, int field, uint value)
        {
            WriteVarUInt64(stream, (ulong)field << 3);
            WriteVarUInt64(stream, value);
        }
        public static void WriteUInt64(Stream stream, int field, ulong value)
        {
            WriteVarUInt64(stream, (ulong)field << 3);
            WriteVarUInt64(stream, value);
        }
        public static void WriteBool(Stream stream, int field, bool value)
        {
            WriteVarUInt64(stream, (ulong)field << 3);
            WriteVarUInt64(stream, value ? 1UL : 0UL);
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

        public ref struct Reader
        {
            private ReadOnlySpan<byte> _remaining;
            public Reader(ReadOnlySpan<byte> encoded) => _remaining = encoded;
            public bool End => _remaining.IsEmpty;
            public (int Field, int Wire) ReadTag()
            {
                var tag = ReadVarUInt64();
                if (tag == 0) throw new InvalidDataException("persistence.snapshot.manifest-tag-zero");
                return (checked((int)(tag >> 3)), checked((int)(tag & 7)));
            }
            public void RequireWire(int actual, int expected)
            {
                if (actual != expected) throw new InvalidDataException("persistence.snapshot.manifest-wire-type");
            }
            public ulong ReadVarUInt64()
            {
                ulong value = 0;
                for (var shift = 0; shift < 64; shift += 7)
                {
                    if (_remaining.IsEmpty) throw new InvalidDataException("persistence.snapshot.manifest-truncated-varint");
                    var current = _remaining[0];
                    _remaining = _remaining[1..];
                    value |= (ulong)(current & 0x7f) << shift;
                    if ((current & 0x80) == 0) return value;
                }
                throw new InvalidDataException("persistence.snapshot.manifest-varint-overflow");
            }
            public uint ReadUInt32()
            {
                var value = ReadVarUInt64();
                if (value > uint.MaxValue) throw new InvalidDataException("persistence.snapshot.manifest-uint32-overflow");
                return (uint)value;
            }
            public bool ReadBool()
            {
                var value = ReadVarUInt64();
                if (value > 1) throw new InvalidDataException("persistence.snapshot.manifest-bool-range");
                return value == 1;
            }
            public byte[] ReadBytes()
            {
                var length = ReadVarUInt64();
                if (length > int.MaxValue || (ulong)_remaining.Length < length)
                    throw new InvalidDataException("persistence.snapshot.manifest-truncated-bytes");
                var result = _remaining[..(int)length].ToArray();
                _remaining = _remaining[(int)length..];
                return result;
            }
            public string ReadString()
            {
                var bytes = ReadBytes();
                try { return new UTF8Encoding(false, true).GetString(bytes); }
                catch (DecoderFallbackException ex) { throw new InvalidDataException("persistence.snapshot.manifest-utf8", ex); }
            }
        }
    }
}
