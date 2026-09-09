# Phase 4 `spatial.terrain_geometry` v2 production migration

Status: Decided / migration-aware exact-97 path implemented / canonical benchmark material pending  
Tracking: #240  
Implementation: Draft PR #265  
Record schema migration: `domain.spatial.terrain_geometry.record / 1.0 -> 2.0`

## 1. Purpose

This document defines the production Snapshot/recovery activation boundary for the exact Terrain v2 record schema frozen in `phase4-terrain-geometry-record-v2.md`.

The migration must not weaken the standard 97-partition contract, accept arbitrary future schema versions, rewrite actual recovered record schemas back to v1, or imply that the canonical `perf.reference.v1` Terrain contents have been defined.

The standard partition identity remains:

```text
partition_id      = spatial.terrain_geometry
owner_domain      = spatial
partition_schema  = domain.spatial.terrain_geometry / 1.0
```

Only the record schema is migration-aware:

```text
source = domain.spatial.terrain_geometry.record / 1.0
target = domain.spatial.terrain_geometry.record / 2.0
```

The partition schema is unchanged because `PartitionStateHeaderV1` identifies the partition schema, while its canonical digest already commits every record's schema id, major version, and minor version.

## 2. Explicit migration registry

`StandardDomainRecordSchemaMigrationRegistryV1` is the only WorldState-level permission source for a standard record-schema migration.

At this checkpoint its canonical registry contains exactly one entry:

```text
spatial.terrain_geometry: 1.0 -> 2.0
```

Allowed record schemas for this partition are therefore exactly:

```text
1.0
2.0
```

Examples that remain invalid:

```text
spatial.terrain_geometry / 2.1
spatial.terrain_geometry / 3.0
physical.presence / 2.0
infrastructure.network_topology / 2.0
society.market_transaction / 2.0
```

A migration registration changes only the permitted record schema. `partition_id`, owner, owner rank, partition schema, primary-key kind, persistence class, and canonical-order kind must still equal the standard identity exactly.

Registration alone does not mutate `StandardDomainPartitionRegistry` and does not materialize any benchmark records.

## 3. Authority boundary

The generic `DomainPartitionSnapshotAuthorityV1<TPayload>` remains strictly bound to the standard v1 identity. It is not relaxed to accept arbitrary migrated payloads.

Terrain v2 uses the specialized `SpatialTerrainGeometrySnapshotAuthorityV2`, which:

- binds actual mixed Terrain v2 records;
- re-computes `PartitionStateHeaderV1` from semantic Terrain v2 payload digests;
- validates canonical record-id ordering;
- validates local root -> `terrain_brick` target-kind closure;
- preserves the standard partition id/owner/partition schema;
- exposes record schema `2.0` to the reference resolver.

`DomainPartitionSnapshotAuthoritySetV1` accepts an authority whose identity differs from the standard identity only when the complete identity is accepted by `StandardDomainRecordSchemaMigrationRegistryV1`.

Thus an exact-97 authority set may contain:

```text
96 standard v1 authorities
1 registered spatial.terrain_geometry v2 authority
```

without permitting any unregistered schema drift.

## 4. Production provider selection

Callers continue to supply the normal canonical 97-provider set.

`DomainSnapshotRecordSchemaMigrationProviderRegistryV1` resolves the actual provider from the actual authority record schema:

```text
standard record schema -> supplied standard provider
registered Terrain 2.0 -> SpatialTerrainGeometrySnapshotSectionProviderV2
anything else          -> fail closed
```

The persistence-side provider registry is intentionally separate from the WorldState migration registry so WorldState does not depend on persistence implementation types.

Every persistence provider registration must correspond exactly to a WorldState migration target.

No provider fallback from unknown 2.x/3.x material to v1 is allowed.

## 5. Production all-97 reference resolver

`DomainSnapshotReferenceResolverV1` stores the actual record schema supplied by each partition authority.

It must not replace a migrated record schema with `StandardDomainPartitionRegistry.RecordSchema`.

For example, after a Terrain v2 migration:

```text
TryGetRecordSchema(spatial.terrain_geometry, brick_id)
    == domain.spatial.terrain_geometry.record / 2.0
```

Resolver construction accepts only:

- the exact standard record schema; or
- an exact target schema registered by `StandardDomainRecordSchemaMigrationRegistryV1`.

All unregistered versions fail with the existing reference-source schema boundary.

## 6. Cross-partition Ref validation

`StandardDomainPayloadCodecValidatorV1` validates an actual resolved target record schema against the explicit migration registry.

