using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

/// <summary>
/// Recovered society.market_transaction /2.0 phase-1 reference source.
/// Besides ids/schema it preserves record_kind so phase-2 can enforce market_ref -> market_state.
/// </summary>
public sealed class SocietyMarketTransactionRecoveredReferenceSourceV2 : IDomainPartitionSnapshotReferenceSourceV1
{
    private readonly IReadOnlyDictionary<OpaqueId128, string> _recordKinds;

    public SocietyMarketTransactionRecoveredReferenceSourceV2(
        IReadOnlyList<SnapshotSectionFragmentMaterialV1> fragments)
    {
        ArgumentNullException.ThrowIfNull(fragments);
        if (fragments.Count == 0)
            throw Error("fragment-missing");

        var standard = StandardDomainPartitionRegistry.Get(SocietyMarketTransactionRecordSchemaV2.PartitionId);
        var ids = new List<OpaqueId128>();
        var kinds = new Dictionary<OpaqueId128, string>();
        PartitionStateHeaderV1? repeatedHeader = null;
        OpaqueId128? previous = null;
        ulong total = 0;

        for (var index = 0; index < fragments.Count; index++)
        {
            var fragment = fragments[index] ?? throw Error("fragment-null");
            if (!string.Equals(fragment.SectionId, SocietyMarketTransactionRecordSchemaV2.PartitionId, StringComparison.Ordinal) ||
                fragment.FragmentIndex != checked((uint)index) ||
                fragment.FragmentCount != checked((uint)fragments.Count))
                throw Error("fragment-shape");

            var decoded = SocietyMarketTransactionSnapshotFragmentWireV2.Decode(fragment.FragmentPayload);
            if (repeatedHeader is null) repeatedHeader = decoded.Header;
            else RequireSameHeader(repeatedHeader, decoded.Header);

            if (decoded.Records.Count != checked((int)fragment.ItemCount))
                throw Error("item-count");
            if (decoded.Records.Count == 0)
            {
                if (fragment.FirstRecordId is not null || fragment.LastRecordId is not null)
                    throw Error("range-empty");
            }
            else
            {
                var first = decoded.Records[0].RecordId.ToBytes();
                var last = decoded.Records[^1].RecordId.ToBytes();
                if (fragment.FirstRecordId is null || fragment.LastRecordId is null ||
                    !first.AsSpan().SequenceEqual(fragment.FirstRecordId) ||
                    !last.AsSpan().SequenceEqual(fragment.LastRecordId))
                    throw Error("range");
            }

            foreach (var record in decoded.Records)
            {
                if (record.RecordSchema != SocietyMarketTransactionRecordSchemaV2.RecordSchema)
                    throw Error("schema");
                if (previous is { } prior && prior.CompareTo(record.RecordId) >= 0)
                    throw Error("order");
                previous = record.RecordId;
                ids.Add(record.RecordId);
                if (!kinds.TryAdd(record.RecordId, record.Payload.RecordKind))
                    throw Error("record-id-duplicate");
            }
            total = checked(total + fragment.ItemCount);
        }

        Header = repeatedHeader ?? throw Error("header-missing");
        if (Header.PartitionId != standard.PartitionId ||
            Header.OwnerDomain != standard.OwnerDomain ||
            Header.Schema != standard.PartitionSchema)
            throw Error("header-identity");
        if (total != Header.ItemCount || total != checked((ulong)ids.Count))
            throw Error("total-count");
        if (Header.ItemCount == 0 && fragments.Count != 1)
            throw Error("empty-fragment-count");

        PartitionId = standard.PartitionId;
        RecordSchema = SocietyMarketTransactionRecordSchemaV2.RecordSchema;
        ActualItemCount = total;
        RecordIdsCanonical = Array.AsReadOnly(ids.ToArray());
        _recordKinds = kinds;
    }

    public StableToken PartitionId { get; }
    public SchemaRefV1 RecordSchema { get; }
    public ulong ActualItemCount { get; }
    public IReadOnlyList<OpaqueId128> RecordIdsCanonical { get; }
    public PartitionStateHeaderV1 Header { get; }

    public bool TryGetRecordKind(OpaqueId128 recordId, out string? recordKind)
        => _recordKinds.TryGetValue(recordId, out recordKind);

    public bool IsMarketState(OpaqueId128 recordId)
        => _recordKinds.TryGetValue(recordId, out var kind) &&
           string.Equals(kind, SocietyMarketTransactionRecordSchemaV2.MarketStateKind, StringComparison.Ordinal);

    private static void RequireSameHeader(PartitionStateHeaderV1 expected, PartitionStateHeaderV1 actual)
    {
        if (expected.PartitionId != actual.PartitionId ||
            expected.OwnerDomain != actual.OwnerDomain ||
            expected.Schema != actual.Schema ||
            expected.Revision != actual.Revision ||
            expected.BasisStep != actual.BasisStep ||
            expected.DetailLevel != actual.DetailLevel ||
            expected.ItemCount != actual.ItemCount ||
            !CryptographicOperations.FixedTimeEquals(expected.CanonicalDigest, actual.CanonicalDigest))
            throw Error("header-mismatch");
    }

    private static InvalidDataException Error(string suffix)
        => new($"persistence.snapshot.recovered-reference-{suffix}:society.market_transaction");
}
