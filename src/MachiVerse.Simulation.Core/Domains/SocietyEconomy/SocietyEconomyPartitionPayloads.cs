using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Domains.SocietyEconomy;

public sealed record SocietyOrganizationPayloadV1(
    OpaqueId128 OrganizationId,
    StableToken OrganizationClass,
    StableToken Lifecycle,
    IReadOnlyList<StableToken> PurposeTokens,
    IReadOnlyList<PartitionRecordRefV1> ParentRefs,
    IReadOnlyList<PartitionRecordRefV1> FacilityRefs,
    ulong FoundedStep)
{
    public const string PartitionId = "society.organization";
    public IReadOnlyDictionary<string, object?> ToStandardPayload() => SocietyPayloadFields.Map(
        ("organization_id", OrganizationId),
        ("organization_class", OrganizationClass.Value),
        ("lifecycle", Lifecycle.Value),
        ("purpose_tokens", SocietyPayloadFields.TokenValues(PurposeTokens)),
        ("parent_refs", ParentRefs),
        ("facility_refs", FacilityRefs),
        ("founded_step", FoundedStep));
    public byte[] CanonicalDigest() => SocietyPayloadFields.Digest(PartitionId, ToStandardPayload());
    public static SocietyOrganizationPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        SocietyPayloadFields.Required<OpaqueId128>(values, PartitionId, "organization_id"),
        new StableToken(SocietyPayloadFields.Required<string>(values, PartitionId, "organization_class")),
        new StableToken(SocietyPayloadFields.Required<string>(values, PartitionId, "lifecycle")),
        SocietyPayloadFields.Tokens(values, PartitionId, "purpose_tokens"),
        SocietyPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "parent_refs"),
        SocietyPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "facility_refs"),
        SocietyPayloadFields.Required<ulong>(values, PartitionId, "founded_step"));
}

public sealed record SocietyMembershipRolePayloadV1(
    PartitionRecordRefV1 OrganizationRef,
    PartitionRecordRefV1 MemberRef,
    IReadOnlyList<StableToken> RoleTokens,
    IReadOnlyList<StableToken> AuthorityTokens,
    ulong JoinedStep,
    ulong? EndedStep,
    StableToken Status)
{
    public const string PartitionId = "society.membership_role";
    public IReadOnlyDictionary<string, object?> ToStandardPayload()
    {
        var values = SocietyPayloadFields.Map(
            ("organization_ref", OrganizationRef),
            ("member_ref", MemberRef),
            ("role_tokens", SocietyPayloadFields.TokenValues(RoleTokens)),
            ("authority_tokens", SocietyPayloadFields.TokenValues(AuthorityTokens)),
            ("joined_step", JoinedStep),
            ("status", Status.Value));
        SocietyPayloadFields.AddOptional(values, "ended_step", EndedStep);
        return values;
    }
    public byte[] CanonicalDigest() => SocietyPayloadFields.Digest(PartitionId, ToStandardPayload());
    public static SocietyMembershipRolePayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        SocietyPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "organization_ref"),
        SocietyPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "member_ref"),
        SocietyPayloadFields.Tokens(values, PartitionId, "role_tokens"),
        SocietyPayloadFields.Tokens(values, PartitionId, "authority_tokens"),
        SocietyPayloadFields.Required<ulong>(values, PartitionId, "joined_step"),
        SocietyPayloadFields.Optional<ulong>(values, "ended_step"),
        new StableToken(SocietyPayloadFields.Required<string>(values, PartitionId, "status")));
}

