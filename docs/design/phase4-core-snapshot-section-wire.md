# Phase 4 amendment: six Core Snapshot section wire / semantic authority

Status: Complete candidate / P4-04 amendment  
Tracking: Issue #253  
Parents: `phase4-persistence-specification.md`, `phase4-persistence-record-catalog.md`, `phase4-core-data-structures.md`, `phase4-config-specification.md`

## 1. Purpose

P4-04 already requires six Core recovery sections but previously did not define the exact section payload wire or the decode-normalized semantic digest mapping.

This amendment fixes those missing contracts for:

```text
core.world-state-header
core.scheduler-state
core.operation-state
core.detail-directory
core.domain-registry
core.config-state
```

Normative protobuf declarations are in:

```text
docs/design/proto/phase4-core-snapshot-sections-v1.proto
```

The protobuf serialized bytes are physical transport only. No Core logical digest is calculated from protobuf bytes. The verifier MUST decode, validate, reconstruct the semantic owner state, then compute the MV-DCBOR-v1 authority described below.

Digest-only payloads are invalid recovery material.

## 2. Common section rules

### 2.1 Section schema table

| section_id | Logical `section_schema` | logical item |
|---|---|---|
| `core.world-state-header` | `core.world-state / 1.0` | singleton `WorldStateHeaderV1` |
| `core.scheduler-state` | `core.scheduler-state / 1.0` | one scheduled Operation reference |
| `core.operation-state` | `core.operation-state / 1.0` | one `DurableOperationStateV1` |
| `core.detail-directory` | `core.detail-state / 1.0` | one region or one pending transition |
| `core.domain-registry` | `core.domain-registry-state / 1.0` | one domain runtime descriptor |
| `core.config-state` | `config.simulation-core / 1.0` | one normalized Config field |

`section_id` identifies Snapshot placement. `section_schema` identifies the recovered semantic authority and therefore is not required to equal `section_id`.

### 2.2 Fragment payload placement

The messages in `phase4-core-snapshot-sections-v1.proto` are carried inside:

```text
SnapshotSectionFragmentV1.fragment_payload
```

The existing outer fragment remains authoritative for:

- `section_id`
- `fragment_index`
- `fragment_count`
- `item_count`
- optional record range

Core sections MUST omit outer `first_record_id` / `last_record_id`. A Core item is not always a 16-octet domain record identity, and generating artificial record IDs only for fragmentation is forbidden.

### 2.3 Fragmentation

Existing P4-04 limits remain unchanged:

```text
target_uncompressed_bytes = 32 MiB
hard_max_uncompressed_bytes = 64 MiB
```

Rules:

1. concatenate inner items in the section-specific canonical order below;
2. split only between logical items;
3. every fragment repeats the section-level metadata needed to bind it to the same frozen cut;
4. repeated metadata MUST be identical across all fragments;
5. outer `item_count` MUST equal the number of inner logical items in that fragment;
6. sum of outer `item_count` MUST equal `LogicalSnapshotSection.logical_item_count`;
7. zero-item scheduler / operation / detail sections still emit exactly one fragment carrying the section metadata with `item_count = 0`;
8. `core.world-state-header` emits exactly one fragment with `item_count = 1`.

### 2.4 Frozen Step binding

Every Core payload belongs to exactly the same `snapshot_step`.

- header: `step == snapshot_step`.
- scheduler: `world_step == snapshot_step`.
- operation: every fragment `basis_step == snapshot_step`.
- detail: every fragment `basis_step == snapshot_step`.
- domain registry: every fragment `basis_step == snapshot_step`.
- config: every fragment `basis_step == snapshot_step`.

A mismatch is candidate-fatal before Snapshot commit.

### 2.5 Protobuf validation profile

For authoritative Core snapshot schema `1.0`:

- malformed protobuf: reject;
- duplicate occurrence of a singular field: reject;
- unknown field number: reject;
- unknown enum value: reject;
- invalid UTF-8: reject;
- StableToken field not matching StableToken rules: reject;
- fixed-size identity/hash length mismatch: reject;
- ZERO identity where runtime type forbids ZERO: reject;
- repeated values that are required canonical but are unsorted or duplicated: reject; do not silently sort during verification;
- unsupported schema major or minor: reject.

A future minor may define explicit forward compatibility. Protobuf's generic unknown-field preservation is not, by itself, permission to ignore unknown authoritative semantics.

## 3. `core.world-state-header`

Wire:

```proto
message CoreWorldStateHeaderSnapshotWireV1 {
  bytes world_id = 1;
  uint64 step = 2;
  bytes world_seed_digest = 3;
  uint64 config_generation = 4;
  uint64 master_generation = 5;
  uint32 rate_generation = 6;
  optional bytes previous_state_digest = 7;
}
```

Validation:

