using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.GovernanceSecurity;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed class Qa04GovernanceTerritorialFoundationCanonicalMaterializationV1
{
    internal Qa04GovernanceTerritorialFoundationCanonicalMaterializationV1(
        DomainPartitionStateV1<GovernancePolityPayloadV1> polities,
        DomainPartitionStateV1<SpatialScopeRegistryPayloadV1> tileScopes,
        DomainPartitionStateV1<GovernancePublicAuthorityPayloadV1> publicAuthorities,
        DomainPartitionStateV1<GovernanceJurisdictionPayloadV1> jurisdictions,
        DomainPartitionStateV1<GovernanceTerritorialClaimPayloadV1> territorialClaims,
        DomainPartitionStateV1<GovernanceEffectiveControlPayloadV1> effectiveControls,
        IReadOnlyList<DomainRecordEnvelopeV1<GovernanceJurisdictionPayloadV1>> jurisdictionRecordsByOrdinal,
        IReadOnlyList<DomainRecordEnvelopeV1<GovernanceTerritorialClaimPayloadV1>> territorialClaimRecordsByOrdinal,
        IReadOnlyList<DomainRecordEnvelopeV1<GovernanceEffectiveControlPayloadV1>> effectiveControlRecordsByOrdinal,
        IDomainRecordSchemaResolverV1 references)
    {
        Polities = polities;
        TileScopes = tileScopes;
        PublicAuthorities = publicAuthorities;
        Jurisdictions = jurisdictions;
        TerritorialClaims = territorialClaims;
        EffectiveControls = effectiveControls;
        JurisdictionRecordsByOrdinal = jurisdictionRecordsByOrdinal;
        TerritorialClaimRecordsByOrdinal = territorialClaimRecordsByOrdinal;
        EffectiveControlRecordsByOrdinal = effectiveControlRecordsByOrdinal;
        References = references;
    }

    public DomainPartitionStateV1<GovernancePolityPayloadV1> Polities { get; }
    public DomainPartitionStateV1<SpatialScopeRegistryPayloadV1> TileScopes { get; }
    public DomainPartitionStateV1<GovernancePublicAuthorityPayloadV1> PublicAuthorities { get; }
    public DomainPartitionStateV1<GovernanceJurisdictionPayloadV1> Jurisdictions { get; }
    public DomainPartitionStateV1<GovernanceTerritorialClaimPayloadV1> TerritorialClaims { get; }
    public DomainPartitionStateV1<GovernanceEffectiveControlPayloadV1> EffectiveControls { get; }
    public IReadOnlyList<DomainRecordEnvelopeV1<GovernanceJurisdictionPayloadV1>> JurisdictionRecordsByOrdinal { get; }
    public IReadOnlyList<DomainRecordEnvelopeV1<GovernanceTerritorialClaimPayloadV1>> TerritorialClaimRecordsByOrdinal { get; }
    public IReadOnlyList<DomainRecordEnvelopeV1<GovernanceEffectiveControlPayloadV1>> EffectiveControlRecordsByOrdinal { get; }
    public IDomainRecordSchemaResolverV1 References { get; }

    public ulong MaterializedRecordCount
        => checked(Jurisdictions.ItemCount + TerritorialClaims.ItemCount + EffectiveControls.ItemCount);
}

/// <summary>
/// Production-path materialization for the owner-approved perf.reference.v1 Governance territorial
/// foundation. All outgoing references resolve to already accepted production authority: Polity,
/// PublicAuthority, and the canonical Spatial TileScope set. No fabricated reference targets or
/// permissive resolver participate in this materialization.
/// </summary>
public static class Qa04GovernanceTerritorialFoundationCanonicalAuthorityV1
{
    public const ulong JurisdictionCount = 10_000;
    public const ulong TerritorialClaimCount = 10_000;
    public const ulong EffectiveControlCount = 20_000;
    public const ulong CanonicalCount = JurisdictionCount + TerritorialClaimCount + EffectiveControlCount;
    public const ulong PolityCount = 1_000;
    public const ulong PublicAuthorityCountUsed = 20_000;
    public const ulong GenesisStep = 0;
    public const uint FullPpm = 1_000_000;
    public const ushort FiveRecordScopeCount = 3_616;
    public const uint FiveRecordSharePpm = 200_000;
    public const uint FourRecordSharePpm = 250_000;