public sealed record SocietyEmploymentPayloadV1(
    PartitionRecordRefV1 EmployerRef,
    PartitionRecordRefV1 WorkerRef,
    StableToken JobToken,
    StableToken Status,
    ulong StartedStep,
    ulong? EndedStep,
    long WageMicrounitPerPeriod,
    ulong PayPeriodSteps,
    IReadOnlyList<PartitionRecordRefV1> ObligationRefs)
{
    public const string PartitionId = "society.employment";
    public IReadOnlyDictionary<string, object?> ToStandardPayload()
    {
        var values = SocietyPayloadFields.Map(
            ("employer_ref", EmployerRef),
            ("worker_ref", WorkerRef),
            ("job_token", JobToken.Value),
            ("status", Status.Value),
            ("started_step", StartedStep),
            ("wage_microunit_per_period", WageMicrounitPerPeriod),
            ("pay_period_steps", PayPeriodSteps),
            ("obligation_refs", ObligationRefs));
        SocietyPayloadFields.AddOptional(values, "ended_step", EndedStep);
        return values;
    }
    public byte[] CanonicalDigest() => SocietyPayloadFields.Digest(PartitionId, ToStandardPayload());
    public static SocietyEmploymentPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        SocietyPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "employer_ref"),
        SocietyPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "worker_ref"),
        new StableToken(SocietyPayloadFields.Required<string>(values, PartitionId, "job_token")),
        new StableToken(SocietyPayloadFields.Required<string>(values, PartitionId, "status")),
        SocietyPayloadFields.Required<ulong>(values, PartitionId, "started_step"),
        SocietyPayloadFields.Optional<ulong>(values, "ended_step"),
        SocietyPayloadFields.Required<long>(values, PartitionId, "wage_microunit_per_period"),
        SocietyPayloadFields.Required<ulong>(values, PartitionId, "pay_period_steps"),
        SocietyPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "obligation_refs"));
}

public sealed record SocietyHouseholdPayloadV1(
    IReadOnlyList<PartitionRecordRefV1> MemberRefs,
    IReadOnlyList<PartitionRecordRefV1> SharedAccountRefs,
    IReadOnlyList<PartitionRecordRefV1> ResidenceRefs,
    IReadOnlyList<PartitionRecordRefV1> ResourceBudgetRefs,
    StableToken Status)
{
    public const string PartitionId = "society.household";
    public IReadOnlyDictionary<string, object?> ToStandardPayload() => SocietyPayloadFields.Map(
        ("member_refs", MemberRefs),
        ("shared_account_refs", SharedAccountRefs),
        ("residence_refs", ResidenceRefs),
        ("resource_budget_refs", ResourceBudgetRefs),
        ("status", Status.Value));
    public byte[] CanonicalDigest() => SocietyPayloadFields.Digest(PartitionId, ToStandardPayload());
    public static SocietyHouseholdPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        SocietyPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "member_refs"),
        SocietyPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "shared_account_refs"),
        SocietyPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "residence_refs"),
        SocietyPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "resource_budget_refs"),
        new StableToken(SocietyPayloadFields.Required<string>(values, PartitionId, "status")));
}

public sealed record SocietyContractClaimPayloadV1(
    StableToken ContractKind,
    IReadOnlyList<PartitionRecordRefV1> PartyRefs,
    PartitionRecordRefV1? ClaimantRef,
    PartitionRecordRefV1? ObligorRef,
    long? Amount,
    long? Quantity,
    ulong? DueStep,
    StableToken Status,
    byte[] TermsDigest)
{
    public const string PartitionId = "society.contract_claim";
    public IReadOnlyDictionary<string, object?> ToStandardPayload()
    {
        var values = SocietyPayloadFields.Map(
            ("contract_kind", ContractKind.Value),
            ("party_refs", PartyRefs),
            ("status", Status.Value),
            ("terms_digest", TermsDigest));
        SocietyPayloadFields.AddOptional(values, "claimant_ref", ClaimantRef);
        SocietyPayloadFields.AddOptional(values, "obligor_ref", ObligorRef);
        SocietyPayloadFields.AddOptional(values, "amount", Amount);
        SocietyPayloadFields.AddOptional(values, "quantity", Quantity);
        SocietyPayloadFields.AddOptional(values, "due_step", DueStep);
        return values;
    }
    public byte[] CanonicalDigest() => SocietyPayloadFields.Digest(PartitionId, ToStandardPayload());
    public static SocietyContractClaimPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        new StableToken(SocietyPayloadFields.Required<string>(values, PartitionId, "contract_kind")),
        SocietyPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "party_refs"),
        SocietyPayloadFields.Optional<PartitionRecordRefV1>(values, "claimant_ref"),
        SocietyPayloadFields.Optional<PartitionRecordRefV1>(values, "obligor_ref"),
        SocietyPayloadFields.Optional<long>(values, "amount"),
        SocietyPayloadFields.Optional<long>(values, "quantity"),
        SocietyPayloadFields.Optional<ulong>(values, "due_step"),
        new StableToken(SocietyPayloadFields.Required<string>(values, PartitionId, "status")),
        SocietyPayloadFields.Required<byte[]>(values, PartitionId, "terms_digest").ToArray());
}

