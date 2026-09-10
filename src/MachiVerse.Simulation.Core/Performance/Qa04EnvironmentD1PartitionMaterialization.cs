using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

/// <summary>
/// Production envelope/partition materializer for canonical Environment D1 descriptors. Exact
/// descriptor identity, same-partition four-source binding, schema, and genesis envelope are owned
/// here. Aggregate payload construction remains explicit so unresolved lineage authority cannot be
/// silently synthesized.
/// </summary>
public static class Qa04EnvironmentD1PartitionMaterializerV1
{
    public const ulong InitialRecordRevision = 1;
    public const ulong InitialCreatedStep = 0;
    public const string SpatialScopePartitionId = Qa04EnvironmentD0PartitionMaterializerV1.SpatialScopePartitionId;

    public static void ValidateCanonicalContract()
    {
        Qa04EnvironmentReferenceDecompositionV1.ValidateCanonicalContract();
        Qa04EnvironmentD1AggregationV1.ValidateCanonicalContract();
        _ = StandardDomainPartitionRegistry.Get(SpatialScopePartitionId);
        if (InitialRecordRevision != 1 || InitialCreatedStep != 0)
            throw new InvalidDataException("qa04.environment.d1-genesis-envelope-drift");
    }

    /// <summary>
    /// Resolves the D1 descriptor's own canonical regional tile through the same Spatial authority
    /// seam used by D0. D1 does not derive scope by guessing from its four source records; the
    /// perf.reference.v1 descriptor already owns the RegionalTileIndex deterministically.
    /// </summary>
    public static PartitionRecordRefV1 ResolveSpatialScope(
        Qa04EnvironmentD1BindingV1 binding,
        Func<ushort, PartitionRecordRefV1> tileScopeForTile)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(tileScopeForTile);
        if (binding.Descriptor.RegionalTileIndex >= Qa04ReferenceLoadV1.RegionalTileCount)
            throw new InvalidDataException("qa04.environment.d1-regional-tile-range");

