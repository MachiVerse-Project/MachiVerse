using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Runtime;

namespace MachiVerse.Simulation.Core.Persistence;

public sealed record RecoveredQa04CompactOperationStateV1(
    ulong BasisStep,
    IReadOnlyList<DurableOperationStateV1> MutableOperations,
    Qa04OperationClosedPrefixV1 ClosedPrefix,
    IReadOnlyList<CrossDomainTransactionStateV1> ActiveTransactions,
    byte[] LogicalContentDigest);

/// <summary>
/// QA-04 Gate4 Step2 physical binding for the existing core.operation-state /2.0 Snapshot section.
/// The wrapper stores exactly one compact closed-prefix certificate plus the ordinary mutable rows
/// and current active CrossDomainTransaction set. It intentionally reuses the existing v2 item wire
/// for mutable rows/transactions so the standard exact-103 section identity remains unchanged.
/// </summary>
public static class Qa04CompactOperationStateSnapshotSectionProviderV1
{
    private const string SectionIdValue = "core.operation-state";
    private const int PrefixField = 11;
    private const int NestedV2PayloadField = 12;

    private sealed record DecodedCompactAuthorityV1(
        IReadOnlyList<DurableOperationStateV1> Operations,
        Qa04OperationClosedPrefixV1 Prefix,
        IReadOnlyList<CrossDomainTransactionStateV1> Transactions,
        ulong LogicalItemCount,
        byte[] CanonicalDigest);

    private sealed record CompactFragmentV1(
        ulong BasisStep,
        Qa04OperationClosedPrefixV1? Prefix,
        CoreOperationStateSnapshotFragmentV2 Nested);

    public static CanonicalSnapshotSectionMaterialV1 Create(
        ulong basisStep,
        IReadOnlyList<DurableOperationStateV1> mutableOperations,
        Qa04OperationClosedPrefixV1 closedPrefix,
        IReadOnlyList<CrossDomainTransactionStateV1> activeTransactions)
    {
        ArgumentNullException.ThrowIfNull(mutableOperations);
        ArgumentNullException.ThrowIfNull(closedPrefix);
        ArgumentNullException.ThrowIfNull(activeTransactions);
        closedPrefix.Validate(basisStep);

        var authority = Qa04OperationAuthorityV1.Canonicalize(
            mutableOperations,
            closedPrefix,
            activeTransactions,
            basisStep);
        if (authority.Schema != CoreOperationStateSnapshotAuthorityV2.Schema)
            throw new InvalidDataException("snapshot-core.operation-v2.qa04-schema-mismatch");

        // Reuse the standard v2 fragmentation and item codecs. Its semantic digest is deliberately
        // not used here; QA-04 commits mv.qa04-operation-authority.v1 instead.
        var generic = CoreOperationStateSnapshotSectionProviderV2.Create(
            basisStep,
            mutableOperations,
            activeTransactions);
        var fragments = new SnapshotSectionFragmentMaterialV1[generic.Fragments.Count];
        for (var i = 0; i < generic.Fragments.Count; i++)
        {
            var source = generic.Fragments[i];
            var payload = EncodeCompactFragment(
                basisStep,
                i == 0 ? closedPrefix : null,
                source.FragmentPayload);
            if (payload.Length > CanonicalSnapshotSectionValidationV1.HardMaxUncompressedBytes)
                throw new InvalidDataException("persistence.snapshot-item-too-large");
            fragments[i] = new SnapshotSectionFragmentMaterialV1(
                SectionIdValue,
                source.FragmentIndex,
                source.FragmentCount,
                null,
                null,
                checked(source.ItemCount + (i == 0 ? 1UL : 0UL)),
                payload);
        }

        return new CanonicalSnapshotSectionMaterialV1(
            SectionIdValue,
            CoreOperationStateSnapshotAuthorityV2.Schema,
            checked(generic.LogicalItemCount + 1UL),
            authority.CanonicalDigest.ToArray(),
            Array.AsReadOnly(fragments));
    }