- `world_id`: 16 octets, non-ZERO.
- `world_id == LogicalSnapshotManifest.world_id`.
- `step == LogicalSnapshotManifest.snapshot_step`.
- `world_seed_digest`: 32 octets and equals SHA-256 of `LogicalSnapshotManifest.world_seed`.
- `config_generation >= 1` and equals manifest `simulation_config_generation`.
- `master_generation >= 1` and equals manifest `master_generation`.
- `previous_state_digest`, when present, exactly 32 octets.

Logical item count:

```text
1
```

The semantic verifier reconstructs `WorldStateHeaderV1` and computes the section digest:

```text
DomainHash("mv.core-world-state-header.v1",
  map {
    0: "core.world-state",
    1: world_id bytes16,
    2: step uint64,
    3: world_seed_digest bytes32,
    4: config_generation uint64,
    5: master_generation uint64,
    6: rate_generation uint32,
    7: [] | [previous_state_digest bytes32]
  })
```

That value is `LogicalSnapshotSection.logical_content_digest` for `core.world-state-header`.

This dedicated section digest does not replace `StateDiagnosticV1.state_digest`. After all 103 sections are reconstructed, recovery MUST reconstruct `WorldStateV1` and recompute the normal `StateDiagnosticV1` before READY.

## 4. `core.scheduler-state`

Wire:

```proto
message CoreSchedulerStateSnapshotFragmentWireV1 {
  uint64 world_step = 1;
  uint64 next_schedulable_step = 2;
  optional uint64 freeze_step = 3;
  repeated CoreScheduledOperationSnapshotWireV1 scheduled_operations = 4;
}

message CoreScheduledOperationSnapshotWireV1 {
  bytes operation_id = 1;
  uint64 effective_step = 2;
  bytes same_step_order_key = 3;
}
```

Canonical logical item order is the flattened scheduler order:

```text
(effective_step numeric ASC,
 same_step_order_key bytewise ASC,
 operation_id bytewise ASC)
```

`same_step_order_key` is exactly the P4-04/P4-05 `SameStepOrderKeyDbV1` 55-octet representation.

Additional validation:

- OperationId exactly 16 octets and non-ZERO;
- no duplicate OperationId;
- no duplicate complete scheduler key;
- `freeze_step`, when present, MUST be `< next_schedulable_step`;
- an entry MUST not be earlier than the retained scheduler boundary accepted by `OperationSchedulerStateV1`;
- metadata is identical across fragments.

Logical item count:

```text
number of scheduled Operation references
```

Verifier reconstruction:

1. concatenate entries by `fragment_index`;
2. validate canonical order;
3. reconstruct `ScheduledOperationRefV1` using `SameStepOrderKey.FromDatabaseBytes`;
4. reconstruct `OperationSchedulerStateV1(next_schedulable_step, freeze_step, entries)`;
5. call `OperationSchedulerSubstateV1.Canonicalize(scheduler, world_step)`.

`logical_content_digest` MUST equal the resulting `WorldSubstateRefV1.CanonicalDigest` exactly, including the existing empty-state special case.

No parallel snapshot-only scheduler digest is introduced.

## 5. `core.operation-state`

Wire:

```proto
message CoreOperationStateSnapshotFragmentWireV1 {
  uint64 basis_step = 1;
  repeated DurableOperationStateSnapshotWireV1 operations = 2;
}

message DurableOperationStateSnapshotWireV1 {
  bytes operation_id = 1;
  bytes operation_payload_digest = 2;
  DurableOperationLifecycleSnapshotWireV1 lifecycle = 3;
  optional uint64 accepted_sequence = 4;
  optional uint64 scheduled_sequence = 5;
  optional uint64 effective_step = 6;
  optional uint64 terminal_sequence = 7;
  optional CoreOperationResultStatusSnapshotWireV1 terminal_status = 8;
  optional string result_code = 9;
  optional bytes rich_result_payload = 10;
}
```

Lifecycle wire values are exactly:

```text
1 ACCEPTED_DURABLE
2 SCHEDULED_DURABLE
3 TERMINAL_DURABLE
```

Terminal status wire values mirror `CoreOperationResultStatusV1` 1..7. A terminal state may only use a status accepted by `OperationLifecycleRulesV1.IsTerminalResult`.

Canonical item order:

```text
operation_id bytewise ASC
```

Shape validation is exactly the current `DurableOperationSubstateV1` authority:

- ACCEPTED: accepted sequence present; scheduled/effective/terminal/result absent.
- SCHEDULED: accepted, scheduled, effective present; terminal/result absent.
- TERMINAL: terminal sequence/status/result code present and either direct-terminal provenance or scheduled-terminal provenance is structurally complete.
- payload digest exactly 32 octets.
- result code, when present, is StableToken.
- duplicate OperationId reject.

