# Alpha 1.1 Infrastructure network service / ServiceQueue reference authority

Status: **Proposed normative design / approval pending**

Tracking: #300, #240, #265

## 1. Purpose

This document proposes the minimum benchmark-only authority package needed to turn the canonical `perf.reference.v1` queued infrastructure-service load into production records without fabricating references or bypassing schema validation.

Current accepted Infrastructure material is the heterogeneous topology v2 package:

```text
network       100
node       20,000
edge      100,000
----------------
accepted  120,100 / 500,000
remaining 379,900
```

The benchmark profile also fixes exactly 250,000 queued service requests. Those queue records cannot become authoritative until the services referenced by `service_ref` are actual records.

The proposed package is therefore:

```text
infrastructure.transport_service       10,000
infrastructure.water_service           10,000
infrastructure.power_service           10,000
infrastructure.communication_service   10,000
infrastructure.service_queue          250,000
----------------------------------------------
total                                 290,000
```

`infrastructure.facility_service` is intentionally excluded because its physical-facility authority is a separate unresolved dependency.

This proposal defines only `perf.reference.v1` benchmark genesis semantics. It does not define a universal infrastructure ontology, realistic engineering capacities, or general queue policy.

## 2. Existing authority reused by this proposal

The following are already authoritative and must be reused rather than duplicated:

- 100 `infrastructure.network_topology /2.0` network records;
- 20,000 topology node records;
- 100,000 topology edge records;
- canonical TileScope authority;
- canonical Resident identity authority;
- topology network kind selection:
  - ordinal mod 4 = 0: transport;
  - ordinal mod 4 = 1: water;
  - ordinal mod 4 = 2: power;
  - ordinal mod 4 = 3: communication.

The topology v2 target schema is explicitly registered by `StandardDomainRecordSchemaMigrationRegistryV1`, and standard payload reference validation accepts registered migration-target schemas. Service records therefore may reference the existing v2 network records through the ordinary production validation path.

## 3. Common network selector

For service local ordinal `i` where `0 <= i < 10,000`:

```text
kind_local_network = i mod 25

transport_network_ordinal     = 4 * kind_local_network + 0
water_network_ordinal         = 4 * kind_local_network + 1
power_network_ordinal         = 4 * kind_local_network + 2
communication_network_ordinal = 4 * kind_local_network + 3
```

The authoritative reference is:

```text
network_ref = infrastructure.network_topology[
  Qa04InfrastructureNetworkMaterializerV1.NetworkId(network_ordinal)
]
```

This selects only actual v2 records whose record kind is `network` and whose network kind matches the service partition.

## 4. Scope selector

Water, power, and communication services use the same canonical TileScope as their target network.

The existing network materializer uses:

```text
tile = floor(network_ordinal * 4096 / 100)
service_scope_ref = TileScope[tile]
```

No new spatial identity or geometry semantics are introduced.

## 5. TransportService — 10,000

For local ordinal `i`:

```text
network_ordinal = 4 * (i mod 25)
network_local_service_ordinal = floor(i / 25)   // 0..399
edge_ordinal = network_ordinal * 1000 + network_local_service_ordinal
```

Proposed payload:

```text
network_ref       = actual transport network(network_ordinal)
service_kind      = perf.transport-service
route_refs        = [actual topology edge(edge_ordinal)]
capacity_per_step = 1000
load              = 0
schedule_ref      = NONE
availability_ppm  = 1000000
status            = active
```

The selected edge is owned by the same network under the already-proven topology closure. The one-edge route is a benchmark load fixture and is not a general route model.

## 6. WaterService — 10,000

Proposed payload:

```text
network_ref        = actual kind-matched water network
service_scope_ref  = target network TileScope
supply_ml_per_step = 1000000
demand_ml_per_step = 0
pressure_head_mm   = 1000
quality_ppm        = 1000000
availability_ppm   = 1000000
status             = active
```

`demand_ml_per_step = 0` means no queued request has entered active allocation at genesis. Queue demand remains represented by the separate ServiceQueue state.

The fixed values are benchmark-only load values; they do not claim realistic hydraulic behavior.

## 7. PowerService — 10,000

Proposed payload:

```text
network_ref       = actual kind-matched power network
service_scope_ref = target network TileScope
generation_mw     = 1000
demand_mw         = 0
delivered_mw      = 0
availability_ppm  = 1000000
status            = active
```

The fixed values are benchmark-only load values and do not define realistic generation or demand behavior.

## 8. CommunicationService — 10,000

Proposed payload:

