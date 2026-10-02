using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Domains.SocietyEconomy;

public sealed class SocietyEconomyDomainStateV1
{
    public SocietyEconomyDomainStateV1(
        DomainPartitionStateV1<SocietyOrganizationPayloadV1> organization,
        DomainPartitionStateV1<SocietyMembershipRolePayloadV1> membershipRole,
        DomainPartitionStateV1<SocietyEmploymentPayloadV1> employment,
        DomainPartitionStateV1<SocietyHouseholdPayloadV1> household,
        DomainPartitionStateV1<SocietyContractClaimPayloadV1> contractClaim,
        DomainPartitionStateV1<SocietyPropertyRightPayloadV1> propertyRight,
        DomainPartitionStateV1<SocietyCurrencyMoneyPayloadV1> currencyMoney,
        DomainPartitionStateV1<SocietyFinanceAccountPayloadV1> financeAccount,
        DomainPartitionStateV1<SocietyMarketTransactionPayloadV1> marketTransaction,
        DomainPartitionStateV1<SocietyBusinessProductionPayloadV1> businessProduction,
        DomainPartitionStateV1<SocietyLogisticsObligationPayloadV1> logisticsObligation,
        DomainPartitionStateV1<SocietyEducationPayloadV1> education,
        DomainPartitionStateV1<SocietyCulturePayloadV1> culture,
        DomainPartitionStateV1<SocietyReputationPayloadV1> reputation,
        DomainPartitionStateV1<SocietyInformationClaimPayloadV1> informationClaim,
        DomainPartitionStateV1<SocietyHistoryLineagePayloadV1> historyLineage)
    {
        Organization = RequireIdentity(organization, SocietyOrganizationPayloadV1.PartitionId);
        MembershipRole = RequireIdentity(membershipRole, SocietyMembershipRolePayloadV1.PartitionId);
        Employment = RequireIdentity(employment, SocietyEmploymentPayloadV1.PartitionId);
        Household = RequireIdentity(household, SocietyHouseholdPayloadV1.PartitionId);
        ContractClaim = RequireIdentity(contractClaim, SocietyContractClaimPayloadV1.PartitionId);
        PropertyRight = RequireIdentity(propertyRight, SocietyPropertyRightPayloadV1.PartitionId);
        CurrencyMoney = RequireIdentity(currencyMoney, SocietyCurrencyMoneyPayloadV1.PartitionId);
        FinanceAccount = RequireIdentity(financeAccount, SocietyFinanceAccountPayloadV1.PartitionId);
        MarketTransaction = RequireIdentity(marketTransaction, SocietyMarketTransactionPayloadV1.PartitionId);
        BusinessProduction = RequireIdentity(businessProduction, SocietyBusinessProductionPayloadV1.PartitionId);
        LogisticsObligation = RequireIdentity(logisticsObligation, SocietyLogisticsObligationPayloadV1.PartitionId);
        Education = RequireIdentity(education, SocietyEducationPayloadV1.PartitionId);
        Culture = RequireIdentity(culture, SocietyCulturePayloadV1.PartitionId);
        Reputation = RequireIdentity(reputation, SocietyReputationPayloadV1.PartitionId);
        InformationClaim = RequireIdentity(informationClaim, SocietyInformationClaimPayloadV1.PartitionId);
        HistoryLineage = RequireIdentity(historyLineage, SocietyHistoryLineagePayloadV1.PartitionId);
    }