public sealed record SocietyPropertyRightPayloadV1(
    PartitionRecordRefV1 AssetRef,
    PartitionRecordRefV1 HolderRef,
    StableToken RightKind,
    uint SharePpm,
    ulong EffectiveFrom,
    ulong? EffectiveUntil,
    PartitionRecordRefV1? ClaimRef)
{
    public const string PartitionId = "society.property_right";
    public IReadOnlyDictionary<string, object?> ToStandardPayload()
    {
        var values = SocietyPayloadFields.Map(
            ("asset_ref", AssetRef),
            ("holder_ref", HolderRef),
            ("right_kind", RightKind.Value),
            ("share_ppm", SharePpm),
            ("effective_from", EffectiveFrom));
        SocietyPayloadFields.AddOptional(values, "effective_until", EffectiveUntil);
        SocietyPayloadFields.AddOptional(values, "claim_ref", ClaimRef);
        return values;
    }
    public byte[] CanonicalDigest() => SocietyPayloadFields.Digest(PartitionId, ToStandardPayload());
    public static SocietyPropertyRightPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        SocietyPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "asset_ref"),
        SocietyPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "holder_ref"),
        new StableToken(SocietyPayloadFields.Required<string>(values, PartitionId, "right_kind")),
        SocietyPayloadFields.Required<uint>(values, PartitionId, "share_ppm"),
        SocietyPayloadFields.Required<ulong>(values, PartitionId, "effective_from"),
        SocietyPayloadFields.Optional<ulong>(values, "effective_until"),
        SocietyPayloadFields.Optional<PartitionRecordRefV1>(values, "claim_ref"));
}

public sealed record SocietyCurrencyMoneyPayloadV1(
    StableToken CurrencyToken,
    PartitionRecordRefV1 IssuerRef,
    long SupplyMicrounit,
    StableToken Status,
    IReadOnlyList<PartitionRecordRefV1> PolicyRefs,
    uint UnitScale)
{
    public const string PartitionId = "society.currency_money";
    public IReadOnlyDictionary<string, object?> ToStandardPayload() => SocietyPayloadFields.Map(
        ("currency_token", CurrencyToken.Value),
        ("issuer_ref", IssuerRef),
        ("supply_microunit", SupplyMicrounit),
        ("status", Status.Value),
        ("policy_refs", PolicyRefs),
        ("unit_scale", UnitScale));
    public byte[] CanonicalDigest() => SocietyPayloadFields.Digest(PartitionId, ToStandardPayload());
    public static SocietyCurrencyMoneyPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        new StableToken(SocietyPayloadFields.Required<string>(values, PartitionId, "currency_token")),
        SocietyPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "issuer_ref"),
        SocietyPayloadFields.Required<long>(values, PartitionId, "supply_microunit"),
        new StableToken(SocietyPayloadFields.Required<string>(values, PartitionId, "status")),
        SocietyPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "policy_refs"),
        SocietyPayloadFields.Required<uint>(values, PartitionId, "unit_scale"));
}

