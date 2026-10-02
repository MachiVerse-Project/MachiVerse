using System.Diagnostics;
using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Domains.Participation;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04ParticipationControlModeSnapshotRecoveryMeasurementV1(
    ulong RecoveredRecordCount,
    ulong EncodedFragmentPayloadBytes,
    int FragmentCount,
    TimeSpan EncodeElapsed,
    TimeSpan RecoveryElapsed,
    long ManagedMemoryDeltaBytes);

/// <summary>
/// Production Snapshot/recovery and measurement proof for the full 1,000,000-record canonical
/// participation.control_mode authority. Measurements are observed values from the actual provider
/// invocation; no record-cardinality extrapolation is used.
/// </summary>
public static class Qa04ParticipationControlModeSnapshotRecoveryEvidenceV1
{
    public const ulong CanonicalRecordCount = Qa04ParticipationControlModeCanonicalAuthorityV1.CanonicalCount;

    public static Qa04ParticipationControlModeSnapshotRecoveryMeasurementV1 Verify(
        Qa04ParticipationControlModeCanonicalMaterializationV1 materialization)
    {
        ArgumentNullException.ThrowIfNull(materialization);
        if (materialization.MaterializedRecordCount != CanonicalRecordCount)
            throw new InvalidDataException("qa04.participation.control-mode-snapshot-record-count");

        var partition = materialization.Partition;
        if (partition.Identity.PartitionId.Value != ParticipationControlModePayloadV1.PartitionId)
            throw new InvalidDataException("qa04.participation.control-mode-snapshot-partition-identity");

        var header = PartitionStateHeaderV1.CreateCanonical(
            partition,
            revision: 1,
            basisStep: 0,
            detailLevel: DetailLevelV1.D0Entity,
            static payload => payload.CanonicalDigest());
        if (header.ItemCount != CanonicalRecordCount)
            throw new InvalidDataException("qa04.participation.control-mode-snapshot-header-count");

        var authority = new DomainPartitionSnapshotAuthorityV1<ParticipationControlModePayloadV1>(
            partition,
            header,
            static payload => payload.CanonicalDigest());
        var provider = new DomainPartitionSnapshotSectionProviderV1<ParticipationControlModePayloadV1>(
            ParticipationControlModePayloadV1.PartitionId,
            static payload => payload.ToStandardPayload(),
            ParticipationControlModePayloadV1.FromStandardPayload,
            static payload => payload.CanonicalDigest(),
            StandardDomainNestedSnapshotCodecRegistryV1.Default);

        var memoryBefore = GC.GetTotalMemory(forceFullCollection: false);
        var encodeWatch = Stopwatch.StartNew();
        var section = provider.Create(authority, materialization.References);
        encodeWatch.Stop();

        if (section.LogicalItemCount != header.ItemCount ||
            !CryptographicOperations.FixedTimeEquals(section.LogicalContentDigest, header.CanonicalDigest))
            throw new InvalidDataException("qa04.participation.control-mode-snapshot-section-authority");
        if (section.Fragments.Count == 0)
            throw new InvalidDataException("qa04.participation.control-mode-snapshot-fragment-missing");

        var encodedBytes = section.Fragments.Aggregate(
            0UL,
            static (sum, fragment) => checked(sum + checked((ulong)fragment.FragmentPayload.LongLength)));
        if (encodedBytes == 0)
            throw new InvalidDataException("qa04.participation.control-mode-snapshot-byte-count-zero");

        var verifier = provider.CreateSemanticVerifier(header, materialization.References);
        var recoveryWatch = Stopwatch.StartNew();
        var recovered = verifier.VerifyWithContext is not null
            ? verifier.VerifyWithContext(
                section.Fragments,
                new SnapshotSectionSemanticVerificationContextV1(materialization.References))
            : verifier.Verify(section.Fragments);
        recoveryWatch.Stop();

        if (recovered.LogicalItemCount != header.ItemCount ||
            !CryptographicOperations.FixedTimeEquals(recovered.LogicalContentDigest, header.CanonicalDigest))
            throw new InvalidDataException("qa04.participation.control-mode-snapshot-semantic-rehash");

        var memoryAfter = GC.GetTotalMemory(forceFullCollection: false);
        return new Qa04ParticipationControlModeSnapshotRecoveryMeasurementV1(
            recovered.LogicalItemCount,
            encodedBytes,
            section.Fragments.Count,
            encodeWatch.Elapsed,
            recoveryWatch.Elapsed,
            Math.Max(0L, checked(memoryAfter - memoryBefore)));
    }
}
