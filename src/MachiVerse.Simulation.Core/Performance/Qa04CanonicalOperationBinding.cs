using Google.Protobuf;
using MachiVerse.Protocol.V1;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04CanonicalOperationBindingResultV1(
    Qa04OperationDescriptorV1 SourceDescriptor,
    Qa04OperationDescriptorV1 BoundDescriptor,
    StandardOperationV1 Operation,
    StableToken OwnerDomain,
    PartitionRecordRefV1 PrimaryTarget,
    SameStepOrderKey OrderKey,
    ScheduledOperationRefV1 ScheduledOperation);

/// <summary>
/// Binds the canonical perf.reference.v1 Operation descriptor families whose target authorities are
/// already materialized to the ordinary StandardOperationV1 / scheduling identity surface.
///
/// Infrastructure remains fail-closed until the canonical service pool exists. Governance remains
/// fail-closed until the canonical InformationClaim authority required by incident registration
/// exists. Those two unresolved families keep the parent workload blocker active.
/// </summary>
public static class Qa04CanonicalOperationBindingV1
{
    public const uint PayloadSchemaMajor = 1;
    public const uint PayloadSchemaMinor = 0;

    private const string OperationDigestDomain = "mv.operation-payload.v1";
    private const string ConflictScopeDomain = "mv.perf-reference-operation-scope.v1";

    private const string ResidentFamily = "participation-control-resident-action";
    private const string PhysicalFamily = "physical-item-movement-work";
    private const string MarketFamily = "society-market-payment-contract";
    private const string InfrastructureFamily = "infrastructure-service-delivery";
    private const string GovernanceFamily = "governance-security";
    private const string EnvironmentFamily = "environment-spatial-admin-synthetic";

    private static readonly StableToken ResidentClass = new("resident.persistent-identity");
    private static readonly StableToken PhysicalClass = new("physical.d0-presence");
    private static readonly StableToken ResidentDomain = new("resident");
    private static readonly StableToken PhysicalDomain = new("physical_built");
    private static readonly StableToken SocietyDomain = new("society_economy");
    private static readonly StableToken EnvironmentDomain = new("environment");
    private static readonly StableToken Buy = new("buy");
    private static readonly StableToken Sell = new("sell");
    private static readonly StableToken SyntheticHazard = new("perf.synthetic-hazard");

    private static readonly IReadOnlyDictionary<StableToken, ushort> DomainRankByToken =
        StandardDomainExecutionPlanV1.Create().Entries.ToDictionary(
            static entry => entry.DomainToken,
            static entry => entry.DomainRank);

    public static IReadOnlyList<StableToken> BoundFamilies { get; } = Array.AsReadOnly(new[]
    {
        new StableToken(ResidentFamily),
        new StableToken(PhysicalFamily),
        new StableToken(MarketFamily),
        new StableToken(EnvironmentFamily),
    });

    public static IReadOnlyList<StableToken> PendingAuthorityFamilies { get; } = Array.AsReadOnly(new[]
    {
        new StableToken(InfrastructureFamily),
        new StableToken(GovernanceFamily),
    });

    public static void ValidateCanonicalContract()
    {
        Qa04ReferenceLoadV1.ValidateCanonicalContract();
        Qa04ReferenceScenariosV1.ValidateCanonicalContract();

        var canonicalFamilies = Qa04ReferenceLoadV1.OperationFamilies
            .Select(static family => family.FamilyToken)
            .ToHashSet();
        if (!BoundFamilies.Concat(PendingAuthorityFamilies).ToHashSet().SetEquals(canonicalFamilies))
            throw new InvalidDataException("qa04.workload.operation-binding-family-coverage-drift");
        if (BoundFamilies.Intersect(PendingAuthorityFamilies).Any())
            throw new InvalidDataException("qa04.workload.operation-binding-family-overlap");

        foreach (var domain in new[] { ResidentDomain, PhysicalDomain, SocietyDomain, EnvironmentDomain })
        {
            if (!DomainRankByToken.ContainsKey(domain))
                throw new InvalidDataException($"qa04.workload.operation-domain-rank-missing:{domain.Value}");
        }
    }