public sealed record SocietyFinanceAccountPayloadV1(
    PartitionRecordRefV1 OwnerRef,
    PartitionRecordRefV1? InstitutionRef,
    StableToken CurrencyToken,
    long BalanceMicrounit,
    long CreditLimitMicrounit,
    StableToken Status,
    byte[] LedgerHeadDigest)
{
    public const string PartitionId = "society.finance_account";
    public IReadOnlyDictionary<string, object?> ToStandardPayload()
    {
        var values = SocietyPayloadFields.Map(
            ("owner_ref", OwnerRef),
            ("currency_token", CurrencyToken.Value),
            ("balance_microunit", BalanceMicrounit),
            ("credit_limit_microunit", CreditLimitMicrounit),
            ("status", Status.Value),
            ("ledger_head_digest", LedgerHeadDigest));
        SocietyPayloadFields.AddOptional(values, "institution_ref", InstitutionRef);
        return values;
    }
    public byte[] CanonicalDigest() => SocietyPayloadFields.Digest(PartitionId, ToStandardPayload());
    public static SocietyFinanceAccountPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        SocietyPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "owner_ref"),
        SocietyPayloadFields.Optional<PartitionRecordRefV1>(values, "institution_ref"),
        new StableToken(SocietyPayloadFields.Required<string>(values, PartitionId, "currency_token")),
        SocietyPayloadFields.Required<long>(values, PartitionId, "balance_microunit"),
        SocietyPayloadFields.Required<long>(values, PartitionId, "credit_limit_microunit"),
        new StableToken(SocietyPayloadFields.Required<string>(values, PartitionId, "status")),
        SocietyPayloadFields.Required<byte[]>(values, PartitionId, "ledger_head_digest").ToArray());
}

public sealed record SocietyMarketTransactionPayloadV1(
    PartitionRecordRefV1 MarketRef,
    StableToken InstrumentToken,
    StableToken? OrderSide,
    long? LimitPrice,
    long Quantity,
    long? ClearingPrice,
    PartitionRecordRefV1? BuyerRef,
    PartitionRecordRefV1? SellerRef,
    ulong EligibleStep,
    StableToken Status)
{
    public const string PartitionId = "society.market_transaction";
    public IReadOnlyDictionary<string, object?> ToStandardPayload()
    {
        var values = SocietyPayloadFields.Map(
            ("market_ref", MarketRef),
            ("instrument_token", InstrumentToken.Value),
            ("quantity", Quantity),
            ("eligible_step", EligibleStep),
            ("status", Status.Value));
        SocietyPayloadFields.AddOptionalToken(values, "order_side", OrderSide);
        SocietyPayloadFields.AddOptional(values, "limit_price", LimitPrice);
        SocietyPayloadFields.AddOptional(values, "clearing_price", ClearingPrice);
        SocietyPayloadFields.AddOptional(values, "buyer_ref", BuyerRef);
        SocietyPayloadFields.AddOptional(values, "seller_ref", SellerRef);
        return values;
    }
    public byte[] CanonicalDigest() => SocietyPayloadFields.Digest(PartitionId, ToStandardPayload());
    public static SocietyMarketTransactionPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        SocietyPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "market_ref"),
        new StableToken(SocietyPayloadFields.Required<string>(values, PartitionId, "instrument_token")),
        SocietyPayloadFields.OptionalToken(values, "order_side"),
        SocietyPayloadFields.Optional<long>(values, "limit_price"),
        SocietyPayloadFields.Required<long>(values, PartitionId, "quantity"),
        SocietyPayloadFields.Optional<long>(values, "clearing_price"),
        SocietyPayloadFields.Optional<PartitionRecordRefV1>(values, "buyer_ref"),
        SocietyPayloadFields.Optional<PartitionRecordRefV1>(values, "seller_ref"),
        SocietyPayloadFields.Required<ulong>(values, PartitionId, "eligible_step"),
        new StableToken(SocietyPayloadFields.Required<string>(values, PartitionId, "status")));
}

