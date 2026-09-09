using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.Performance;

public enum Qa04ReferenceMaterialBindingStateV1 : byte
{
    ProductionMaterializerAvailable = 1,
    BlockedByNormativeAuthority = 2,
    BlockedByNormativeMapping = 3,
    BlockedByNormativePayload = 4,
}

public sealed record Qa04ReferenceMaterialBindingV1(
    StableToken ClassToken,
    ulong CanonicalCount,
    Qa04ReferenceMaterialBindingStateV1 State,
    StableToken? PrimaryPartitionId,
    StableToken? BlockingFailureCode)
{
    public bool ProductionMaterializerAvailable
        => State == Qa04ReferenceMaterialBindingStateV1.ProductionMaterializerAvailable;
}

/// <summary>
/// Machine-readable audit of perf.reference.v1 initial-world classes against the actual production
/// authority model. This registry describes whether a production materializer can be implemented
/// without inventing a P4-05 partition mapping, reference target, payload schema, or persistent
/// authority. It is intentionally fail-closed and is not itself evidence that a world instance has
/// been materialized.
/// </summary>
public static class Qa04ReferenceWorldMaterialContractV1
{
    private static readonly IReadOnlyList<Qa04ReferenceMaterialBindingV1> BindingsValue = Array.AsReadOnly(new[]
    {
        Available(
            "resident.persistent-identity",
            Qa04ReferenceWorldMaterializerV1.CanonicalResidentCount,
            "resident.identity_lifecycle"),

        BlockedAuthority(
            "physical.d0-presence",
            500_000,
            "physical.presence",
            "qa04.material.physical-presence-shape-authority-undefined"),

        BlockedMapping(
            "environment.d0-cell-cohort",
            1_000_000,
            "qa04.material.environment-d0-partition-mapping-undefined"),

        BlockedMapping(
            "environment.d1-aggregate",
            250_000,
            "qa04.material.environment-d1-partition-mapping-undefined"),

        BlockedMapping(
            "society-governance.active-record",
            2_000_000,
            "qa04.material.society-governance-partition-mapping-undefined"),

        BlockedAuthority(
            "infrastructure.active-record",
            500_000,
            null,
            "qa04.material.infrastructure-node-edge-authority-undefined"),

        BlockedAuthority(
            "spatial.hot-terrain-brick",
            500_000,
            "spatial.terrain_geometry",
            "qa04.material.terrain-brick-authority-undefined"),

        BlockedAuthority(
            "transaction.active-cross-domain",
            Qa04ReferenceScenariosV1.ActiveCrossDomainTransactionTarget,
            null,
            "qa04.material.cross-domain-transaction-authority-undefined"),
    });

    public static IReadOnlyList<Qa04ReferenceMaterialBindingV1> Bindings => BindingsValue;

    public static bool AllProductionMaterializersAvailable
        => BindingsValue.All(static binding => binding.ProductionMaterializerAvailable);

    public static IReadOnlyList<StableToken> BlockingFailureCodes
        => BindingsValue
            .Where(static binding => !binding.ProductionMaterializerAvailable)
            .Select(static binding => binding.BlockingFailureCode
                ?? throw new InvalidDataException("qa04.material.blocked-binding-missing-failure-code"))
            .ToArray();

    public static Qa04ReferenceMaterialBindingV1 Get(StableToken classToken)
        => BindingsValue.SingleOrDefault(binding => binding.ClassToken == classToken)
            ?? throw new KeyNotFoundException($"Unknown QA-04 material binding: {classToken.Value}");

    public static void ValidateCanonicalContract()
    {
        Qa04ReferenceLoadV1.ValidateCanonicalContract();
        Qa04ReferenceScenariosV1.ValidateCanonicalContract();

        if (BindingsValue.Count != Qa04ReferenceLoadV1.RecordClasses.Count)
            throw new InvalidDataException("qa04.material.binding-count-mismatch");
        if (BindingsValue.Select(static binding => binding.ClassToken).Distinct().Count() != BindingsValue.Count)
            throw new InvalidDataException("qa04.material.binding-class-duplicate");

        foreach (var referenceClass in Qa04ReferenceLoadV1.RecordClasses)
        {
            var binding = Get(referenceClass.ClassToken);
            if (binding.CanonicalCount != referenceClass.Count)
                throw new InvalidDataException("qa04.material.binding-count-drift");
            if (!Enum.IsDefined(binding.State))
                throw new InvalidDataException("qa04.material.binding-state-invalid");
            if (binding.ProductionMaterializerAvailable)
            {
                if (binding.PrimaryPartitionId is null || binding.BlockingFailureCode is not null)
                    throw new InvalidDataException("qa04.material.available-binding-invalid");
            }
            else if (binding.BlockingFailureCode is null)
            {
                throw new InvalidDataException("qa04.material.blocked-binding-missing-failure-code");
            }
        }

        var resident = Get(new StableToken("resident.persistent-identity"));
        if (!resident.ProductionMaterializerAvailable ||
            resident.PrimaryPartitionId?.Value != "resident.identity_lifecycle")
            throw new InvalidDataException("qa04.material.resident-binding-drift");

        if (AllProductionMaterializersAvailable)
            throw new InvalidDataException("qa04.material.contract-unexpectedly-complete");
    }

    public static void RequireAllProductionMaterializersAvailable()
    {
        ValidateCanonicalContract();
        var firstBlocked = BindingsValue.FirstOrDefault(static binding => !binding.ProductionMaterializerAvailable);
        if (firstBlocked is not null)
            throw new InvalidDataException(firstBlocked.BlockingFailureCode!.Value.Value);
    }

    private static Qa04ReferenceMaterialBindingV1 Available(
        string classToken,
        ulong count,
        string partitionId)
        => new(
            new StableToken(classToken),
            count,
            Qa04ReferenceMaterialBindingStateV1.ProductionMaterializerAvailable,
            new StableToken(partitionId),
            null);

    private static Qa04ReferenceMaterialBindingV1 BlockedAuthority(
        string classToken,
        ulong count,
        string? partitionId,
        string failureCode)
        => new(
            new StableToken(classToken),
            count,
            Qa04ReferenceMaterialBindingStateV1.BlockedByNormativeAuthority,
            partitionId is null ? null : new StableToken(partitionId),
            new StableToken(failureCode));

    private static Qa04ReferenceMaterialBindingV1 BlockedMapping(
        string classToken,
        ulong count,
        string failureCode)
        => new(
            new StableToken(classToken),
            count,
            Qa04ReferenceMaterialBindingStateV1.BlockedByNormativeMapping,
            null,
            new StableToken(failureCode));
}