        var scope = tileScopeForTile(binding.Descriptor.RegionalTileIndex);
        if (scope.PartitionId.Value != SpatialScopePartitionId || scope.RecordId.IsZero)
            throw new InvalidDataException("qa04.environment.d1-spatial-scope-ref-invalid");
        return scope;
    }

    public static DomainPartitionStateV1<TPayload> MaterializeCanonicalPartition<TPayload>(
        string partitionId,
        Func<Qa04EnvironmentD1BindingV1, TPayload> aggregatePayloadFactory,
        Func<TPayload, IReadOnlyDictionary<string, object?>> toStandardPayload,
        IDomainRecordSchemaResolverV1 referenceResolver)
        => MaterializePartition(
            partitionId,
            Qa04EnvironmentReferenceDecompositionV1.Get(partitionId).D1Count,
            aggregatePayloadFactory,
            toStandardPayload,
            referenceResolver);

    public static DomainPartitionStateV1<TPayload> MaterializePartition<TPayload>(
        string partitionId,
        ulong recordCount,
        Func<Qa04EnvironmentD1BindingV1, TPayload> aggregatePayloadFactory,
        Func<TPayload, IReadOnlyDictionary<string, object?>> toStandardPayload,
        IDomainRecordSchemaResolverV1 referenceResolver)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(partitionId);
        ArgumentNullException.ThrowIfNull(aggregatePayloadFactory);
        ArgumentNullException.ThrowIfNull(toStandardPayload);
        ArgumentNullException.ThrowIfNull(referenceResolver);
        ValidateCanonicalContract();

        var slice = Qa04EnvironmentReferenceDecompositionV1.Get(partitionId);
        if (recordCount is 0 || recordCount > slice.D1Count)
            throw new ArgumentOutOfRangeException(nameof(recordCount));
        var identity = StandardDomainPartitionRegistry.Get(partitionId);
        if (identity.OwnerDomain.Value != "environment")
            throw new InvalidDataException($"qa04.environment.d1-foreign-owner:{partitionId}");

        var validator = new StandardDomainPayloadCodecValidatorV1();
        var records = new DomainRecordEnvelopeV1<TPayload>[checked((int)recordCount)];
        for (ulong local = 0; local < recordCount; local++)
        {
            var binding = Qa04EnvironmentReferenceDecompositionV1.BindD1(
                checked(slice.D1StartOrdinal + local));
            records[checked((int)local)] = CreateRecordValidated(
                binding,
                identity,
                aggregatePayloadFactory,
                toStandardPayload,
                referenceResolver,
                validator);
        }

        var partition = new DomainPartitionStateV1<TPayload>(identity, records);
        if (partition.ItemCount != recordCount)
            throw new InvalidDataException($"qa04.environment.d1-partition-count-mismatch:{partitionId}");
        return partition;
    }

    public static DomainRecordEnvelopeV1<TPayload> CreateRecord<TPayload>(
        ulong globalOrdinal,
        Func<Qa04EnvironmentD1BindingV1, TPayload> aggregatePayloadFactory,
        Func<TPayload, IReadOnlyDictionary<string, object?>> toStandardPayload,
        IDomainRecordSchemaResolverV1 referenceResolver)
    {
        ArgumentNullException.ThrowIfNull(aggregatePayloadFactory);
        ArgumentNullException.ThrowIfNull(toStandardPayload);
        ArgumentNullException.ThrowIfNull(referenceResolver);
        ValidateCanonicalContract();

        var binding = Qa04EnvironmentReferenceDecompositionV1.BindD1(globalOrdinal);
        var identity = StandardDomainPartitionRegistry.Get(binding.PartitionId.Value);
        return CreateRecordValidated(
            binding,
            identity,
            aggregatePayloadFactory,
            toStandardPayload,
            referenceResolver,
            new StandardDomainPayloadCodecValidatorV1());
    }

    private static DomainRecordEnvelopeV1<TPayload> CreateRecordValidated<TPayload>(
        Qa04EnvironmentD1BindingV1 binding,
        DomainPartitionIdentityV1 identity,
        Func<Qa04EnvironmentD1BindingV1, TPayload> aggregatePayloadFactory,
        Func<TPayload, IReadOnlyDictionary<string, object?>> toStandardPayload,
        IDomainRecordSchemaResolverV1 referenceResolver,
        StandardDomainPayloadCodecValidatorV1 validator)
    {
        if (binding.PartitionId != identity.PartitionId)
            throw new InvalidDataException("qa04.environment.d1-binding-partition-mismatch");
        if (binding.Descriptor.DetailLevel != DetailLevelV1.D1LocalAggregate)
            throw new InvalidDataException("qa04.environment.d1-detail-level-drift");
        if (binding.SourceD0GlobalOrdinals.Count != Qa04EnvironmentD1AggregationV1.SourceCount)
            throw new InvalidDataException("qa04.environment.d1-source-cardinality-drift");
        foreach (var sourceOrdinal in binding.SourceD0GlobalOrdinals)
        {
            var source = Qa04EnvironmentReferenceDecompositionV1.BindD0(sourceOrdinal);
            if (source.PartitionId != binding.PartitionId)
                throw new InvalidDataException("qa04.environment.d1-cross-partition-source");
        }

        var payload = aggregatePayloadFactory(binding)
            ?? throw new InvalidDataException($"qa04.environment.d1-payload-null:{binding.PartitionId.Value}");
        var standardPayload = toStandardPayload(payload)
            ?? throw new InvalidDataException($"qa04.environment.d1-standard-payload-null:{binding.PartitionId.Value}");
        validator.Validate(binding.PartitionId.Value, standardPayload, referenceResolver);

        return new DomainRecordEnvelopeV1<TPayload>(
            binding.Descriptor.RecordId,
            identity.RecordSchema,
            revision: InitialRecordRevision,
            createdStep: InitialCreatedStep,
            retiredStep: null,
            detailLevel: DetailLevelV1.D1LocalAggregate,
            lineageRef: null,
            payload);
    }
}
