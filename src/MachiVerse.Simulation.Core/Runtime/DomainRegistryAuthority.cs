using System.Collections.ObjectModel;
using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains;
using MachiVerse.Simulation.Core.Domains.ResidentParticipation;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Runtime;

public sealed record DomainIntentCapabilityV1(
    StableToken SourceDomain,
    StableToken TargetDomain,
    StableToken TargetPartition,
    StableToken IntentKind);

public sealed class DomainRuntimeDescriptorV1
{
    public DomainRuntimeDescriptorV1(
        StableToken domainToken,
        ushort domainRank,
        SchemaRefV1 domainSchema,
        IEnumerable<StableToken> ownedPartitions,
        IEnumerable<StableToken> stateReadDependencies,
        IEnumerable<StableToken> sameStepDependencies,
        IEnumerable<StableToken> acceptedIntentKinds,
        IEnumerable<StableToken> emittedIntentKinds,
        IEnumerable<StableToken> emittedEventKinds,
        IEnumerable<StableToken> invariantIds)
    {
        DomainToken = domainToken;
        DomainRank = domainRank;
        DomainSchema = domainSchema;
        OwnedPartitions = FreezeCanonical(ownedPartitions, "domain-registry.owned-partitions-noncanonical");
        StateReadDependencies = FreezeCanonical(stateReadDependencies, "domain-registry.state-read-dependencies-noncanonical");
        SameStepDependencies = FreezeCanonical(sameStepDependencies, "domain-registry.same-step-dependencies-noncanonical");
        AcceptedIntentKinds = FreezeCanonical(acceptedIntentKinds, "domain-registry.accepted-intent-kinds-noncanonical");
        EmittedIntentKinds = FreezeCanonical(emittedIntentKinds, "domain-registry.emitted-intent-kinds-noncanonical");
        EmittedEventKinds = FreezeCanonical(emittedEventKinds, "domain-registry.emitted-event-kinds-noncanonical");
        InvariantIds = FreezeCanonical(invariantIds, "domain-registry.invariant-ids-noncanonical");
        if (domainRank == 0) throw new InvalidDataException("domain-registry.domain-rank-zero");
        if (domainSchema.Version.Major == 0) throw new InvalidDataException("domain-registry.domain-schema-invalid");
    }

    public StableToken DomainToken { get; }
    public ushort DomainRank { get; }
    public SchemaRefV1 DomainSchema { get; }
    public IReadOnlyList<StableToken> OwnedPartitions { get; }
    public IReadOnlyList<StableToken> StateReadDependencies { get; }
    public IReadOnlyList<StableToken> SameStepDependencies { get; }
    public IReadOnlyList<StableToken> AcceptedIntentKinds { get; }
    public IReadOnlyList<StableToken> EmittedIntentKinds { get; }
    public IReadOnlyList<StableToken> EmittedEventKinds { get; }
    public IReadOnlyList<StableToken> InvariantIds { get; }

    private static IReadOnlyList<StableToken> FreezeCanonical(IEnumerable<StableToken> values, string error)
    {
        ArgumentNullException.ThrowIfNull(values);
        var array = values.ToArray();
        for (var i = 1; i < array.Length; i++)
        {
            if (string.CompareOrdinal(array[i - 1].Value, array[i].Value) >= 0)
                throw new InvalidDataException(error);
        }
        return Array.AsReadOnly(array);
    }
}

public sealed class DomainRegistryStateV1
{
    public const uint StandardGeneration1 = 1;
    public static readonly SchemaRefV1 StateSchema = new("core.domain-registry-state");

    private readonly IReadOnlyDictionary<StableToken, DomainRuntimeDescriptorV1> _byDomain;

