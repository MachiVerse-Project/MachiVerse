# Alpha 1.1 — `perf.reference.v1` canonical TileScope authority

Status: Decided normative design / implementation pending  
Tracking: #240  
Implementation: Draft PR #265  
Parent: `phase4-alpha11-normative-closure.md`

## 1. Purpose

`perf.reference.v1` uses `TileScope(t)` as the shared spatial authority for Terrain, Environment, Infrastructure, Market and detail-state bindings. The existing Alpha 1.1 documents fixed the 64x64 regional tile lattice and required `TileScope(t)` references, but did not spell out the exact `spatial.scope_registry` RecordId and genesis payload. This document closes that omission without changing the 97-partition baseline.

## 2. Canonical tile set

The canonical set is exactly the existing regional lattice:

```text
RegionalTileRows = 64
RegionalTileColumns = 64
RegionalTileCount = 4096
tile index t = 0..4095
row = t / 64
column = t % 64
```

There is exactly one active `spatial.scope_registry` TileScope record for every regional tile.

## 3. Record identity

TileScope identity is derived at Step 0 using the same production `DerivedIdentity.DeriveEntityId` family used by Terrain root identities:

```text
world_id      = perf.reference.v1 WorldId
creation_step = 0
owner_domain  = spatial
parent_id     = ZERO
creation_kind = perf.tile-scope
local_ordinal = t
```

The result is `TileScopeId(t)` and is the authoritative RecordId in `spatial.scope_registry`.

No descriptor RecordId is substituted and no random/runtime-generated ID is permitted.

## 4. Genesis payload

Each TileScope record uses standard `SpatialScopeRegistryPayloadV1` with:

```text
scope_class  = perf.regional-tile
geometry_ref = Ref(spatial.terrain_geometry, TerrainRootId(t))
parent_scope = NONE
active_from  = 0
retired_at   = NONE
scope_flags  = 0
```

Envelope:

```text
revision     = 1
created_step = 0
retired_step = NONE
detail_level = D2RegionalAggregate
lineage_ref  = NONE
```

The `geometry_ref` target must be the canonical Terrain `/2.0` `terrain_root` record for the same tile.

## 5. Terrain reciprocal binding

The canonical Terrain root for tile `t` continues to use:

```text
scope_ref = Ref(spatial.scope_registry, TileScopeId(t))
```

Therefore `TileScope(t)` and `TerrainRoot(t)` form an intentional reciprocal authority pair. This does not introduce materialization-order dependence because both RecordIds are independently derivable from immutable Step-0 inputs before either record is instantiated.

## 6. Consumers

Any benchmark materializer that requires the regional tile scope must call the canonical TileScope authority instead of accepting a synthetic fixture in production paths. In particular:

- Terrain root `scope_ref`
- Environment D0/D1 `spatial_scope`
- Market/infrastructure spatial scope selectors where the owning spec selects a regional tile
- CrossDomainTransaction detail-guard reconstruction via `DetailDirectory.SpatialScopeRef`

Tests may still inject negative/synthetic authorities when explicitly testing fail-closed behavior, but such fixtures are not release material.

## 7. Acceptance

Before removing a TileScope-dependent world blocker, production-path proof must establish:

- exact 4,096 active TileScope records;
- one and only one RecordId per tile index;
- deterministic identity independent of iteration/materialization order;
- every `geometry_ref` resolves to the same-tile Terrain `terrain_root`;
- every Terrain root `scope_ref` resolves back to the same TileScope;
- no ZERO or foreign-partition refs;
- canonical Snapshot/recovery preserves the scope RecordIds and semantic payload digests.

This authority is benchmark-profile material and does not define a universal gameplay tile identity outside `perf.reference.v1`.