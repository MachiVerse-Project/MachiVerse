using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

/// <summary>
/// Frozen authority for society.market_transaction record schema 2.0.
/// The generic v1 authority remains strict; migration-aware composition must opt into this authority explicitly.
/// </summary>
public sealed class SocietyMarketTransactionSnapshotAuthorityV2 : IDomainPartitionSnapshotAuthorityV1
{
    private readonly bool _preparedHeaderVerified;
    private IReadOnlyList<OpaqueId128>? _recordIdsCanonical;
    private IReadOnlyList<PartitionDigestSliceV2>? _snapshotCommitments;

    public SocietyMarketTransactionSnapshotAuthorityV2(
        SocietyMarketTransactionPartitionStateV2 partition,
        PartitionStateHeaderV1 header)
        : this(partition, header, preparedHeaderVerified: false)
    {
        _ = RecordIdsCanonical;
        VerifyBoundAuthority();
    }

    private SocietyMarketTransactionSnapshotAuthorityV2(
        SocietyMarketTransactionPartitionStateV2 partition,
        PartitionStateHeaderV1 header,
        bool preparedHeaderVerified)
    {
        Partition = partition ?? throw new ArgumentNullException(nameof(partition));
        Header = header ?? throw new ArgumentNullException(nameof(header));
        _preparedHeaderVerified = preparedHeaderVerified;
        RequireStructuralBinding();
    }

    internal static SocietyMarketTransactionSnapshotAuthorityV2 FromPreparedHeader(
        SocietyMarketTransactionPartitionStateV2 partition,
        PartitionStateHeaderV1 header)
        => new(partition, header, preparedHeaderVerified: true);

    internal SocietyMarketTransactionSnapshotAuthorityV2 WithSnapshotCommitments(
        IReadOnlyList<PartitionDigestSliceV2> commitments)
    {
        ArgumentNullException.ThrowIfNull(commitments);
        var captured = commitments.Select(static slice => new PartitionDigestSliceV2(
            slice.Prefix, slice.RecordCount, slice.ContentDigest)).ToArray();
        var root = RecordIdPrefixPartitionDigestV2.CreateHeaderFromPrevalidatedSlices(
            Identity, Header.Revision, Header.BasisStep, Header.DetailLevel, Header.ItemCount, captured);
        if (Header.DigestAlgorithm != PartitionCanonicalDigestAlgorithmV1.RecordIdPrefixV2 ||
            !CryptographicOperations.FixedTimeEquals(root.CanonicalDigest, Header.CanonicalDigest))
            throw new InvalidDataException("persistence.snapshot.society-market-v2-commitment-root");
        return new SocietyMarketTransactionSnapshotAuthorityV2(Partition, Header, _preparedHeaderVerified)
        {
            _snapshotCommitments = Array.AsReadOnly(captured),
        };
    }

    public SocietyMarketTransactionPartitionStateV2 Partition { get; }
    public StableToken PartitionId => Identity.PartitionId;
    public DomainPartitionIdentityV1 Identity => SocietyMarketTransactionPartitionIdentityV2.Identity;
    public PartitionStateHeaderV1 Header { get; }
    public SchemaRefV1 RecordSchema => SocietyMarketTransactionRecordSchemaV2.RecordSchema;
    public ulong ActualItemCount => Partition.State.ItemCount;
    public IReadOnlyList<OpaqueId128> RecordIdsCanonical
        => _recordIdsCanonical ??= Array.AsReadOnly(
            Partition.State.RecordsCanonical.Select(static record => record.RecordId).ToArray());

    public static SocietyMarketTransactionSnapshotAuthorityV2 CreateCanonical(
        SocietyMarketTransactionPartitionStateV2 partition,
        ulong revision,
        ulong basisStep,
        DetailLevelV1 detailLevel)
    {
        ArgumentNullException.ThrowIfNull(partition);
        var header = PartitionStateHeaderV1.CreateCanonical(
            partition.State,
            revision,
            basisStep,
            detailLevel,
            static payload => SocietyMarketTransactionPayloadCanonicalDigestV2.Compute(payload));
        return new SocietyMarketTransactionSnapshotAuthorityV2(partition, header);
    }

    public void VerifyBoundAuthority()
        => VerifyBoundAuthorityCore(allowPreparedHeader: true);

    internal void VerifySnapshotMaterial()
        => VerifyBoundAuthorityCore(allowPreparedHeader: false);

