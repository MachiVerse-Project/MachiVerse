using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

public sealed class FrozenDomainRegistrySnapshotOwnerV1 : IFrozenCoreSnapshotOwnerMaterialV1
{
    private readonly IReadOnlyList<DomainRuntimeDescriptorV1> _domains;

    private FrozenDomainRegistrySnapshotOwnerV1(ulong basisStep, DomainRegistryStateV1 registry)
    {
        registry.RequireExactStandardGeneration1();
        BasisStep = basisStep;
        RegistryGeneration = registry.RegistryGeneration;
        _domains = CloneDomains(registry.Domains);
    }

    public string SectionId => CoreSnapshotOwnerSectionRegistryV1.DomainRegistry;
    public ulong BasisStep { get; }
    public uint RegistryGeneration { get; }
    public IReadOnlyList<DomainRuntimeDescriptorV1> Domains => _domains;

    public static FrozenDomainRegistrySnapshotOwnerV1 Freeze(ulong basisStep, DomainRegistryStateV1 registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        return new FrozenDomainRegistrySnapshotOwnerV1(basisStep, registry);
    }

    public DomainRegistryStateV1 Rehydrate()
    {
        var registry = new DomainRegistryStateV1(RegistryGeneration, CloneDomains(_domains));
        registry.RequireExactStandardGeneration1();
        return registry;
    }

    public CoreSnapshotOwnerAuthorityV1 RecomputeAuthority()
    {
        var registry = Rehydrate();
        return new CoreSnapshotOwnerAuthorityV1(DomainRegistryStateV1.StateSchema, registry.CanonicalDigest);
    }

    internal static IReadOnlyList<DomainRuntimeDescriptorV1> CloneDomains(
        IEnumerable<DomainRuntimeDescriptorV1> domains)
        => Array.AsReadOnly(domains.Select(static domain => new DomainRuntimeDescriptorV1(
            domain.DomainToken,
            domain.DomainRank,
            domain.DomainSchema,
            domain.OwnedPartitions.ToArray(),
            domain.StateReadDependencies.ToArray(),
            domain.SameStepDependencies.ToArray(),
            domain.AcceptedIntentKinds.ToArray(),
            domain.EmittedIntentKinds.ToArray(),
            domain.EmittedEventKinds.ToArray(),
            domain.InvariantIds.ToArray())).ToArray());
}

internal sealed record CoreDomainRegistrySnapshotFragmentV1(
    ulong BasisStep,
    uint RegistryGeneration,
    IReadOnlyList<DomainRuntimeDescriptorV1> Domains);

public static class CoreDomainRegistrySnapshotWireCodecV1
{
    public static byte[] Encode(
        ulong basisStep,
        uint registryGeneration,
        IReadOnlyList<DomainRuntimeDescriptorV1> domains)
    {
        ArgumentNullException.ThrowIfNull(domains);
        _ = new DomainRegistryStateV1(registryGeneration, domains);

        using var stream = new MemoryStream();
        CoreSnapshotProtoV1.WriteUInt64(stream, 1, basisStep);
        CoreSnapshotProtoV1.WriteUInt32(stream, 2, registryGeneration);
        foreach (var domain in domains)
            CoreSnapshotProtoV1.WriteMessage(stream, 3, EncodeDomain(domain));
        return stream.ToArray();
    }

