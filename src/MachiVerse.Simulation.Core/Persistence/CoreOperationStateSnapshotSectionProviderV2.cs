using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Runtime;

namespace MachiVerse.Simulation.Core.Persistence;

public sealed record RecoveredCoreOperationStateV2(
    ulong BasisStep,
    IReadOnlyList<DurableOperationStateV1> Operations,
    IReadOnlyList<CrossDomainTransactionStateV1> Transactions,
    byte[] LogicalContentDigest);

/// <summary>
/// Production materializer/recovery verifier for the existing core.operation-state logical section
/// under schema /2.0. It does not add a seventh Core Snapshot section.
/// </summary>
public static class CoreOperationStateSnapshotSectionProviderV2
{
    private const string SectionIdValue = "core.operation-state";

    public static CanonicalSnapshotSectionMaterialV1 Create(
        ulong basisStep,
        IReadOnlyList<DurableOperationStateV1> operations,
        IReadOnlyList<CrossDomainTransactionStateV1> transactions)
    {
        var authority = CoreOperationStateSnapshotAuthorityV2.Create(operations, transactions, basisStep);
        var items = authority.Operations.Cast<object>().Concat(authority.Transactions).ToArray();
        var groups = Fragment(items, basisStep);
        var fragments = new SnapshotSectionFragmentMaterialV1[groups.Count];
        for (var i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            var groupOperations = group.TakeWhile(static item => item is DurableOperationStateV1)
                .Cast<DurableOperationStateV1>().ToArray();
            if (group.Skip(groupOperations.Length).Any(static item => item is not CrossDomainTransactionStateV1))
                throw new InvalidDataException("snapshot-core.operation-v2.provider-item-order");
            var groupTransactions = group.Skip(groupOperations.Length).Cast<CrossDomainTransactionStateV1>().ToArray();
            var payload = CoreOperationStateSnapshotWireCodecV2.Encode(basisStep, groupOperations, groupTransactions);
            if (payload.Length > CanonicalSnapshotSectionValidationV1.HardMaxUncompressedBytes)
                throw new InvalidDataException("persistence.snapshot-item-too-large");
            fragments[i] = new SnapshotSectionFragmentMaterialV1(
                SectionIdValue,
                checked((uint)i),
                checked((uint)groups.Count),
                null,
                null,
                checked((ulong)group.Count),
                payload);
        }

        var material = new CanonicalSnapshotSectionMaterialV1(
            SectionIdValue,
            CoreOperationStateSnapshotAuthorityV2.Schema,
            authority.LogicalItemCount,
            authority.CanonicalDigest.ToArray(),
            Array.AsReadOnly(fragments));
        ValidateMaterialShape(material);
        return material;
    }

    public static RecoveredCoreOperationStateV2 Recover(
        CanonicalSnapshotSectionMaterialV1 section,
        ulong expectedBasisStep)
    {
        ArgumentNullException.ThrowIfNull(section);
        ValidateMaterialShape(section);
        if (section.SectionSchema != CoreOperationStateSnapshotAuthorityV2.Schema)
            throw new InvalidDataException("snapshot-core.operation-v2.section-schema-mismatch");

        var authority = DecodeAuthority(section.Fragments, expectedBasisStep);
        if (authority.LogicalItemCount != section.LogicalItemCount)
            throw new InvalidDataException("snapshot-core.operation-v2.semantic-item-count-mismatch");
        if (!CryptographicOperations.FixedTimeEquals(authority.CanonicalDigest, section.LogicalContentDigest))
            throw new InvalidDataException("snapshot-core.operation-v2.semantic-digest-mismatch");
        return new RecoveredCoreOperationStateV2(
            expectedBasisStep,
            authority.Operations,
            authority.Transactions,
            authority.CanonicalDigest.ToArray());
    }

    public static SnapshotSectionSemanticVerifierV1 SemanticVerifier(ulong expectedBasisStep)
        => new(
            SectionIdValue,
            CoreOperationStateSnapshotAuthorityV2.Schema,
            fragments =>
            {
                var authority = DecodeAuthority(fragments, expectedBasisStep);
                return new SnapshotSectionSemanticVerificationV1(
                    authority.LogicalItemCount,
                    authority.CanonicalDigest.ToArray());
            });