public sealed record SocietyBusinessProductionPayloadV1(
    PartitionRecordRefV1 OrganizationRef,
    StableToken RecipeToken,
    long PlannedQuantity,
    long CompletedQuantity,
    IReadOnlyList<PartitionRecordRefV1> InputRefs,
    IReadOnlyList<PartitionRecordRefV1> OutputRefs,
    ulong WorkRequired,
    long EnergyRequiredMj,
    StableToken Status)
{
    public const string PartitionId = "society.business_production";
    public IReadOnlyDictionary<string, object?> ToStandardPayload() => SocietyPayloadFields.Map(
        ("organization_ref", OrganizationRef),
        ("recipe_token", RecipeToken.Value),
        ("planned_quantity", PlannedQuantity),
        ("completed_quantity", CompletedQuantity),
        ("input_refs", InputRefs),
        ("output_refs", OutputRefs),
        ("work_required", WorkRequired),
        ("energy_required_mj", EnergyRequiredMj),
        ("status", Status.Value));
    public byte[] CanonicalDigest() => SocietyPayloadFields.Digest(PartitionId, ToStandardPayload());
    public static SocietyBusinessProductionPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        SocietyPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "organization_ref"),
        new StableToken(SocietyPayloadFields.Required<string>(values, PartitionId, "recipe_token")),
        SocietyPayloadFields.Required<long>(values, PartitionId, "planned_quantity"),
        SocietyPayloadFields.Required<long>(values, PartitionId, "completed_quantity"),
        SocietyPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "input_refs"),
        SocietyPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "output_refs"),
        SocietyPayloadFields.Required<ulong>(values, PartitionId, "work_required"),
        SocietyPayloadFields.Required<long>(values, PartitionId, "energy_required_mj"),
        new StableToken(SocietyPayloadFields.Required<string>(values, PartitionId, "status")));
}

public sealed record SocietyLogisticsObligationPayloadV1(
    PartitionRecordRefV1 ShipperRef,
    PartitionRecordRefV1 ConsigneeRef,
    IReadOnlyList<PartitionRecordRefV1> CargoRefs,
    long Quantity,
    PartitionRecordRefV1 OriginRef,
    PartitionRecordRefV1 DestinationRef,
    ulong? DueStep,
    StableToken Status,
    PartitionRecordRefV1? CarrierRef)
{
    public const string PartitionId = "society.logistics_obligation";
    public IReadOnlyDictionary<string, object?> ToStandardPayload()
    {
        var values = SocietyPayloadFields.Map(
            ("shipper_ref", ShipperRef),
            ("consignee_ref", ConsigneeRef),
            ("cargo_refs", CargoRefs),
            ("quantity", Quantity),
            ("origin_ref", OriginRef),
            ("destination_ref", DestinationRef),
            ("status", Status.Value));
        SocietyPayloadFields.AddOptional(values, "due_step", DueStep);
        SocietyPayloadFields.AddOptional(values, "carrier_ref", CarrierRef);
        return values;
    }
    public byte[] CanonicalDigest() => SocietyPayloadFields.Digest(PartitionId, ToStandardPayload());
    public static SocietyLogisticsObligationPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        SocietyPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "shipper_ref"),
        SocietyPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "consignee_ref"),
        SocietyPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "cargo_refs"),
        SocietyPayloadFields.Required<long>(values, PartitionId, "quantity"),
        SocietyPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "origin_ref"),
        SocietyPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "destination_ref"),
        SocietyPayloadFields.Optional<ulong>(values, "due_step"),
        new StableToken(SocietyPayloadFields.Required<string>(values, PartitionId, "status")),
        SocietyPayloadFields.Optional<PartitionRecordRefV1>(values, "carrier_ref"));
}

public sealed record SocietyEducationPayloadV1(
    PartitionRecordRefV1 ProviderRef,
    PartitionRecordRefV1 LearnerRef,
    StableToken ProgramToken,
    StableToken Status,
    uint ProgressPpm,
    IReadOnlyList<PartitionRecordRefV1> SkillRefs,
    ulong StartedStep,
    ulong? EndedStep)
{
    public const string PartitionId = "society.education";
    public IReadOnlyDictionary<string, object?> ToStandardPayload()
    {
        var values = SocietyPayloadFields.Map(
            ("provider_ref", ProviderRef),
            ("learner_ref", LearnerRef),
            ("program_token", ProgramToken.Value),
            ("status", Status.Value),
            ("progress_ppm", ProgressPpm),
            ("skill_refs", SkillRefs),
            ("started_step", StartedStep));
        SocietyPayloadFields.AddOptional(values, "ended_step", EndedStep);
        return values;
    }
    public byte[] CanonicalDigest() => SocietyPayloadFields.Digest(PartitionId, ToStandardPayload());
    public static SocietyEducationPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        SocietyPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "provider_ref"),
        SocietyPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "learner_ref"),
        new StableToken(SocietyPayloadFields.Required<string>(values, PartitionId, "program_token")),
        new StableToken(SocietyPayloadFields.Required<string>(values, PartitionId, "status")),
        SocietyPayloadFields.Required<uint>(values, PartitionId, "progress_ppm"),
        SocietyPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "skill_refs"),
        SocietyPayloadFields.Required<ulong>(values, PartitionId, "started_step"),
        SocietyPayloadFields.Optional<ulong>(values, "ended_step"));
}

