using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

/// <summary>
/// Production composition boundary for one complete standard Snapshot: six Core owner sections plus
/// the exact 97 Domain owner sections. This class only combines already-frozen owner material; it
/// never creates Domain material from WorldState headers or substitutes digest-only Core fixtures.
/// </summary>
public static class StandardSnapshotOwnerCompositionV1
{
    public static IReadOnlyList<CanonicalSnapshotSectionMaterialV1> CreateAll103(
        CoreSnapshotOwnerMaterialCutV1 coreCut,
        DomainPartitionSnapshotAuthoritySetV1 domainAuthorities,
        IEnumerable<IDomainPartitionSnapshotSectionProviderV1> domainProviders)
    {
        ArgumentNullException.ThrowIfNull(coreCut);
        ArgumentNullException.ThrowIfNull(domainAuthorities);
        ArgumentNullException.ThrowIfNull(domainProviders);
        RequireSameFrozenHeader(coreCut.Header, domainAuthorities.FrozenState.Header);

        var coreSections = CoreSnapshotProductionSectionProviderV1.CreateAllSix(coreCut);
        CoreSnapshotProductionSectionProviderV1.VerifyAllSix(
            coreSections,
            coreCut.BasisStep,
            coreCut.Header.ConfigGeneration);
        return CombineAll103(coreSections, domainAuthorities, domainProviders);
    }

    public static IReadOnlyList<CanonicalSnapshotSectionMaterialV1> CreateAll103V2(
        CoreSnapshotOwnerMaterialCutV1 coreCut,
        IReadOnlyList<CrossDomainTransactionStateV1> transactions,
        DomainPartitionSnapshotAuthoritySetV1 domainAuthorities,
        IEnumerable<IDomainPartitionSnapshotSectionProviderV1> domainProviders)
    {
        ArgumentNullException.ThrowIfNull(coreCut);
        ArgumentNullException.ThrowIfNull(transactions);
        ArgumentNullException.ThrowIfNull(domainAuthorities);
        ArgumentNullException.ThrowIfNull(domainProviders);
        RequireSameFrozenHeader(coreCut.Header, domainAuthorities.FrozenState.Header);

        var coreSections = CoreSnapshotProductionSectionProviderV1.CreateAllSixV2(coreCut, transactions);
        CoreSnapshotProductionSectionProviderV1.VerifyAllSixV2(
            coreSections,
            coreCut.BasisStep,
            coreCut.Header.ConfigGeneration);
        return CombineAll103(coreSections, domainAuthorities, domainProviders);
    }

    public static CanonicalSnapshotSemanticVerifierRegistryV1 CreateSemanticVerifierRegistry(
        CoreSnapshotOwnerMaterialCutV1 coreCut,
        DomainPartitionSnapshotAuthoritySetV1 domainAuthorities,
        IEnumerable<IDomainPartitionSnapshotSectionProviderV1> domainProviders)
        => CreateSemanticVerifierRegistryInternal(coreCut, domainAuthorities, domainProviders, operationV2: false);

    public static CanonicalSnapshotSemanticVerifierRegistryV1 CreateSemanticVerifierRegistryV2(
        CoreSnapshotOwnerMaterialCutV1 coreCut,
        DomainPartitionSnapshotAuthoritySetV1 domainAuthorities,
        IEnumerable<IDomainPartitionSnapshotSectionProviderV1> domainProviders)
        => CreateSemanticVerifierRegistryInternal(coreCut, domainAuthorities, domainProviders, operationV2: true);

    private static IReadOnlyList<CanonicalSnapshotSectionMaterialV1> CombineAll103(
        IReadOnlyList<CanonicalSnapshotSectionMaterialV1> coreSections,
        DomainPartitionSnapshotAuthoritySetV1 domainAuthorities,
        IEnumerable<IDomainPartitionSnapshotSectionProviderV1> domainProviders)
    {
        var domainSections = DomainPartitionSnapshotProductionProviderV1.CreateAll97(
            domainAuthorities,
            domainProviders);

        var sections = CanonicalSnapshotSectionValidationV1.ValidateStandard(
            coreSections.Concat(domainSections),
            domainAuthorities.FrozenState);
        if (sections.Count != SnapshotManifestValidation.StandardRequiredSectionCount || sections.Count != 103)
            throw new InvalidDataException("persistence.snapshot.standard-section-count-not-103");
        if (sections.Count(static section => StandardSnapshotSectionSetV1.IsCoreSection(section.SectionId)) != 6)
            throw new InvalidDataException("persistence.snapshot.standard-core-section-count-not-6");
        if (sections.Count(static section => StandardDomainPartitionRegistry.TryGet(section.SectionId, out _)) !=
            StandardDomainPartitionRegistry.StandardPartitionCount)
            throw new InvalidDataException("persistence.snapshot.standard-domain-section-count-not-97");
        return sections;
    }

    private static CanonicalSnapshotSemanticVerifierRegistryV1 CreateSemanticVerifierRegistryInternal(
        CoreSnapshotOwnerMaterialCutV1 coreCut,
        DomainPartitionSnapshotAuthoritySetV1 domainAuthorities,
        IEnumerable<IDomainPartitionSnapshotSectionProviderV1> domainProviders,
        bool operationV2)
    {
        ArgumentNullException.ThrowIfNull(coreCut);
        ArgumentNullException.ThrowIfNull(domainAuthorities);
        ArgumentNullException.ThrowIfNull(domainProviders);
        RequireSameFrozenHeader(coreCut.Header, domainAuthorities.FrozenState.Header);

        var coreVerifiers = new SnapshotSectionSemanticVerifierV1[]
        {
            CoreSnapshotSecondarySemanticVerifierV1.Config(coreCut.BasisStep, coreCut.Header.ConfigGeneration),
            CoreSnapshotSecondarySemanticVerifierV1.Detail(coreCut.BasisStep),
            CoreSnapshotDomainRegistrySemanticVerifierV1.Create(coreCut.BasisStep),
            operationV2
                ? CoreOperationStateSnapshotSectionProviderV2.SemanticVerifier(coreCut.BasisStep)
                : CoreSnapshotPrimarySemanticVerifierV1.Operation(coreCut.BasisStep),
            CoreSnapshotPrimarySemanticVerifierV1.Scheduler(coreCut.BasisStep),
            CoreSnapshotPrimarySemanticVerifierV1.WorldStateHeader(coreCut.BasisStep),
        };
        return DomainPartitionSnapshotProductionProviderV1.CreateSemanticVerifierRegistry(
            coreVerifiers,
            domainAuthorities,
            domainProviders);
    }

    private static void RequireSameFrozenHeader(WorldStateHeaderV1 core, WorldStateHeaderV1 domain)
    {
        if (core.WorldId != domain.WorldId ||
            core.Step != domain.Step ||
            core.ConfigGeneration != domain.ConfigGeneration ||
            core.MasterGeneration != domain.MasterGeneration ||
            core.RateGeneration != domain.RateGeneration ||
            !CryptographicOperations.FixedTimeEquals(core.WorldSeedDigest, domain.WorldSeedDigest) ||
            !SameOptionalDigest(core.PreviousStateDigest, domain.PreviousStateDigest))
            throw new InvalidDataException("persistence.snapshot.standard-owner-cut-mismatch");
    }

    private static bool SameOptionalDigest(byte[]? left, byte[]? right)
    {
        if (left is null || right is null) return left is null && right is null;
        return left.Length == 32 && right.Length == 32 &&
               CryptographicOperations.FixedTimeEquals(left, right);
    }
}