    public DomainRegistryStateV1(uint registryGeneration, IEnumerable<DomainRuntimeDescriptorV1> domains)
    {
        if (registryGeneration != StandardGeneration1)
            throw new InvalidDataException("domain-registry.generation-mismatch");
        ArgumentNullException.ThrowIfNull(domains);
        var array = domains.ToArray();
        if (array.Length != 8)
            throw new InvalidDataException("domain-registry.standard-domain-count-mismatch");
        if (array.Select(static value => value.DomainToken).Distinct().Count() != array.Length)
            throw new InvalidDataException("domain-registry.duplicate-domain");
        for (var i = 1; i < array.Length; i++)
        {
            if (string.CompareOrdinal(array[i - 1].DomainToken.Value, array[i].DomainToken.Value) >= 0)
                throw new InvalidDataException("domain-registry.domain-order-noncanonical");
        }

        RegistryGeneration = registryGeneration;
        Domains = Array.AsReadOnly(array);
        _byDomain = new ReadOnlyDictionary<StableToken, DomainRuntimeDescriptorV1>(
            array.ToDictionary(static value => value.DomainToken));
        ValidateStandardStructure();
        CanonicalDigest = ComputeCanonicalDigest();
    }

    public uint RegistryGeneration { get; }
    public IReadOnlyList<DomainRuntimeDescriptorV1> Domains { get; }
    public byte[] CanonicalDigest { get; }

    public DomainRuntimeDescriptorV1 Get(StableToken domainToken)
        => _byDomain.TryGetValue(domainToken, out var descriptor)
            ? descriptor
            : throw new InvalidDataException("domain-registry.unknown-domain-token");

    public WorldSubstateRefV1 ToWorldSubstateRef()
        => new(StateSchema, CanonicalDigest.ToArray());

    public void RequireAuthorizedEmission(MutationIntentCandidateV1 intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        var descriptor = Get(intent.SourceDomain);
        if (!descriptor.EmittedIntentKinds.Contains(intent.MutationKind))
            throw new InvalidDataException("domain-runtime.unauthorized-emitted-intent");
        var accepted = Get(intent.TargetDomain);
        if (!accepted.AcceptedIntentKinds.Contains(intent.MutationKind))
            throw new InvalidDataException("domain-runtime.intent-target-not-accepted");
        var capability = StandardDomainRuntimeCapabilityRegistryV1.Generation1IntentCapabilities
            .Any(value => value.SourceDomain == intent.SourceDomain &&
                          value.TargetDomain == intent.TargetDomain &&
                          value.TargetPartition == intent.TargetPartitionId &&
                          value.IntentKind == intent.MutationKind);
        if (!capability)
            throw new InvalidDataException("domain-runtime.intent-capability-binding-mismatch");
    }

    public void RequireAuthorizedEmissions(IEnumerable<MutationIntentCandidateV1> intents)
    {
        ArgumentNullException.ThrowIfNull(intents);
        foreach (var intent in intents) RequireAuthorizedEmission(intent);
    }

    public void RequireExactStandardGeneration1()
    {
        var expected = StandardDomainRegistryAuthorityV1.Generation1;
        if (RegistryGeneration != expected.RegistryGeneration || Domains.Count != expected.Domains.Count)
            throw new InvalidDataException("domain-registry.stale-material");
        for (var i = 0; i < Domains.Count; i++)
        {
            if (!DescriptorEquals(Domains[i], expected.Domains[i]))
                throw new InvalidDataException("domain-registry.stale-material");
        }
        if (!CryptographicOperations.FixedTimeEquals(CanonicalDigest, expected.CanonicalDigest))
            throw new InvalidDataException("domain-registry.stale-material");
    }

