# Alpha 1.1 — `infrastructure.network_topology` record schema v2

Status: Decided normative design / implementation pending  
Tracking: #240  
Parent: `phase4-alpha11-normative-closure.md`

## 1. Schema identity

```text
schema_id = domain.infrastructure.network_topology.record
version   = 2.0
partition = infrastructure.network_topology
```

required `record_kind`:

```text
network
node
edge
```

## 2. `network` arm

v1 payloadをlossless保持する。

```text
1 network_kind: Token
2 node_refs: RefList
3 edge_refs: RefList
4 operator_refs: RefList
5 scope_refs: RefList
6 status: Token
7 topology_revision: uint64
```

Rules:

- node_refs target = same partition /2.0 / node
- edge_refs target = same partition /2.0 / edge
- refs canonical order / duplicate禁止
- topology_revision >0

## 3. `node` arm

```text
1 network_ref: Ref
2 node_kind: Token
3 scope_ref: Ref
4 capacity_units: uint64
5 availability_ppm: uint32
6 status: Token
```

Rules:

- network_ref -> same partition /2.0 / network
- scope_ref -> `spatial.scope_registry`
- `availability_ppm <= 1,000,000`
- initial node_kind registry: `junction`, `terminal`, `facility`
- status registry: `active`, `degraded`, `offline`, `retired`

## 4. `edge` arm

```text
1 network_ref: Ref
2 from_node_ref: Ref
3 to_node_ref: Ref
4 edge_kind: Token
5 cost: uint64
6 capacity_units: uint64
7 availability_ppm: uint32
8 status: Token
```

Rules:

- network_ref -> same partition /2.0 / network
- from/to -> same partition /2.0 / node
- from != to
- both nodes must reference the same network as this edge
- availability <=1,000,000
- initial edge_kind=`link`

## 5. Closure invariant

For every `network` record:

1. all node_refs resolve to `node` of that network;
2. all edge_refs resolve to `edge` of that network;
3. every referenced edge endpoint is present in node_refs;
4. no referenced node/edge belongs to another network;
5. list order is RecordId bytewise ascending.

An orphan node/edge may not exist in canonical `perf.reference.v1` material. General future schema may allow staged construction only through an explicit lifecycle extension, not implicitly in 2.0.

## 6. v1 -> v2 migration

Legacy v1 record -> `network` arm field-for-field.

Migration does not infer node/edge records from old Ref IDs. A migrated legacy network whose refs cannot resolve to actual v2 node/edge material is not target-kind closed and is not eligible for authoritative publication/recovery completion.

## 7. `perf.reference.v1` topology

Exactly:

```text
networks = 100
nodes = 20,000 = 200/network
edges = 100,000 = 1,000/network
```

IDs:

```text
network = DerivedIdentity(... kind=perf.infrastructure-network, ordinal 0..99)
node    = Qa04ReferenceScenariosV1.InfrastructureNodeId(0..19999)
edge    = Qa04ReferenceScenariosV1.InfrastructureEdgeId(0..99999)
```

Assignment:

```text
network(nodeOrdinal) = nodeOrdinal / 200
network(edgeOrdinal) = edgeOrdinal / 1000
localNode = nodeOrdinal % 200
localEdge = edgeOrdinal % 1000
```

Edge endpoints:

```text
fromLocal = localEdge % 200
stride = 1 + ((localEdge / 200) % 199)
toLocal = (fromLocal + stride) % 200
```

Thus no self edge is produced and every node receives five canonical outgoing links in the current 1,000-edge layout.

Network kind by ordinal `%4`:

```text
0 transport
1 water
2 power
3 communication
```

Canonical benchmark values:

```text
network.status = active
network.topology_revision = 1
node.node_kind = junction
node.status = active
node.capacity_units = 1000 + nodeOrdinal % 9001
node.availability_ppm = 1,000,000
edge.edge_kind = link
edge.cost = 1 + localEdge % 1000
edge.capacity_units = 100 + localEdge % 901
edge.availability_ppm = 1,000,000
edge.status = active
```

Network scope:

```text
TileScope(floor(networkOrdinal * 4096 / 100))
```

operator_refs contains exactly `Organization(networkOrdinal)`; scope_refs contains exactly the network scope.

Node scope uses:

```text
TileScope((networkOrdinal * 200 + localNode) % 4096)
```

## 8. Infrastructure 500k integration

The overall 500,000 active infrastructure records are decomposed by `phase4-alpha11-reference-world-decomposition.md`.

250,000 queue records use existing `InfrastructureServiceRequestId`. Their required service_ref targets actual service records across transport/water/power/communication/facility services by a stable concatenated service pool sorted by `(partitionId, RecordId)` and indexed `requestOrdinal % pool.Count`.

## 9. Snapshot / recovery

Implementation follows the Terrain-v2 migration pattern:

- specialized mixed-kind authority
- exact registered 1.0 ->2.0 migration only
- provider selected by actual authority schema
- recovered resolver preserves actual schema and record kind
- semantic rehash validates network/node/edge closure
- StandardDomainPartitionRegistry remains v1 during migration rollout

## 10. Blocker removal

`qa04.material.infrastructure-node-edge-authority-undefined` remains until schema/state/wire/migration/provider/recovery plus exact 100/20k/100k topology and the full 500k Infrastructure material pass production-path validation.
