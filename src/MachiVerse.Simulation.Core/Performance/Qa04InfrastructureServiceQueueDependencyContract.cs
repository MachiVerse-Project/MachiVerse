using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public enum Qa04InfrastructureServiceQueueDependencyKindV1 : byte
{
    ServiceAuthority = 1,
    RequesterAuthorityMapping = 2,
    GenesisQueueState = 3,
}

public sealed record Qa04InfrastructureServiceQueueDependencyV1(
    StableToken DependencyId,
    Qa04InfrastructureServiceQueueDependencyKindV1 Kind,
    StableToken FailureCode);

/// <summary>
/// Exact fail-closed boundary for the canonical 250,000 infrastructure.service_queue records.
///
/// Alpha 1.1 fixes the queue record count and one-to-one specialized request identity helper, while
/// the production payload schema fixes the canonical queue key. The initial world does not define
/// which actual service records own each request, which actual requester pool/mapping is used, or
/// the canonical eligible_step / semantic_priority / requested_units / allocated_units / status
/// genesis state. Generic scalar hashing cannot invent those coupled queue semantics.
/// </summary>
public static class Qa04InfrastructureServiceQueueDependencyContractV1
{
    public const string PartitionId = "infrastructure.service_queue";
    public const ulong CanonicalStartOrdinal = Qa04InfrastructureReferenceDecompositionV1.ServiceQueueStartOrdinal;
    public const ulong CanonicalCount = Qa04InfrastructureReferenceDecompositionV1.ServiceQueueCount;

    private static readonly IReadOnlyList<Qa04InfrastructureServiceQueueDependencyV1> BlockersValue =
        Array.AsReadOnly(new[]
        {
            Blocker(
                "infrastructure.service-queue.service-authority",
                Qa04InfrastructureServiceQueueDependencyKindV1.ServiceAuthority,
                "qa04.material.service-queue-service-authority-undefined"),
            Blocker(
                "infrastructure.service-queue.requester-ref-mapping",
                Qa04InfrastructureServiceQueueDependencyKindV1.RequesterAuthorityMapping,
                "qa04.material.service-queue-requester-mapping-undefined"),
            Blocker(
                "infrastructure.service-queue.genesis-state",
                Qa04InfrastructureServiceQueueDependencyKindV1.GenesisQueueState,
                "qa04.material.service-queue-genesis-state-undefined"),
        }
        .OrderBy(static blocker => blocker.DependencyId.Value, StringComparer.Ordinal)
        .ToArray());

    public static IReadOnlyList<Qa04InfrastructureServiceQueueDependencyV1> Blockers => BlockersValue;

    public static void ValidateCanonicalContract()
    {
        Qa04InfrastructureReferenceDecompositionV1.ValidateCanonicalContract();
        Qa04ReferenceScenariosV1.ValidateCanonicalContract();

        var slice = Qa04InfrastructureReferenceDecompositionV1.Get("service_queue");
        if (slice.StartOrdinal != CanonicalStartOrdinal || slice.Count != CanonicalCount || !slice.UsesSpecializedIdentity)
            throw new InvalidDataException("qa04.infrastructure.service-queue-decomposition-drift");
        if (CanonicalCount != checked((ulong)Qa04ReferenceScenariosV1.InfrastructureQueuedServiceRequestCount))
            throw new InvalidDataException("qa04.infrastructure.service-queue-count-drift");

        var identity = StandardDomainPartitionRegistry.Get(PartitionId);
        if (identity.OwnerDomain.Value != "infrastructure_information")
            throw new InvalidDataException("qa04.infrastructure.service-queue-owner-drift");

        var schema = StandardDomainPayloadSchemaRegistry.Get(PartitionId);
        RequireField(schema, "service_ref", DomainPayloadFieldKindV1.Ref);
        RequireField(schema, "requester_ref", DomainPayloadFieldKindV1.Ref);
        RequireField(schema, "eligible_step", DomainPayloadFieldKindV1.Step);
        RequireField(schema, "semantic_priority", DomainPayloadFieldKindV1.Int32);
        RequireField(schema, "requested_units", DomainPayloadFieldKindV1.UInt64);
        RequireField(schema, "allocated_units", DomainPayloadFieldKindV1.UInt64);
        RequireField(schema, "status", DomainPayloadFieldKindV1.Token);

        var firstId = Qa04ReferenceScenariosV1.InfrastructureServiceRequestId(0);
        var lastId = Qa04ReferenceScenariosV1.InfrastructureServiceRequestId(
            checked(Qa04ReferenceScenariosV1.InfrastructureQueuedServiceRequestCount - 1));
        if (firstId.IsZero || lastId.IsZero || firstId == lastId)
            throw new InvalidDataException("qa04.infrastructure.service-queue-specialized-identity-drift");

        if (BlockersValue.Count != 3 ||
            BlockersValue.Select(static blocker => blocker.Kind).Distinct().Count() != BlockersValue.Count ||
            BlockersValue.Select(static blocker => blocker.DependencyId).Distinct().Count() != BlockersValue.Count ||
            BlockersValue.Select(static blocker => blocker.FailureCode).Distinct().Count() != BlockersValue.Count)
            throw new InvalidDataException("qa04.infrastructure.service-queue-dependency-drift");
    }

    private static Qa04InfrastructureServiceQueueDependencyV1 Blocker(
        string dependencyId,
        Qa04InfrastructureServiceQueueDependencyKindV1 kind,
        string failureCode)
        => new(new StableToken(dependencyId), kind, new StableToken(failureCode));

    private static void RequireField(
        DomainPayloadSchemaDescriptorV1 schema,
        string fieldName,
        DomainPayloadFieldKindV1 kind)
    {
        var field = schema.Fields.SingleOrDefault(candidate => string.Equals(candidate.Name, fieldName, StringComparison.Ordinal))
            ?? throw new InvalidDataException($"qa04.infrastructure.service-queue-field-missing:{fieldName}");
        if (field.Kind != kind || field.Optional)
            throw new InvalidDataException($"qa04.infrastructure.service-queue-field-drift:{fieldName}");
    }
}