    private void ValidateStandardStructure()
    {
        var standardDomains = StandardDomainPartitionRegistry.Entries
            .Select(static entry => entry.OwnerDomain)
            .Distinct()
            .ToHashSet();
        foreach (var descriptor in Domains)
        {
            if (!standardDomains.Contains(descriptor.DomainToken))
                throw new InvalidDataException("domain-registry.unknown-domain-token");
            if (descriptor.StateReadDependencies.Any(value => !standardDomains.Contains(value)) ||
                descriptor.SameStepDependencies.Any(value => !standardDomains.Contains(value)))
                throw new InvalidDataException("domain-registry.unknown-dependency-domain-token");
        }

        var ownerships = Domains
            .SelectMany(static descriptor => descriptor.OwnedPartitions.Select(partition => (descriptor.DomainToken, Partition: partition)))
            .ToArray();
        if (ownerships.GroupBy(static value => value.Partition).Any(static group => group.Count() != 1))
            throw new InvalidDataException("domain-registry.duplicate-partition-owner");
        if (ownerships.Length != StandardDomainPartitionRegistry.StandardPartitionCount)
            throw new InvalidDataException("domain-registry.missing-partition-owner");
        var standardPartitions = StandardDomainPartitionRegistry.Entries
            .Select(static entry => entry.PartitionId)
            .ToHashSet();
        if (!ownerships.Select(static value => value.Partition).ToHashSet().SetEquals(standardPartitions))
            throw new InvalidDataException("domain-registry.missing-partition-owner");

        foreach (var descriptor in Domains)
        {
            var expectedPartitions = StandardDomainPartitionRegistry.Entries
                .Where(entry => entry.OwnerDomain == descriptor.DomainToken)
                .OrderBy(static entry => entry.PartitionId.Value, StringComparer.Ordinal)
                .ToArray();
            if (expectedPartitions.Length == 0)
                throw new InvalidDataException("domain-registry.domain-has-no-standard-partitions");
            if (descriptor.DomainRank != expectedPartitions[0].OwnerDomainRank)
                throw new InvalidDataException("domain-registry.domain-rank-mismatch");
            if (descriptor.OwnedPartitions.Count != expectedPartitions.Length)
                throw new InvalidDataException("domain-registry.missing-partition-owner");
            for (var i = 0; i < expectedPartitions.Length; i++)
            {
                if (descriptor.OwnedPartitions[i] != expectedPartitions[i].PartitionId)
                    throw new InvalidDataException("domain-registry.partition-owner-mismatch");
            }
        }
    }

    private byte[] ComputeCanonicalDigest()
        => HashSuite.DomainHash("mv.core-domain-registry-state.v1", writer =>
        {
            writer.WriteMapStart(3);
            writer.WriteUnsigned(0); writer.WriteAsciiText("core.domain-registry-state");
            writer.WriteUnsigned(1); writer.WriteUnsigned(RegistryGeneration);
            writer.WriteUnsigned(2); writer.WriteArrayStart((ulong)Domains.Count);
            foreach (var domain in Domains)
            {
                writer.WriteMapStart(12);
                writer.WriteUnsigned(0); writer.WriteAsciiText(domain.DomainToken.Value);
                writer.WriteUnsigned(1); writer.WriteUnsigned(domain.DomainRank);
                writer.WriteUnsigned(2); writer.WriteAsciiText(domain.DomainSchema.SchemaId.Value);
                writer.WriteUnsigned(3); writer.WriteUnsigned(domain.DomainSchema.Version.Major);
                writer.WriteUnsigned(4); writer.WriteUnsigned(domain.DomainSchema.Version.Minor);
                writer.WriteUnsigned(5); WriteTokens(writer, domain.OwnedPartitions);
                writer.WriteUnsigned(6); WriteTokens(writer, domain.StateReadDependencies);
                writer.WriteUnsigned(7); WriteTokens(writer, domain.SameStepDependencies);
                writer.WriteUnsigned(8); WriteTokens(writer, domain.AcceptedIntentKinds);
                writer.WriteUnsigned(9); WriteTokens(writer, domain.EmittedIntentKinds);
                writer.WriteUnsigned(10); WriteTokens(writer, domain.EmittedEventKinds);
                writer.WriteUnsigned(11); WriteTokens(writer, domain.InvariantIds);
            }
        });

    private static void WriteTokens(MvDcborWriter writer, IReadOnlyList<StableToken> values)
    {
        writer.WriteArrayStart((ulong)values.Count);
        foreach (var value in values) writer.WriteAsciiText(value.Value);
    }

    private static bool DescriptorEquals(DomainRuntimeDescriptorV1 left, DomainRuntimeDescriptorV1 right)
        => left.DomainToken == right.DomainToken &&
           left.DomainRank == right.DomainRank &&
           left.DomainSchema == right.DomainSchema &&
           left.OwnedPartitions.SequenceEqual(right.OwnedPartitions) &&
           left.StateReadDependencies.SequenceEqual(right.StateReadDependencies) &&
           left.SameStepDependencies.SequenceEqual(right.SameStepDependencies) &&
           left.AcceptedIntentKinds.SequenceEqual(right.AcceptedIntentKinds) &&
           left.EmittedIntentKinds.SequenceEqual(right.EmittedIntentKinds) &&
           left.EmittedEventKinds.SequenceEqual(right.EmittedEventKinds) &&
           left.InvariantIds.SequenceEqual(right.InvariantIds);
}

