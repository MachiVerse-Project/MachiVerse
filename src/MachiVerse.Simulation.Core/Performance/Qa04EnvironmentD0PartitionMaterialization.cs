using MachiVerse.Simulation.Core.Domains.Environment;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

/// <summary>
/// Production envelope/partition materializer for one canonical Environment D0 slice. Canonical
/// descriptor identity and genesis envelope are owned here; partition-specific payload values and
/// cross-partition Ref targets remain explicit owner inputs until their authority materializers are
/// connected. This prevents synthetic scope ids or lineage targets from leaking into release state.
/// </summary>
public static class Qa04EnvironmentD0PartitionMaterializerV1
{
    public const ulong InitialRecordRevision = 1;
    public const ulong InitialCreatedStep = 0;
    public const string SpatialScopePartitionId = "spatial.scope_registry";

    public static void ValidateCanonicalContract()
    {
        Qa04EnvironmentReferenceDecompositionV1.ValidateCanonicalContract();
        Qa04EnvironmentGenesisContractV1.ValidateCanonicalContract();
        _ = StandardDomainPartitionRegistry.Get(SpatialScopePartitionId);
        if (InitialRecordRevision != 1 || InitialCreatedStep != 0)
            throw new InvalidDataException("qa04.environment.d0-genesis-envelope-drift");
    }