    internal static CoreDomainRegistrySnapshotFragmentV1 Decode(ReadOnlySpan<byte> encoded)
    {
        var reader = new CoreSnapshotProtoV1.Reader(encoded);
        ulong basisStep = 0;
        uint generation = 0;
        var domains = new List<DomainRuntimeDescriptorV1>();
        var seen = 0u;
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag("snapshot-core.domain-registry.field");
            switch (field)
            {
                case 1:
                    RequireOnce(ref seen, 1, "snapshot-core.domain-registry.duplicate-field");
                    basisStep = reader.ReadUInt64(wire, "snapshot-core.domain-registry.basis-step");
                    break;
                case 2:
                    RequireOnce(ref seen, 2, "snapshot-core.domain-registry.duplicate-field");
                    generation = reader.ReadUInt32(wire, "snapshot-core.domain-registry.registry-generation");
                    break;
                case 3:
                    domains.Add(DecodeDomain(reader.ReadBytes(wire, "snapshot-core.domain-registry.domain")));
                    break;
                default:
                    throw new InvalidDataException("snapshot-core.domain-registry.unknown-field");
            }
        }
        if ((seen & 0b11u) != 0b11u)
            throw new InvalidDataException("snapshot-core.domain-registry.required-field-missing");
        ValidateDomainOrder(domains);
        return new CoreDomainRegistrySnapshotFragmentV1(
            basisStep,
            generation,
            Array.AsReadOnly(domains.ToArray()));
    }

    private static byte[] EncodeDomain(DomainRuntimeDescriptorV1 domain)
    {
        using var stream = new MemoryStream();
        CoreSnapshotProtoV1.WriteString(stream, 1, domain.DomainToken.Value);
        CoreSnapshotProtoV1.WriteUInt32(stream, 2, domain.DomainRank);
        CoreSnapshotProtoV1.WriteMessage(stream, 3, EncodeSchema(domain.DomainSchema));
        WriteTokens(stream, 4, domain.OwnedPartitions);
        WriteTokens(stream, 5, domain.StateReadDependencies);
        WriteTokens(stream, 6, domain.SameStepDependencies);
        WriteTokens(stream, 7, domain.AcceptedIntentKinds);
        WriteTokens(stream, 8, domain.EmittedIntentKinds);
        WriteTokens(stream, 9, domain.EmittedEventKinds);
        WriteTokens(stream, 10, domain.InvariantIds);
        return stream.ToArray();
    }

    private static DomainRuntimeDescriptorV1 DecodeDomain(ReadOnlySpan<byte> encoded)
    {
        var reader = new CoreSnapshotProtoV1.Reader(encoded);
        string? domainToken = null;
        uint rank = 0;
        SchemaRefV1? schema = null;
        var owned = new List<StableToken>();
        var reads = new List<StableToken>();
        var sameStep = new List<StableToken>();
        var accepted = new List<StableToken>();
        var emitted = new List<StableToken>();
        var events = new List<StableToken>();
        var invariants = new List<StableToken>();
        var seen = 0u;
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag("snapshot-core.domain-registry.domain-field");
            switch (field)
            {
                case 1:
                    RequireOnce(ref seen, 1, "snapshot-core.domain-registry.domain-duplicate-field");
                    domainToken = reader.ReadString(wire, "snapshot-core.domain-registry.domain-token");
                    break;
                case 2:
                    RequireOnce(ref seen, 2, "snapshot-core.domain-registry.domain-duplicate-field");
                    rank = reader.ReadUInt32(wire, "snapshot-core.domain-registry.domain-rank");
                    break;
                case 3:
                    RequireOnce(ref seen, 3, "snapshot-core.domain-registry.domain-duplicate-field");
                    schema = DecodeSchema(reader.ReadBytes(wire, "snapshot-core.domain-registry.domain-schema"));
                    break;
                case 4: owned.Add(ReadToken(ref reader, wire, "snapshot-core.domain-registry.owned-partition")); break;
                case 5: reads.Add(ReadToken(ref reader, wire, "snapshot-core.domain-registry.state-read-dependency")); break;
                case 6: sameStep.Add(ReadToken(ref reader, wire, "snapshot-core.domain-registry.same-step-dependency")); break;
                case 7: accepted.Add(ReadToken(ref reader, wire, "snapshot-core.domain-registry.accepted-intent-kind")); break;
                case 8: emitted.Add(ReadToken(ref reader, wire, "snapshot-core.domain-registry.emitted-intent-kind")); break;
                case 9: events.Add(ReadToken(ref reader, wire, "snapshot-core.domain-registry.emitted-event-kind")); break;
                case 10: invariants.Add(ReadToken(ref reader, wire, "snapshot-core.domain-registry.invariant-id")); break;
                default: throw new InvalidDataException("snapshot-core.domain-registry.domain-unknown-field");
            }
        }
        if ((seen & 0b111u) != 0b111u || domainToken is null || schema is null)
            throw new InvalidDataException("snapshot-core.domain-registry.domain-required-field-missing");
        if (rank > ushort.MaxValue)
            throw new InvalidDataException("snapshot-core.domain-registry.domain-rank-range");
        return new DomainRuntimeDescriptorV1(
            new StableToken(domainToken),
            checked((ushort)rank),
            schema.Value,
            owned,
            reads,
            sameStep,
            accepted,
            emitted,
            events,
            invariants);
    }

    private static byte[] EncodeSchema(SchemaRefV1 schema)
    {
        using var stream = new MemoryStream();
        CoreSnapshotProtoV1.WriteString(stream, 1, schema.SchemaId.Value);
        CoreSnapshotProtoV1.WriteUInt32(stream, 2, schema.Version.Major);
        CoreSnapshotProtoV1.WriteUInt32(stream, 3, schema.Version.Minor);
        return stream.ToArray();
    }

    private static SchemaRefV1 DecodeSchema(ReadOnlySpan<byte> encoded)
    {
        var reader = new CoreSnapshotProtoV1.Reader(encoded);
        string? id = null;
        uint major = 0;
        uint minor = 0;
        var seen = 0u;
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag("snapshot-core.domain-registry.schema-field");
            switch (field)
            {
                case 1:
                    RequireOnce(ref seen, 1, "snapshot-core.domain-registry.schema-duplicate-field");
                    id = reader.ReadString(wire, "snapshot-core.domain-registry.schema-id");
                    break;
                case 2:
                    RequireOnce(ref seen, 2, "snapshot-core.domain-registry.schema-duplicate-field");
                    major = reader.ReadUInt32(wire, "snapshot-core.domain-registry.schema-major");
                    break;
                case 3:
                    RequireOnce(ref seen, 3, "snapshot-core.domain-registry.schema-duplicate-field");
                    minor = reader.ReadUInt32(wire, "snapshot-core.domain-registry.schema-minor");
                    break;
                default: throw new InvalidDataException("snapshot-core.domain-registry.schema-unknown-field");
            }
        }
        if ((seen & 0b111u) != 0b111u || id is null || major == 0 || major > ushort.MaxValue || minor > ushort.MaxValue)
            throw new InvalidDataException("snapshot-core.domain-registry.schema-shape");
        return new SchemaRefV1(id, checked((ushort)major), checked((ushort)minor));
    }

    private static void WriteTokens(Stream stream, int field, IReadOnlyList<StableToken> values)
    {
        foreach (var value in values) CoreSnapshotProtoV1.WriteString(stream, field, value.Value);
    }

    private static StableToken ReadToken(ref CoreSnapshotProtoV1.Reader reader, int wire, string error)
        => new(reader.ReadString(wire, error));

    private static void ValidateDomainOrder(IReadOnlyList<DomainRuntimeDescriptorV1> domains)
    {
        for (var i = 1; i < domains.Count; i++)
        {
            if (string.CompareOrdinal(domains[i - 1].DomainToken.Value, domains[i].DomainToken.Value) >= 0)
                throw new InvalidDataException("snapshot-core.domain-registry.noncanonical-order");
        }
    }

    private static void RequireOnce(ref uint seen, int field, string error)
    {
        var bit = 1u << (field - 1);
        if ((seen & bit) != 0) throw new InvalidDataException(error);
        seen |= bit;
    }
}