public sealed record SocietyCulturePayloadV1(
    PartitionRecordRefV1 SubjectRef,
    StableToken TraitToken,
    uint AffiliationPpm,
    ulong AdoptionStep,
    IReadOnlyList<PartitionRecordRefV1> SourceRefs,
    StableToken Status)
{
    public const string PartitionId = "society.culture";
    public IReadOnlyDictionary<string, object?> ToStandardPayload() => SocietyPayloadFields.Map(
        ("subject_ref", SubjectRef),
        ("trait_token", TraitToken.Value),
        ("affiliation_ppm", AffiliationPpm),
        ("adoption_step", AdoptionStep),
        ("source_refs", SourceRefs),
        ("status", Status.Value));
    public byte[] CanonicalDigest() => SocietyPayloadFields.Digest(PartitionId, ToStandardPayload());
    public static SocietyCulturePayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        SocietyPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "subject_ref"),
        new StableToken(SocietyPayloadFields.Required<string>(values, PartitionId, "trait_token")),
        SocietyPayloadFields.Required<uint>(values, PartitionId, "affiliation_ppm"),
        SocietyPayloadFields.Required<ulong>(values, PartitionId, "adoption_step"),
        SocietyPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "source_refs"),
        new StableToken(SocietyPayloadFields.Required<string>(values, PartitionId, "status")));
}

public sealed record SocietyReputationPayloadV1(
    PartitionRecordRefV1 SubjectRef,
    PartitionRecordRefV1? AudienceScopeRef,
    StableToken DimensionToken,
    int Score,
    uint ConfidencePpm,
    IReadOnlyList<PartitionRecordRefV1> EvidenceRefs,
    ulong UpdatedStep)
{
    public const string PartitionId = "society.reputation";
    public IReadOnlyDictionary<string, object?> ToStandardPayload()
    {
        var values = SocietyPayloadFields.Map(
            ("subject_ref", SubjectRef),
            ("dimension_token", DimensionToken.Value),
            ("score", Score),
            ("confidence_ppm", ConfidencePpm),
            ("evidence_refs", EvidenceRefs),
            ("updated_step", UpdatedStep));
        SocietyPayloadFields.AddOptional(values, "audience_scope_ref", AudienceScopeRef);
        return values;
    }
    public byte[] CanonicalDigest() => SocietyPayloadFields.Digest(PartitionId, ToStandardPayload());
    public static SocietyReputationPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        SocietyPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "subject_ref"),
        SocietyPayloadFields.Optional<PartitionRecordRefV1>(values, "audience_scope_ref"),
        new StableToken(SocietyPayloadFields.Required<string>(values, PartitionId, "dimension_token")),
        SocietyPayloadFields.Required<int>(values, PartitionId, "score"),
        SocietyPayloadFields.Required<uint>(values, PartitionId, "confidence_ppm"),
        SocietyPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "evidence_refs"),
        SocietyPayloadFields.Required<ulong>(values, PartitionId, "updated_step"));
}