    public static RecoveredQa04CompactOperationStateV1 Recover(
        CanonicalSnapshotSectionMaterialV1 section,
        ulong expectedBasisStep)
    {
        ArgumentNullException.ThrowIfNull(section);
        if (!string.Equals(section.SectionId, SectionIdValue, StringComparison.Ordinal) ||
            section.SectionSchema != CoreOperationStateSnapshotAuthorityV2.Schema ||
            section.LogicalContentDigest is null || section.LogicalContentDigest.Length != 32)
            throw new InvalidDataException("snapshot-core.operation-v2.qa04-section-shape");

        var decoded = DecodeAuthority(section.Fragments, expectedBasisStep);
        if (decoded.LogicalItemCount != section.LogicalItemCount)
            throw new InvalidDataException("snapshot-core.operation-v2.qa04-semantic-item-count-mismatch");
        if (!CryptographicOperations.FixedTimeEquals(decoded.CanonicalDigest, section.LogicalContentDigest))
            throw new InvalidDataException("snapshot-core.operation-v2.qa04-semantic-digest-mismatch");
        return new RecoveredQa04CompactOperationStateV1(
            expectedBasisStep,
            decoded.Operations,
            decoded.Prefix,
            decoded.Transactions,
            decoded.CanonicalDigest.ToArray());
    }

    public static bool TryVerify(
        IReadOnlyList<SnapshotSectionFragmentMaterialV1> fragments,
        ulong expectedBasisStep,
        out SnapshotSectionSemanticVerificationV1 verification)
    {
        ArgumentNullException.ThrowIfNull(fragments);
        if (fragments.Count == 0 || !IsCompactPayload(fragments[0].FragmentPayload))
        {
            verification = null!;
            return false;
        }

        var decoded = DecodeAuthority(fragments, expectedBasisStep);
        verification = new SnapshotSectionSemanticVerificationV1(
            decoded.LogicalItemCount,
            decoded.CanonicalDigest.ToArray());
        return true;
    }

