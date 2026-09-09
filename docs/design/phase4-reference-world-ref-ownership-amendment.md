# Phase 4 QA-04 reference-world Ref ownership amendment

Status: Decided normative amendment / partial implementation  
Tracking: #240  
Implementation: Draft PR #265  
Applies to: `perf.reference.v1`

## 1. Purpose

The Stage 2 materialization audit found four cases where P4-05 contains a required `Ref`/`RefList`, while the referenced semantic family already belongs to one of the existing 97 standard partitions but the current v1 record schema has no record arm that can own the target.

This amendment fixes **ownership** and tracks the record-schema repair needed to make each decided target materially representable. It does not claim that every repaired v2 partition is production-active or that canonical benchmark material is populated. Until production migrations/materializers and the remaining independent dependencies exist, the existing `Qa04ReferenceWorldDependencyContractV1` blockers remain active and `referenceWorldMaterialized` remains false.

## 2. Invariants

The repair must preserve all of the following:

- standard Domain partition count remains exactly **97**;
- Snapshot section count remains exactly **6 Core + 97 Domain = 103**;
- no new authority partition is introduced solely to satisfy the benchmark;
- no Ref may target a semantically unrelated record;
- current persisted v1 record schemas remain immutable;
- every repaired partition uses an explicit record-schema **major version 2.0** before mixed record kinds are materialized;
- Snapshot recovery preserves the serialized record schema version and fails closed on an unknown version or record kind.

## 3. Decided ownership

| source field | authoritative target partition | required target record kind | required v2 record kinds |
|---|---|---|---|
| `physical.presence.shape_ref` | `physical.occupancy` | `collision_shape` | `collision_shape`, `occupancy` |
| `spatial.terrain_geometry.root_brick_ref` | `spatial.terrain_geometry` | `terrain_brick` | `terrain_brick`, `terrain_root` |
| `infrastructure.network_topology.node_refs` | `infrastructure.network_topology` | `node` | `edge`, `network`, `node` |
| `infrastructure.network_topology.edge_refs` | `infrastructure.network_topology` | `edge` | `edge`, `network`, `node` |
| `society.market_transaction.market_ref` | `society.market_transaction` | `market_state` | `market_state`, `order_or_offer`, `transaction_or_price_fact` |

The machine-readable ownership mirror is `Qa04ReferenceWorldRefOwnershipContractV1`.

## 4. Why these owners

### 4.1 Collision shape -> `physical.occupancy`

P4-01 assigns collision/occupancy state to `physical_built`, and `physical.occupancy` is the existing standard partition that owns collision occupancy semantics. P4-04 already defines the standard collision geometry families. A `collision_shape` record arm therefore stays inside the existing Physical/Built authority instead of creating a 98th partition or pointing `shape_ref` at unrelated Spatial material.

The exact lossless v2 shape arm still needs to freeze the canonical fields for Sphere, Capsule, OrientedBox, ConvexPolytope, TriangleMeshStatic, and any permitted SDF reference form before the dependency blocker can be removed.

### 4.2 Terrain brick -> `spatial.terrain_geometry`

P4-01 assigns natural terrain solid/void geometry authority to `spatial`, and P4-04 already defines `TerrainBrickV1` as SBO-SDF algorithm state. The root record and brick records therefore belong to the same `spatial.terrain_geometry` authority family.

The exact v2 record shape is now frozen in `phase4-terrain-geometry-record-v2.md` and implemented by `SpatialTerrainGeometryRecordSchemaV2` plus the standalone fail-closed `SpatialTerrainGeometryRecordWireCodecV2`:

- `terrain_root` is a lossless v1-root arm with explicit `record_kind`;
- `terrain_brick` maps `TerrainBrickV1.brick_id -> record_id` and `TerrainBrickV1.revision -> record revision`;
- the brick payload preserves `level`, `SpatialCellKeyV1`, spacing, all 729 SDF samples, and all 512 material ids;
- valid standalone v2 material is canonical under decode -> encode;
- the current production standard registry intentionally remains at v1.

This removes the **terrain field-schema ambiguity**, but it does not define the canonical `perf.reference.v1` contents of the 500,000 hot bricks or activate mixed-record v2 in production Snapshot/recovery. Benchmark SDF/material sample generation and production v2 integration therefore remain unresolved materialization work.

### 4.3 Network node/edge -> `infrastructure.network_topology`

P4-01 defines `infrastructure.network_topology` as the logical network topology authority, while P4-06 fixes stable node and edge identities. `network`, `node`, and `edge` are therefore record kinds of the same existing partition. No extra Infrastructure partition is required.

The exact v2 node/edge payload fields and reference constraints still have to be frozen before the 500,000 Infrastructure benchmark class can be materially populated.

### 4.4 Market state -> `society.market_transaction`

Phase 3 defines conceptual MarketState and P4-06 fixes 100 market scopes with 10,000 active orders per scope. P4-01 gives `society.market_transaction` responsibility for offer/demand/trade/price history. A `market_state` arm in that same partition is therefore the authority target for `market_ref` without inventing another Society partition.

The exact v2 MarketState/order/transaction record fields and the relationship between those records and the 2,000,000 Society/Governance aggregate class remain to be frozen.

## 5. Compatibility rule

The four affected v1 schemas are not extended by adding optional fields and pretending wire compatibility. The v2 contract must use the same stable schema id with `SchemaVersion { major = 2, minor = 0 }` and must define:

1. required canonical `record_kind` discrimination;
2. arm-specific required and forbidden field sets;
3. exact descriptor ordering for every arm;
4. semantic Ref target-kind validation;
5. deterministic v1 -> v2 migration only where an exact recipe exists;
6. recovery rejection of unknown arm/version and preservation of serialized schema identity.

For `spatial.terrain_geometry`, items 1-3 and the lossless standalone migration/wire basis are now implemented. Item 4 and production partition-wide migration/recovery activation remain pending.

The standard runtime registry remains at v1 until each production migration is implemented and validated. A standalone v2 schema/codec must not silently flip a production partition to v2.

## 6. Remaining blockers

This decision removes ambiguity about **which existing partition owns each orphan target**. The Terrain slice additionally removes ambiguity about the exact `terrain_root` / `terrain_brick` v2 field shape. Stage 2 still cannot materialize the affected benchmark classes until the remaining production integrations and canonical contents exist.

Still unresolved independently:

- Environment D0 1,000,000 / D1 250,000 mapping and canonical initial payload values;
- Society/Governance 2,000,000 decomposition across the 33 owner partitions;
- exact nested schemas for `BodyRegionStateV1`, `PerceivedFactV1`, and governance `RuleAst`;
- active CrossDomainTransaction 10,000 persistent/reconstruction authority;
- canonical SDF/material content for the 500,000 hot `TerrainBrickV1` records;
- production mixed-record v2 state/snapshot/recovery + target-kind validation for `spatial.terrain_geometry`;
- exact v2 field schemas/migrations for `physical.occupancy`, `infrastructure.network_topology`, and `society.market_transaction`.

`Qa04ReferenceWorldDependencyContractV1` remains the release gate until the implementation-specific blockers are actually removed. Reduced typed-empty roots and synthetic Ref targets remain prohibited.
