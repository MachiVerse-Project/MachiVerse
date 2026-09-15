using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public enum Qa04ReferenceMaterialBindingStateV1 : byte
{
    ProductionMaterializerAvailable = 1,
    BlockedByAuthorityTarget = 2,
    BlockedByPartitionMapping = 3,
    BlockedByNestedPayloadSchema = 4,
    BlockedByPersistentAuthority = 5,
    BlockedByRecordSchema = 6,
    BlockedByCanonicalMaterial = 7,
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
/// authority model. A null PrimaryPartitionId is valid for an available class whose canonical
/// material spans multiple partitions or is owned by a non-Domain authority.
/// </summary>
public static class Qa04ReferenceWorldMaterialContractV1
{
    private static readonly IReadOnlyList<Qa04ReferenceMaterialBindingV1> BindingsValue = Array.AsReadOnly(new[]
    {
        Available(
            "resident.persistent-identity",
            Qa04ReferenceWorldMaterializerV1.CanonicalResidentCount,
            "resident.identity_lifecycle"),

        Available(
            "participation.control_mode",
            Qa04ParticipationControlModeCanonicalAuthorityV1.CanonicalCount,
            "participation.control_mode"),

        Available(
            "physical.d0-presence",
            Qa04PhysicalD0MaterializerV1.CanonicalPhysicalCount,
            "physical.presence"),

        Available(
            "environment.d0-cell-cohort",
            1_000_000,
            null),

        Available(
            "environment.d1-aggregate",
            250_000,
            null),

        Available(
            "society-governance.active-record",
            2_000_000,
            null),

        Available(
            "infrastructure.active-record",
            Qa04InfrastructureReferenceDecompositionV1.CanonicalCount,
            null),

        Available(
            "spatial.hot-terrain-brick",
            500_000,
            "spatial.terrain_geometry"),

        Available(
            "transaction.active-cross-domain",
            Qa04ReferenceScenariosV1.ActiveCrossDomainTransactionTarget,
            null),
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
        Qa04ReferenceWorldDependencyContractV1.ValidateCanonicalContract();
        Qa04ParticipationControlModeCanonicalAuthorityV1.ValidateCanonicalContract();
        Qa04InfrastructureReferenceDecompositionV1.ValidateCanonicalContract();

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
                if (binding.BlockingFailureCode is not null)
                    throw new InvalidDataException("qa04.material.available-binding-invalid");
                if (binding.PrimaryPartitionId is { } partition)
                    _ = StandardDomainPartitionRegistry.Get(partition.Value);
            }
            else
            {
                ValidateBlockedBinding(binding);
            }
        }

        var blockedCodes = BlockingFailureCodes;
        if (blockedCodes.Distinct().Count() != blockedCodes.Count)
            throw new InvalidDataException("qa04.material.blocked-binding-failure-code-duplicate");

        var resident = Get(new StableToken("resident.persistent-identity"));
        if (!resident.ProductionMaterializerAvailable ||
            resident.PrimaryPartitionId?.Value != "resident.identity_lifecycle")
            throw new InvalidDataException("qa04.material.resident-binding-drift");

        var participation = Get(new StableToken("participation.control_mode"));
        if (!participation.ProductionMaterializerAvailable ||
            participation.PrimaryPartitionId?.Value != "participation.control_mode" ||
            participation.CanonicalCount != Qa04ParticipationControlModeCanonicalAuthorityV1.CanonicalCount)
            throw new InvalidDataException("qa04.material.participation-control-mode-binding-drift");

        var physical = Get(new StableToken("physical.d0-presence"));
        if (!physical.ProductionMaterializerAvailable ||
            physical.PrimaryPartitionId?.Value != "physical.presence" ||
            physical.CanonicalCount != Qa04PhysicalD0MaterializerV1.CanonicalPhysicalCount)
            throw new InvalidDataException("qa04.material.physical-binding-drift");

        var societyGovernance = Get(new StableToken("society-governance.active-record"));
        if (!societyGovernance.ProductionMaterializerAvailable ||
            societyGovernance.PrimaryPartitionId is not null ||
            societyGovernance.CanonicalCount != 2_000_000)
            throw new InvalidDataException("qa04.material.society-governance-binding-drift");

        var infrastructure = Get(new StableToken("infrastructure.active-record"));
        if (!infrastructure.ProductionMaterializerAvailable ||
            infrastructure.PrimaryPartitionId is not null ||
            infrastructure.CanonicalCount != Qa04InfrastructureReferenceDecompositionV1.CanonicalCount)
            throw new InvalidDataException("qa04.material.infrastructure-binding-drift");

        var terrain = Get(new StableToken("spatial.hot-terrain-brick"));
        if (!terrain.ProductionMaterializerAvailable ||
            terrain.PrimaryPartitionId?.Value != "spatial.terrain_geometry" ||
            terrain.CanonicalCount != Qa04TerrainBrickDescriptorMaterializerV1.CanonicalTerrainBrickCount)
            throw new InvalidDataException("qa04.material.terrain-binding-drift");

        if (!Get(new StableToken("environment.d0-cell-cohort")).ProductionMaterializerAvailable ||
            !Get(new StableToken("environment.d1-aggregate")).ProductionMaterializerAvailable ||
            !Get(new StableToken("transaction.active-cross-domain")).ProductionMaterializerAvailable)
            throw new InvalidDataException("qa04.material.implemented-binding-regressed");

        if (!AllProductionMaterializersAvailable || BlockingFailureCodes.Count != 0)
            throw new InvalidDataException("qa04.material.contract-not-complete");
    }

    public static void RequireAllProductionMaterializersAvailable()
    {
        ValidateCanonicalContract();
        var firstBlocked = BindingsValue.FirstOrDefault(static binding => !binding.ProductionMaterializerAvailable);
        if (firstBlocked is not null)
            throw new InvalidDataException(firstBlocked.BlockingFailureCode!.Value.Value);
    }

    private static void ValidateBlockedBinding(Qa04ReferenceMaterialBindingV1 binding)
    {
        var failureCode = binding.BlockingFailureCode
            ?? throw new InvalidDataException("qa04.material.blocked-binding-missing-failure-code");
        var dependency = Qa04ReferenceWorldDependencyContractV1.Blockers
            .SingleOrDefault(blocker => blocker.FailureCode == failureCode)
            ?? throw new InvalidDataException($"qa04.material.blocked-binding-dependency-missing:{binding.ClassToken.Value}");
        var expectedState = dependency.Kind switch
        {
            Qa04ReferenceDependencyBlockerKindV1.AuthorityTarget
                => Qa04ReferenceMaterialBindingStateV1.BlockedByAuthorityTarget,
            Qa04ReferenceDependencyBlockerKindV1.PartitionMapping
                => Qa04ReferenceMaterialBindingStateV1.BlockedByPartitionMapping,
            Qa04ReferenceDependencyBlockerKindV1.PersistentAuthority
                => Qa04ReferenceMaterialBindingStateV1.BlockedByPersistentAuthority,
            Qa04ReferenceDependencyBlockerKindV1.NestedPayloadSchema
                => Qa04ReferenceMaterialBindingStateV1.BlockedByNestedPayloadSchema,
            Qa04ReferenceDependencyBlockerKindV1.RecordSchema
                => Qa04ReferenceMaterialBindingStateV1.BlockedByRecordSchema,
            Qa04ReferenceDependencyBlockerKindV1.CanonicalMaterial
                => Qa04ReferenceMaterialBindingStateV1.BlockedByCanonicalMaterial,
            _ => throw new InvalidDataException("qa04.material.blocked-binding-dependency-kind-invalid"),
        };
        if (binding.State != expectedState)
            throw new InvalidDataException($"qa04.material.blocked-binding-kind-drift:{binding.ClassToken.Value}");
    }

    private static Qa04ReferenceMaterialBindingV1 Available(
        string classToken,
        ulong count,
        string? partitionId)
        => new(
            new StableToken(classToken),
            count,
            Qa04ReferenceMaterialBindingStateV1.ProductionMaterializerAvailable,
            partitionId is null ? null : new StableToken(partitionId),
            null);

    private static Qa04ReferenceMaterialBindingV1 Blocked(
        string classToken,
        ulong count,
        string? partitionId,
        Qa04ReferenceMaterialBindingStateV1 state,
        string failureCode)
    {
        if (state == Qa04ReferenceMaterialBindingStateV1.ProductionMaterializerAvailable)
            throw new ArgumentException("Blocked binding cannot use the available state.", nameof(state));
        return new Qa04ReferenceMaterialBindingV1(
            new StableToken(classToken),
            count,
            state,
            partitionId is null ? null : new StableToken(partitionId),
            new StableToken(failureCode));
    }
}
