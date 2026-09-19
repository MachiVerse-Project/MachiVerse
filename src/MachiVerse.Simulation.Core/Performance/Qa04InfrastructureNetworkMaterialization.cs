using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04InfrastructureDescriptorIdentityBindingV1(
    OpaqueId128 DescriptorId,
    OpaqueId128 AuthoritativeRecordId,
    StableToken MaterialClass,
    ulong LocalOrdinal);

/// <summary>
/// Canonical perf.reference.v1 network/node/edge materialization. TileScope identity remains an
/// explicit Spatial authority input; all Infrastructure-specific topology identities and values are
/// fixed by phase4-alpha11-infrastructure-network-v2.md.
/// </summary>
public static class Qa04InfrastructureNetworkMaterializerV1
{
    public const uint Networks = 100;
    public const uint NodesPerNetwork = 200;
    public const uint EdgesPerNetwork = 1_000;
    public const ulong CanonicalNetworkCount = Networks;
    public const ulong CanonicalNodeCount = checked((ulong)Networks * NodesPerNetwork);
    public const ulong CanonicalEdgeCount = checked((ulong)Networks * EdgesPerNetwork);
    public const ulong CanonicalTopologyRecordCount = CanonicalNetworkCount + CanonicalNodeCount + CanonicalEdgeCount;

    private static readonly StableToken PerformanceDomain = new("performance");
    private static readonly StableToken NetworkCreationKind = new("perf.infrastructure-network");
    private static readonly StableToken Active = new("active");
    private static readonly StableToken Junction = new("junction");
    private static readonly StableToken Link = new("link");

    public static void ValidateCanonicalContract()
    {
        Qa04ReferenceLoadV1.ValidateCanonicalContract();
        Qa04ReferenceScenariosV1.ValidateCanonicalContract();
        Qa04InfrastructureReferenceDecompositionV1.ValidateCanonicalContract();
        Qa04SocietyGovernanceReferenceDecompositionV1.ValidateCanonicalContract();
        InfrastructureNetworkTopologyRecordSchemaV2.ValidateCanonicalContract();

        if (CanonicalNetworkCount != Qa04InfrastructureReferenceDecompositionV1.NetworkCount ||
            CanonicalNodeCount != Qa04InfrastructureReferenceDecompositionV1.NodeCount ||
            CanonicalEdgeCount != Qa04InfrastructureReferenceDecompositionV1.EdgeCount ||
            CanonicalNodeCount != checked((ulong)Qa04ReferenceScenariosV1.InfrastructureNetworkNodeCount) ||
            CanonicalEdgeCount != checked((ulong)Qa04ReferenceScenariosV1.InfrastructureStableEdgeCount) ||
            CanonicalTopologyRecordCount != 120_100)
            throw new InvalidDataException("qa04.infrastructure.topology-count-drift");
        if (NodesPerNetwork != 200 || EdgesPerNetwork != 1_000)
            throw new InvalidDataException("qa04.infrastructure.topology-shape-drift");
    }

    public static OpaqueId128 NetworkId(uint networkOrdinal)
    {
        if (networkOrdinal >= Networks) throw new ArgumentOutOfRangeException(nameof(networkOrdinal));
        return DerivedIdentity.DeriveEntityId(
            Qa04ReferenceLoadV1.WorldId,
            creationStep: 0,
            PerformanceDomain,
            OpaqueId128.Zero,
            NetworkCreationKind,
            networkOrdinal);
    }

    public static InfrastructureNetworkTopologyRecordMaterialV2 CreateNetwork(
        uint networkOrdinal,
        Func<ushort, PartitionRecordRefV1> tileScopeForTile,
        out Qa04InfrastructureDescriptorIdentityBindingV1 identityBinding)
    {
        ArgumentNullException.ThrowIfNull(tileScopeForTile);
        ValidateCanonicalContract();
        return CreateNetworkValidated(networkOrdinal, tileScopeForTile, out identityBinding);
    }

    public static InfrastructureNetworkTopologyRecordMaterialV2 CreateNode(
        uint nodeOrdinal,
        Func<ushort, PartitionRecordRefV1> tileScopeForTile,
        out Qa04InfrastructureDescriptorIdentityBindingV1 identityBinding)
    {
        ArgumentNullException.ThrowIfNull(tileScopeForTile);
        ValidateCanonicalContract();
        return CreateNodeValidated(nodeOrdinal, tileScopeForTile, out identityBinding);
    }

