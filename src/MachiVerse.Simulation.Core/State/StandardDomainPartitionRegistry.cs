using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.State;

public sealed record DomainPartitionRegistrationV1(
    StableToken PartitionId,
    StableToken OwnerDomain,
    SchemaRefV1 PartitionSchema,
    SchemaRefV1 RecordSchema,
    PrimaryKeyKindV1 PrimaryKeyKind,
    PersistenceClassV1 PersistenceClass,
    CanonicalOrderKindV1 CanonicalOrder,
    IReadOnlyList<StableToken> RequiredIndexes,
    IReadOnlyList<StableToken> InvariantIds);

public sealed record DomainFamilyDescriptorV1(
    StableToken DomainToken,
    ushort DomainRank,
    IReadOnlyList<DomainPartitionRegistrationV1> OwnedPartitions);

public sealed class DomainPartitionRegistryV1
{
    private readonly IReadOnlyList<DomainPartitionRegistrationV1> _entries;
    private readonly Dictionary<string, DomainPartitionRegistrationV1> _byId;

    internal DomainPartitionRegistryV1(uint registryGeneration, IEnumerable<DomainPartitionRegistrationV1> entries)
    {
        if (registryGeneration == 0) throw new ArgumentOutOfRangeException(nameof(registryGeneration));
        ArgumentNullException.ThrowIfNull(entries);

        var ordered = entries.OrderBy(static entry => entry.PartitionId.Value, StringComparer.Ordinal).ToArray();
        if (ordered.Length == 0) throw new ArgumentException("Partition registry cannot be empty.", nameof(entries));
        if (ordered.Select(static entry => entry.PartitionId.Value).Distinct(StringComparer.Ordinal).Count() != ordered.Length)
            throw new InvalidDataException("state.partition-registry.duplicate-partition-id");

        RegistryGeneration = registryGeneration;
        Schema = new SchemaRefV1(new StableToken("core.domain-partition-registry"), new SchemaVersionV1(1, 0));
        _entries = Array.AsReadOnly(ordered);
        _byId = ordered.ToDictionary(static entry => entry.PartitionId.Value, StringComparer.Ordinal);
    }

    public SchemaRefV1 Schema { get; }
    public uint RegistryGeneration { get; }
    public IReadOnlyList<DomainPartitionRegistrationV1> Entries => _entries;

    public DomainPartitionRegistrationV1 GetRequired(StableToken partitionId)
        => _byId.TryGetValue(partitionId.Value, out var entry)
            ? entry
            : throw new KeyNotFoundException($"Unknown PartitionId '{partitionId.Value}'.");
}

public static class StandardDomainPartitionRegistry
{
    public const int StandardPartitionCount = 97;

    private static readonly (string Domain, ushort Rank, string[] Partitions)[] StandardDomains =
    [
        ("spatial", 10,
        [
            "spatial.world_frame",
            "spatial.scope_registry",
            "spatial.terrain_geometry",
            "spatial.void_geometry",
            "spatial.containment_topology",
            "spatial.boundary_topology",
            "spatial.detail_regions",
            "spatial.geometry_lineage",
        ]),
        ("environment", 20,
        [
            "environment.geology",
            "environment.soil",
            "environment.resource_deposit",
            "environment.groundwater",
            "environment.atmosphere",
            "environment.climate",
            "environment.weather",
            "environment.surface_water",
            "environment.ocean",
            "environment.ecosystem",
            "environment.contaminant",
            "environment.hazard",
            "environment.environment_lineage",
        ]),
        ("physical_built", 30,
        [
            "physical.presence",
            "physical.occupancy",
            "built.structure",
            "built.space",
            "built.opening",
            "physical.container_location",
            "built.worksite",
            "physical.condition",
            "physical.combustion",
            "physical.material_handoff",
            "physical.lineage",
        ]),
        ("participation", 40,
        [
            "participation.binding",
            "participation.absence_policy",
            "participation.control_mode",
            "participation.history",
            "participation.detail_requirement",
        ]),
        ("resident", 50,
        [
            "resident.identity_lifecycle",
            "resident.body_health",
            "resident.physiology",
            "resident.perception",
            "resident.knowledge_belief",
            "resident.memory",
            "resident.psychology",
            "resident.goal_plan",
            "resident.skill_aptitude",
            "resident.relationship",
            "resident.family_lineage",
            "resident.behavior_state",
            "resident.lineage",
        ]),
        ("society_economy", 60,
        [
            "society.organization",
            "society.membership_role",
            "society.employment",
            "society.household",
            "society.contract_claim",
            "society.property_right",
            "society.currency_money",
            "society.finance_account",
            "society.market_transaction",
            "society.business_production",
            "society.logistics_obligation",
            "society.education",
            "society.culture",
            "society.reputation",
            "society.information_claim",
            "society.history_lineage",
        ]),
        ("governance_security", 70,
        [
            "governance.polity",
            "governance.institution",
            "governance.law_rule",
            "governance.jurisdiction",
            "governance.territorial_claim",
            "governance.effective_control",
            "governance.public_authority",
            "governance.tax_fiscal",
            "governance.permission_license",
            "governance.diplomacy",
            "governance.security_incident",
            "governance.investigation",
            "governance.judicial_case",
            "governance.enforcement",
            "governance.military_authority",
            "governance.border_control",
            "governance.lineage",
        ]),
        ("infrastructure_information", 80,
        [
            "infrastructure.network_topology",
            "infrastructure.transport_service",
            "infrastructure.water_service",
            "infrastructure.power_service",
            "infrastructure.communication_service",
            "infrastructure.dependency",
            "infrastructure.facility_service",
            "infrastructure.service_queue",
            "information.delivery",
            "information.media_distribution",
            "information.record_store",
            "information.address_place_index",
            "infrastructure.failure_recovery",
            "infrastructure.lineage",
        ]),
    ];