    public DomainPartitionStateV1<SocietyOrganizationPayloadV1> Organization { get; }
    public DomainPartitionStateV1<SocietyMembershipRolePayloadV1> MembershipRole { get; }
    public DomainPartitionStateV1<SocietyEmploymentPayloadV1> Employment { get; }
    public DomainPartitionStateV1<SocietyHouseholdPayloadV1> Household { get; }
    public DomainPartitionStateV1<SocietyContractClaimPayloadV1> ContractClaim { get; }
    public DomainPartitionStateV1<SocietyPropertyRightPayloadV1> PropertyRight { get; }
    public DomainPartitionStateV1<SocietyCurrencyMoneyPayloadV1> CurrencyMoney { get; }
    public DomainPartitionStateV1<SocietyFinanceAccountPayloadV1> FinanceAccount { get; }
    public DomainPartitionStateV1<SocietyMarketTransactionPayloadV1> MarketTransaction { get; }
    public DomainPartitionStateV1<SocietyBusinessProductionPayloadV1> BusinessProduction { get; }
    public DomainPartitionStateV1<SocietyLogisticsObligationPayloadV1> LogisticsObligation { get; }
    public DomainPartitionStateV1<SocietyEducationPayloadV1> Education { get; }
    public DomainPartitionStateV1<SocietyCulturePayloadV1> Culture { get; }
    public DomainPartitionStateV1<SocietyReputationPayloadV1> Reputation { get; }
    public DomainPartitionStateV1<SocietyInformationClaimPayloadV1> InformationClaim { get; }
    public DomainPartitionStateV1<SocietyHistoryLineagePayloadV1> HistoryLineage { get; }

    public static SocietyEconomyDomainStateV1 CreateEmpty()
        => new(
            Empty<SocietyOrganizationPayloadV1>(SocietyOrganizationPayloadV1.PartitionId),
            Empty<SocietyMembershipRolePayloadV1>(SocietyMembershipRolePayloadV1.PartitionId),
            Empty<SocietyEmploymentPayloadV1>(SocietyEmploymentPayloadV1.PartitionId),
            Empty<SocietyHouseholdPayloadV1>(SocietyHouseholdPayloadV1.PartitionId),
            Empty<SocietyContractClaimPayloadV1>(SocietyContractClaimPayloadV1.PartitionId),
            Empty<SocietyPropertyRightPayloadV1>(SocietyPropertyRightPayloadV1.PartitionId),
            Empty<SocietyCurrencyMoneyPayloadV1>(SocietyCurrencyMoneyPayloadV1.PartitionId),
            Empty<SocietyFinanceAccountPayloadV1>(SocietyFinanceAccountPayloadV1.PartitionId),
            Empty<SocietyMarketTransactionPayloadV1>(SocietyMarketTransactionPayloadV1.PartitionId),
            Empty<SocietyBusinessProductionPayloadV1>(SocietyBusinessProductionPayloadV1.PartitionId),
            Empty<SocietyLogisticsObligationPayloadV1>(SocietyLogisticsObligationPayloadV1.PartitionId),
            Empty<SocietyEducationPayloadV1>(SocietyEducationPayloadV1.PartitionId),
            Empty<SocietyCulturePayloadV1>(SocietyCulturePayloadV1.PartitionId),
            Empty<SocietyReputationPayloadV1>(SocietyReputationPayloadV1.PartitionId),
            Empty<SocietyInformationClaimPayloadV1>(SocietyInformationClaimPayloadV1.PartitionId),
            Empty<SocietyHistoryLineagePayloadV1>(SocietyHistoryLineagePayloadV1.PartitionId));

    public SocietyEconomyDomainSnapshotMaterialV1 BindSnapshotMaterial(WorldStateV1 frozenState)
        => SocietyEconomyDomainSnapshotMaterialV1.Bind(frozenState, this);

    private static DomainPartitionStateV1<TPayload> Empty<TPayload>(string partitionId)
        => new(StandardDomainPartitionRegistry.Get(partitionId), Array.Empty<DomainRecordEnvelopeV1<TPayload>>());

    private static DomainPartitionStateV1<TPayload> RequireIdentity<TPayload>(DomainPartitionStateV1<TPayload> partition, string partitionId)
    {
        ArgumentNullException.ThrowIfNull(partition);
        if (partition.Identity != StandardDomainPartitionRegistry.Get(partitionId))
            throw new InvalidDataException($"society.runtime-state.partition-identity:{partitionId}");
        return partition;
    }
}