Logical item count:

```text
number of DurableOperationStateV1 rows
```

Verifier reconstructs the exact `DurableOperationStateV1` sequence and calls:

```text
DurableOperationSubstateV1.Canonicalize(operations)
```

`logical_content_digest` MUST equal that existing `WorldSubstateRefV1.CanonicalDigest`, including its existing empty-state special case.

## 6. `core.detail-directory`

Wire is a canonical stream of heterogeneous directory items:

```proto
message CoreDetailDirectorySnapshotFragmentWireV1 {
  uint64 basis_step = 1;
  repeated DetailDirectoryItemSnapshotWireV1 items = 2;
}

message DetailDirectoryItemSnapshotWireV1 {
  oneof item {
    DetailRegionSnapshotWireV1 region = 1;
    DetailPendingTransitionSnapshotWireV1 pending_transition = 2;
  }
}
```

Canonical item order:

1. all regions by `detail_region_id` bytewise ASC;
2. then all pending transitions using `DetailTransitionCanonicalOrderV1`:
   - `required_effective_step` ASC,
   - `semantic_priority` ASC,
   - `detail_region_id` ASC,
   - standard domain rank ASC,
   - `trigger_id` ASC.

A fragment sequence MUST NOT return to region items after the first pending-transition item.

### 6.1 Region semantics

`DetailRegionSnapshotWireV1` reconstructs exactly:

- `DetailRegionId`
- `SpatialScopeRef`
- `LineageGeneration`
- `LastTransitionStep`
- `LevelByDomain`
- `ActiveGuards`

`level_by_domain` is DomainToken ASCII ascending, unique. Every domain token must be registered for the restored world. Detail wire values map as:

```text
1 -> D0Entity (runtime value 0)
2 -> D1LocalAggregate (runtime value 1)
3 -> D2RegionalAggregate (runtime value 2)
4 -> D3BoundarySummary (runtime value 3)
```

`active_guards` is StableToken ASCII ascending and unique.

### 6.2 Pending transition semantics

`DetailPendingTransitionSnapshotWireV1` stores every non-derived field needed to reconstruct `DetailTransitionCandidateV1`:

```text
detail_region_id
domain_token
current_level
target_level
required_effective_step
semantic_priority
trigger_source
trigger_id
trigger_observed_step
estimated_record_count
```

`Direction` is derived from current/target level and is not separately persisted.

`estimated_record_count >= 1`, current and target differ, IDs are non-ZERO, domain is registered, and trigger source is one of the defined v1 values.

Logical item count:

```text
region_count + pending_transition_count
```

Verifier reconstructs `DetailDirectoryV1(regions, pendingTransitions)` and calls:

```text
DetailDirectorySubstateV1.Canonicalize(directory)
```

`logical_content_digest` MUST equal that existing Core detail-state digest, including the existing empty-directory special case.

## 7. `core.domain-registry`

P4-01 already fixes the logical structure:

```text
DomainRegistryStateV1 {
  registry_generation,
  domains: ordered map<DomainToken, DomainRuntimeDescriptorV1>
}
```

This amendment fixes its snapshot wire and canonical authority.

Wire:

```proto
message CoreDomainRegistrySnapshotFragmentWireV1 {
  uint64 basis_step = 1;
  uint32 registry_generation = 2;
  repeated DomainRuntimeDescriptorSnapshotWireV1 domains = 3;
}
```

Each descriptor stores exactly the P4-01 logical fields:

```text
domain_token
domain_rank
domain_schema
owned_partitions
state_read_dependencies
same_step_dependencies
accepted_intent_kinds
emitted_intent_kinds
emitted_event_kinds
invariant_ids
```

Canonical item order:

```text
domain_token ASCII bytewise ASC
```

Every repeated StableToken list inside a descriptor is ASCII bytewise ascending and unique. `owned_partitions` must contain only partitions actually owned by the domain. In the standard world:

- exactly 8 domain descriptors are required;
- their union of `owned_partitions` is exactly the standard 97-partition set;
- ownership/rank agrees with `StandardDomainPartitionRegistry`;
- the domain-token set equals logical manifest `required_domains`.

Logical item count:

```text
number of DomainRuntimeDescriptorV1 entries
```

The canonical domain-registry digest is fixed as:

```text
DomainHash("mv.core-domain-registry-state.v1",
  map {
    0: "core.domain-registry-state",
    1: registry_generation uint32,
    2: [
      map {
        0: domain_token,
        1: domain_rank uint16,
        2: domain_schema.schema_id,
        3: domain_schema.major,
        4: domain_schema.minor,
        5: owned_partitions[],
        6: state_read_dependencies[],
        7: same_step_dependencies[],
        8: accepted_intent_kinds[],
        9: emitted_intent_kinds[],
        10: emitted_event_kinds[],
        11: invariant_ids[]
      }, ...
    ]
  })
```