    public static readonly StableToken JurisdictionKind = new("perf.regional-jurisdiction");
    public static readonly StableToken SubjectClass = new("perf.subject");
    public static readonly StableToken TerritorialClaimKind = new("perf.territorial-claim");

    public static void ValidateCanonicalContract()
    {
        Qa04SocietyGovernanceReferenceDecompositionV1.ValidateCanonicalContract();
        Qa04SocietyGovernanceCanonicalMaterializerV1.ValidateCanonicalContract();
        Qa04GovernancePolityMaterializerV1.ValidateCanonicalContract();
        Qa04SpatialTileScopeAuthorityV1.ValidateCanonicalContract();
        Qa04GovernancePublicAuthorityResolvedMaterializerV1.ValidateCanonicalContract();

        ValidateSlice(GovernanceJurisdictionPayloadV1.PartitionId, 1_636_000, JurisdictionCount);
        ValidateSlice(GovernanceTerritorialClaimPayloadV1.PartitionId, 1_646_000, TerritorialClaimCount);
        ValidateSlice(GovernanceEffectiveControlPayloadV1.PartitionId, 1_656_000, EffectiveControlCount);

        if (CanonicalCount != 40_000 ||
            PolityCount != Qa04GovernancePolityMaterializerV1.CanonicalCount ||
            Qa04SpatialTileScopeAuthorityV1.CanonicalScopeCount != 4_096 ||
            PublicAuthorityCountUsed > Qa04GovernancePublicAuthorityResolvedMaterializerV1.CanonicalCount ||
            FiveRecordScopeCount != 3_616 ||
            FiveRecordScopeCount >= Qa04SpatialTileScopeAuthorityV1.CanonicalScopeCount ||
            FiveRecordSharePpm * 5 != FullPpm ||
            FourRecordSharePpm * 4 != FullPpm ||
            JurisdictionKind.Value != "perf.regional-jurisdiction" ||
            SubjectClass.Value != "perf.subject" ||
            TerritorialClaimKind.Value != "perf.territorial-claim" ||
            GenesisStep != 0)
            throw new InvalidDataException("qa04.governance.territorial-foundation-contract-drift");

        foreach (var partitionId in new[]
                 {
                     GovernanceJurisdictionPayloadV1.PartitionId,
                     GovernanceTerritorialClaimPayloadV1.PartitionId,
                     GovernanceEffectiveControlPayloadV1.PartitionId,
                 })
        {
            var identity = StandardDomainPartitionRegistry.Get(partitionId);
            if (identity.OwnerDomain.Value != "governance_security")
                throw new InvalidDataException($"qa04.governance.territorial-foundation-owner-drift:{partitionId}");
        }

        ValidateRequiredSecondaryIndexRegistrations();
    }

