# Alpha 1.1 Infrastructure network service / ServiceQueue reference authority

Status: **Complete / normative benchmark authority**

Tracking: #300, #240, #265

## 1. Purpose

This document fixes the benchmark-only `perf.reference.v1` initial authority for the minimum Infrastructure package required to make the canonical queued service load production-authoritative.

This decision is intentionally limited to benchmark genesis semantics. It does not define a universal infrastructure ontology, realistic engineering capacities, general queue policy, or runtime allocation policy.

Current accepted Infrastructure material before implementation is the heterogeneous topology v2 package:

```text
network       100
node       20,000
edge      100,000
----------------
accepted  120,100 / 500,000
remaining 379,900
```

The normative package fixed by this document is:

```text
infrastructure.transport_service       10,000
infrastructure.water_service           10,000
infrastructure.power_service           10,000
infrastructure.communication_service   10,000
infrastructure.service_queue          250,000
----------------------------------------------
total                                 290,000
```

`infrastructure.facility_service` is excluded because its physical-facility authority remains a separate dependency.

## 2. Existing authority reused

Implementation MUST reuse the already-authoritative records and identities:

- 100 `infrastructure.network_topology /2.0` network records;
- 20,000 topology node records;
- 100,000 topology edge records;
- canonical TileScope authority;
- canonical Resident identity authority;
- existing topology network kind selector:
  - ordinal mod 4 = 0: transport;
  - ordinal mod 4 = 1: water;
  - ordinal mod 4 = 2: power;
  - ordinal mod 4 = 3: communication.

The topology v2 target schema is registered by `StandardDomainRecordSchemaMigrationRegistryV1`. Production payload validation therefore MUST resolve these references through the ordinary migration-aware record-schema path; permissive fixture resolvers are not acceptable.

## 3. Common network selector

For service local ordinal `i`, where `0 <= i < 10,000`:

```text
kind_local_network = i mod 25

transport_network_ordinal     = 4 * kind_local_network + 0
water_network_ordinal         = 4 * kind_local_network + 1
power_network_ordinal         = 4 * kind_local_network + 2
communication_network_ordinal = 4 * kind_local_network + 3
```

The network reference is the actual topology v2 network record returned by:

```text
Qa04InfrastructureNetworkMaterializerV1.NetworkId(network_ordinal)
```

The resolved target MUST have record kind `network` and the service-compatible network kind.

## 4. Scope selector

Water, power, and communication services use the same canonical TileScope as the selected network:

```text
tile = floor(network_ordinal * 4096 / 100)
service_scope_ref = TileScope[tile]
```

No new Spatial identity or geometry semantics are introduced.

## 5. TransportService authority — 10,000

For local ordinal `i`:

```text
network_ordinal = 4 * (i mod 25)
network_local_service_ordinal = floor(i / 25)   // 0..399
edge_ordinal = network_ordinal * 1000 + network_local_service_ordinal
```

Canonical payload:

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

The route edge MUST be an actual edge owned by the same network. The one-edge route is benchmark-only fixture authority and is not a general transport-route model.

## 6. WaterService authority — 10,000

Canonical payload:

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

`demand_ml_per_step = 0` means no queued request has entered active allocation at benchmark genesis. The fixed values are benchmark-only load values.

## 7. PowerService authority — 10,000

Canonical payload:

```text
network_ref       = actual kind-matched power network
service_scope_ref = target network TileScope
generation_mw     = 1000
demand_mw         = 0
delivered_mw      = 0
availability_ppm  = 1000000
status            = active
```

The fixed values are benchmark-only load values and do not define a realistic power model.

## 8. CommunicationService authority — 10,000

Canonical payload:

```text
network_ref             = actual kind-matched communication network
service_scope_ref       = target network TileScope
capacity_units_per_step = 1000
queued_units            = exact queued request units mapped to this service
latency_steps           = 1
availability_ppm        = 1000000
status                  = active
```

`queued_units` MUST be derived from the canonical ServiceQueue mapping in this document.

## 9. Canonical network-service pool — 40,000