public static class CoreSnapshotDomainRegistrySectionProviderV1
{
    public static CanonicalSnapshotSectionMaterialV1 Create(CoreSnapshotOwnerMaterialCutV1 cut)
    {
        ArgumentNullException.ThrowIfNull(cut);
        var owner = cut.GetSupplemental(CoreSnapshotOwnerSectionRegistryV1.DomainRegistry)
            as FrozenDomainRegistrySnapshotOwnerV1
            ?? throw new InvalidDataException("snapshot-core.domain-registry.owner-type-mismatch");
        if (owner.BasisStep != cut.BasisStep)
            throw new InvalidDataException("snapshot-core.domain-registry.owner-step-mismatch");
        var registry = owner.Rehydrate();
        var payload = CoreDomainRegistrySnapshotWireCodecV1.Encode(
            cut.BasisStep,
            registry.RegistryGeneration,
            registry.Domains);
        if (payload.Length > CanonicalSnapshotSectionValidationV1.HardMaxUncompressedBytes)
            throw new InvalidDataException("persistence.snapshot-item-too-large");
        var contract = CoreSnapshotSectionWireRegistryV1.Get(CoreSnapshotOwnerSectionRegistryV1.DomainRegistry);
        var fragment = new SnapshotSectionFragmentMaterialV1(
            contract.SectionId,
            0,
            1,
            null,
            null,
            checked((ulong)registry.Domains.Count),
            payload);
        return new CanonicalSnapshotSectionMaterialV1(
            contract.SectionId,
            contract.SectionSchema,
            checked((ulong)registry.Domains.Count),
            registry.CanonicalDigest,
            Array.AsReadOnly([fragment]));
    }
}