    private static DecodedCompactAuthorityV1 DecodeAuthority(
        IReadOnlyList<SnapshotSectionFragmentMaterialV1> fragments,
        ulong expectedBasisStep)
    {
        if (fragments.Count == 0)
            throw new InvalidDataException("snapshot-core.operation-v2.qa04-fragment-missing");

        var operations = new List<DurableOperationStateV1>();
        var transactions = new List<CrossDomainTransactionStateV1>();
        Qa04OperationClosedPrefixV1? prefix = null;
        OpaqueId128? previousOperationId = null;
        OpaqueId128? previousTransactionId = null;
        var transactionArmSeen = false;
        ulong logicalItemCount = 0;

        for (var i = 0; i < fragments.Count; i++)
        {
            var fragment = fragments[i];
            if (!string.Equals(fragment.SectionId, SectionIdValue, StringComparison.Ordinal) ||
                fragment.FragmentIndex != (uint)i || fragment.FragmentCount != (uint)fragments.Count ||
                fragment.FirstRecordId is not null || fragment.LastRecordId is not null || fragment.FragmentPayload is null ||
                !IsCompactPayload(fragment.FragmentPayload))
                throw new InvalidDataException("snapshot-core.operation-v2.qa04-fragment-shape");
            logicalItemCount = checked(logicalItemCount + fragment.ItemCount);

            var decoded = DecodeCompactFragment(fragment.FragmentPayload);
            if (decoded.BasisStep != expectedBasisStep || decoded.Nested.BasisStep != expectedBasisStep)
                throw new InvalidDataException("snapshot-core.operation-v2.qa04-fragment-basis-step-mismatch");

            if (i == 0)
            {
                prefix = decoded.Prefix
                    ?? throw new InvalidDataException("snapshot-core.operation-v2.qa04-prefix-missing");
                prefix.Validate(expectedBasisStep);
            }
            else if (decoded.Prefix is not null)
            {
                throw new InvalidDataException("snapshot-core.operation-v2.qa04-prefix-duplicate");
            }

            if (transactionArmSeen && decoded.Nested.Operations.Count != 0)
                throw new InvalidDataException("snapshot-core.operation-v2.qa04-fragment-kind-order");
            foreach (var operation in decoded.Nested.Operations)
            {
                if (previousOperationId is { } prior && prior.CompareTo(operation.OperationId) >= 0)
                    throw new InvalidDataException("snapshot-core.operation-v2.qa04-operation-order");
                operations.Add(operation);
                previousOperationId = operation.OperationId;
            }
            foreach (var transaction in decoded.Nested.Transactions)
            {
                transactionArmSeen = true;
                if (previousTransactionId is { } prior && prior.CompareTo(transaction.TransactionId) >= 0)
                    throw new InvalidDataException("snapshot-core.operation-v2.qa04-transaction-order");
                transactions.Add(transaction);
                previousTransactionId = transaction.TransactionId;
            }
        }

        var closedPrefix = prefix
            ?? throw new InvalidDataException("snapshot-core.operation-v2.qa04-prefix-missing");
        var canonical = Qa04OperationAuthorityV1.Canonicalize(
            operations,
            closedPrefix,
            transactions,
            expectedBasisStep);
        var expectedItems = checked((ulong)operations.Count + (ulong)transactions.Count + 1UL);
        if (logicalItemCount != expectedItems)
            throw new InvalidDataException("snapshot-core.operation-v2.qa04-fragment-item-count-mismatch");
        return new DecodedCompactAuthorityV1(
            Array.AsReadOnly(operations.ToArray()),
            closedPrefix,
            Array.AsReadOnly(transactions.ToArray()),
            expectedItems,
            canonical.CanonicalDigest.ToArray());
    }

    private static byte[] EncodeCompactFragment(
        ulong basisStep,
        Qa04OperationClosedPrefixV1? prefix,
        byte[] nestedV2Payload)
    {
        ArgumentNullException.ThrowIfNull(nestedV2Payload);
        using var stream = new MemoryStream();
        CoreSnapshotProtoV1.WriteUInt64(stream, 1, basisStep);
        if (prefix is not null)
            CoreSnapshotProtoV1.WriteMessage(stream, PrefixField, EncodePrefix(prefix));
        CoreSnapshotProtoV1.WriteMessage(stream, NestedV2PayloadField, nestedV2Payload);
        return stream.ToArray();
    }