This means a standard-v1 partition may legally reference a migrated Terrain 2.0 record when the reference itself is otherwise valid.

The rule is not "same schema id with any newer version". It is:

```text
actual target schema must be exactly standard OR exactly a registered migration target
```

Terrain 2.1, Terrain 3.0, and unimplemented v2 record families remain invalid.

## 7. Recovery Phase 1

The ordinary `DomainSnapshotRecoveredReferenceSourceV1` remains the strict v1 decoder.

Non-empty Terrain v2 sections use `SpatialTerrainGeometryRecoveredReferenceSourceV2`, which structurally validates:

- section id;
- fragment index/count;
- repeated partition header equality;
- fragment item count;
- first/last record-id ranges;
- canonical record ordering;
- exact record schema 2.0;
- total item count and header item count.

It then exposes the actual v2 record ids and schema to the all-97 resolver without running cross-partition semantic reference validation yet.

For a non-empty Terrain section, recovery attempts the registered v2 structural decoder and then the strict v1 decoder. If neither recognizes the material, recovery fails closed as an unrecognized Terrain record schema.

An empty Terrain section contains no record from which a record schema version can be proven. It remains structurally compatible with the v1 empty-section path because no record reference can resolve to that empty partition.

## 8. Recovery Phase 2

The Terrain v2 semantic verifier receives the recovered all-97 resolver and then:

1. decodes all Terrain v2 fragments;
2. validates record ordering/ranges/counts;
3. validates semantic payload/reference material;
4. reconstructs `SpatialTerrainGeometryPartitionStateV2`;
5. re-runs root -> brick local target-kind closure;
6. recomputes the canonical partition header/digest;
7. requires equality with the frozen section authority.

This retains the existing two-phase recovery rule: actual record/schema indexing is reconstructed first, semantic cross-reference verification occurs second.

## 9. Exact-97 migration canary

The smoke suite contains a migration infrastructure canary with truthful reduced material:

```text
resident.identity_lifecycle = 1 real record
spatial.terrain_geometry     = 1 synthetic Terrain v2 brick canary record
all other partitions         = genuinely empty authoritative material
```

The Terrain record is intentionally a non-benchmark canary. Its SDF/material values are test fixture values only and have no normative relationship to `perf.reference.v1`.

The canary proves:

- replacing the empty Terrain header changes `WorldStateV1.StateDigest`;
- the migrated frozen WorldState still contains exactly 97 partition headers;
- the exact-97 authority set accepts the one registered v2 authority alongside 96 v1 authorities;
- the ordinary canonical 97-provider list automatically selects the v2 Terrain provider from actual authority schema;
- production Domain serialization still emits exactly 97 sections;
- the Terrain section item count/digest is bound to actual frozen v2 material;
- recovery Phase 1 rebuilds an all-97 resolver that preserves Terrain record schema 2.0;
- recovery Phase 2 reconstructs/re-hashes the Terrain section using that recovered resolver;
- `StandardDomainPartitionRegistry` remains at Terrain record schema 1.0 during the canary.

This is a production-path migration proof, not a canonical reference-world proof.

## 10. Fail-closed invariants

The migration path must reject at least:

- unregistered record-schema versions;
- record-schema migration with any other partition-identity field changed;
- migrated authority with a frozen header mismatch;
- migrated provider unavailable for an otherwise claimed target schema;
- resolver source whose schema is not standard or registered;
- Terrain fragment material recognized by neither exact v1 nor exact v2 decoder;
- Terrain v2 fragment/schema/order/range/count violations;
- recovered Terrain root whose `root_brick_ref` is missing or resolves to a non-brick arm;
- semantic partition rehash mismatch.

No permissive unknown-field/schema-version fallback is introduced.

## 11. What this does not complete

The following remain unresolved and must not be inferred from migration-path readiness:

- canonical SDF/material contents for the 500,000 hot `TerrainBrickV1` records;
- canonical Terrain-root + scope/material/connectivity population for `perf.reference.v1`;
- fixed-seed materialization of those 500,000 records;
- exact v2 schemas/materializers for the other repaired record families;
- full canonical exact-97 / exact-103 reference-world materialization;
- the full authoritative QA-04 workload loop and 27,000-Step execution;
- 12-run performance/determinism release evidence;
- 24-hour soak evidence.

Accordingly:

```text
qa04.material.terrain-brick-authority-undefined = active
referenceWorldMaterialized = false
```

until actual canonical Terrain contents and the remaining reference-world dependencies are resolved.