public sealed class SocietyEconomyDomainSnapshotMaterialV1
{
    private SocietyEconomyDomainSnapshotMaterialV1(IEnumerable<IDomainPartitionSnapshotAuthorityV1> authorities)
    {
        var materialized = authorities?.ToArray() ?? throw new ArgumentNullException(nameof(authorities));
        if (materialized.Length != 16)
            throw new InvalidDataException("society.snapshot-material.authority-count");
        var byId = new Dictionary<string, IDomainPartitionSnapshotAuthorityV1>(StringComparer.Ordinal);
        foreach (var authority in materialized)
        {
            ArgumentNullException.ThrowIfNull(authority);
            authority.VerifyBoundAuthority();
            if (!string.Equals(authority.Identity.OwnerDomain.Value, "society_economy", StringComparison.Ordinal))
                throw new InvalidDataException($"society.snapshot-material.foreign-owner:{authority.PartitionId.Value}");
            if (!byId.TryAdd(authority.PartitionId.Value, authority))
                throw new InvalidDataException($"society.snapshot-material.duplicate:{authority.PartitionId.Value}");
        }

        Organization = Require<SocietyOrganizationPayloadV1>(byId, SocietyOrganizationPayloadV1.PartitionId);
        MembershipRole = Require<SocietyMembershipRolePayloadV1>(byId, SocietyMembershipRolePayloadV1.PartitionId);
        Employment = Require<SocietyEmploymentPayloadV1>(byId, SocietyEmploymentPayloadV1.PartitionId);
        Household = Require<SocietyHouseholdPayloadV1>(byId, SocietyHouseholdPayloadV1.PartitionId);
        ContractClaim = Require<SocietyContractClaimPayloadV1>(byId, SocietyContractClaimPayloadV1.PartitionId);
        PropertyRight = Require<SocietyPropertyRightPayloadV1>(byId, SocietyPropertyRightPayloadV1.PartitionId);
        CurrencyMoney = Require<SocietyCurrencyMoneyPayloadV1>(byId, SocietyCurrencyMoneyPayloadV1.PartitionId);
        FinanceAccount = Require<SocietyFinanceAccountPayloadV1>(byId, SocietyFinanceAccountPayloadV1.PartitionId);
        MarketTransaction = Require<SocietyMarketTransactionPayloadV1>(byId, SocietyMarketTransactionPayloadV1.PartitionId);
        BusinessProduction = Require<SocietyBusinessProductionPayloadV1>(byId, SocietyBusinessProductionPayloadV1.PartitionId);
        LogisticsObligation = Require<SocietyLogisticsObligationPayloadV1>(byId, SocietyLogisticsObligationPayloadV1.PartitionId);
        Education = Require<SocietyEducationPayloadV1>(byId, SocietyEducationPayloadV1.PartitionId);
        Culture = Require<SocietyCulturePayloadV1>(byId, SocietyCulturePayloadV1.PartitionId);
        Reputation = Require<SocietyReputationPayloadV1>(byId, SocietyReputationPayloadV1.PartitionId);
        InformationClaim = Require<SocietyInformationClaimPayloadV1>(byId, SocietyInformationClaimPayloadV1.PartitionId);
        HistoryLineage = Require<SocietyHistoryLineagePayloadV1>(byId, SocietyHistoryLineagePayloadV1.PartitionId);
        Authorities = Array.AsReadOnly(materialized.OrderBy(static value => value.PartitionId.Value, StringComparer.Ordinal).ToArray());
    }