    private static CoreOperationStateSnapshotAuthorityV2 DecodeAuthority(
        IReadOnlyList<SnapshotSectionFragmentMaterialV1> fragments,
        ulong expectedBasisStep)
    {
        ArgumentNullException.ThrowIfNull(fragments);
        if (fragments.Count == 0)
            throw new InvalidDataException("snapshot-core.operation-v2.fragment-missing");

        var operations = new List<DurableOperationStateV1>();
        var transactions = new List<CrossDomainTransactionStateV1>();
        var transactionArmSeen = false;
        ulong logicalItemCount = 0;
        for (var i = 0; i < fragments.Count; i++)
        {
            var fragment = fragments[i];
            if (!string.Equals(fragment.SectionId, SectionIdValue, StringComparison.Ordinal) ||
                fragment.FragmentIndex != (uint)i || fragment.FragmentCount != (uint)fragments.Count ||
                fragment.FirstRecordId is not null || fragment.LastRecordId is not null || fragment.FragmentPayload is null)
                throw new InvalidDataException("snapshot-core.operation-v2.fragment-shape");
            logicalItemCount = checked(logicalItemCount + fragment.ItemCount);

            var decoded = CoreOperationStateSnapshotWireCodecV2.Decode(fragment.FragmentPayload);
            if (decoded.BasisStep != expectedBasisStep)
                throw new InvalidDataException("snapshot-core.operation-v2.fragment-basis-step-mismatch");
            if (transactionArmSeen && decoded.Operations.Count != 0)
                throw new InvalidDataException("snapshot-core.operation-v2.fragment-kind-order");
            operations.AddRange(decoded.Operations);
            transactions.AddRange(decoded.Transactions);
            if (decoded.Transactions.Count != 0) transactionArmSeen = true;
        }

        var authority = CoreOperationStateSnapshotAuthorityV2.Create(operations, transactions, expectedBasisStep);
        if (authority.LogicalItemCount != logicalItemCount)
            throw new InvalidDataException("snapshot-core.operation-v2.fragment-item-count-mismatch");
        return authority;
    }

    private static IReadOnlyList<IReadOnlyList<object>> Fragment(IReadOnlyList<object> items, ulong basisStep)
    {
        if (items.Count == 0)
            return Array.AsReadOnly<IReadOnlyList<object>>([Array.Empty<object>()]);

        var emptyOverhead = CoreOperationStateSnapshotWireCodecV2.Encode(basisStep, [], []).Length;
        var result = new List<IReadOnlyList<object>>();
        var current = new List<object>();
        var currentLength = emptyOverhead;
        foreach (var item in items)
        {
            var fieldLength = item switch
            {
                DurableOperationStateV1 operation => CoreOperationStateSnapshotWireCodecV2.EncodedOperationItemFieldLength(operation),
                CrossDomainTransactionStateV1 transaction => CoreOperationStateSnapshotWireCodecV2.EncodedTransactionItemFieldLength(transaction),
                _ => throw new InvalidDataException("snapshot-core.operation-v2.provider-item-kind"),
            };
            if (checked(emptyOverhead + fieldLength) > CanonicalSnapshotSectionValidationV1.HardMaxUncompressedBytes)
                throw new InvalidDataException("persistence.snapshot-item-too-large");
            if (current.Count > 0 && checked(currentLength + fieldLength) > CanonicalSnapshotSectionValidationV1.TargetUncompressedBytes)
            {
                result.Add(Array.AsReadOnly(current.ToArray()));
                current = [];
                currentLength = emptyOverhead;
            }
            current.Add(item);
            currentLength = checked(currentLength + fieldLength);
        }
        if (current.Count > 0) result.Add(Array.AsReadOnly(current.ToArray()));
        return Array.AsReadOnly(result.ToArray());
    }

    private static void ValidateMaterialShape(CanonicalSnapshotSectionMaterialV1 section)
    {
        if (!string.Equals(section.SectionId, SectionIdValue, StringComparison.Ordinal))
            throw new InvalidDataException("snapshot-core.operation-v2.section-id-mismatch");
        if (section.LogicalContentDigest is null || section.LogicalContentDigest.Length != 32)
            throw new InvalidDataException("snapshot-core.operation-v2.section-digest-invalid");
        if (section.Fragments is null || section.Fragments.Count == 0)
            throw new InvalidDataException("snapshot-core.operation-v2.fragment-missing");
        ulong count = 0;
        for (var i = 0; i < section.Fragments.Count; i++)
        {
            var fragment = section.Fragments[i];
            if (!string.Equals(fragment.SectionId, SectionIdValue, StringComparison.Ordinal) ||
                fragment.FragmentIndex != (uint)i || fragment.FragmentCount != (uint)section.Fragments.Count ||
                fragment.FirstRecordId is not null || fragment.LastRecordId is not null || fragment.FragmentPayload is null)
                throw new InvalidDataException("snapshot-core.operation-v2.fragment-shape");
            count = checked(count + fragment.ItemCount);
        }
        if (count != section.LogicalItemCount)
            throw new InvalidDataException("snapshot-core.operation-v2.fragment-item-count-mismatch");
    }
}