public static class CoreSnapshotDomainRegistrySemanticVerifierV1
{
    public static SnapshotSectionSemanticVerifierV1 Create(ulong expectedSnapshotStep)
    {
        var contract = CoreSnapshotSectionWireRegistryV1.Get(CoreSnapshotOwnerSectionRegistryV1.DomainRegistry);
        return new SnapshotSectionSemanticVerifierV1(
            contract.SectionId,
            contract.SectionSchema,
            fragments =>
            {
                var registry = Restore(fragments, expectedSnapshotStep);
                return new SnapshotSectionSemanticVerificationV1(
                    checked((ulong)registry.Domains.Count),
                    registry.CanonicalDigest);
            });
    }

    public static DomainRegistryStateV1 Restore(
        IReadOnlyList<SnapshotSectionFragmentMaterialV1> fragments,
        ulong expectedSnapshotStep)
    {
        ArgumentNullException.ThrowIfNull(fragments);
        if (fragments.Count == 0 || fragments.Count > uint.MaxValue)
            throw new InvalidDataException("snapshot-core.domain-registry.fragment-count");
        var domains = new List<DomainRuntimeDescriptorV1>();
        uint? generation = null;
        ulong total = 0;
        for (var i = 0; i < fragments.Count; i++)
        {
            var fragment = fragments[i] ?? throw new InvalidDataException("snapshot-core.domain-registry.fragment-null");
            if (!string.Equals(fragment.SectionId, CoreSnapshotOwnerSectionRegistryV1.DomainRegistry, StringComparison.Ordinal) ||
                fragment.FragmentIndex != checked((uint)i) ||
                fragment.FragmentCount != checked((uint)fragments.Count))
                throw new InvalidDataException("snapshot-core.domain-registry.fragment-shape");
            if (fragment.FirstRecordId is not null || fragment.LastRecordId is not null)
                throw new InvalidDataException("snapshot-core.domain-registry.fragment-record-range-forbidden");
            if (fragment.FragmentPayload is null ||
                fragment.FragmentPayload.Length > CanonicalSnapshotSectionValidationV1.HardMaxUncompressedBytes)
                throw new InvalidDataException("snapshot-core.domain-registry.fragment-payload");

            var decoded = CoreDomainRegistrySnapshotWireCodecV1.Decode(fragment.FragmentPayload);
            if (decoded.BasisStep != expectedSnapshotStep)
                throw new InvalidDataException("snapshot-core.domain-registry.step-mismatch");
            if (generation is null) generation = decoded.RegistryGeneration;
            else if (generation.Value != decoded.RegistryGeneration)
                throw new InvalidDataException("snapshot-core.domain-registry.generation-fragment-mismatch");
            if (fragment.ItemCount != checked((ulong)decoded.Domains.Count))
                throw new InvalidDataException("snapshot-core.domain-registry.fragment-item-count");
            total = checked(total + fragment.ItemCount);
            domains.AddRange(decoded.Domains);
        }
        if (total != 8)
            throw new InvalidDataException("snapshot-core.domain-registry.standard-domain-count-mismatch");
        var registry = new DomainRegistryStateV1(
            generation ?? throw new InvalidDataException("snapshot-core.domain-registry.generation-missing"),
            domains);
        registry.RequireExactStandardGeneration1();
        return registry;
    }
}