    public static InfrastructureNetworkTopologyRecordMaterialV2 CreateEdge(
        uint edgeOrdinal,
        out Qa04InfrastructureDescriptorIdentityBindingV1 identityBinding)
    {
        ValidateCanonicalContract();
        return CreateEdgeValidated(edgeOrdinal, out identityBinding);
    }

    public static IEnumerable<InfrastructureNetworkTopologyRecordMaterialV2> MaterializeCanonicalTopology(
        Func<ushort, PartitionRecordRefV1> tileScopeForTile)
    {
        ArgumentNullException.ThrowIfNull(tileScopeForTile);
        ValidateCanonicalContract();
        for (uint network = 0; network < Networks; network++)
            yield return CreateNetworkValidated(network, tileScopeForTile, out _);
        for (uint node = 0; node < CanonicalNodeCount; node++)
            yield return CreateNodeValidated(node, tileScopeForTile, out _);
        for (uint edge = 0; edge < CanonicalEdgeCount; edge++)
            yield return CreateEdgeValidated(edge, out _);
    }

    private static InfrastructureNetworkTopologyRecordMaterialV2 CreateNetworkValidated(
        uint networkOrdinal,
        Func<ushort, PartitionRecordRefV1> tileScopeForTile,
        out Qa04InfrastructureDescriptorIdentityBindingV1 identityBinding)
    {
        if (networkOrdinal >= Networks) throw new ArgumentOutOfRangeException(nameof(networkOrdinal));
        var descriptor = RequireBinding(
            checked(Qa04InfrastructureReferenceDecompositionV1.NetworkStartOrdinal + networkOrdinal),
            "network");
        var recordId = NetworkId(networkOrdinal);
        identityBinding = new Qa04InfrastructureDescriptorIdentityBindingV1(
            descriptor.Descriptor.RecordId, recordId, descriptor.MaterialClass, descriptor.LocalOrdinal);

        var nodeRefs = Enumerable.Range(0, checked((int)NodesPerNetwork))
            .Select(local => Ref(Qa04ReferenceScenariosV1.InfrastructureNodeId(
                checked((int)(networkOrdinal * NodesPerNetwork + (uint)local)))))
            .OrderBy(static reference => reference.RecordId)
            .ToArray();
        var edgeRefs = Enumerable.Range(0, checked((int)EdgesPerNetwork))
            .Select(local => Ref(Qa04ReferenceScenariosV1.InfrastructureEdgeId(
                checked((int)(networkOrdinal * EdgesPerNetwork + (uint)local)))))
            .OrderBy(static reference => reference.RecordId)
            .ToArray();
        var organization = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(networkOrdinal);
        if (organization.PartitionId.Value != "society.organization" || organization.UsesSpecializedIdentity)
            throw new InvalidDataException("qa04.infrastructure.network-operator-binding-drift");
        var operatorRefs = new[]
        {
            new PartitionRecordRefV1("society.organization", organization.Descriptor.RecordId),
        };
        var tile = checked((ushort)(((ulong)networkOrdinal * Qa04ReferenceLoadV1.RegionalTileCount) / Networks));
        var scopeRef = tileScopeForTile(tile);
        ValidateScopeRef(scopeRef);

        return Material(
            recordId,
            new InfrastructureNetworkPayloadV2(
                NetworkKindFor(networkOrdinal),
                nodeRefs,
                edgeRefs,
                operatorRefs,
                new[] { scopeRef },
                Active,
                topologyRevision: 1));
    }

    private static InfrastructureNetworkTopologyRecordMaterialV2 CreateNodeValidated(
        uint nodeOrdinal,
        Func<ushort, PartitionRecordRefV1> tileScopeForTile,
        out Qa04InfrastructureDescriptorIdentityBindingV1 identityBinding)
    {
        if (nodeOrdinal >= CanonicalNodeCount) throw new ArgumentOutOfRangeException(nameof(nodeOrdinal));
        var descriptor = RequireBinding(
            checked(Qa04InfrastructureReferenceDecompositionV1.NodeStartOrdinal + nodeOrdinal),
            "node");
        var recordId = Qa04ReferenceScenariosV1.InfrastructureNodeId(checked((int)nodeOrdinal));
        identityBinding = new Qa04InfrastructureDescriptorIdentityBindingV1(
            descriptor.Descriptor.RecordId, recordId, descriptor.MaterialClass, descriptor.LocalOrdinal);

        var network = nodeOrdinal / NodesPerNetwork;
        var localNode = nodeOrdinal % NodesPerNetwork;
        var tile = checked((ushort)((network * NodesPerNetwork + localNode) % Qa04ReferenceLoadV1.RegionalTileCount));
        var scopeRef = tileScopeForTile(tile);
        ValidateScopeRef(scopeRef);
        return Material(
            recordId,
            new InfrastructureNetworkNodePayloadV2(
                Ref(NetworkId(network)),
                Junction,
                scopeRef,
                capacityUnits: checked(1_000UL + nodeOrdinal % 9_001u),
                availabilityPpm: 1_000_000,
                Active));
    }

