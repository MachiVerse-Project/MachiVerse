using MachiVerse.Simulation.Core.Domains.Environment;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

/// <summary>
/// Full perf.reference.v1 Environment D1 materialization evidence. This proves exact four-to-one
/// D0 source coverage, full typed D1 materialization, fail-closed reference closure, and canonical
/// partition headers. Snapshot/recovery evidence remains a separate acceptance boundary.
/// </summary>
public sealed class Qa04EnvironmentCanonicalD1FullEvidenceV1
{
    internal Qa04EnvironmentCanonicalD1FullEvidenceV1(
        Qa04EnvironmentD1DomainMaterializationV1 materialization,
        IReadOnlyList<PartitionStateHeaderV1> partitionHeaders,
        ulong consumedD0SourceCount)
    {
        Materialization = materialization ?? throw new ArgumentNullException(nameof(materialization));
        PartitionHeaders = partitionHeaders ?? throw new ArgumentNullException(nameof(partitionHeaders));
        ConsumedD0SourceCount = consumedD0SourceCount;

        if (!Materialization.FullCanonicalD1Materialized)
            throw new InvalidDataException("qa04.environment.d1-full-evidence-materialization-incomplete");
        if (ConsumedD0SourceCount != Qa04EnvironmentReferenceDecompositionV1.CanonicalD0Count)
            throw new InvalidDataException("qa04.environment.d1-full-evidence-source-count");
        if (PartitionHeaders.Count != Qa04EnvironmentReferenceDecompositionV1.Partitions.Count)
            throw new InvalidDataException("qa04.environment.d1-full-evidence-header-count");
        if (PartitionHeaders.Any(static header => header.BasisStep != 0 || header.Revision != 1 || header.DetailLevel != DetailLevelV1.D1LocalAggregate))
            throw new InvalidDataException("qa04.environment.d1-full-evidence-header-envelope");
    }

    public Qa04EnvironmentD1DomainMaterializationV1 Materialization { get; }
    public IReadOnlyList<PartitionStateHeaderV1> PartitionHeaders { get; }
    public ulong ConsumedD0SourceCount { get; }
}

public static class Qa04EnvironmentCanonicalD1FullEvidenceBuilderV1
{
    public static Qa04EnvironmentCanonicalD1FullEvidenceV1 Build()
    {
        Qa04EnvironmentReferenceDecompositionV1.ValidateCanonicalContract();
        Qa04EnvironmentD1AggregationV1.ValidateCanonicalContract();
        Qa04SpatialTileScopeAuthorityV1.ValidateCanonicalContract();
        Qa04EnvironmentLineageAuthorityV1.ValidateCanonicalContract();

        var consumed = ValidateExactSourceCoverage();
        var resolver = new Qa04EnvironmentCanonicalD1ReferenceResolverV1();
        resolver.ValidateFullCoverage();

        var materialized = Qa04EnvironmentD1DomainMaterializerV1.MaterializeCanonical(
            new Qa04EnvironmentCanonicalD1PayloadSourceV1(),
            resolver);

        if (!materialized.FullCanonicalD1Materialized ||
            materialized.MaterializedRecordCount != Qa04EnvironmentReferenceDecompositionV1.CanonicalD1Count)
            throw new InvalidDataException("qa04.environment.d1-full-evidence-count");

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
            var expected = Qa04EnvironmentReferenceDecompositionV1.Get(header.PartitionId.Value).D1Count;
            if (header.OwnerDomain.Value != "environment" || header.ItemCount != expected)
                throw new InvalidDataException($"qa04.environment.d1-full-evidence-header-count:{header.PartitionId.Value}");
            total = checked(total + header.ItemCount);
        }
        if (total != Qa04EnvironmentReferenceDecompositionV1.CanonicalD1Count)
            throw new InvalidDataException("qa04.environment.d1-full-evidence-header-total");

        return new Qa04EnvironmentCanonicalD1FullEvidenceV1(
            materialized,
            Array.AsReadOnly(headers.OrderBy(static header => header.PartitionId.Value, StringComparer.Ordinal).ToArray()),
            consumed);
    }

    private static ulong ValidateExactSourceCoverage()
    {
        ulong nextExpectedD0 = 0;
        ulong consumed = 0;
        for (ulong d1 = 0; d1 < Qa04EnvironmentReferenceDecompositionV1.CanonicalD1Count; d1++)
        {
            var binding = Qa04EnvironmentReferenceDecompositionV1.BindD1(d1);
            if (binding.SourceD0GlobalOrdinals.Count != Qa04EnvironmentD1AggregationV1.SourceCount)
                throw new InvalidDataException("qa04.environment.d1-full-evidence-source-cardinality");
            foreach (var sourceOrdinal in binding.SourceD0GlobalOrdinals)
            {
                if (sourceOrdinal != nextExpectedD0)
                    throw new InvalidDataException($"qa04.environment.d1-full-evidence-source-gap:{nextExpectedD0}:{sourceOrdinal}");
                var source = Qa04EnvironmentReferenceDecompositionV1.BindD0(sourceOrdinal);
                if (source.PartitionId != binding.PartitionId)
                    throw new InvalidDataException("qa04.environment.d1-full-evidence-source-partition");
                nextExpectedD0 = checked(nextExpectedD0 + 1);
                consumed = checked(consumed + 1);
            }
        }
        if (nextExpectedD0 != Qa04EnvironmentReferenceDecompositionV1.CanonicalD0Count ||
            consumed != Qa04EnvironmentReferenceDecompositionV1.CanonicalD0Count)
            throw new InvalidDataException("qa04.environment.d1-full-evidence-source-total");
        return consumed;
    }

    private static PartitionStateHeaderV1 Header<TPayload>(
        DomainPartitionStateV1<TPayload> partition,
        Func<TPayload, byte[]> payloadDigest)
        => PartitionStateHeaderV1.CreateCanonical(
            partition,
            revision: 1,
            basisStep: 0,
            detailLevel: DetailLevelV1.D1LocalAggregate,
            payloadDigest);
}
