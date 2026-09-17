# Phase 4 amendment: Standard runtime DomainRegistry generation 1

Status: Complete candidate / INT-03 authority amendment  
Tracking: Issue #240  
Parents: `phase3-domain-common-contract.md`, `phase3-cross-domain-causality.md`, `phase4-core-data-structures.md`, `phase4-domain-state-registry.md`, `phase4-core-snapshot-section-wire.md`

## 1. Purpose

P4-01 requires `DomainRegistryStateV1` / `DomainRuntimeDescriptorV1`, and the six-Core Snapshot amendment fixes the exact wire and semantic digest for `core.domain-registry`. The remaining ambiguity is the concrete Standard runtime generation-1 descriptor material.

This amendment fixes only capabilities that the current Standard runtime can actually exercise. It does not copy the future P4 Operation/Event/Intent catalog into runtime authority.

Registry generation is:

```text
registry_generation = 1
```

A change that adds a runtime capability not represented below requires a registry-generation compatibility decision before that capability may be emitted as authority.

## 2. Standard domains and runtime schema identity

The eight generation-1 runtime descriptor schemas are fixed as follows. These schemas identify the runtime capability contract; they are not aliases for the 97 partition schemas.

| rank | domain_token | domain_schema |
|---:|---|---|
| 10 | `spatial` | `domain.spatial.runtime / 1.0` |
| 20 | `environment` | `domain.environment.runtime / 1.0` |
| 30 | `physical_built` | `domain.physical_built.runtime / 1.0` |
| 40 | `participation` | `domain.participation.runtime / 1.0` |
| 50 | `resident` | `domain.resident.runtime / 1.0` |
| 60 | `society_economy` | `domain.society_economy.runtime / 1.0` |
| 70 | `governance_security` | `domain.governance_security.runtime / 1.0` |
| 80 | `infrastructure_information` | `domain.infrastructure_information.runtime / 1.0` |

Ranks are the Phase 3 stable domain ranks. `owned_partitions` is not duplicated in this document: it is the exact owner projection of `StandardDomainPartitionRegistry`, whose standard set is 97 partitions. Every standard partition MUST appear once and only once.

## 3. State-read dependencies

Generation 1 records the Phase 3 published-State(S) read contract. Lists are ASCII ascending and do not create same-Step execution edges.

| domain | state_read_dependencies |
|---|---|
| `spatial` | none |
| `environment` | `governance_security`, `infrastructure_information`, `physical_built`, `resident`, `society_economy`, `spatial` |
| `physical_built` | `environment`, `spatial` |
| `participation` | `resident` |
| `resident` | `environment`, `physical_built` |
| `society_economy` | `governance_security` |
| `governance_security` | `society_economy` |
| `infrastructure_information` | `environment`, `governance_security`, `physical_built` |

This is an authority allow-list, not a statement that every dependency is read on every Step.

## 4. same-Step dependencies

The current Standard generation-1 execution plan has no production same-Step dependency edge:

```text
same_step_dependencies = []
```

Phase 3 describes `participation -> resident` control context as a same-Step semantic dependency example, but the current Standard executor does not materialize that edge as production runtime authority. Generation 1 therefore MUST NOT claim it yet. When an executable Standard edge is introduced, DomainRegistry and the execution plan must change together rather than silently broadening generation 1.

## 5. Intent capabilities

`emitted_intent_kinds` is derived from current executable producer paths, not from the future catalog. The generation-1 source/target/partition/kind tuples are exactly:

| source | target | target partition | intent kind |
|---|---|---|---|
| `environment` | `spatial` | `spatial.terrain_geometry` | `spatial.intent.geometry-deform` |
| `physical_built` | `spatial` | `spatial.terrain_geometry` | `spatial.intent.geometry-carve` |
| `physical_built` | `spatial` | `spatial.terrain_geometry` | `spatial.intent.geometry-deform` |
| `physical_built` | `spatial` | `spatial.terrain_geometry` | `spatial.intent.geometry-fill` |
| `resident` | `physical_built` | `physical.presence` | `physical.intent.move` |

For each descriptor:

- `emitted_intent_kinds` is the distinct set of kinds from rows where the domain is source;
- `accepted_intent_kinds` is the distinct set of kinds from rows where the domain is target;
- runtime enforcement also binds source, target, target partition, and kind as one tuple, so sharing a kind does not widen authority to a different partition or domain.

Any Intent emitted outside these tuples is a fail-closed runtime error.

Future catalog entries such as unimplemented detail or service Intents are not generation-1 authority.

## 6. Domain events and invariant IDs

`DomainCandidateOutputV1` generation 1 has no DomainEvent output channel, so no domain can currently emit a DomainEvent through the production executor:

```text
emitted_event_kinds = []
```

This is a closed-world statement about the implemented runtime surface, not a placeholder for the Phase 4 future event catalog.

Likewise, generation-1 domain runtime output has no domain-owned `InvariantResultV1` emission channel:

```text
invariant_ids = []
```

Core `CrossDomainTransaction` invariants remain Core transaction authority and are not reclassified as domain-emitted invariant capability.

## 7. Snapshot / recovery binding

`core.domain-registry` uses the exact wire fixed by `phase4-core-snapshot-section-wire.md`.

Recovery MUST:

1. decode the descriptor material;
2. reject malformed, duplicate, unknown, noncanonical, incomplete, or generation-mismatched material;
3. reconstruct `DomainRegistryStateV1`;
4. require exact Standard generation-1 material;
5. recompute `mv.core-domain-registry-state.v1` from the restored material;
6. compare that recomputed authority digest with the Snapshot section logical digest.

A stored digest by itself is never recovery authority.

## 8. Runtime binding

The same generation-1 registry used for Snapshot is the runtime capability authority. The executor MUST reject an Intent when its source/target/partition/kind tuple is not registered. Snapshot-only or metadata-only registries are forbidden.