public static class StandardDomainRuntimeCapabilityRegistryV1
{
    private static readonly IReadOnlyList<DomainIntentCapabilityV1> Generation1Capabilities = BuildGeneration1IntentCapabilities();

    public static IReadOnlyList<DomainIntentCapabilityV1> Generation1IntentCapabilities => Generation1Capabilities;

    private static IReadOnlyList<DomainIntentCapabilityV1> BuildGeneration1IntentCapabilities()
    {
        var values = PhysicalBuiltCrossDomainIntentFactoryV1.EmittedCapabilities
            .Concat(ResidentPhysicalIntentFactoryV1.EmittedCapabilities)
            .OrderBy(static value => value.SourceDomain.Value, StringComparer.Ordinal)
            .ThenBy(static value => value.TargetDomain.Value, StringComparer.Ordinal)
            .ThenBy(static value => value.TargetPartition.Value, StringComparer.Ordinal)
            .ThenBy(static value => value.IntentKind.Value, StringComparer.Ordinal)
            .ToArray();
        if (values.GroupBy(static value => (value.SourceDomain, value.TargetDomain, value.TargetPartition, value.IntentKind))
            .Any(static group => group.Count() != 1))
            throw new InvalidDataException("domain-registry.duplicate-intent-capability");
        return Array.AsReadOnly(values);
    }
}

public static class StandardDomainRegistryAuthorityV1
{
    private static readonly Lazy<DomainRegistryStateV1> Generation1Lazy = new(CreateGeneration1);

    public static DomainRegistryStateV1 Generation1 => Generation1Lazy.Value;

    public static WorldSubstateRefV1 Generation1SubstateRef()
        => Generation1.ToWorldSubstateRef();

    public static void RequireGeneration1StateRef(WorldSubstateRefV1 stateRef)
    {
        ArgumentNullException.ThrowIfNull(stateRef);
        if (stateRef.Schema != DomainRegistryStateV1.StateSchema ||
            !CryptographicOperations.FixedTimeEquals(stateRef.CanonicalDigest, Generation1.CanonicalDigest))
            throw new InvalidDataException("domain-registry.stale-material");
    }

    private static DomainRegistryStateV1 CreateGeneration1()
    {
        var plan = StandardDomainExecutionPlanV1.Create();
        var domains = plan.Entries
            .Select(static entry => entry.DomainToken)
            .OrderBy(static token => token.Value, StringComparer.Ordinal)
            .ToArray();
        var descriptors = domains.Select(domain =>
        {
            var planEntry = plan.Entries.Single(entry => entry.DomainToken == domain);
            var stateReads = domains.Where(value => value != domain).ToArray();
            var emitted = StandardDomainRuntimeCapabilityRegistryV1.Generation1IntentCapabilities
                .Where(value => value.SourceDomain == domain)
                .Select(static value => value.IntentKind)
                .Distinct()
                .OrderBy(static value => value.Value, StringComparer.Ordinal)
                .ToArray();
            var accepted = StandardDomainRuntimeCapabilityRegistryV1.Generation1IntentCapabilities
                .Where(value => value.TargetDomain == domain)
                .Select(static value => value.IntentKind)
                .Distinct()
                .OrderBy(static value => value.Value, StringComparer.Ordinal)
                .ToArray();
            return new DomainRuntimeDescriptorV1(
                domain,
                planEntry.DomainRank,
                new SchemaRefV1($"domain.{domain.Value}.runtime"),
                planEntry.OwnedPartitions,
                stateReads,
                planEntry.SameStepDependencies,
                accepted,
                emitted,
                Array.Empty<StableToken>(),
                Array.Empty<StableToken>());
        }).ToArray();
        return new DomainRegistryStateV1(DomainRegistryStateV1.StandardGeneration1, descriptors);
    }
}
