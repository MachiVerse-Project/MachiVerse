using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.WorldState;

public enum PrimaryKeyKindV1
{
    RecordId128 = 1
}

public enum PersistenceClassV1
{
    AuthoritativeAlways = 0,
    AuthoritativeReconstructableWithRecipe = 1,
    DerivedCacheRebuildable = 2,
    DiagnosticOnly = 3
}

public enum CanonicalOrderKindV1
{
    RecordIdBytewiseAsc = 1
}

public sealed record DomainPartitionIdentityV1(
    StableToken PartitionId,
    StableToken OwnerDomain,
    ushort OwnerDomainRank,
    SchemaRefV1 PartitionSchema,
    SchemaRefV1 RecordSchema,
    PrimaryKeyKindV1 PrimaryKeyKind,
    PersistenceClassV1 PersistenceClass,
    CanonicalOrderKindV1 CanonicalOrder);

public static class StandardDomainPartitionRegistry
{
    public const int StandardPartitionCount = 97;

    private static readonly IReadOnlyDictionary<string, ushort> DomainRanks = new Dictionary<string, ushort>(StringComparer.Ordinal)
    {
        ["spatial"] = 10,
        ["environment"] = 20,
        ["physical_built"] = 30,
        ["participation"] = 40,
        ["resident"] = 50,
        ["society_economy"] = 60,
        ["governance_security"] = 70,
        ["infrastructure_information"] = 80
    };

