using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Runtime;

namespace MachiVerse.Simulation.Core.Persistence;

public static class CoreSnapshotProductionSectionProviderV1
{
    private static readonly string[] RequiredCoreSections =
    [
        CoreSnapshotOwnerSectionRegistryV1.ConfigState,
        CoreSnapshotOwnerSectionRegistryV1.DetailDirectory,
        CoreSnapshotOwnerSectionRegistryV1.DomainRegistry,
        CoreSnapshotOwnerSectionRegistryV1.OperationState,
        CoreSnapshotOwnerSectionRegistryV1.SchedulerState,
        CoreSnapshotOwnerSectionRegistryV1.WorldStateHeader,
    ];

    public static IReadOnlyList<CanonicalSnapshotSectionMaterialV1> CreateAllSix(CoreSnapshotOwnerMaterialCutV1 cut)
    {
        ArgumentNullException.ThrowIfNull(cut);
        var sections = CoreSnapshotPrimarySectionProviderV1.Create(cut)
            .Concat(new[]
            {
                CoreSnapshotSecondarySectionProviderV1.CreateDetail(cut),
                CoreSnapshotDomainRegistrySectionProviderV1.Create(cut),
                CoreSnapshotSecondarySectionProviderV1.CreateConfig(cut),
            })
            .OrderBy(static section => section.SectionId, StringComparer.Ordinal)
            .ToArray();
        RequireExactCoreSet(sections.Select(static section => section.SectionId));
        return Array.AsReadOnly(sections);
    }

    public static IReadOnlyList<CanonicalSnapshotSectionMaterialV1> CreateAllSixV2(
        CoreSnapshotOwnerMaterialCutV1 cut,
        IReadOnlyList<CrossDomainTransactionStateV1> transactions)
    {
        ArgumentNullException.ThrowIfNull(cut);
        ArgumentNullException.ThrowIfNull(transactions);
        var primary = CoreSnapshotPrimarySectionProviderV1.Create(cut)
            .Where(static section => !string.Equals(
                section.SectionId,
                CoreSnapshotOwnerSectionRegistryV1.OperationState,
                StringComparison.Ordinal));
        var operation = CoreOperationStateSnapshotSectionProviderV2.Create(
            cut.BasisStep,
            cut.DurableOperations,
            transactions);
        var sections = primary
            .Append(operation)
            .Concat(new[]
            {
                CoreSnapshotSecondarySectionProviderV1.CreateDetail(cut),
                CoreSnapshotDomainRegistrySectionProviderV1.Create(cut),
                CoreSnapshotSecondarySectionProviderV1.CreateConfig(cut),
            })
            .OrderBy(static section => section.SectionId, StringComparer.Ordinal)
            .ToArray();
        RequireExactCoreSet(sections.Select(static section => section.SectionId));
        if (sections.Single(static section => section.SectionId == CoreSnapshotOwnerSectionRegistryV1.OperationState).SectionSchema !=
            CoreOperationStateSnapshotAuthorityV2.Schema)
            throw new InvalidDataException("snapshot-core.production-operation-v2-schema-mismatch");
        return Array.AsReadOnly(sections);
    }

    public static void VerifyAllSix(
        IReadOnlyList<CanonicalSnapshotSectionMaterialV1> sections,
        ulong snapshotStep,
        ulong expectedConfigGeneration)
        => VerifyAllSixInternal(sections, snapshotStep, expectedConfigGeneration, operationV2: false);

    public static void VerifyAllSixV2(
        IReadOnlyList<CanonicalSnapshotSectionMaterialV1> sections,
        ulong snapshotStep,
        ulong expectedConfigGeneration)
        => VerifyAllSixInternal(sections, snapshotStep, expectedConfigGeneration, operationV2: true);

    private static void VerifyAllSixInternal(
        IReadOnlyList<CanonicalSnapshotSectionMaterialV1> sections,
        ulong snapshotStep,
        ulong expectedConfigGeneration,
        bool operationV2)
    {
        ArgumentNullException.ThrowIfNull(sections);
        RequireExactCoreSet(sections.Select(static section => section.SectionId));
        var verifiers = new[]
        {
            CoreSnapshotSecondarySemanticVerifierV1.Config(snapshotStep, expectedConfigGeneration),
            CoreSnapshotSecondarySemanticVerifierV1.Detail(snapshotStep),
            CoreSnapshotDomainRegistrySemanticVerifierV1.Create(snapshotStep),
            operationV2
                ? CoreOperationStateSnapshotSectionProviderV2.SemanticVerifier(snapshotStep)
                : CoreSnapshotPrimarySemanticVerifierV1.Operation(snapshotStep),
            CoreSnapshotPrimarySemanticVerifierV1.Scheduler(snapshotStep),
            CoreSnapshotPrimarySemanticVerifierV1.WorldStateHeader(snapshotStep),
        }.ToDictionary(static verifier => verifier.SectionId, StringComparer.Ordinal);

        foreach (var section in sections)
        {
            if (!verifiers.TryGetValue(section.SectionId, out var verifier))
                throw new InvalidDataException($"snapshot-core.production-verifier-missing:{section.SectionId}");
            if (verifier.SectionSchema != section.SectionSchema)
                throw new InvalidDataException($"snapshot-core.production-schema-mismatch:{section.SectionId}");
            var verified = verifier.Verify(section.Fragments)
                ?? throw new InvalidDataException($"snapshot-core.production-verifier-null:{section.SectionId}");
            if (verified.LogicalItemCount != section.LogicalItemCount)
                throw new InvalidDataException($"snapshot-core.production-semantic-item-count-mismatch:{section.SectionId}");
            if (verified.LogicalContentDigest is null || verified.LogicalContentDigest.Length != 32 ||
                !CryptographicOperations.FixedTimeEquals(verified.LogicalContentDigest, section.LogicalContentDigest))
                throw new InvalidDataException($"snapshot-core.production-semantic-digest-mismatch:{section.SectionId}");
        }
    }

    private static void RequireExactCoreSet(IEnumerable<string> sectionIds)
    {
        ArgumentNullException.ThrowIfNull(sectionIds);
        var ordered = sectionIds.OrderBy(static value => value, StringComparer.Ordinal).ToArray();
        if (ordered.Length != RequiredCoreSections.Length ||
            !ordered.SequenceEqual(RequiredCoreSections, StringComparer.Ordinal))
            throw new InvalidDataException("snapshot-core.production-section-set-mismatch");
    }
}