    public static Qa04CanonicalOperationBindingResultV1 Bind(
        Qa04OperationDescriptorV1 descriptor,
        ulong schedulingPolicyGeneration)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (schedulingPolicyGeneration == 0)
            throw new ArgumentOutOfRangeException(nameof(schedulingPolicyGeneration));
        if (descriptor.OperationId.IsZero || descriptor.PayloadDigest.Length != 32)
            throw new InvalidDataException("qa04.workload.operation-descriptor-invalid");

        return descriptor.FamilyToken.Value switch
        {
            ResidentFamily => BindResident(descriptor, schedulingPolicyGeneration),
            PhysicalFamily => BindPhysical(descriptor, schedulingPolicyGeneration),
            MarketFamily => BindMarket(descriptor, schedulingPolicyGeneration),
            EnvironmentFamily => BindEnvironment(descriptor, schedulingPolicyGeneration),
            InfrastructureFamily => throw new InvalidDataException(
                "qa04.workload.operation-authority-binding-undefined:infrastructure-service-pool"),
            GovernanceFamily => throw new InvalidDataException(
                "qa04.workload.operation-authority-binding-undefined:governance-information-claim-authority"),
            _ => throw new InvalidDataException(
                $"qa04.workload.operation-family-unregistered:{descriptor.FamilyToken.Value}"),
        };
    }

    public static byte[] ComputeImmutablePayloadDigest(StandardOperationV1 operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (operation.Admission is null)
            throw new InvalidDataException("operation.scheduling-admission-required");
        if (operation.OperationPayloadSchemaVersion is null ||
            operation.OperationPayloadSchemaVersion.Major != PayloadSchemaMajor ||
            operation.OperationPayloadSchemaVersion.Minor != PayloadSchemaMinor)
            throw new InvalidDataException("qa04.workload.operation-payload-schema-version-invalid");
        if (operation.Admission.SchedulingPolicyGeneration == 0)
            throw new InvalidDataException("operation.scheduling-policy-generation-invalid");

        _ = new StableToken(operation.OperationKind);
        _ = new StableToken(operation.OperationPayloadSchemaId);
        var payload = operation.OperationPayload.ToByteArray();
        if (payload.Length == 0)
            throw new InvalidDataException("qa04.workload.operation-payload-empty");

        return HashSuite.DomainHash(OperationDigestDomain, writer =>
        {
            writer.WriteMapStart(5);
            writer.WriteUnsigned(0); writer.WriteAsciiText(operation.OperationKind);
            writer.WriteUnsigned(1); WriteAdmission(writer, operation.Admission);
            writer.WriteUnsigned(2); writer.WriteAsciiText(operation.OperationPayloadSchemaId);
            writer.WriteUnsigned(3);
            writer.WriteArrayStart(2);
            writer.WriteUnsigned(operation.OperationPayloadSchemaVersion.Major);
            writer.WriteUnsigned(operation.OperationPayloadSchemaVersion.Minor);
            writer.WriteUnsigned(4); writer.WriteCanonicalValue(payload);
        });
    }

    private static Qa04CanonicalOperationBindingResultV1 BindResident(
        Qa04OperationDescriptorV1 descriptor,
        ulong schedulingPolicyGeneration)
    {
        var resident = Qa04ReferenceLoadV1.Record(ResidentClass, descriptor.FamilyOrdinal);
        var residentRef = new PartitionRecordRefV1(ResidentIdentityLifecyclePayloadV1.PartitionId, resident.RecordId);
        var action = Qa04ReferenceLoadV1.ResidentActivity(resident.RecordId, descriptor.InjectionStep);

        var payloadWriter = new MvDcborWriter();
        payloadWriter.WriteArrayStart(4);
        WriteRecordRef(payloadWriter, residentRef);
        payloadWriter.WriteAsciiText(action.Value);
        payloadWriter.WriteArrayStart(0);
        payloadWriter.WriteMapStart(1);
        payloadWriter.WriteAsciiText("perf.ordinal");
        payloadWriter.WriteUnsigned(descriptor.FamilyOrdinal);

        return BindResolved(
            descriptor,
            schedulingPolicyGeneration,
            operationKind: "resident.action.request",
            ResidentDomain,
            residentRef,
            payloadWriter.ToArray());
    }

    private static Qa04CanonicalOperationBindingResultV1 BindPhysical(
        Qa04OperationDescriptorV1 descriptor,
        ulong schedulingPolicyGeneration)
    {
        var physical = Qa04ReferenceLoadV1.Record(PhysicalClass, descriptor.FamilyOrdinal);
        var subjectRef = new PartitionRecordRefV1(PhysicalPresencePayloadV1.PartitionId, physical.RecordId);
        var velocityX = Qa04ReferenceGenesisValueSourceV1.SmallSignedValue(descriptor.OperationId, "vx");
        var velocityY = Qa04ReferenceGenesisValueSourceV1.SmallSignedValue(descriptor.OperationId, "vy");

        var payloadWriter = new MvDcborWriter();
        payloadWriter.WriteArrayStart(3);
        WriteRecordRef(payloadWriter, subjectRef);
        payloadWriter.WriteArrayStart(3);
        payloadWriter.WriteInt64(velocityX);
        payloadWriter.WriteInt64(velocityY);
        payloadWriter.WriteInt64(0);
        payloadWriter.WriteArrayStart(0); // target_ref = NONE

        return BindResolved(
            descriptor,
            schedulingPolicyGeneration,
            operationKind: "physical.move.request",
            PhysicalDomain,
            subjectRef,
            payloadWriter.ToArray());
    }

    private static Qa04CanonicalOperationBindingResultV1 BindMarket(
        Qa04OperationDescriptorV1 descriptor,
        ulong schedulingPolicyGeneration)
    {
        var scopeOrdinal = checked((int)(descriptor.FamilyOrdinal % Qa04ReferenceScenariosV1.MarketScopeCount));
        var marketRef = new PartitionRecordRefV1(
            SocietyMarketTransactionRecordSchemaV2.PartitionId,
            Qa04ReferenceScenariosV1.MarketScopeId(scopeOrdinal));
        var owner = Qa04ReferenceLoadV1.Record(ResidentClass, descriptor.FamilyOrdinal);
        var ownerRef = new PartitionRecordRefV1(ResidentIdentityLifecyclePayloadV1.PartitionId, owner.RecordId);
        var side = (descriptor.FamilyOrdinal & 1UL) == 0 ? Buy : Sell;
        var price = side == Buy
            ? checked(100_000L + (long)(descriptor.FamilyOrdinal % 1_000UL))
            : checked(99_500L + (long)(descriptor.FamilyOrdinal % 1_000UL));
        var quantity = checked(1L + (long)(descriptor.FamilyOrdinal % 20UL));

        var payloadWriter = new MvDcborWriter();
        payloadWriter.WriteArrayStart(5);
        WriteRecordRef(payloadWriter, marketRef);
        WriteRecordRef(payloadWriter, ownerRef);
        payloadWriter.WriteAsciiText(side.Value);
        payloadWriter.WriteInt64(price);
        payloadWriter.WriteInt64(quantity);

        return BindResolved(
            descriptor,
            schedulingPolicyGeneration,
            operationKind: "society.market.order-place",
            SocietyDomain,
            marketRef,
            payloadWriter.ToArray());
    }

    private static Qa04CanonicalOperationBindingResultV1 BindEnvironment(
        Qa04OperationDescriptorV1 descriptor,
        ulong schedulingPolicyGeneration)
    {
        var tile = checked((ushort)(descriptor.FamilyOrdinal % Qa04ReferenceLoadV1.RegionalTileCount));
        var scopeRef = Qa04SpatialTileScopeAuthorityV1.ScopeRef(tile);
        var intensityPpm = checked(100_000UL + descriptor.FamilyOrdinal % 800_001UL);

        var payloadWriter = new MvDcborWriter();
        payloadWriter.WriteArrayStart(4);
        payloadWriter.WriteAsciiText(SyntheticHazard.Value);
        WriteRecordRef(payloadWriter, scopeRef);
        payloadWriter.WriteUnsigned(intensityPpm);
        payloadWriter.WriteUnsigned(30);

        return BindResolved(
            descriptor,
            schedulingPolicyGeneration,
            operationKind: "environment.hazard.inject",
            EnvironmentDomain,
            scopeRef,
            payloadWriter.ToArray());
    }

    private static Qa04CanonicalOperationBindingResultV1 BindResolved(
        Qa04OperationDescriptorV1 descriptor,
        ulong schedulingPolicyGeneration,
        string operationKind,
        StableToken ownerDomain,
        PartitionRecordRefV1 primaryTarget,
        byte[] canonicalPayload)
    {
        if (canonicalPayload.Length == 0)
            throw new InvalidDataException("qa04.workload.operation-payload-empty");
        if (!DomainRankByToken.TryGetValue(ownerDomain, out var domainRank))
            throw new InvalidDataException($"qa04.workload.operation-domain-rank-missing:{ownerDomain.Value}");

        var admission = new OperationSchedulingAdmissionWireV1
        {
            AdmissionBasisStep = descriptor.InjectionStep,
            SchedulingPolicyGeneration = schedulingPolicyGeneration,
        };
        var schemaId = $"operation.{operationKind}";
        var operation = new StandardOperationV1
        {
            OperationId = ByteString.CopyFrom(descriptor.OperationId.ToBytes()),
            OperationKind = operationKind,
            Admission = admission,
            Candidate = new CandidateSchedulingWireV1
            {
                CandidateStep = checked(descriptor.InjectionStep + 1),
            },
            OperationPayloadSchemaId = schemaId,
            OperationPayloadSchemaVersion = new SchemaVersionWireV1
            {
                Major = PayloadSchemaMajor,
                Minor = PayloadSchemaMinor,
            },
            OperationPayload = ByteString.CopyFrom(canonicalPayload),
        };
        var digest = ComputeImmutablePayloadDigest(operation);
        operation.ImmutablePayloadDigest = ByteString.CopyFrom(digest);

        var conflictScope = HashSuite.DomainHash(ConflictScopeDomain, writer =>
        {
            writer.WriteArrayStart(2);
            writer.WriteAsciiText(operationKind);
            writer.WriteBytes(primaryTarget.RecordId.ToBytes());
        });
        var orderKey = new SameStepOrderKey(
            phase: 1,
            domainRank,
            conflictScope,
            semanticPriority: 0,
            descriptor.OperationId);
        var effectiveStep = checked(descriptor.InjectionStep + 1);
        var scheduled = new ScheduledOperationRefV1(descriptor.OperationId, effectiveStep, orderKey);
        scheduled.Validate();

        var boundDescriptor = descriptor with { PayloadDigest = digest.ToArray() };
        if (!boundDescriptor.PayloadDigest.AsSpan().SequenceEqual(operation.ImmutablePayloadDigest.Span))
            throw new InvalidDataException("qa04.workload.operation-bound-digest-mismatch");

        return new Qa04CanonicalOperationBindingResultV1(
            descriptor,
            boundDescriptor,
            operation,
            ownerDomain,
            primaryTarget,
            orderKey,
            scheduled);
    }

    private static void WriteAdmission(MvDcborWriter writer, OperationSchedulingAdmissionWireV1 admission)
    {
        writer.WriteMapStart(4);
        writer.WriteUnsigned(0); writer.WriteUnsigned(admission.AdmissionBasisStep);
        writer.WriteUnsigned(1); writer.WriteUnsigned(admission.SchedulingPolicyGeneration);
        writer.WriteUnsigned(2); WriteOptionalU64(writer, admission.HasRequestedNotBeforeStep, admission.RequestedNotBeforeStep);
        writer.WriteUnsigned(3); WriteOptionalU64(writer, admission.HasRequestedDeadlineStep, admission.RequestedDeadlineStep);
    }

    private static void WriteOptionalU64(MvDcborWriter writer, bool present, ulong value)
    {
        writer.WriteArrayStart(present ? 1UL : 0UL);
        if (present) writer.WriteUnsigned(value);
    }

    private static void WriteRecordRef(MvDcborWriter writer, PartitionRecordRefV1 reference)
    {
        if (reference.RecordId.IsZero)
            throw new InvalidDataException("qa04.workload.operation-target-ref-zero");
        writer.WriteArrayStart(2);
        writer.WriteAsciiText(reference.PartitionId.Value);
        writer.WriteBytes(reference.RecordId.ToBytes());
    }
}