public sealed record SocietyInformationClaimPayloadV1(
    PartitionRecordRefV1 ClaimantRef,
    IReadOnlyList<PartitionRecordRefV1> SubjectRefs,
    StableToken ClaimToken,
    byte[] ContentDigest,
    IReadOnlyList<PartitionRecordRefV1> ProvenanceRefs,
    ulong CreatedStep,
    StableToken Status)
{
    public const string PartitionId = "society.information_claim";
    public IReadOnlyDictionary<string, object?> ToStandardPayload() => SocietyPayloadFields.Map(
        ("claimant_ref", ClaimantRef),
        ("subject_refs", SubjectRefs),
        ("claim_token", ClaimToken.Value),
        ("content_digest", ContentDigest),
        ("provenance_refs", ProvenanceRefs),
        ("created_step", CreatedStep),
        ("status", Status.Value));
    public byte[] CanonicalDigest() => SocietyPayloadFields.Digest(PartitionId, ToStandardPayload());
    public static SocietyInformationClaimPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        SocietyPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "claimant_ref"),
        SocietyPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "subject_refs"),
        new StableToken(SocietyPayloadFields.Required<string>(values, PartitionId, "claim_token")),
        SocietyPayloadFields.Required<byte[]>(values, PartitionId, "content_digest").ToArray(),
        SocietyPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "provenance_refs"),
        SocietyPayloadFields.Required<ulong>(values, PartitionId, "created_step"),
        new StableToken(SocietyPayloadFields.Required<string>(values, PartitionId, "status")));
}

public sealed record SocietyHistoryLineagePayloadV1(
    PartitionRecordRefV1 SubjectRef,
    StableToken HistoryKind,
    IReadOnlyList<PartitionRecordRefV1> ParentRefs,
    ulong BasisStep,
    byte[] CausalityDigest)
{
    public const string PartitionId = "society.history_lineage";
    public IReadOnlyDictionary<string, object?> ToStandardPayload() => SocietyPayloadFields.Map(
        ("subject_ref", SubjectRef),
        ("history_kind", HistoryKind.Value),
        ("parent_refs", ParentRefs),
        ("basis_step", BasisStep),
        ("causality_digest", CausalityDigest));
    public byte[] CanonicalDigest() => SocietyPayloadFields.Digest(PartitionId, ToStandardPayload());
    public static SocietyHistoryLineagePayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        SocietyPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "subject_ref"),
        new StableToken(SocietyPayloadFields.Required<string>(values, PartitionId, "history_kind")),
        SocietyPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "parent_refs"),
        SocietyPayloadFields.Required<ulong>(values, PartitionId, "basis_step"),
        SocietyPayloadFields.Required<byte[]>(values, PartitionId, "causality_digest").ToArray());
}