Define one explicit benchmark-only ordered pool:

```text
for i = 0..9999:
  transport_service[i]
  water_service[i]
  power_service[i]
  communication_service[i]
```

The resulting pool index is `0..39999`. This order is normative for `perf.reference.v1` and MUST NOT depend on dictionary, filesystem, task, or runtime enumeration order.

## 10. ServiceQueue authority — 250,000

Record identity remains the existing specialized request identity:

```text
record_id = Qa04ReferenceScenariosV1.InfrastructureServiceRequestId(q)
```

For request local ordinal `q`, where `0 <= q < 250,000`:

```text
service_ref       = CanonicalNetworkServicePool[q mod 40000]
requester_ref     = Resident[q mod 1000000]
eligible_step     = 0
semantic_priority = 0
requested_units   = 1
allocated_units   = 0
status            = queued
```

Normative consequences:

- every `service_ref` resolves to one actual network-service record;
- every `requester_ref` resolves to one actual Resident identity record;
- every service receives deterministically six or seven queued requests;
- each service-pool entry with pool index `< 10000` receives seven requests, and each entry with index `>= 10000` receives six requests;
- communication `queued_units` equals the exact sum of `requested_units` mapped to that communication service;
- `allocated_units = 0` and `status = queued` represent waiting-for-allocation benchmark genesis state.

`queued` is benchmark initial state only; it does not replace the general Phase 3 service-request lifecycle.

## 11. Workload compatibility boundary

The standard Operation catalog already defines `infrastructure.service.reserve` around requester, service reference, units, and eligible range. This world authority therefore supplies the actual requester/service surfaces needed for the pending `infrastructure-service-delivery` family.

World authority and workload binding remain separate gates. Implementing this document MUST NOT automatically clear the workload parent blocker or mark the Operation family bound.

Likewise, ServiceQueue may become an available transaction participant only after its actual authority and Snapshot/recovery proof succeed. The transaction parent blocker remains until the full transaction binding contract is satisfied.

## 12. Required implementation order

Implementation MUST follow this dependency order:

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

All references MUST resolve against actual records and allowed actual schemas. An exists-everywhere, synthetic-only, or permissive smoke resolver is forbidden for acceptance evidence.

## 13. Acceptance impact

Documentation integration alone changes no release state.

Only after full canonical materialization, production Snapshot encoding, recovery decode, and semantic rehash succeed may release tracking change to:

```text
Infrastructure accepted  = 410,100 / 500,000
Infrastructure remaining =  89,900
```

At that point the three ServiceQueue direct dependencies may move to zero:

```text
qa04.material.service-queue-service-authority-undefined
qa04.material.service-queue-requester-mapping-undefined
qa04.material.service-queue-genesis-state-undefined
```

Potential direct canonical dependency count after proof:

```text
8 -> 5
```

The remaining five are Participation 3 + DetailRegion 2. The Infrastructure parent reference-world blocker remains until the other 89,900 Infrastructure records are authoritative.

## 14. Explicit non-decisions

This authority does not decide:

- `infrastructure.facility_service`;
- infrastructure dependency graph records;
- information delivery/media/record/address records;
- failure/recovery or lineage records;
- general transport route or schedule semantics;
- realistic hydraulic/electrical engineering values;
- realistic communication capacity modeling;
- general requester class taxonomy;
- dynamic semantic-priority policy;
- queue allocation or cancellation algorithms;
- runtime creation/retirement policy for service records;
- infrastructure-service Operation binding details beyond compatibility;
- transaction participant binding details beyond authority availability.

## 15. Normative boundary

This document is approved normative benchmark authority for `perf.reference.v1`.

After this document is integrated through `documentation` and synchronized into `develop`, PR #265 may implement exactly the semantics fixed above. Implementation MUST still remain fail-closed until actual materialization and production Snapshot/recovery proof succeed.

Until that proof succeeds:

- do not remove the three ServiceQueue blockers;
- do not increase Infrastructure accepted material;
- do not mark ServiceQueue transaction authority available;
- do not mark the infrastructure Operation family bound;
- do not alter release flags.