    /// <summary>
    /// Resolves the descriptor's canonical regional tile into its owning Spatial scope authority.
    /// The scope RecordId is deliberately supplied by Spatial; Environment only validates the target
    /// partition, non-zero identity, and descriptor tile range.
    /// </summary>
    public static PartitionRecordRefV1 ResolveSpatialScope(
        Qa04EnvironmentD0BindingV1 binding,
        Func<ushort, PartitionRecordRefV1> tileScopeForTile)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(tileScopeForTile);
        if (binding.Descriptor.RegionalTileIndex >= Qa04ReferenceLoadV1.RegionalTileCount)
            throw new InvalidDataException("qa04.environment.d0-regional-tile-range");
        var scope = tileScopeForTile(binding.Descriptor.RegionalTileIndex);
        if (scope.PartitionId.Value != SpatialScopePartitionId || scope.RecordId.IsZero)
            throw new InvalidDataException("qa04.environment.d0-spatial-scope-ref-invalid");
        return scope;
    }

    public static DomainPartitionStateV1<TPayload> MaterializeCanonicalPartition<TPayload>(
        string partitionId,
        Func<Qa04EnvironmentD0BindingV1, TPayload> payloadFactory,
        Func<TPayload, IReadOnlyDictionary<string, object?>> toStandardPayload,
        IDomainRecordSchemaResolverV1 referenceResolver)
        => MaterializePartition(
            partitionId,
            Qa04EnvironmentReferenceDecompositionV1.Get(partitionId).D0Count,
            payloadFactory,
            toStandardPayload,
            referenceResolver);

    public static DomainPartitionStateV1<TPayload> MaterializePartition<TPayload>(
        string partitionId,
        ulong recordCount,
        Func<Qa04EnvironmentD0BindingV1, TPayload> payloadFactory,
        Func<TPayload, IReadOnlyDictionary<string, object?>> toStandardPayload,
        IDomainRecordSchemaResolverV1 referenceResolver)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(partitionId);
        ArgumentNullException.ThrowIfNull(payloadFactory);
        ArgumentNullException.ThrowIfNull(toStandardPayload);
        ArgumentNullException.ThrowIfNull(referenceResolver);
        ValidateCanonicalContract();

        var slice = Qa04EnvironmentReferenceDecompositionV1.Get(partitionId);
        if (recordCount is 0 || recordCount > slice.D0Count)
            throw new ArgumentOutOfRangeException(nameof(recordCount));

        var identity = StandardDomainPartitionRegistry.Get(partitionId);
        if (identity.OwnerDomain.Value != "environment")
            throw new InvalidDataException($"qa04.environment.d0-foreign-owner:{partitionId}");

        var validator = new StandardDomainPayloadCodecValidatorV1();
        var records = CreateRecords(
                slice,
                recordCount,
                identity,
                payloadFactory,
                toStandardPayload,
                referenceResolver,
                validator)
            .ToArray();
        var partition = new DomainPartitionStateV1<TPayload>(identity, records);
        if (partition.ItemCount != recordCount)
            throw new InvalidDataException($"qa04.environment.d0-partition-count-mismatch:{partitionId}");
        return partition;
    }

    public static DomainRecordEnvelopeV1<TPayload> CreateRecord<TPayload>(
        ulong globalOrdinal,
        Func<Qa04EnvironmentD0BindingV1, TPayload> payloadFactory,
        Func<TPayload, IReadOnlyDictionary<string, object?>> toStandardPayload,
        IDomainRecordSchemaResolverV1 referenceResolver)
    {
        ArgumentNullException.ThrowIfNull(payloadFactory);
        ArgumentNullException.ThrowIfNull(toStandardPayload);
        ArgumentNullException.ThrowIfNull(referenceResolver);
        ValidateCanonicalContract();

        var binding = Qa04EnvironmentReferenceDecompositionV1.BindD0(globalOrdinal);
        var identity = StandardDomainPartitionRegistry.Get(binding.PartitionId.Value);
        var validator = new StandardDomainPayloadCodecValidatorV1();
        return CreateRecordValidated(binding, identity, payloadFactory, toStandardPayload, referenceResolver, validator);
    }

    private static IEnumerable<DomainRecordEnvelopeV1<TPayload>> CreateRecords<TPayload>(
        Qa04EnvironmentPartitionDecompositionV1 slice,
        ulong count,
        DomainPartitionIdentityV1 identity,
        Func<Qa04EnvironmentD0BindingV1, TPayload> payloadFactory,
        Func<TPayload, IReadOnlyDictionary<string, object?>> toStandardPayload,
        IDomainRecordSchemaResolverV1 referenceResolver,
        StandardDomainPayloadCodecValidatorV1 validator)
    {
        for (ulong local = 0; local < count; local++)
        {
            var binding = Qa04EnvironmentReferenceDecompositionV1.BindD0(
                checked(slice.D0StartOrdinal + local));
            yield return CreateRecordValidated(
                binding,
                identity,
                payloadFactory,
                toStandardPayload,
                referenceResolver,
                validator);
        }
    }

    private static DomainRecordEnvelopeV1<TPayload> CreateRecordValidated<TPayload>(
        Qa04EnvironmentD0BindingV1 binding,
        DomainPartitionIdentityV1 identity,
        Func<Qa04EnvironmentD0BindingV1, TPayload> payloadFactory,
        Func<TPayload, IReadOnlyDictionary<string, object?>> toStandardPayload,
        IDomainRecordSchemaResolverV1 referenceResolver,
        StandardDomainPayloadCodecValidatorV1 validator)
    {
        if (binding.PartitionId != identity.PartitionId)
            throw new InvalidDataException("qa04.environment.d0-binding-partition-mismatch");
        if (binding.Descriptor.DetailLevel != DetailLevelV1.D0Entity)
            throw new InvalidDataException("qa04.environment.d0-detail-level-drift");

        var payload = payloadFactory(binding)
            ?? throw new InvalidDataException($"qa04.environment.d0-payload-null:{binding.PartitionId.Value}");
        var standardPayload = toStandardPayload(payload)
            ?? throw new InvalidDataException($"qa04.environment.d0-standard-payload-null:{binding.PartitionId.Value}");
        validator.Validate(binding.PartitionId.Value, standardPayload, referenceResolver);

        return new DomainRecordEnvelopeV1<TPayload>(
            binding.Descriptor.RecordId,
            identity.RecordSchema,
            revision: InitialRecordRevision,
            createdStep: InitialCreatedStep,
            retiredStep: null,
            detailLevel: DetailLevelV1.D0Entity,
            lineageRef: null,
            payload);
    }
}
