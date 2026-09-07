using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.State;

internal static class Sim04WorldStateSmoke
{
    private sealed class SchedulerRef : SchedulerStateRefV1;
    private sealed class OperationRef : OperationStateRefV1;
    private sealed class DetailRef : DetailDirectoryRefV1;
    private sealed class RegistryRef : DomainRegistryRefV1;

    [ModuleInitializer]
    internal static void Initialize() => Run();

    internal static void Run()
    {
        var registry = StandardDomainPartitionRegistry.Create();
        var digestBytes = Enumerable.Repeat((byte)0x33, 32).ToArray();
        var partitionDigest = new Hash256(digestBytes);

        var partitions = registry.Entries
            .Select(registration => DomainPartitionStateV1.RestoreValidated(
                registration,
                new PartitionStateHeaderV1(
                    registration.PartitionId,
                    registration.OwnerDomain,
                    registration.PartitionSchema,
                    Revision: 0,
                    BasisStep: 0,
                    DetailLevelV1.D0Entity,
                    ItemCount: 0,
                    partitionDigest),
                Array.Empty<DomainRecordEnvelopeV1>()))
            .Reverse()
            .ToArray();

        var directory = new OrderedPartitionDirectoryV1(partitions);
        directory.ValidateStandardCoverage(registry);
        if (directory.Partitions.Count != 97 ||
            !directory.Partitions.Select(static value => value.Header.PartitionId.Value)
                .SequenceEqual(registry.Entries.Select(static value => value.PartitionId.Value), StringComparer.Ordinal))
            throw new InvalidOperationException("WorldState partition directory must normalize to standard registry order.");

        var diagnostic = new StateDiagnosticV1(
            new Hash256(Enumerable.Repeat((byte)0x44, 32).ToArray()),
            directory.Partitions
                .Reverse()
                .Select(static partition => new KeyValuePair<StableToken, Hash256>(
                    partition.Header.PartitionId,
                    partition.Header.CanonicalDigest)),
            new Hash256(Enumerable.Repeat((byte)0x55, 32).ToArray()),
            new Hash256(Enumerable.Repeat((byte)0x66, 32).ToArray()));

        var worldState = new WorldStateV1(
            new WorldStateHeaderV1(
                WorldStateHeaderV1.StandardSchema,
                OpaqueId128.Parse("00000000000000000000000000000091"),
                Step: 0,
                new Hash256(Enumerable.Repeat((byte)0x11, 32).ToArray()),
                ConfigGeneration: 1,
                MasterGeneration: 1,
                RateGeneration: 1,
                PreviousStateDigest: null),
            directory,
            new SchedulerRef(),
            new OperationRef(),
            new DetailRef(),
            new RegistryRef(),
            diagnostic,
            registry);

        if (worldState.Partitions.Partitions.Count != 97)
            throw new InvalidOperationException("WorldStateV1 must retain all 97 standard partitions.");

        var missingRejected = false;
        try
        {
            _ = new OrderedPartitionDirectoryV1(partitions[..^1]);
            new OrderedPartitionDirectoryV1(partitions[..^1]).ValidateStandardCoverage(registry);
        }
        catch (InvalidDataException ex) when (ex.Message == "state.world.standard-partition-count-mismatch")
        {
            missingRejected = true;
        }
        if (!missingRejected)
            throw new InvalidOperationException("Missing standard partition must reject WorldState coverage validation.");
    }
}
