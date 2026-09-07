using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.State;

// Phase 4 fixes these logical references in WorldStateV1 but does not fix an in-memory pointer or
// compiled DTO representation for the referenced roots. Keep them abstract so SIM-05/SIM-06 can
// provide component-local concrete state without inventing persistence/wire identity here.
public interface SchedulerStateRefV1 { }
public interface OperationStateRefV1 { }
public interface DetailDirectoryRefV1 { }
public interface DomainRegistryRefV1 { }

public sealed record WorldStateHeaderV1(
    SchemaRefV1 Schema,
    OpaqueId128 WorldId,
    ulong Step,
    Hash256 WorldSeedDigest,
    ulong ConfigGeneration,
    ulong MasterGeneration,
    uint RateGeneration,
    Hash256? PreviousStateDigest)
{
    public static SchemaRefV1 StandardSchema =>
        new(new StableToken("core.world-state"), new SchemaVersionV1(1, 0));

    public void Validate()
    {
        if (Schema != StandardSchema)
            throw new InvalidDataException("state.world.header-schema-mismatch");
        if (WorldId.IsZero)
            throw new InvalidDataException("state.world.world-id-zero");
        if (ConfigGeneration == 0)
            throw new InvalidDataException("state.world.config-generation-uninitialized");
        if (MasterGeneration == 0)
            throw new InvalidDataException("state.world.master-generation-uninitialized");
    }
}

public sealed class StateDiagnosticV1
{
    private readonly IReadOnlyList<KeyValuePair<StableToken, Hash256>> _partitionDigests;

    public StateDiagnosticV1(
        Hash256 stateDigest,
        IEnumerable<KeyValuePair<StableToken, Hash256>> partitionDigests,
        Hash256 schemaRegistryDigest,
        Hash256 configDigest)
    {
        ArgumentNullException.ThrowIfNull(partitionDigests);
        var ordered = partitionDigests
            .OrderBy(static pair => pair.Key.Value, StringComparer.Ordinal)
            .ToArray();
        if (ordered.Select(static pair => pair.Key.Value).Distinct(StringComparer.Ordinal).Count() != ordered.Length)
            throw new InvalidDataException("state.diagnostic.duplicate-partition-digest");

        StateDigest = stateDigest;
        SchemaRegistryDigest = schemaRegistryDigest;
        ConfigDigest = configDigest;
        _partitionDigests = Array.AsReadOnly(ordered);
    }

    public Hash256 StateDigest { get; }
    public IReadOnlyList<KeyValuePair<StableToken, Hash256>> PartitionDigests => _partitionDigests;
    public Hash256 SchemaRegistryDigest { get; }
    public Hash256 ConfigDigest { get; }
}

public sealed class WorldStateV1
{
    public WorldStateV1(
        WorldStateHeaderV1 header,
        OrderedPartitionDirectoryV1 partitions,
        SchedulerStateRefV1 schedulerState,
        OperationStateRefV1 operationState,
        DetailDirectoryRefV1 detailState,
        DomainRegistryRefV1 domainRegistryState,
        StateDiagnosticV1 diagnostic,
        DomainPartitionRegistryV1 standardRegistry)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(partitions);
        ArgumentNullException.ThrowIfNull(schedulerState);
        ArgumentNullException.ThrowIfNull(operationState);
        ArgumentNullException.ThrowIfNull(detailState);
        ArgumentNullException.ThrowIfNull(domainRegistryState);
        ArgumentNullException.ThrowIfNull(diagnostic);
        ArgumentNullException.ThrowIfNull(standardRegistry);

        header.Validate();
        StandardDomainPartitionRegistry.ValidateStandardProfile(standardRegistry);
        partitions.ValidateStandardCoverage(standardRegistry);

        if (diagnostic.PartitionDigests.Count != StandardDomainPartitionRegistry.StandardPartitionCount)
            throw new InvalidDataException("state.diagnostic.standard-partition-count-mismatch");
        for (var i = 0; i < standardRegistry.Entries.Count; i++)
        {
            if (diagnostic.PartitionDigests[i].Key != standardRegistry.Entries[i].PartitionId)
                throw new InvalidDataException("state.diagnostic.partition-order-or-coverage-mismatch");
            if (diagnostic.PartitionDigests[i].Value != partitions.Partitions[i].Header.CanonicalDigest)
                throw new InvalidDataException("state.diagnostic.partition-digest-mismatch");
        }

        Header = header;
        Partitions = partitions;
        SchedulerState = schedulerState;
        OperationState = operationState;
        DetailState = detailState;
        DomainRegistryState = domainRegistryState;
        Diagnostic = diagnostic;
    }

    public WorldStateHeaderV1 Header { get; }
    public OrderedPartitionDirectoryV1 Partitions { get; }
    public SchedulerStateRefV1 SchedulerState { get; }
    public OperationStateRefV1 OperationState { get; }
    public DetailDirectoryRefV1 DetailState { get; }
    public DomainRegistryRefV1 DomainRegistryState { get; }
    public StateDiagnosticV1 Diagnostic { get; }
}