    public static Qa04GovernanceTerritorialFoundationCanonicalMaterializationV1 MaterializeCanonical()
    {
        ValidateCanonicalContract();

        // Reuse the already production-proven Society/Governance materialization because its resolver
        // contains the actual Polity, TileScope and PublicAuthority records required by this package.
        var existing = Qa04SocietyGovernanceCanonicalMaterializerV1.MaterializeCanonical();
        var references = existing.References;
        var polities = Qa04GovernancePolityMaterializerV1.MaterializeCanonicalPartition();
        var tileScopes = Qa04SpatialTileScopeAuthorityV1.MaterializeCanonical();
        var publicAuthorities = existing.PublicAuthorities;

        if (polities.ItemCount != PolityCount ||
            tileScopes.ItemCount != Qa04SpatialTileScopeAuthorityV1.CanonicalScopeCount ||
            publicAuthorities.ItemCount != Qa04GovernancePublicAuthorityResolvedMaterializerV1.CanonicalCount)
            throw new InvalidDataException("qa04.governance.territorial-foundation-upstream-count-drift");

        ValidateUpstreamReferences(polities, tileScopes, publicAuthorities, references);

        var validator = new StandardDomainPayloadCodecValidatorV1();
        var jurisdictionRecords = MaterializeJurisdictions(validator, references);
        var territorialClaimRecords = MaterializeTerritorialClaims(validator, references);
        var effectiveControlRecords = MaterializeEffectiveControls(validator, references);

        var jurisdictions = new DomainPartitionStateV1<GovernanceJurisdictionPayloadV1>(
            StandardDomainPartitionRegistry.Get(GovernanceJurisdictionPayloadV1.PartitionId),
            jurisdictionRecords);
        var territorialClaims = new DomainPartitionStateV1<GovernanceTerritorialClaimPayloadV1>(
            StandardDomainPartitionRegistry.Get(GovernanceTerritorialClaimPayloadV1.PartitionId),
            territorialClaimRecords);
        var effectiveControls = new DomainPartitionStateV1<GovernanceEffectiveControlPayloadV1>(
            StandardDomainPartitionRegistry.Get(GovernanceEffectiveControlPayloadV1.PartitionId),
            effectiveControlRecords);

        ValidateJurisdictionInvariants(jurisdictionRecords);
        ValidateTerritorialClaimInvariants(territorialClaimRecords);
        ValidateEffectiveControlInvariants(effectiveControlRecords);
        ValidateSecondaryIndexes(jurisdictions, territorialClaims, effectiveControls);

        var materialization = new Qa04GovernanceTerritorialFoundationCanonicalMaterializationV1(
            polities,
            tileScopes,
            publicAuthorities,
            jurisdictions,
            territorialClaims,
            effectiveControls,
            Array.AsReadOnly(jurisdictionRecords),
            Array.AsReadOnly(territorialClaimRecords),
            Array.AsReadOnly(effectiveControlRecords),
            references);

        if (materialization.MaterializedRecordCount != CanonicalCount)
            throw new InvalidDataException("qa04.governance.territorial-foundation-total-count-drift");

        return materialization;
    }

    public static void ValidateJurisdictionRecord(
        ulong localOrdinal,
        DomainRecordEnvelopeV1<GovernanceJurisdictionPayloadV1> record,
        IDomainRecordSchemaResolverV1 references)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(references);
        if (localOrdinal >= JurisdictionCount) throw new ArgumentOutOfRangeException(nameof(localOrdinal));

        ValidateEnvelope(localOrdinal, GovernanceJurisdictionPayloadV1.PartitionId, record);
        var payload = record.Payload;
        var expectedPolity = ResolvePolityRef(localOrdinal % PolityCount);
        var expectedScope = Qa04SpatialTileScopeAuthorityV1.ScopeRef(checked((ushort)(localOrdinal % 4_096)));
        if (payload.PolityRef != expectedPolity ||
            payload.ScopeRef != expectedScope ||
            payload.JurisdictionKind != JurisdictionKind ||
            payload.SubjectClasses.Count != 1 || payload.SubjectClasses[0] != SubjectClass ||
            payload.EffectiveFrom != GenesisStep || payload.EffectiveUntil is not null)
            throw new InvalidDataException("qa04.governance.jurisdiction-genesis-drift");