    private void VerifyBoundAuthorityCore(bool allowPreparedHeader)
    {
        RequireStructuralBinding();
        if (allowPreparedHeader && _preparedHeaderVerified)
            return;

        if (!allowPreparedHeader)
            RequireSnapshotRecordMaterial();

        if (ActualItemCount != checked((ulong)RecordIdsCanonical.Count))
            throw new InvalidDataException("persistence.snapshot.society-market-v2-item-count");

        OpaqueId128? previous = null;
        foreach (var recordId in RecordIdsCanonical)
        {
            if (recordId.IsZero)
                throw new InvalidDataException("persistence.snapshot.society-market-v2-record-id-zero");
            if (previous is { } prior && prior.CompareTo(recordId) >= 0)
                throw new InvalidDataException("persistence.snapshot.society-market-v2-record-order");
            previous = recordId;
        }

        var observedSliceIndex = 0;
        void ObserveSlice(PartitionDigestSliceV2 actual)
        {
            if (_snapshotCommitments is null) return;
            var expected = observedSliceIndex < _snapshotCommitments.Count
                ? _snapshotCommitments[observedSliceIndex] : null;
            if (expected is null || expected.Prefix != actual.Prefix ||
                expected.RecordCount != actual.RecordCount ||
                !CryptographicOperations.FixedTimeEquals(expected.ContentDigest, actual.ContentDigest))
            {
                throw new InvalidDataException(
                    $"persistence.snapshot.society-market-v2-slice-material partition={PartitionId.Value} " +
                    $"step={Header.BasisStep} prefix={actual.Prefix} " +
                    $"expected_prefix={expected?.Prefix} expected_items={expected?.RecordCount} actual_items={actual.RecordCount} " +
                    $"expected={(expected is null ? "missing" : Convert.ToHexString(expected.ContentDigest))} " +
                    $"actual={Convert.ToHexString(actual.ContentDigest)}");
            }
            observedSliceIndex++;
        }

        var recomputed = Header.DigestAlgorithm switch
        {
            PartitionCanonicalDigestAlgorithmV1.LegacyFlatV1 =>
                PartitionStateHeaderV1.CreateCanonical(
                    Partition.State,
                    Header.Revision,
                    Header.BasisStep,
                    Header.DetailLevel,
                    static payload => SocietyMarketTransactionPayloadCanonicalDigestV2.Compute(payload)),
            PartitionCanonicalDigestAlgorithmV1.RecordIdPrefixV2 =>
                RecordIdPrefixPartitionDigestV2.CreateHeaderWithSliceObserver(
                    Partition.State,
                    Header.Revision,
                    Header.BasisStep,
                    Header.DetailLevel,
                    static record => PartitionStateHeaderV1.EncodeCanonicalRecord(
                        record,
                        SocietyMarketTransactionPayloadCanonicalDigestV2.Compute(record.Payload)),
                    ObserveSlice),
            _ => throw new InvalidDataException(
                "persistence.snapshot.society-market-v2-digest-algorithm"),
        };
        if (_snapshotCommitments is not null && observedSliceIndex != _snapshotCommitments.Count)
            throw new InvalidDataException("persistence.snapshot.society-market-v2-slice-count");
        if (recomputed.PartitionId != Header.PartitionId ||
            recomputed.OwnerDomain != Header.OwnerDomain ||
            recomputed.Schema != Header.Schema ||
            recomputed.Revision != Header.Revision ||
            recomputed.BasisStep != Header.BasisStep ||
            recomputed.DetailLevel != Header.DetailLevel ||
            recomputed.ItemCount != Header.ItemCount ||
            recomputed.DigestAlgorithm != Header.DigestAlgorithm ||
            !CryptographicOperations.FixedTimeEquals(recomputed.CanonicalDigest, Header.CanonicalDigest))
        {
            Console.Error.WriteLine(
                $"QA04_SNAPSHOT_DIGEST_MISMATCH partition={PartitionId.Value} " +
                $"algorithm={Header.DigestAlgorithm} step={Header.BasisStep} revision={Header.Revision} " +
                $"items={Header.ItemCount} expected={Convert.ToHexString(Header.CanonicalDigest)} " +
                $"actual={Convert.ToHexString(recomputed.CanonicalDigest)}");
            throw new InvalidDataException("persistence.snapshot.society-market-v2-header-material");
        }
    }

    private void RequireSnapshotRecordMaterial()
    {
        using var serializedRecords = Partition.RecordSet.RecordsCanonical.GetEnumerator();
        foreach (var record in Partition.State.RecordsCanonical)
        {
            if (!serializedRecords.MoveNext())
                throw new InvalidDataException("persistence.snapshot.society-market-v2-record-material");
            var serialized = serializedRecords.Current;
            if (record.RecordId != serialized.RecordId ||
                record.RecordSchema != serialized.RecordSchema ||
                record.Revision != serialized.Revision ||
                record.CreatedStep != serialized.CreatedStep ||
                record.RetiredStep != serialized.RetiredStep ||
                record.DetailLevel != serialized.DetailLevel ||
                record.LineageRef != serialized.LineageRef ||
                (!ReferenceEquals(record.Payload, serialized.Payload) &&
                 !CryptographicOperations.FixedTimeEquals(
                     SocietyMarketTransactionPayloadCanonicalDigestV2.Compute(record.Payload),
                     SocietyMarketTransactionPayloadCanonicalDigestV2.Compute(serialized.Payload))))
            {
                Console.Error.WriteLine(
                    $"QA04_SNAPSHOT_RECORD_MISMATCH partition={PartitionId.Value} " +
                    $"step={Header.BasisStep} record_id={record.RecordId}");
                throw new InvalidDataException("persistence.snapshot.society-market-v2-record-material");
            }
        }
        if (serializedRecords.MoveNext())
            throw new InvalidDataException("persistence.snapshot.society-market-v2-record-material");
    }

    private void RequireStructuralBinding()
    {
        SocietyMarketTransactionPartitionIdentityV2.ValidateCanonicalContract();
        if (Partition.State.Identity != Identity)
            throw new InvalidDataException("persistence.snapshot.society-market-v2-authority-identity");
        if (Header.PartitionId != Identity.PartitionId ||
            Header.OwnerDomain != Identity.OwnerDomain ||
            Header.Schema != Identity.PartitionSchema)
            throw new InvalidDataException("persistence.snapshot.society-market-v2-header-identity");
        if (Header.ItemCount != ActualItemCount)
            throw new InvalidDataException("persistence.snapshot.society-market-v2-item-count");
    }
}