public static class SocietyEconomyDomainSnapshotProviderV1
{
    public static IReadOnlyList<IDomainPartitionSnapshotSectionProviderV1> CreateAll()
    {
        IDomainPartitionSnapshotSectionProviderV1[] providers =
        {
            Provider<SocietyOrganizationPayloadV1>(SocietyOrganizationPayloadV1.PartitionId, static value => value.ToStandardPayload(), SocietyOrganizationPayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<SocietyMembershipRolePayloadV1>(SocietyMembershipRolePayloadV1.PartitionId, static value => value.ToStandardPayload(), SocietyMembershipRolePayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<SocietyEmploymentPayloadV1>(SocietyEmploymentPayloadV1.PartitionId, static value => value.ToStandardPayload(), SocietyEmploymentPayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<SocietyHouseholdPayloadV1>(SocietyHouseholdPayloadV1.PartitionId, static value => value.ToStandardPayload(), SocietyHouseholdPayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<SocietyContractClaimPayloadV1>(SocietyContractClaimPayloadV1.PartitionId, static value => value.ToStandardPayload(), SocietyContractClaimPayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<SocietyPropertyRightPayloadV1>(SocietyPropertyRightPayloadV1.PartitionId, static value => value.ToStandardPayload(), SocietyPropertyRightPayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<SocietyCurrencyMoneyPayloadV1>(SocietyCurrencyMoneyPayloadV1.PartitionId, static value => value.ToStandardPayload(), SocietyCurrencyMoneyPayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<SocietyFinanceAccountPayloadV1>(SocietyFinanceAccountPayloadV1.PartitionId, static value => value.ToStandardPayload(), SocietyFinanceAccountPayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<SocietyMarketTransactionPayloadV1>(SocietyMarketTransactionPayloadV1.PartitionId, static value => value.ToStandardPayload(), SocietyMarketTransactionPayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<SocietyBusinessProductionPayloadV1>(SocietyBusinessProductionPayloadV1.PartitionId, static value => value.ToStandardPayload(), SocietyBusinessProductionPayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<SocietyLogisticsObligationPayloadV1>(SocietyLogisticsObligationPayloadV1.PartitionId, static value => value.ToStandardPayload(), SocietyLogisticsObligationPayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<SocietyEducationPayloadV1>(SocietyEducationPayloadV1.PartitionId, static value => value.ToStandardPayload(), SocietyEducationPayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<SocietyCulturePayloadV1>(SocietyCulturePayloadV1.PartitionId, static value => value.ToStandardPayload(), SocietyCulturePayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<SocietyReputationPayloadV1>(SocietyReputationPayloadV1.PartitionId, static value => value.ToStandardPayload(), SocietyReputationPayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<SocietyInformationClaimPayloadV1>(SocietyInformationClaimPayloadV1.PartitionId, static value => value.ToStandardPayload(), SocietyInformationClaimPayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<SocietyHistoryLineagePayloadV1>(SocietyHistoryLineagePayloadV1.PartitionId, static value => value.ToStandardPayload(), SocietyHistoryLineagePayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
        };
        return Array.AsReadOnly(providers.OrderBy(static provider => provider.SectionId, StringComparer.Ordinal).ToArray());
    }

    private static IDomainPartitionSnapshotSectionProviderV1 Provider<TPayload>(
        string partitionId,
        Func<TPayload, IReadOnlyDictionary<string, object?>> toStandard,
        Func<IReadOnlyDictionary<string, object?>, TPayload> fromStandard,
        Func<TPayload, byte[]> digest)
        => new DomainPartitionSnapshotSectionProviderV1<TPayload>(
            partitionId,
            toStandard,
            fromStandard,
            digest,
            StandardDomainNestedSnapshotCodecRegistryV1.Default);
}

internal static class SocietyPayloadFields
{
    public static Dictionary<string, object?> Map(params (string Name, object? Value)[] values)
        => values.ToDictionary(static pair => pair.Name, static pair => pair.Value, StringComparer.Ordinal);

    public static void AddOptional<T>(Dictionary<string, object?> values, string field, T? value) where T : struct
    {
        if (value is { } present) values[field] = present;
    }

    public static void AddOptionalToken(Dictionary<string, object?> values, string field, StableToken? value)
    {
        if (value is { } present) values[field] = present.Value;
    }

    public static T Required<T>(IReadOnlyDictionary<string, object?> values, string partitionId, string field)
        => values.TryGetValue(field, out var value) && value is T typed
            ? typed
            : throw new InvalidDataException($"society.snapshot-payload.required:{partitionId}:{field}");

    public static T? Optional<T>(IReadOnlyDictionary<string, object?> values, string field) where T : struct
        => values.TryGetValue(field, out var value) && value is not null ? (T)value : null;

    public static StableToken? OptionalToken(IReadOnlyDictionary<string, object?> values, string field)
        => values.TryGetValue(field, out var value) && value is string token ? new StableToken(token) : null;

    public static IReadOnlyList<string> TokenValues(IReadOnlyList<StableToken> tokens)
        => Array.AsReadOnly(tokens.Select(static token => token.Value).ToArray());

    public static IReadOnlyList<StableToken> Tokens(IReadOnlyDictionary<string, object?> values, string partitionId, string field)
    {
        var source = Required<IReadOnlyList<string>>(values, partitionId, field);
        return Array.AsReadOnly(source.Select(static token => new StableToken(token)).ToArray());
    }

    public static byte[] Digest(string partitionId, IReadOnlyDictionary<string, object?> payload)
        => StandardDomainPayloadCanonicalDigestV1.Compute(partitionId, payload);
}