    public DomainPartitionSnapshotAuthorityV1<SocietyOrganizationPayloadV1> Organization { get; }
    public DomainPartitionSnapshotAuthorityV1<SocietyMembershipRolePayloadV1> MembershipRole { get; }
    public DomainPartitionSnapshotAuthorityV1<SocietyEmploymentPayloadV1> Employment { get; }
    public DomainPartitionSnapshotAuthorityV1<SocietyHouseholdPayloadV1> Household { get; }
    public DomainPartitionSnapshotAuthorityV1<SocietyContractClaimPayloadV1> ContractClaim { get; }
    public DomainPartitionSnapshotAuthorityV1<SocietyPropertyRightPayloadV1> PropertyRight { get; }
    public DomainPartitionSnapshotAuthorityV1<SocietyCurrencyMoneyPayloadV1> CurrencyMoney { get; }
    public DomainPartitionSnapshotAuthorityV1<SocietyFinanceAccountPayloadV1> FinanceAccount { get; }
    public DomainPartitionSnapshotAuthorityV1<SocietyMarketTransactionPayloadV1> MarketTransaction { get; }
    public DomainPartitionSnapshotAuthorityV1<SocietyBusinessProductionPayloadV1> BusinessProduction { get; }
    public DomainPartitionSnapshotAuthorityV1<SocietyLogisticsObligationPayloadV1> LogisticsObligation { get; }
    public DomainPartitionSnapshotAuthorityV1<SocietyEducationPayloadV1> Education { get; }
    public DomainPartitionSnapshotAuthorityV1<SocietyCulturePayloadV1> Culture { get; }
    public DomainPartitionSnapshotAuthorityV1<SocietyReputationPayloadV1> Reputation { get; }
    public DomainPartitionSnapshotAuthorityV1<SocietyInformationClaimPayloadV1> InformationClaim { get; }
    public DomainPartitionSnapshotAuthorityV1<SocietyHistoryLineagePayloadV1> HistoryLineage { get; }
    public IReadOnlyList<IDomainPartitionSnapshotAuthorityV1> Authorities { get; }

    public static SocietyEconomyDomainSnapshotMaterialV1 Bind(WorldStateV1 frozenState, SocietyEconomyDomainStateV1 state)
    {
        ArgumentNullException.ThrowIfNull(frozenState);
        ArgumentNullException.ThrowIfNull(state);
        return new SocietyEconomyDomainSnapshotMaterialV1(
        [
            Bind(frozenState, state.Organization, SocietyOrganizationPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.MembershipRole, SocietyMembershipRolePayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.Employment, SocietyEmploymentPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.Household, SocietyHouseholdPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.ContractClaim, SocietyContractClaimPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.PropertyRight, SocietyPropertyRightPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.CurrencyMoney, SocietyCurrencyMoneyPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.FinanceAccount, SocietyFinanceAccountPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.MarketTransaction, SocietyMarketTransactionPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.BusinessProduction, SocietyBusinessProductionPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.LogisticsObligation, SocietyLogisticsObligationPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.Education, SocietyEducationPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.Culture, SocietyCulturePayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.Reputation, SocietyReputationPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.InformationClaim, SocietyInformationClaimPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.HistoryLineage, SocietyHistoryLineagePayloadV1.PartitionId, static value => value.CanonicalDigest()),
        ]);
    }

    private static DomainPartitionSnapshotAuthorityV1<TPayload> Bind<TPayload>(WorldStateV1 frozenState, DomainPartitionStateV1<TPayload> partition, string partitionId, Func<TPayload, byte[]> digest)
    {
        if (partition.Identity != StandardDomainPartitionRegistry.Get(partitionId))
            throw new InvalidDataException($"society.snapshot-material.partition-identity:{partitionId}");
        return new DomainPartitionSnapshotAuthorityV1<TPayload>(partition, frozenState.Partitions.Get(partitionId).Header, digest);
    }

    private static DomainPartitionSnapshotAuthorityV1<TPayload> Require<TPayload>(IReadOnlyDictionary<string, IDomainPartitionSnapshotAuthorityV1> byId, string partitionId)
    {
        if (!byId.TryGetValue(partitionId, out var authority))
            throw new InvalidDataException($"society.snapshot-material.missing:{partitionId}");
        return authority as DomainPartitionSnapshotAuthorityV1<TPayload>
            ?? throw new InvalidDataException($"society.snapshot-material.payload-type:{partitionId}");
    }
}