    private static CompactFragmentV1 DecodeCompactFragment(ReadOnlySpan<byte> encoded)
    {
        var reader = new CoreSnapshotProtoV1.Reader(encoded);
        ulong basisStep = 0;
        var seenBasis = false;
        Qa04OperationClosedPrefixV1? prefix = null;
        byte[]? nested = null;
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag("snapshot-core.operation-v2.qa04-field");
            switch (field)
            {
                case 1:
                    if (seenBasis) throw new InvalidDataException("snapshot-core.operation-v2.qa04-duplicate-basis-step");
                    seenBasis = true;
                    basisStep = reader.ReadUInt64(wire, "snapshot-core.operation-v2.qa04-basis-step");
                    break;
                case PrefixField:
                    if (prefix is not null) throw new InvalidDataException("snapshot-core.operation-v2.qa04-prefix-duplicate");
                    prefix = DecodePrefix(reader.ReadBytes(wire, "snapshot-core.operation-v2.qa04-prefix"));
                    break;
                case NestedV2PayloadField:
                    if (nested is not null) throw new InvalidDataException("snapshot-core.operation-v2.qa04-nested-duplicate");
                    nested = reader.ReadBytes(wire, "snapshot-core.operation-v2.qa04-nested-v2");
                    break;
                default:
                    throw new InvalidDataException("snapshot-core.operation-v2.qa04-unknown-field");
            }
        }
        if (!seenBasis || nested is null)
            throw new InvalidDataException("snapshot-core.operation-v2.qa04-required-field-missing");
        var decoded = CoreOperationStateSnapshotWireCodecV2.Decode(nested);
        return new CompactFragmentV1(basisStep, prefix, decoded);
    }

    private static byte[] EncodePrefix(Qa04OperationClosedPrefixV1 prefix)
    {
        using var stream = new MemoryStream();
        CoreSnapshotProtoV1.WriteString(stream, 1, prefix.ProfileId);
        CoreSnapshotProtoV1.WriteUInt64(stream, 2, prefix.FirstInjectionStep);
        if (prefix.LastClosedInjectionStep is { } last)
            CoreSnapshotProtoV1.WriteUInt64(stream, 3, last);
        CoreSnapshotProtoV1.WriteUInt64(stream, 4, prefix.TerminalOperationCount);
        CoreSnapshotProtoV1.WriteBytes(stream, 5, prefix.TerminalSemanticDigest);
        return stream.ToArray();
    }

    private static Qa04OperationClosedPrefixV1 DecodePrefix(ReadOnlySpan<byte> encoded)
    {
        var reader = new CoreSnapshotProtoV1.Reader(encoded);
        string? profileId = null;
        ulong firstInjectionStep = 0;
        ulong? lastClosedInjectionStep = null;
        ulong terminalOperationCount = 0;
        byte[]? terminalSemanticDigest = null;
        uint seen = 0;
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag("snapshot-core.operation-v2.qa04-prefix-field");
            if (field is < 1 or > 5)
                throw new InvalidDataException("snapshot-core.operation-v2.qa04-prefix-unknown-field");
            var bit = 1u << (field - 1);
            if ((seen & bit) != 0)
                throw new InvalidDataException("snapshot-core.operation-v2.qa04-prefix-duplicate-field");
            seen |= bit;
            switch (field)
            {
                case 1: profileId = reader.ReadString(wire, "snapshot-core.operation-v2.qa04-profile-id"); break;
                case 2: firstInjectionStep = reader.ReadUInt64(wire, "snapshot-core.operation-v2.qa04-first-injection-step"); break;
                case 3: lastClosedInjectionStep = reader.ReadUInt64(wire, "snapshot-core.operation-v2.qa04-last-closed-injection-step"); break;
                case 4: terminalOperationCount = reader.ReadUInt64(wire, "snapshot-core.operation-v2.qa04-terminal-operation-count"); break;
                case 5: terminalSemanticDigest = reader.ReadBytes(wire, "snapshot-core.operation-v2.qa04-terminal-semantic-digest"); break;
            }
        }
        const uint required = (1u << 0) | (1u << 1) | (1u << 3) | (1u << 4);
        if ((seen & required) != required || profileId is null || terminalSemanticDigest is null || terminalSemanticDigest.Length != 32)
            throw new InvalidDataException("snapshot-core.operation-v2.qa04-prefix-shape");
        return new Qa04OperationClosedPrefixV1(
            profileId,
            firstInjectionStep,
            lastClosedInjectionStep,
            terminalOperationCount,
            terminalSemanticDigest.ToArray());
    }

    private static bool IsCompactPayload(ReadOnlySpan<byte> encoded)
    {
        try
        {
            var reader = new CoreSnapshotProtoV1.Reader(encoded);
            if (reader.End) return false;
            var (field, wire) = reader.ReadTag("snapshot-core.operation-v2.qa04-detect-field");
            if (field != 1) return false;
            _ = reader.ReadUInt64(wire, "snapshot-core.operation-v2.qa04-detect-basis-step");
            if (reader.End) return false;
            var (nextField, _) = reader.ReadTag("snapshot-core.operation-v2.qa04-detect-next-field");
            return nextField is PrefixField or NestedV2PayloadField;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }
}
