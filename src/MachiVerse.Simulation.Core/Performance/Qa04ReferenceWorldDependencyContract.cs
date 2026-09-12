using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public enum Qa04ReferenceDependencyBlockerKindV1 : byte
{
    AuthorityTarget = 1,
    PartitionMapping = 2,
    PersistentAuthority = 3,
    NestedPayloadSchema = 4,
    RecordSchema = 5,
    CanonicalMaterial = 6,
}

public sealed record Qa04ReferenceDependencyBlockerV1(
    StableToken DependencyId,
    Qa04ReferenceDependencyBlockerKindV1 Kind,
    StableToken? PartitionId,
    string? FieldName,
    StableToken FailureCode);

public static class Qa04ReferenceWorldDependencyContractV1
{
    private static readonly IReadOnlyList<Qa04ReferenceDependencyBlockerV1> BlockersValue = Array.AsReadOnly(new[]
    {
        PartitionMapping(
            "society-governance.active-record.partition-mapping",
            "qa04.material.society-governance-partition-mapping-undefined"),
        RecordSchema(
            "infrastructure.network-topology.node-edge-targets",
            "infrastructure.network_topology",
            "node_refs/edge_refs",
            "qa04.material.infrastructure-node-edge-authority-undefined"),
    }
    .OrderBy(static blocker => blocker.DependencyId.Value, StringComparer.Ordinal)
    .ToArray());

    public static IReadOnlyList<Qa04ReferenceDependencyBlockerV1> Blockers => BlockersValue;
    public static IReadOnlyList<StableToken> FailureCodes => BlockersValue.Select(static blocker => blocker.FailureCode).ToArray();

    public static void ValidateCanonicalContract()
    {
        Qa04SocietyOrganizationDependencyContractV1.ValidateCanonicalContract();
        Qa04SocietyContractClaimDependencyContractV1.ValidateCanonicalContract();
        Qa04GovernancePermissionLicenseDependencyContractV1.ValidateCanonicalContract();

        if (BlockersValue.Count != 2)
            throw new InvalidDataException("qa04.material.dependency-blocker-count-drift");
        if (BlockersValue.Select(static blocker => blocker.DependencyId).Distinct().Count() != BlockersValue.Count)
            throw new InvalidDataException("qa04.material.dependency-blocker-id-duplicate");
        if (BlockersValue.Select(static blocker => blocker.FailureCode).Distinct().Count() != BlockersValue.Count)
            throw new InvalidDataException("qa04.material.dependency-blocker-code-duplicate");
        if (Qa04SocietyOrganizationDependencyContractV1.Blockers.Count != 0 ||
            Qa04SocietyContractClaimDependencyContractV1.Blockers.Count != 0 ||
            Qa04GovernancePermissionLicenseDependencyContractV1.Blockers.Count != 0)
            throw new InvalidDataException("qa04.material.society-authority-progress-drift");

        string? previous = null;
        foreach (var blocker in BlockersValue)
        {
            if (!Enum.IsDefined(blocker.Kind)) throw new InvalidDataException("qa04.material.dependency-blocker-kind-invalid");
            if (previous is not null && string.CompareOrdinal(previous, blocker.DependencyId.Value) >= 0)
                throw new InvalidDataException("qa04.material.dependency-blocker-order");
            previous = blocker.DependencyId.Value;
            if (blocker.PartitionId is { } partition)
            {
                _ = StandardDomainPartitionRegistry.Get(partition.Value);
                if (string.IsNullOrWhiteSpace(blocker.FieldName)) throw new InvalidDataException("qa04.material.dependency-blocker-field-required");
            }
            else if (blocker.FieldName is not null) throw new InvalidDataException("qa04.material.dependency-blocker-field-without-partition");
        }

        var implemented = new[]
        {
            "qa04.material.environment-d0-partition-mapping-undefined",
            "qa04.material.environment-d1-partition-mapping-undefined",
            "qa04.material.cross-domain-transaction-authority-undefined",
            "qa04.material.terrain-brick-authority-undefined",
        };
        if (FailureCodes.Any(code => implemented.Contains(code.Value, StringComparer.Ordinal)))
            throw new InvalidDataException("qa04.material.implemented-world-blocker-retained");
    }

    private static Qa04ReferenceDependencyBlockerV1 PartitionScoped(string dependencyId, Qa04ReferenceDependencyBlockerKindV1 kind, string partitionId, string fieldName, string failureCode)
        => new(new StableToken(dependencyId), kind, new StableToken(partitionId), fieldName, new StableToken(failureCode));
    private static Qa04ReferenceDependencyBlockerV1 RecordSchema(string dependencyId, string partitionId, string fieldName, string failureCode)
        => PartitionScoped(dependencyId, Qa04ReferenceDependencyBlockerKindV1.RecordSchema, partitionId, fieldName, failureCode);
    private static Qa04ReferenceDependencyBlockerV1 PartitionMapping(string dependencyId, string failureCode)
        => new(new StableToken(dependencyId), Qa04ReferenceDependencyBlockerKindV1.PartitionMapping, null, null, new StableToken(failureCode));
}