        new StandardDomainPayloadCodecValidatorV1().Validate(
            GovernanceJurisdictionPayloadV1.PartitionId,
            payload.ToStandardPayload(),
            references);
    }

    public static void ValidateTerritorialClaimRecord(
        ulong localOrdinal,
        DomainRecordEnvelopeV1<GovernanceTerritorialClaimPayloadV1> record,
        IDomainRecordSchemaResolverV1 references)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(references);
        if (localOrdinal >= TerritorialClaimCount) throw new ArgumentOutOfRangeException(nameof(localOrdinal));

        ValidateEnvelope(localOrdinal, GovernanceTerritorialClaimPayloadV1.PartitionId, record);
        var payload = record.Payload;
        var expectedPolity = ResolvePolityRef(localOrdinal % PolityCount);
        var expectedScope = Qa04SpatialTileScopeAuthorityV1.ScopeRef(checked((ushort)(localOrdinal % 4_096)));
        if (payload.ClaimantPolityRef != expectedPolity ||
            payload.ScopeRef != expectedScope ||
            payload.ClaimKind != TerritorialClaimKind ||
            payload.StrengthPpm != FullPpm ||
            payload.EffectiveFrom != GenesisStep || payload.EffectiveUntil is not null ||
            payload.BasisRefs.Count != 0)
            throw new InvalidDataException("qa04.governance.territorial-claim-genesis-drift");

        new StandardDomainPayloadCodecValidatorV1().Validate(
            GovernanceTerritorialClaimPayloadV1.PartitionId,
            payload.ToStandardPayload(),
            references);
    }

    public static void ValidateEffectiveControlRecord(
        ulong localOrdinal,
        DomainRecordEnvelopeV1<GovernanceEffectiveControlPayloadV1> record,
        IDomainRecordSchemaResolverV1 references)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(references);
        if (localOrdinal >= EffectiveControlCount) throw new ArgumentOutOfRangeException(nameof(localOrdinal));

        ValidateEnvelope(localOrdinal, GovernanceEffectiveControlPayloadV1.PartitionId, record);
        var scopeOrdinal = checked((ushort)(localOrdinal % 4_096));
        var expectedController = Qa04SocietyGovernanceCanonicalAuthorityV1.ResolvePublicAuthorityRef(localOrdinal);
        var expectedScope = Qa04SpatialTileScopeAuthorityV1.ScopeRef(scopeOrdinal);
        var expectedShare = scopeOrdinal < FiveRecordScopeCount ? FiveRecordSharePpm : FourRecordSharePpm;
        var payload = record.Payload;
        if (payload.ControllerRef != expectedController ||
            payload.ScopeRef != expectedScope ||
            payload.ControlPpm != expectedShare ||
            payload.SecurityCapacityPpm != expectedShare ||
            payload.EffectiveFrom != GenesisStep ||
            payload.BasisRefs.Count != 0)
            throw new InvalidDataException("qa04.governance.effective-control-genesis-drift");

        new StandardDomainPayloadCodecValidatorV1().Validate(
            GovernanceEffectiveControlPayloadV1.PartitionId,
            payload.ToStandardPayload(),
            references);
    }

    public static void ValidateJurisdictionInvariants(
        IEnumerable<DomainRecordEnvelopeV1<GovernanceJurisdictionPayloadV1>> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var material = records.ToArray();
        if ((ulong)material.Length != JurisdictionCount)
            throw new InvalidDataException("qa04.governance.jurisdiction-count-drift");
        EnsureUniqueIds(material.Select(static record => record.RecordId), "qa04.governance.jurisdiction-record-id-duplicate");
        EnsureUniqueRelations(
            material.Select(static record => (record.Payload.PolityRef, record.Payload.ScopeRef)),
            "qa04.governance.jurisdiction-relation-duplicate");

        var polityCounts = material.GroupBy(static record => record.Payload.PolityRef).Select(static group => group.Count()).ToArray();
        if (polityCounts.Length != PolityCount || polityCounts.Any(static count => count != 10))
            throw new InvalidDataException("qa04.governance.jurisdiction-polity-cardinality-drift");

        ValidateThreeTwoScopeDistribution(
            material.GroupBy(static record => record.Payload.ScopeRef).ToDictionary(static group => group.Key, static group => group.Count()),
            "qa04.governance.jurisdiction-scope-cardinality-drift");
    }

    public static void ValidateTerritorialClaimInvariants(
        IEnumerable<DomainRecordEnvelopeV1<GovernanceTerritorialClaimPayloadV1>> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var material = records.ToArray();
        if ((ulong)material.Length != TerritorialClaimCount)
            throw new InvalidDataException("qa04.governance.territorial-claim-count-drift");
        EnsureUniqueIds(material.Select(static record => record.RecordId), "qa04.governance.territorial-claim-record-id-duplicate");
        EnsureUniqueRelations(
            material.Select(static record => (record.Payload.ClaimantPolityRef, record.Payload.ScopeRef)),
            "qa04.governance.territorial-claim-relation-duplicate");

        var polityCounts = material.GroupBy(static record => record.Payload.ClaimantPolityRef).Select(static group => group.Count()).ToArray();
        if (polityCounts.Length != PolityCount || polityCounts.Any(static count => count != 10))
            throw new InvalidDataException("qa04.governance.territorial-claim-polity-cardinality-drift");

        ValidateThreeTwoScopeDistribution(
            material.GroupBy(static record => record.Payload.ScopeRef).ToDictionary(static group => group.Key, static group => group.Count()),
            "qa04.governance.territorial-claim-scope-cardinality-drift");
    }

    public static void ValidateEffectiveControlInvariants(
        IEnumerable<DomainRecordEnvelopeV1<GovernanceEffectiveControlPayloadV1>> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var material = records.ToArray();
        if ((ulong)material.Length != EffectiveControlCount)
            throw new InvalidDataException("qa04.governance.effective-control-count-drift");
        EnsureUniqueIds(material.Select(static record => record.RecordId), "qa04.governance.effective-control-record-id-duplicate");
        EnsureUniqueRelations(
            material.Select(static record => (record.Payload.ControllerRef, record.Payload.ScopeRef)),
            "qa04.governance.effective-control-relation-duplicate");

        var controllerCounts = material.GroupBy(static record => record.Payload.ControllerRef).Select(static group => group.Count()).ToArray();
        if (controllerCounts.Length != PublicAuthorityCountUsed || controllerCounts.Any(static count => count != 1))
            throw new InvalidDataException("qa04.governance.effective-control-controller-cardinality-drift");

        var scopes = material.GroupBy(static record => record.Payload.ScopeRef).ToArray();
        if (scopes.Length != Qa04SpatialTileScopeAuthorityV1.CanonicalScopeCount)
            throw new InvalidDataException("qa04.governance.effective-control-scope-count-drift");

        var fiveCount = 0;
        var fourCount = 0;
        foreach (var scope in scopes)
        {
            if (scope.Count() == 5) fiveCount++;
            else if (scope.Count() == 4) fourCount++;
            else throw new InvalidDataException("qa04.governance.effective-control-scope-cardinality-drift");

            if (scope.Aggregate(0UL, static (sum, record) => checked(sum + record.Payload.ControlPpm)) != FullPpm)
                throw new InvalidDataException("qa04.governance.effective-control-aggregate-control-drift");
            if (scope.Aggregate(0UL, static (sum, record) => checked(sum + record.Payload.SecurityCapacityPpm)) != FullPpm)
                throw new InvalidDataException("qa04.governance.effective-control-aggregate-security-drift");
        }

        if (fiveCount != FiveRecordScopeCount || fourCount != 4_096 - FiveRecordScopeCount)
            throw new InvalidDataException("qa04.governance.effective-control-scope-distribution-drift");
    }

    public static void ValidateSecondaryIndexes(
        DomainPartitionStateV1<GovernanceJurisdictionPayloadV1> jurisdictions,
        DomainPartitionStateV1<GovernanceTerritorialClaimPayloadV1> territorialClaims,
        DomainPartitionStateV1<GovernanceEffectiveControlPayloadV1> effectiveControls)
    {
        ArgumentNullException.ThrowIfNull(jurisdictions);
        ArgumentNullException.ThrowIfNull(territorialClaims);
        ArgumentNullException.ThrowIfNull(effectiveControls);

        var jurisdictionByScope = DerivedRecordIndexV1<OpaqueId128>.Rebuild(
            "governance.jurisdiction-by-scope",
            jurisdictions,
            static record => new[] { record.Payload.ScopeRef.RecordId });
        var jurisdictionByPolity = DerivedRecordIndexV1<OpaqueId128>.Rebuild(
            "governance.jurisdiction-by-polity",
            jurisdictions,
            static record => new[] { record.Payload.PolityRef.RecordId });
        ValidateIndex(jurisdictionByScope, 4_096, JurisdictionCount, "qa04.governance.jurisdiction-by-scope-index-drift");
        ValidateIndex(jurisdictionByPolity, checked((int)PolityCount), JurisdictionCount, "qa04.governance.jurisdiction-by-polity-index-drift");

        var claimByScope = DerivedRecordIndexV1<OpaqueId128>.Rebuild(
            "governance.claim-by-scope",
            territorialClaims,
            static record => new[] { record.Payload.ScopeRef.RecordId });
        var claimByPolity = DerivedRecordIndexV1<OpaqueId128>.Rebuild(
            "governance.claim-by-polity",
            territorialClaims,
            static record => new[] { record.Payload.ClaimantPolityRef.RecordId });
        ValidateIndex(claimByScope, 4_096, TerritorialClaimCount, "qa04.governance.claim-by-scope-index-drift");
        ValidateIndex(claimByPolity, checked((int)PolityCount), TerritorialClaimCount, "qa04.governance.claim-by-polity-index-drift");

        var controlByScope = DerivedRecordIndexV1<OpaqueId128>.Rebuild(
            "governance.control-by-scope",
            effectiveControls,
            static record => new[] { record.Payload.ScopeRef.RecordId });
        var controlByController = DerivedRecordIndexV1<OpaqueId128>.Rebuild(
            "governance.control-by-controller",
            effectiveControls,
            static record => new[] { record.Payload.ControllerRef.RecordId });
        ValidateIndex(controlByScope, 4_096, EffectiveControlCount, "qa04.governance.control-by-scope-index-drift");
        ValidateIndex(controlByController, checked((int)PublicAuthorityCountUsed), EffectiveControlCount, "qa04.governance.control-by-controller-index-drift");
    }

    private static DomainRecordEnvelopeV1<GovernanceJurisdictionPayloadV1>[] MaterializeJurisdictions(
        StandardDomainPayloadCodecValidatorV1 validator,
        IDomainRecordSchemaResolverV1 references)
    {
        var records = new DomainRecordEnvelopeV1<GovernanceJurisdictionPayloadV1>[checked((int)JurisdictionCount)];
        for (ulong i = 0; i < JurisdictionCount; i++)
        {
            var payload = new GovernanceJurisdictionPayloadV1(
                ResolvePolityRef(i % PolityCount),
                Qa04SpatialTileScopeAuthorityV1.ScopeRef(checked((ushort)(i % 4_096))),
                JurisdictionKind,
                new[] { SubjectClass },
                GenesisStep,
                EffectiveUntil: null);
            validator.Validate(GovernanceJurisdictionPayloadV1.PartitionId, payload.ToStandardPayload(), references);
            records[checked((int)i)] = CreateEnvelope(i, GovernanceJurisdictionPayloadV1.PartitionId, payload);
        }
        return records;
    }

    private static DomainRecordEnvelopeV1<GovernanceTerritorialClaimPayloadV1>[] MaterializeTerritorialClaims(
        StandardDomainPayloadCodecValidatorV1 validator,
        IDomainRecordSchemaResolverV1 references)
    {
        var records = new DomainRecordEnvelopeV1<GovernanceTerritorialClaimPayloadV1>[checked((int)TerritorialClaimCount)];
        for (ulong i = 0; i < TerritorialClaimCount; i++)
        {
            var payload = new GovernanceTerritorialClaimPayloadV1(
                ResolvePolityRef(i % PolityCount),
                Qa04SpatialTileScopeAuthorityV1.ScopeRef(checked((ushort)(i % 4_096))),
                TerritorialClaimKind,
                FullPpm,
                GenesisStep,
                EffectiveUntil: null,
                Array.Empty<PartitionRecordRefV1>());
            validator.Validate(GovernanceTerritorialClaimPayloadV1.PartitionId, payload.ToStandardPayload(), references);
            records[checked((int)i)] = CreateEnvelope(i, GovernanceTerritorialClaimPayloadV1.PartitionId, payload);
        }
        return records;
    }

    private static DomainRecordEnvelopeV1<GovernanceEffectiveControlPayloadV1>[] MaterializeEffectiveControls(
        StandardDomainPayloadCodecValidatorV1 validator,
        IDomainRecordSchemaResolverV1 references)
    {
        var records = new DomainRecordEnvelopeV1<GovernanceEffectiveControlPayloadV1>[checked((int)EffectiveControlCount)];
        for (ulong i = 0; i < EffectiveControlCount; i++)
        {
            var scopeOrdinal = checked((ushort)(i % 4_096));
            var share = scopeOrdinal < FiveRecordScopeCount ? FiveRecordSharePpm : FourRecordSharePpm;
            var payload = new GovernanceEffectiveControlPayloadV1(
                Qa04SocietyGovernanceCanonicalAuthorityV1.ResolvePublicAuthorityRef(i),
                Qa04SpatialTileScopeAuthorityV1.ScopeRef(scopeOrdinal),
                share,
                share,
                GenesisStep,
                Array.Empty<PartitionRecordRefV1>());
            validator.Validate(GovernanceEffectiveControlPayloadV1.PartitionId, payload.ToStandardPayload(), references);
            records[checked((int)i)] = CreateEnvelope(i, GovernanceEffectiveControlPayloadV1.PartitionId, payload);
        }
        return records;
    }

    private static DomainRecordEnvelopeV1<TPayload> CreateEnvelope<TPayload>(
        ulong localOrdinal,
        string partitionId,
        TPayload payload)
    {
        var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(partitionId);
        var binding = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(checked(slice.StartOrdinal + localOrdinal));
        if (binding.PartitionId.Value != partitionId ||
            binding.PartitionLocalOrdinal != localOrdinal ||
            binding.UsesSpecializedIdentity ||
            binding.Descriptor.DetailLevel != DetailLevelV1.D2RegionalAggregate)
            throw new InvalidDataException($"qa04.governance.territorial-foundation-descriptor-binding-drift:{partitionId}");

        return new DomainRecordEnvelopeV1<TPayload>(
            binding.Descriptor.RecordId,
            StandardDomainPartitionRegistry.Get(partitionId).RecordSchema,
            revision: 1,
            createdStep: 0,
            retiredStep: null,
            binding.Descriptor.DetailLevel,
            lineageRef: null,
            payload);
    }

    private static void ValidateEnvelope<TPayload>(
        ulong localOrdinal,
        string partitionId,
        DomainRecordEnvelopeV1<TPayload> record)
    {
        var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(partitionId);
        var binding = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(checked(slice.StartOrdinal + localOrdinal));
        var identity = StandardDomainPartitionRegistry.Get(partitionId);
        if (record.RecordId != binding.Descriptor.RecordId ||
            record.RecordSchema != identity.RecordSchema ||
            record.Revision != 1 || record.CreatedStep != 0 || record.RetiredStep is not null ||
            record.DetailLevel != DetailLevelV1.D2RegionalAggregate || record.LineageRef is not null)
            throw new InvalidDataException($"qa04.governance.territorial-foundation-envelope-drift:{partitionId}");
    }

    private static PartitionRecordRefV1 ResolvePolityRef(ulong localOrdinal)
    {
        if (localOrdinal >= PolityCount) throw new ArgumentOutOfRangeException(nameof(localOrdinal));
        var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(GovernancePolityPayloadV1.PartitionId);
        var binding = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(checked(slice.StartOrdinal + localOrdinal));
        if (binding.PartitionId.Value != GovernancePolityPayloadV1.PartitionId ||
            binding.PartitionLocalOrdinal != localOrdinal || binding.UsesSpecializedIdentity)
            throw new InvalidDataException("qa04.governance.territorial-foundation-polity-binding-drift");
        return new PartitionRecordRefV1(GovernancePolityPayloadV1.PartitionId, binding.Descriptor.RecordId);
    }

    private static void ValidateSlice(string partitionId, ulong expectedStart, ulong expectedCount)
    {
        var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(partitionId);
        if (slice.StartOrdinal != expectedStart || slice.Count != expectedCount || slice.UsesSpecializedIdentity)
            throw new InvalidDataException($"qa04.governance.territorial-foundation-slice-drift:{partitionId}");
    }

    private static void ValidateUpstreamReferences(
        DomainPartitionStateV1<GovernancePolityPayloadV1> polities,
        DomainPartitionStateV1<SpatialScopeRegistryPayloadV1> tileScopes,
        DomainPartitionStateV1<GovernancePublicAuthorityPayloadV1> publicAuthorities,
        IDomainRecordSchemaResolverV1 references)
    {
        foreach (var record in polities.RecordsCanonical)
            RequireResolved(
                new PartitionRecordRefV1(GovernancePolityPayloadV1.PartitionId, record.RecordId),
                record.RecordSchema,
                references,
                "qa04.governance.territorial-foundation-polity-reference-missing");
        foreach (var record in tileScopes.RecordsCanonical)
            RequireResolved(
                new PartitionRecordRefV1(SpatialScopeRegistryPayloadV1.PartitionId, record.RecordId),
                record.RecordSchema,
                references,
                "qa04.governance.territorial-foundation-scope-reference-missing");
        foreach (var record in publicAuthorities.RecordsCanonical.Take(checked((int)PublicAuthorityCountUsed)))
            RequireResolved(
                new PartitionRecordRefV1(GovernancePublicAuthorityPayloadV1.PartitionId, record.RecordId),
                record.RecordSchema,
                references,
                "qa04.governance.territorial-foundation-authority-reference-missing");
    }

    private static void RequireResolved(
        PartitionRecordRefV1 reference,
        SchemaRefV1 expectedSchema,
        IDomainRecordSchemaResolverV1 references,
        string error)
    {
        if (!references.Exists(reference) ||
            !references.TryGetRecordSchema(reference, out var actualSchema) ||
            actualSchema != expectedSchema)
            throw new InvalidDataException(error);
    }

    private static void ValidateRequiredSecondaryIndexRegistrations()
    {
        var expected = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [GovernanceJurisdictionPayloadV1.PartitionId] = new[]
            {
                "governance.jurisdiction-by-scope", "governance.jurisdiction-by-polity",
            },
            [GovernanceTerritorialClaimPayloadV1.PartitionId] = new[]
            {
                "governance.claim-by-scope", "governance.claim-by-polity",
            },
            [GovernanceEffectiveControlPayloadV1.PartitionId] = new[]
            {
                "governance.control-by-scope", "governance.control-by-controller",
            },
        };

        foreach (var pair in expected)
        {
            var registrations = StandardSecondaryIndexRegistry.ForPartition(pair.Key);
            if (registrations.Count != pair.Value.Length ||
                pair.Value.Any(indexId => registrations.All(entry => entry.IndexId.Value != indexId)))
                throw new InvalidDataException($"qa04.governance.territorial-foundation-index-registration-drift:{pair.Key}");
        }
    }

    private static void ValidateThreeTwoScopeDistribution(
        IReadOnlyDictionary<PartitionRecordRefV1, int> counts,
        string error)
    {
        if (counts.Count != 4_096)
            throw new InvalidDataException(error);
        var three = 0;
        var two = 0;
        foreach (var pair in counts)
        {
            if (pair.Value == 3) three++;
            else if (pair.Value == 2) two++;
            else throw new InvalidDataException(error);
        }
        if (three != 1_808 || two != 2_288)
            throw new InvalidDataException(error);
    }

    private static void EnsureUniqueIds(IEnumerable<OpaqueId128> ids, string error)
    {
        var set = new HashSet<OpaqueId128>();
        foreach (var id in ids)
            if (!set.Add(id)) throw new InvalidDataException(error);
    }

    private static void EnsureUniqueRelations(
        IEnumerable<(PartitionRecordRefV1 First, PartitionRecordRefV1 Second)> relations,
        string error)
    {
        var set = new HashSet<(PartitionRecordRefV1 First, PartitionRecordRefV1 Second)>();
        foreach (var relation in relations)
            if (!set.Add(relation)) throw new InvalidDataException(error);
    }

    private static void ValidateIndex(
        DerivedRecordIndexV1<OpaqueId128> index,
        int expectedKeyCount,
        ulong expectedPostingCount,
        string error)
    {
        var entries = index.CanonicalEntries.ToArray();
        if (entries.Length != expectedKeyCount ||
            entries.Aggregate(0UL, static (sum, pair) => checked(sum + (ulong)pair.Value.Count)) != expectedPostingCount)
            throw new InvalidDataException(error);
    }
}