    public static DomainPartitionRegistryV1 Create(uint registryGeneration = 1)
    {
        var entries = new List<DomainPartitionRegistrationV1>(StandardPartitionCount);
        foreach (var (domain, _, partitionIds) in StandardDomains)
        {
            var owner = new StableToken(domain);
            foreach (var rawPartitionId in partitionIds)
            {
                var partitionId = new StableToken(rawPartitionId);
                entries.Add(new DomainPartitionRegistrationV1(
                    partitionId,
                    owner,
                    Schema($"domain.{rawPartitionId}"),
                    Schema($"domain.{rawPartitionId}.record"),
                    PrimaryKeyKindV1.RecordId128,
                    PersistenceClassV1.AuthoritativeAlways,
                    CanonicalOrderKindV1.RecordIdBytewiseAscending,
                    Array.Empty<StableToken>(),
                    Array.Empty<StableToken>()));
            }
        }

        if (entries.Count != StandardPartitionCount)
            throw new InvalidOperationException("state.partition-registry.standard-count-mismatch");
        return new DomainPartitionRegistryV1(registryGeneration, entries);
    }

    public static IReadOnlyList<DomainFamilyDescriptorV1> CreateDomainFamilies(DomainPartitionRegistryV1 registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var families = new List<DomainFamilyDescriptorV1>(StandardDomains.Length);
        foreach (var (domain, rank, partitionIds) in StandardDomains)
        {
            var owned = partitionIds
                .Select(id => registry.GetRequired(new StableToken(id)))
                .OrderBy(static entry => entry.PartitionId.Value, StringComparer.Ordinal)
                .ToArray();
            families.Add(new DomainFamilyDescriptorV1(new StableToken(domain), rank, Array.AsReadOnly(owned)));
        }
        return Array.AsReadOnly(families.OrderBy(static family => family.DomainRank).ToArray());
    }

    public static void ValidateStandardProfile(DomainPartitionRegistryV1 registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        if (registry.Entries.Count != StandardPartitionCount)
            throw new InvalidDataException("state.partition-registry.standard-count-mismatch");

        string? previous = null;
        foreach (var entry in registry.Entries)
        {
            if (previous is not null && string.CompareOrdinal(previous, entry.PartitionId.Value) >= 0)
                throw new InvalidDataException("state.partition-registry.noncanonical-order");
            previous = entry.PartitionId.Value;

            var expectedPartitionSchema = $"domain.{entry.PartitionId.Value}";
            var expectedRecordSchema = expectedPartitionSchema + ".record";
            if (!string.Equals(entry.PartitionSchema.SchemaId.Value, expectedPartitionSchema, StringComparison.Ordinal) ||
                !string.Equals(entry.RecordSchema.SchemaId.Value, expectedRecordSchema, StringComparison.Ordinal) ||
                entry.PartitionSchema.Version != new SchemaVersionV1(1, 0) ||
                entry.RecordSchema.Version != new SchemaVersionV1(1, 0))
                throw new InvalidDataException("state.partition-registry.schema-identity-mismatch");
            if (entry.PersistenceClass != PersistenceClassV1.AuthoritativeAlways ||
                entry.PrimaryKeyKind != PrimaryKeyKindV1.RecordId128 ||
                entry.CanonicalOrder != CanonicalOrderKindV1.RecordIdBytewiseAscending)
                throw new InvalidDataException("state.partition-registry.standard-contract-mismatch");
        }

        var families = CreateDomainFamilies(registry);
        var expectedRanks = new ushort[] { 10, 20, 30, 40, 50, 60, 70, 80 };
        if (!families.Select(static family => family.DomainRank).SequenceEqual(expectedRanks))
            throw new InvalidDataException("state.partition-registry.domain-rank-mismatch");
        if (families.Sum(static family => family.OwnedPartitions.Count) != StandardPartitionCount)
            throw new InvalidDataException("state.partition-registry.owner-coverage-mismatch");
    }

    private static SchemaRefV1 Schema(string id)
        => new(new StableToken(id), new SchemaVersionV1(1, 0));
}