    private static readonly IReadOnlyDictionary<string, int> ExpectedOwnerCounts = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["spatial"] = 8,
        ["environment"] = 13,
        ["physical_built"] = 11,
        ["participation"] = 5,
        ["resident"] = 13,
        ["society_economy"] = 16,
        ["governance_security"] = 17,
        ["infrastructure_information"] = 14
    };

    private static readonly DomainPartitionIdentityV1[] CanonicalEntries = BuildEntries();
    private static readonly IReadOnlyDictionary<string, DomainPartitionIdentityV1> ById =
        CanonicalEntries.ToDictionary(static entry => entry.PartitionId.Value, StringComparer.Ordinal);

    public static IReadOnlyList<DomainPartitionIdentityV1> Entries => CanonicalEntries;

    public static DomainPartitionIdentityV1 Get(string partitionId)
        => ById.TryGetValue(partitionId, out var entry)
            ? entry
            : throw new KeyNotFoundException($"Unknown standard PartitionId: {partitionId}");

    public static bool TryGet(string partitionId, out DomainPartitionIdentityV1? entry)
        => ById.TryGetValue(partitionId, out entry);

    private static DomainPartitionIdentityV1[] BuildEntries()
    {
        var entries = new List<DomainPartitionIdentityV1>(StandardPartitionCount);

        Add(entries, "spatial",
            "spatial.world_frame", "spatial.scope_registry", "spatial.terrain_geometry", "spatial.void_geometry",
            "spatial.containment_topology", "spatial.boundary_topology", "spatial.detail_regions", "spatial.geometry_lineage");

        Add(entries, "environment",
            "environment.geology", "environment.soil", "environment.resource_deposit", "environment.groundwater",
            "environment.atmosphere", "environment.climate", "environment.weather", "environment.surface_water",
            "environment.ocean", "environment.ecosystem", "environment.contaminant", "environment.hazard",
            "environment.environment_lineage");

        Add(entries, "physical_built",
            "physical.presence", "physical.occupancy", "built.structure", "built.space", "built.opening",
            "physical.container_location", "built.worksite", "physical.condition", "physical.combustion",
            "physical.material_handoff", "physical.lineage");

        Add(entries, "participation",
            "participation.binding", "participation.absence_policy", "participation.control_mode",
            "participation.history", "participation.detail_requirement");

        Add(entries, "resident",
            "resident.identity_lifecycle", "resident.body_health", "resident.physiology", "resident.perception",
            "resident.knowledge_belief", "resident.memory", "resident.psychology", "resident.goal_plan",
            "resident.skill_aptitude", "resident.relationship", "resident.family_lineage", "resident.behavior_state",
            "resident.lineage");

        Add(entries, "society_economy",
            "society.organization", "society.membership_role", "society.employment", "society.household",
            "society.contract_claim", "society.property_right", "society.currency_money", "society.finance_account",
            "society.market_transaction", "society.business_production", "society.logistics_obligation",
            "society.education", "society.culture", "society.reputation", "society.information_claim",
            "society.history_lineage");

        Add(entries, "governance_security",
            "governance.polity", "governance.institution", "governance.law_rule", "governance.jurisdiction",
            "governance.territorial_claim", "governance.effective_control", "governance.public_authority",
            "governance.tax_fiscal", "governance.permission_license", "governance.diplomacy",
            "governance.security_incident", "governance.investigation", "governance.judicial_case",
            "governance.enforcement", "governance.military_authority", "governance.border_control",
            "governance.lineage");

        Add(entries, "infrastructure_information",
            "infrastructure.network_topology", "infrastructure.transport_service", "infrastructure.water_service",
            "infrastructure.power_service", "infrastructure.communication_service", "infrastructure.dependency",
            "infrastructure.facility_service", "infrastructure.service_queue", "information.delivery",
            "information.media_distribution", "information.record_store", "information.address_place_index",
            "infrastructure.failure_recovery", "infrastructure.lineage");

        var canonical = entries.OrderBy(static entry => entry.PartitionId.Value, StringComparer.Ordinal).ToArray();
        Validate(canonical);
        return canonical;
    }

    private static void Add(List<DomainPartitionIdentityV1> entries, string owner, params string[] partitionIds)
    {
        var ownerToken = new StableToken(owner);
        var rank = DomainRanks[owner];
        foreach (var partitionId in partitionIds)
        {
            var partitionToken = new StableToken(partitionId);
            entries.Add(new DomainPartitionIdentityV1(
                partitionToken,
                ownerToken,
                rank,
                new SchemaRefV1("domain." + partitionId),
                new SchemaRefV1("domain." + partitionId + ".record"),
                PrimaryKeyKindV1.RecordId128,
                PersistenceClassV1.AuthoritativeAlways,
                CanonicalOrderKindV1.RecordIdBytewiseAsc));
        }
    }

    private static void Validate(IReadOnlyList<DomainPartitionIdentityV1> entries)
    {
        if (entries.Count != StandardPartitionCount)
            throw new InvalidOperationException($"Standard partition registry must contain exactly {StandardPartitionCount} entries.");

        var duplicate = entries
            .GroupBy(static entry => entry.PartitionId.Value, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() != 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Duplicate standard PartitionId: {duplicate.Key}");

        foreach (var expected in ExpectedOwnerCounts)
        {
            var actual = entries.Count(entry => string.Equals(entry.OwnerDomain.Value, expected.Key, StringComparison.Ordinal));
            if (actual != expected.Value)
                throw new InvalidOperationException($"Owner {expected.Key} must contain exactly {expected.Value} standard partitions; actual={actual}.");
        }

        for (var index = 1; index < entries.Count; index++)
        {
            if (string.CompareOrdinal(entries[index - 1].PartitionId.Value, entries[index].PartitionId.Value) >= 0)
                throw new InvalidOperationException("Standard partition registry is not in canonical ASCII bytewise order.");
        }

        foreach (var entry in entries)
        {
            var expectedPartitionSchema = "domain." + entry.PartitionId.Value;
            var expectedRecordSchema = expectedPartitionSchema + ".record";
            if (!string.Equals(entry.PartitionSchema.SchemaId.Value, expectedPartitionSchema, StringComparison.Ordinal) ||
                !string.Equals(entry.RecordSchema.SchemaId.Value, expectedRecordSchema, StringComparison.Ordinal) ||
                entry.PartitionSchema.Version != new SchemaVersionV1(1, 0) ||
                entry.RecordSchema.Version != new SchemaVersionV1(1, 0))
                throw new InvalidOperationException($"Schema identity mismatch for {entry.PartitionId.Value}.");
        }
    }
}
