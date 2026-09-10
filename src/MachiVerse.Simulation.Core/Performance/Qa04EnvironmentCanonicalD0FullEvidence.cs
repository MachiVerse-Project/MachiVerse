using MachiVerse.Simulation.Core.Domains.Environment;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

/// <summary>
/// Full perf.reference.v1 Environment D0 materialization evidence.
/// This path intentionally materializes every canonical D0 record through the production payload
/// authority and fail-closed reference resolver, then derives the canonical partition headers used
/// by Snapshot authority. Reduced fixtures must not use this type as release evidence.
/// </summary>
public sealed class Qa04EnvironmentCanonicalD0FullEvidenceV1
{
    internal Qa04EnvironmentCanonicalD0FullEvidenceV1(
        Qa04EnvironmentD0DomainMaterializationV1 materialization,
        IReadOnlyList<PartitionStateHeaderV1> partitionHeaders)
    {
        Materialization = materialization ?? throw new ArgumentNullException(nameof(materialization));
        PartitionHeaders = partitionHeaders ?? throw new ArgumentNullException(nameof(partitionHeaders));

        if (!Materialization.FullCanonicalD0Materialized)
            throw new InvalidDataException("qa04.environment.d0-full-evidence-materialization-incomplete");
        if (PartitionHeaders.Count != Qa04EnvironmentReferenceDecompositionV1.Partitions.Count)
            throw new InvalidDataException("qa04.environment.d0-full-evidence-header-count");
        if (PartitionHeaders.Any(static header => header.BasisStep != 0 || header.Revision != 1 || header.DetailLevel != DetailLevelV1.D0Entity))
            throw new InvalidDataException("qa04.environment.d0-full-evidence-header-envelope");
    }

    public Qa04EnvironmentD0DomainMaterializationV1 Materialization { get; }
    public IReadOnlyList<PartitionStateHeaderV1> PartitionHeaders { get; }
}

public static class Qa04EnvironmentCanonicalD0FullEvidenceBuilderV1
{
    public static Qa04EnvironmentCanonicalD0FullEvidenceV1 Build()
    {
        Qa04EnvironmentReferenceDecompositionV1.ValidateCanonicalContract();
        Qa04SpatialTileScopeAuthorityV1.ValidateCanonicalContract();
        Qa04EnvironmentLineageAuthorityV1.ValidateCanonicalContract();

        var resolver = new Qa04EnvironmentCanonicalD0ReferenceResolverV1();
        resolver.ValidateFullD0Coverage();

        var materialized = Qa04EnvironmentD0DomainMaterializerV1.MaterializeCanonical(
            new Qa04EnvironmentCanonicalD0PayloadSourceV1(),
            resolver);

        if (!materialized.FullCanonicalD0Materialized ||
            materialized.MaterializedRecordCount != Qa04EnvironmentReferenceDecompositionV1.CanonicalD0Count)
            throw new InvalidDataException("qa04.environment.d0-full-evidence-count");

        var state = materialized.State;
        var headers = new PartitionStateHeaderV1[]
        {
            Header(state.Geology, static payload => payload.CanonicalDigest()),
            Header(state.Soil, static payload => payload.CanonicalDigest()),
            Header(state.ResourceDeposit, static payload => payload.CanonicalDigest()),
            Header(state.Groundwater, static payload => payload.CanonicalDigest()),
            Header(state.Atmosphere, static payload => payload.CanonicalDigest()),
            Header(state.Climate, static payload => payload.CanonicalDigest()),
            Header(state.Weather, static payload => payload.CanonicalDigest()),
            Header(state.SurfaceWater, static payload => payload.CanonicalDigest()),
            Header(state.Ocean, static payload => payload.CanonicalDigest()),
            Header(state.Ecosystem, static payload => payload.CanonicalDigest()),
            Header(state.Contaminant, static payload => payload.CanonicalDigest()),
            Header(state.Hazard, static payload => payload.CanonicalDigest()),
            Header(state.Lineage, static payload => payload.CanonicalDigest()),
        };

        ulong total = 0;
        foreach (var header in headers)
        {
            var expected = Qa04EnvironmentReferenceDecompositionV1.Get(header.PartitionId.Value).D0Count;
            if (header.OwnerDomain.Value != "environment" || header.ItemCount != expected)
                throw new InvalidDataException($"qa04.environment.d0-full-evidence-header-count:{header.PartitionId.Value}");
            total = checked(total + header.ItemCount);
        }
        if (total != Qa04EnvironmentReferenceDecompositionV1.CanonicalD0Count)
            throw new InvalidDataException("qa04.environment.d0-full-evidence-header-total");

        return new Qa04EnvironmentCanonicalD0FullEvidenceV1(
            materialized,
            Array.AsReadOnly(headers.OrderBy(static header => header.PartitionId.Value, StringComparer.Ordinal).ToArray()));
    }

    private static PartitionStateHeaderV1 Header<TPayload>(
        DomainPartitionStateV1<TPayload> partition,
        Func<TPayload, byte[]> payloadDigest)
        => PartitionStateHeaderV1.CreateCanonical(
            partition,
            revision: 1,
            basisStep: 0,
            detailLevel: DetailLevelV1.D0Entity,
            payloadDigest);
}
