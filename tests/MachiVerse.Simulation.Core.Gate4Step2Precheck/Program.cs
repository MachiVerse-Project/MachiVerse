using System.Diagnostics;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

const ulong burstStep = Qa04ReferenceLoadV1.BurstEverySteps;
const string infrastructureFamily = "infrastructure-service-delivery";

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidDataException(message);
}

var total = Stopwatch.StartNew();
Console.WriteLine("GATE4_STEP2_QUICK_PRECHECK phase=contract-validation");
Qa04ReferenceLoadV1.ValidateCanonicalContract();
Qa04CanonicalOperationBindingV1.ValidateCanonicalContract();
Qa04FacilityServiceCanonicalAuthorityV1.ValidateCanonicalContract();

var materialization = Stopwatch.StartNew();
Console.WriteLine("GATE4_STEP2_QUICK_PRECHECK phase=targeted-authority-materialization");
var serviceAuthority = Qa04InfrastructureServiceQueueCanonicalMaterializerV1.MaterializeCanonical();
var physicalAuthority = Qa04PhysicalD0PropertyAssetSupportCanonicalAuthorityV1.MaterializeCanonical();
var facilityAuthority = Qa04FacilityServiceCanonicalAuthorityV1.MaterializeCanonical(
    physicalAuthority,
    serviceAuthority);
materialization.Stop();

var resolver = new CompositeResolver(serviceAuthority.References, facilityAuthority.References);
Require(
    facilityAuthority.CanonicalServicePool.Count == checked((int)Qa04InfrastructureCanonicalServicePoolV1.CanonicalCount),
    "gate4.step2.precheck.canonical-service-pool-count-drift");

foreach (var serviceRef in facilityAuthority.CanonicalServicePool)
{
    Require(resolver.TryGetRecordSchema(serviceRef, out var actualSchema),
        $"gate4.step2.precheck.service-ref-missing:{serviceRef.PartitionId.Value}");
    var expectedSchema = StandardDomainPartitionRegistry.Get(serviceRef.PartitionId.Value).RecordSchema;
    Require(actualSchema == expectedSchema,
        $"gate4.step2.precheck.service-ref-schema-drift:{serviceRef.PartitionId.Value}");
}

Console.WriteLine(
    $"GATE4_STEP2_QUICK_PRECHECK phase=burst-binding-validation materialization_ms={materialization.Elapsed.TotalMilliseconds:F1}");

var infrastructureFamilyIndex = Qa04ReferenceLoadV1.OperationFamilies
    .Select((family, index) => (family, index))
    .Single(item => item.family.FamilyToken.Value == infrastructureFamily);
var expectedSteady = checked(
    Qa04ReferenceLoadV1.SteadyOperationsPerStep *
    infrastructureFamilyIndex.family.SharePermille / 1_000UL);
var familyCount = checked((ulong)Qa04ReferenceLoadV1.OperationFamilies.Count);
var expectedBurst = checked(
    Qa04ReferenceLoadV1.BurstOperations / familyCount +
    ((ulong)infrastructureFamilyIndex.index < Qa04ReferenceLoadV1.BurstOperations % familyCount ? 1UL : 0UL));
var expectedInfrastructureOperations = checked(expectedSteady + expectedBurst);

var descriptors = Qa04ReferenceLoadV1.OperationsForStep(burstStep)
    .Where(descriptor => descriptor.FamilyToken.Value == infrastructureFamily)
    .ToArray();
Require(
    checked((ulong)descriptors.Length) == expectedInfrastructureOperations,
    "gate4.step2.precheck.burst-infrastructure-operation-count-drift");

var validator = new StandardDomainPayloadCodecValidatorV1();
var residentClass = new StableToken("resident.persistent-identity");
var facilityTargetCount = 0UL;
var validatedCount = 0UL;

foreach (var descriptor in descriptors)
{
    var binding = Qa04CanonicalOperationBindingV1.Bind(descriptor, schedulingPolicyGeneration: 1);
    var expectedServiceRef = Qa04InfrastructureCanonicalServicePoolV1.Resolve(descriptor.FamilyOrdinal);
    Require(binding.PrimaryTarget == expectedServiceRef,
        "gate4.step2.precheck.bound-service-ref-drift");
    Require(resolver.TryGetRecordSchema(expectedServiceRef, out var serviceSchema),
        $"gate4.step2.precheck.bound-service-ref-missing:{expectedServiceRef.PartitionId.Value}");
    Require(
        serviceSchema == StandardDomainPartitionRegistry.Get(expectedServiceRef.PartitionId.Value).RecordSchema,
        $"gate4.step2.precheck.bound-service-schema-drift:{expectedServiceRef.PartitionId.Value}");

    var requester = Qa04ReferenceLoadV1.Record(residentClass, descriptor.FamilyOrdinal);
    var requesterRef = new PartitionRecordRefV1(
        ResidentIdentityLifecyclePayloadV1.PartitionId,
        requester.RecordId);
    var payload = new InfrastructureServiceQueuePayloadV1(
        expectedServiceRef,
        requesterRef,
        EligibleStep: checked(descriptor.InjectionStep + 1UL),
        SemanticPriority: 0,
        RequestedUnits: checked(1UL + descriptor.FamilyOrdinal % 100UL),
        AllocatedUnits: 0,
        Status: new StableToken("queued"));
    validator.Validate(
        InfrastructureServiceQueuePayloadV1.PartitionId,
        payload.ToStandardPayload(),
        resolver);

    if (expectedServiceRef.PartitionId.Value == InfrastructureFacilityServicePayloadV1.PartitionId)
        facilityTargetCount++;
    validatedCount++;
}

Require(facilityTargetCount > 0, "gate4.step2.precheck.burst-does-not-cover-facility-service");
Require(validatedCount == expectedInfrastructureOperations, "gate4.step2.precheck.validation-count-drift");

Console.WriteLine(
    $"GATE4_STEP2_QUICK_PRECHECK PASS burst_step={burstStep} " +
    $"infrastructure_operations={validatedCount} facility_targets={facilityTargetCount} " +
    $"service_pool={facilityAuthority.CanonicalServicePool.Count} " +
    $"materialization_ms={materialization.Elapsed.TotalMilliseconds:F1} total_ms={total.Elapsed.TotalMilliseconds:F1}");

file sealed class CompositeResolver : IDomainRecordSchemaResolverV1
{
    private readonly IReadOnlyList<IDomainRecordSchemaResolverV1> _resolvers;

    public CompositeResolver(params IDomainRecordSchemaResolverV1[] resolvers)
    {
        if (resolvers.Length == 0 || resolvers.Any(static resolver => resolver is null))
            throw new ArgumentException("At least one resolver is required.", nameof(resolvers));
        _resolvers = Array.AsReadOnly(resolvers.ToArray());
    }

    public bool Exists(PartitionRecordRefV1 reference)
        => _resolvers.Any(resolver => resolver.Exists(reference));

    public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
    {
        foreach (var resolver in _resolvers)
        {
            if (resolver.TryGetRecordSchema(reference, out schema))
                return true;
        }

        schema = default!;
        return false;
    }
}