The restored `WorldSubstateRefV1` is:

```text
schema = core.domain-registry-state / 1.0
canonical_digest = value above
```

This amendment therefore closes a previous implementation ambiguity: a standard initialized world MUST materialize the P4-01 domain registry authority and MUST NOT substitute an arbitrary digest-only `core.domain-registry-state` ref.

## 8. `core.config-state`

Wire:

```proto
message CoreConfigStateSnapshotFragmentWireV1 {
  uint64 basis_step = 1;
  uint64 generation = 2;
  string schema_version = 3;
  string component = 4;
  repeated CoreConfigFieldSnapshotWireV1 fields = 5;
}
```

For schema `1.0`:

```text
schema_version = "1.0"
component = "simulation-core"
```

Each field contains exactly one value kind:

- `sint64 int64_value`
- `bool bool_value`
- `string string_value`

These map exactly to the current `EffectiveCoreConfig.Fields` runtime types `long`, `bool`, and `string`.

Canonical item order:

```text
path ASCII bytewise ASC
```

Rules:

- duplicate path reject;
- field set MUST exactly match `CoreConfigSchema.Fields` for schema 1.0;
- each value MUST pass its `ConfigFieldSpec.Validate` rule;
- cross-field constraints and reduced step-rate normalization MUST be satisfied;
- string values are ASCII for current schema enums/tokens;
- `generation >= 1` and equals both header config generation and logical manifest `simulation_config_generation`.

Logical item count:

```text
number of normalized Config fields
```

`basis_step`, `generation`, fragment boundaries, and protobuf bytes do not enter the Config semantic digest.

Verifier reconstructs the normalized field dictionary and computes the existing Config authority exactly:

```text
DomainHash("mv.config.v1",
  [
    "1.0",
    "simulation-core",
    [
      [path, typed_value],
      ... path ASCII ascending
    ]
  ])
```

`logical_content_digest` MUST equal:

- the recomputed `mv.config.v1` digest;
- `LogicalSnapshotManifest.simulation_config_digest`;
- the config digest used to reconstruct `WorldStateV1`.

`NormalizedToml` is a presentation/storage rendering of the normalized field dictionary and is not separately authoritative snapshot material.

## 9. Cross-section reconstruction and verification

A Core section verifier MUST NOT trust `logical_content_digest` as input authority. Recovery sequence is:

1. validate outer fragment coverage and stored/chunk digests;
2. decode Core fragment payloads;
3. validate exact schema/version and canonical item ordering;
4. verify every Core fragment binds to the same `snapshot_step`;
5. reconstruct header, scheduler, operation state, detail directory, domain registry, and normalized Config;
6. recompute each section semantic digest according to this document;
7. compare recomputed values with each logical section digest;
8. compare Config/header/manifest generation/digest/world/master bindings;
9. reconstruct the 97 authoritative partition states from their owner schemas;
10. build the restored `WorldStateV1` from reconstructed values and recompute the normal `StateDiagnosticV1`;
11. validate history anchor / continuity and then replay H+1 onward according to P4-04.

A mismatch at any step invalidates that Snapshot candidate. No partial restore is permitted.

## 10. Snapshot logical digest independence

The following MUST NOT change any `logical_content_digest`:

- protobuf field serialization order where semantic decode is equivalent;
- unknown-field storage, because unknown authoritative fields are rejected in schema 1.0;
- physical fragment boundary;
- physical chunk boundary;
- Zstandard vs NONE;
- Zstandard level;
- file path or filesystem ordering.

Only decoded normalized semantic state determines logical section digests.

## 11. Implementation handoff

After this amendment is merged, implementation under #252 must provide:

1. frozen production owner material for detail, domain registry, and Config rather than test-only material;
2. exact codecs for the messages in `phase4-core-snapshot-sections-v1.proto`;
3. Core `CanonicalSnapshotSectionMaterialV1` providers using the item-count rules above;
4. Core semantic verifiers registered with the PR #251 reassembly pipeline;
5. State(30) freeze -> State(31) advance -> Snapshot(30) commit/recovery smoke using production providers;
6. negative tests for malformed/unknown/unsorted/duplicate/stale-Step/digest mismatch cases.

97 domain partition payload codecs/providers remain separate owner work and are not redefined by this amendment.

## 12. Acceptance

This amendment is complete when:

- all six Core section protobuf messages are exact;
- item/fragment boundaries are deterministic;
- every section can be losslessly reconstructed;
- scheduler/operation/detail/config reuse existing runtime authority exactly;
- domain registry canonical authority is implementation-unambiguous and matches P4-01;
- no digest-only payload can satisfy semantic verification;
- terminology matches P4-01/P4-03/P4-04.