    private static InfrastructureNetworkTopologyRecordMaterialV2 CreateEdgeValidated(
        uint edgeOrdinal,
        out Qa04InfrastructureDescriptorIdentityBindingV1 identityBinding)
    {
        if (edgeOrdinal >= CanonicalEdgeCount) throw new ArgumentOutOfRangeException(nameof(edgeOrdinal));
        var descriptor = RequireBinding(
            checked(Qa04InfrastructureReferenceDecompositionV1.EdgeStartOrdinal + edgeOrdinal),
            "edge");
        var recordId = Qa04ReferenceScenariosV1.InfrastructureEdgeId(checked((int)edgeOrdinal));
        identityBinding = new Qa04InfrastructureDescriptorIdentityBindingV1(
            descriptor.Descriptor.RecordId, recordId, descriptor.MaterialClass, descriptor.LocalOrdinal);

        var network = edgeOrdinal / EdgesPerNetwork;
        var localEdge = edgeOrdinal % EdgesPerNetwork;
        var fromLocal = localEdge % NodesPerNetwork;
        var stride = checked(1u + (localEdge / NodesPerNetwork) % (NodesPerNetwork - 1));
        var toLocal = (fromLocal + stride) % NodesPerNetwork;
        var nodeBase = checked(network * NodesPerNetwork);
        var fromRef = Ref(Qa04ReferenceScenariosV1.InfrastructureNodeId(checked((int)(nodeBase + fromLocal))));
        var toRef = Ref(Qa04ReferenceScenariosV1.InfrastructureNodeId(checked((int)(nodeBase + toLocal))));

        return Material(
            recordId,
            new InfrastructureNetworkEdgePayloadV2(
                Ref(NetworkId(network)),
                fromRef,
                toRef,
                Link,
                cost: checked(1UL + localEdge % 1_000u),
                capacityUnits: checked(100UL + localEdge % 901u),
                availabilityPpm: 1_000_000,
                Active));
    }

    private static InfrastructureNetworkTopologyRecordMaterialV2 Material(
        OpaqueId128 recordId,
        InfrastructureNetworkTopologyRecordPayloadV2 payload)
        => new(
            recordId,
            revision: 1,
            createdStep: 0,
            retiredStep: null,
            detailLevel: DetailLevelV1.D2RegionalAggregate,
            lineageRef: null,
            payload);

    private static Qa04InfrastructureBindingV1 RequireBinding(ulong ordinal, string expectedMaterialClass)
    {
        var binding = Qa04InfrastructureReferenceDecompositionV1.Bind(ordinal);
        if (binding.MaterialClass.Value != expectedMaterialClass ||
            binding.PartitionId.Value != InfrastructureNetworkTopologyRecordSchemaV2.PartitionId ||
            !binding.UsesSpecializedIdentity)
            throw new InvalidDataException($"qa04.infrastructure.{expectedMaterialClass}-descriptor-binding-drift");
        return binding;
    }

    private static StableToken NetworkKindFor(uint networkOrdinal)
        => new((networkOrdinal % 4u) switch
        {
            0 => "transport",
            1 => "water",
            2 => "power",
            _ => "communication",
        });

    private static PartitionRecordRefV1 Ref(OpaqueId128 id)
        => new(InfrastructureNetworkTopologyRecordSchemaV2.PartitionId, id);

    private static void ValidateScopeRef(PartitionRecordRefV1 scopeRef)
    {
        if (scopeRef.PartitionId.Value != "spatial.scope_registry" || scopeRef.RecordId.IsZero)
            throw new InvalidDataException("qa04.infrastructure.tile-scope-ref-invalid");
    }
}