```text
network_ref             = actual kind-matched communication network
service_scope_ref       = target network TileScope
capacity_units_per_step = 1000
queued_units            = exact queued request units mapped to this service
latency_steps           = 1
availability_ppm        = 1000000
status                  = active
```

`queued_units` is derived from the canonical ServiceQueue mapping below. It is not an unrelated constant.

## 9. Canonical network-service pool — 40,000

Define one explicit benchmark-only ordered service pool, interleaved by service local ordinal:

```text
for i = 0..9999:
  transport_service[i]
  water_service[i]
  power_service[i]
  communication_service[i]
```

The resulting pool has exactly 40,000 actual service references. This ordering is normative for `perf.reference.v1` only and must not depend on dictionary, filesystem, task, or runtime enumeration order.

## 10. ServiceQueue — 250,000

The record identity remains the already-fixed specialized request identity:

```text
record_id = Qa04ReferenceScenariosV1.InfrastructureServiceRequestId(q)
```

For request local ordinal `q`, `0 <= q < 250,000`, propose:

```text
service_ref       = CanonicalNetworkServicePool[q mod 40000]
requester_ref     = Resident[q mod 1000000]
eligible_step     = 0
semantic_priority = 0
requested_units   = 1
allocated_units   = 0
status            = queued
```

Consequences:

- every `service_ref` resolves to one actual network-service record;
- every `requester_ref` resolves to one actual Resident record;
- every service receives deterministically six or seven queued requests;
- communication `queued_units` is the exact sum of `requested_units` for queue records mapped to that communication service;
- `allocated_units = 0` and `status = queued` represent requests waiting for allocation at benchmark genesis.

`queued` is the benchmark initial state for these 250,000 records. It does not replace or restrict the general Phase 3 request lifecycle.

## 11. Workload compatibility

The standard Operation catalog already defines:

```text
infrastructure.service.reserve
  requester
  service ref
  units
  eligible range
```

The proposed world authority therefore supplies the missing actual requester/service target surfaces needed to design the pending `infrastructure-service-delivery` operation-family binding.

World authority and workload binding remain separate acceptance gates. Implementing this package does not automatically remove the workload parent blocker.

Likewise, after ServiceQueue is actual and Snapshot/recovery-proven, it may become an available transaction participant partition. The transaction parent blocker remains until all required participant authority and binding logic are proven.

## 12. Materialization dependency order

After approval, implementation should follow:

```text
accepted topology v2 + TileScope + Resident
  -> TransportService
  -> WaterService
  -> PowerService
  -> CommunicationService
  -> canonical 40,000-service pool
  -> ServiceQueue
  -> production Snapshot/recovery semantic proof
```

All required references must be validated against actual records and their actual allowed schemas. An exists-everywhere or smoke-only resolver is not acceptable for production proof.

## 13. Acceptance impact after implementation/proof

Documentation approval alone changes no release state.

Only after full canonical materialization plus production Snapshot encoding and semantic recovery rehash succeeds:

```text
Infrastructure accepted  = 120,100 -> 410,100 / 500,000
Infrastructure remaining = 379,900 ->  89,900
```

The three ServiceQueue direct dependencies can then move to zero:

```text
qa04.material.service-queue-service-authority-undefined
qa04.material.service-queue-requester-mapping-undefined
qa04.material.service-queue-genesis-state-undefined
```

Potential direct canonical dependency count after proof:

```text
8 -> 5
```

The remaining five are Participation 3 + DetailRegion 2. Infrastructure's parent reference-world blocker remains until the other 89,900 Infrastructure records are authoritative.

## 14. Explicit non-decisions

This proposal intentionally does not decide:

- `infrastructure.facility_service`;
- infrastructure dependency graph records;
- information delivery/media/record/address records;
- failure/recovery or lineage records;
- general transport route or schedule semantics;
- realistic hydraulic or electrical engineering values;
- realistic communication capacity modeling;
- general requester class taxonomy;
- dynamic semantic-priority policy;
- queue allocation or cancellation algorithms;
- runtime creation/retirement policy for service records;
- infrastructure-service Operation binding details beyond compatibility;
- transaction participant binding details beyond authority availability.

## 15. Approval boundary

This document is **Proposed normative design / approval pending**.

Until explicitly approved and integrated through `documentation` and `develop`:

- do not implement these semantic values in PR #265;
- do not remove the three ServiceQueue blockers;
- do not increase Infrastructure accepted material;
- do not mark ServiceQueue transaction authority available;
- do not mark the infrastructure Operation family bound;
- do not alter release flags.