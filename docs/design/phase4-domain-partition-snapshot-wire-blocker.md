# Phase 4 canonical domain-partition Snapshot wire contract

Status: **DECIDED / normative wire amendment**  
Tracking: Issue #240  
Scope: INT-03 canonical Snapshot / recovery  
Persistence schema: v1.x

## 1. Purpose

Stage 2 requires all authoritative domain partitions that are actually materialized by the runtime
to be serialized from `DomainPartitionStateV1<TPayload>` and restored without inventing
persistence semantics.

The original Phase 4 persistence specification fixed the outer Snapshot framing and P4-05 fixed
the 97 standard partition payload schemas semantically, but the exact inner protobuf contracts were
not fully assigned. This document is the normative amendment that closes that wire-design gap.

This decision does **not** mean that the 97 production providers or the complete 103-section
Snapshot/recovery implementation are finished. Stage 2 remains open until actual runtime material,
production codecs, semantic verification, manifest verification, recovery, tests, and CI satisfy
#240.

## 2. Existing contracts that remain authoritative

The following existing contracts are reused without semantic replacement:

- exact 97 `StandardDomainPartitionRegistry` identities and ownership;
- exact P4-05 `StandardDomainPayloadSchemaRegistry` semantic fields, scalar families, optionality,
  collection rules, and validators;
- `DomainRecordEnvelopeV1<TPayload>` semantic envelope;
- `PartitionStateHeaderV1.CreateCanonical` partition semantic digest;
- exact 103 logical section registry: six Core sections plus 97 Domain sections;
- `SnapshotSectionFragmentV1` outer protobuf wire;
- 32 MiB target / 64 MiB hard maximum and record-boundary fragmentation;
- `MVCHNK01` physical chunk framing;
- logical chunk semantic-digest and stored-payload digest verification;
- Snapshot staging / atomic finalization / SQLite catalog commit boundary;
- the existing six exact Core section wires and semantic verifiers;
- Phase 1 `SnapshotDigest = DomainHash("mv.snapshot.v1", normalized logical manifest without the
  snapshot_digest field)` rule.

Protobuf bytes are a physical representation. They are not the authoritative world semantic hash
input.

## 3. Common protobuf types

`SchemaVersionWireV1` is the existing common message:

```proto
message SchemaVersionWireV1 {
  uint32 major = 1;
  uint32 minor = 2;
}
```

Both values must be `<= 65535`.

The Domain Snapshot wire uses:

```proto
enum DomainDetailLevelWireV1 {
  DOMAIN_DETAIL_LEVEL_D0_ENTITY = 0;
  DOMAIN_DETAIL_LEVEL_D1_LOCAL_AGGREGATE = 1;
  DOMAIN_DETAIL_LEVEL_D2_REGIONAL_AGGREGATE = 2;
  DOMAIN_DETAIL_LEVEL_D3_BOUNDARY_SUMMARY = 3;
}
```

All enum values outside `0..3` are rejected. The numeric value is exactly the existing
`DetailLevelV1` numeric value.

## 4. `PartitionStateHeaderWireV1`

```proto
message PartitionStateHeaderWireV1 {
  string partition_id = 1;
  string owner_domain = 2;
  string partition_schema_id = 3;
  SchemaVersionWireV1 partition_schema_version = 4;
  uint64 revision = 5;
  uint64 basis_step = 6;
  DomainDetailLevelWireV1 detail_level = 7;
  uint64 item_count = 8;
  bytes canonical_digest = 9;
}
```

Validation:

- `partition_id`, `owner_domain`, and `partition_schema_id` are valid `StableToken` values;
- identity must exactly equal `StandardDomainPartitionRegistry.Get(partition_id)`;
- `partition_schema_version` must equal the registered partition schema version;
- `revision >= 1`;
- `canonical_digest` is exactly 32 bytes;
- `basis_step`, `detail_level`, `item_count`, and digest must equal the frozen
  `PartitionStateHeaderV1` for this Snapshot cut.

## 5. `DomainRecordSnapshotV1`

```proto
message DomainRecordSnapshotV1 {
  bytes record_id = 1;
  string record_schema_id = 2;
  SchemaVersionWireV1 record_schema_version = 3;
  uint64 revision = 4;
  uint64 created_step = 5;
  optional uint64 retired_step = 6;
  DomainDetailLevelWireV1 detail_level = 7;
  optional bytes lineage_ref = 8;
  DomainPayloadWireV1 payload = 9;
}
```

Validation:

- `record_id` is exactly 16 bytes and non-zero;
- `record_schema_id` / version exactly equal the partition registry record schema;
- `revision >= 1`;
- when present, `retired_step >= created_step`;
- `created_step <= partition basis_step` and present `retired_step <= partition basis_step`;
- `lineage_ref`, when present, is exactly 16 bytes and non-zero;
- detail level is `0..3`;
- records are strictly `record_id` bytewise ascending and duplicate-free.

## 6. `DomainPartitionSnapshotV1`

```proto
message DomainPartitionSnapshotV1 {
  PartitionStateHeaderWireV1 header = 1;
  repeated DomainRecordSnapshotV1 records = 2;
}
```

The message is used as the exact `fragment_payload` for every Domain partition fragment. Section
fragment semantics are fixed in section 13 of this document.

## 7. Standard Domain payload field numbering

The 97 standard record payload schemas do **not** receive 97 independent hand-written persistence
messages in v1. Instead, one generic typed wire is bound to the already-authoritative P4-05 schema
registry.

For every standard partition record schema v1.0:

```text
field_number = 1-based ordinal in StandardDomainPayloadSchemaRegistry.Get(partition_id).Fields
```

The current P4-05 order, field name, field kind, and optionality are therefore normative persistence
schema metadata for record schema v1.0.

Rules:

- changing a field number, field name, kind, or optionality is not allowed silently;
- an incompatible change requires the record schema version to change and an explicit persistence
  migration/compatibility decision;
- serializers and decoders must use the explicit schema descriptor; reflection-based object layout
  is not a schema authority.

## 8. `DomainPayloadWireV1`

```proto
message DomainPayloadWireV1 {
  repeated DomainPayloadFieldWireV1 fields = 1;
}

message DomainPayloadFieldWireV1 {
  uint32 field_number = 1;
  DomainPayloadValueWireV1 value = 2;
}
```

Rules:

- `fields` are strictly `field_number` ascending and duplicate-free;
- `field_number` starts at 1;
- every required P4-05 descriptor field appears exactly once;
- an optional field is omitted when semantically absent;
- absent and present-with-zero are distinct;
- an unknown/out-of-range field number is rejected for the selected record schema version;
- a value whose wire arm does not match the P4-05 field kind is rejected.

Top-level field names are intentionally not duplicated into each stored record. The descriptor
binds the numeric field to the semantic name.

## 9. `DomainPayloadValueWireV1`

```proto
message DomainPayloadValueWireV1 {
  oneof value {
    PartitionRecordRefWireV1 ref_value = 1;
    DomainRefListWireV1 ref_list_value = 2;
    bytes id128_value = 3;
    string token_value = 4;
    DomainTokenListWireV1 token_list_value = 5;
    uint32 uint32_value = 6;
    uint64 uint64_value = 7;
    sint32 sint32_value = 8;
    sint64 sint64_value = 9;
    bool bool_value = 10;
    bytes digest_value = 11;
    Vec3Int64WireV1 vec3_value = 12;
    QuaternionQ30WireV1 quat_value = 13;
    DomainTokenUInt8MapWireV1 token_uint8_map_value = 14;
    DomainTokenUInt32MapWireV1 token_uint32_map_value = 15;
    DomainTokenInt32MapWireV1 token_int32_map_value = 16;
    DomainNestedListWireV1 nested_list_value = 17;
    DomainNestedValueWireV1 nested_value = 18;
  }
}

message PartitionRecordRefWireV1 {
  string partition_id = 1;
  bytes record_id = 2;
}

message DomainRefListWireV1 {
  repeated PartitionRecordRefWireV1 values = 1;
}

message DomainTokenListWireV1 {
  repeated string values = 1;
}

message Vec3Int64WireV1 {
  sint64 x = 1;
  sint64 y = 2;
  sint64 z = 3;
}

message QuaternionQ30WireV1 {
  sint32 x = 1;
  sint32 y = 2;
  sint32 z = 3;
  sint32 w = 4;
}

message DomainTokenUInt8MapEntryWireV1 {
  string key = 1;
  uint32 value = 2;
}

message DomainTokenUInt8MapWireV1 {
  repeated DomainTokenUInt8MapEntryWireV1 entries = 1;
}

message DomainTokenUInt32MapEntryWireV1 {
  string key = 1;
  uint32 value = 2;
}

message DomainTokenUInt32MapWireV1 {
  repeated DomainTokenUInt32MapEntryWireV1 entries = 1;
}

message DomainTokenInt32MapEntryWireV1 {
  string key = 1;
  sint32 value = 2;
}

message DomainTokenInt32MapWireV1 {
  repeated DomainTokenInt32MapEntryWireV1 entries = 1;
}
```

Wrapper messages are intentional: an explicitly present empty list/map remains distinguishable
from an absent optional top-level field.

### 9.1 P4-05 kind mapping

| P4-05 kind | exact wire arm |
|---|---|
| `Ref` | `ref_value` |
| `RefList` | `ref_list_value` |
| `Id128` | `id128_value` exactly 16 non-zero bytes |
| `Token` | `token_value` valid StableToken |
| `TokenList` | `token_list_value` with P4-05 canonical token ordering |
| `OrderedTokenList` | `token_list_value` with the schema-defined semantic order |
| `Ratio` | `uint32_value`, `0..1_000_000` |
| `Step`, `UInt64` | `uint64_value` |
| `UInt8` | `uint32_value`, `<=255` |
| `UInt16` | `uint32_value`, `<=65535` |
| `UInt32` | `uint32_value` |
| `Int32`, `Temperature`, `Pressure` | `sint32_value` plus existing scalar-range rules |
| `Int64`, `Length`, `Mass`, `Volume`, `Power`, `Energy`, `Money` | `sint64_value` plus existing scalar-range rules |
| `Bool` | `bool_value` |
| `Digest` | `digest_value` exactly 32 bytes |
| `Vec3` | `vec3_value` |
| `Quat` | `quat_value`, existing QuaternionQ30 canonical validation |
| `OrderedTokenUInt8Map` | `token_uint8_map_value` |
| `OrderedTokenUInt32Map` | `token_uint32_map_value` |
| `OrderedTokenInt32Map` | `token_int32_map_value` |
| `OrderedNestedList` | `nested_list_value` |
| `RuleAst` | `nested_value` |

All ordered token maps have strictly ASCII-ascending unique keys. Reference/list ordering remains
whatever P4-05 declares for that field; no protobuf container iteration order is semantic.

## 10. Nested values: explicit registry, no reflection

The generic wire for nested semantic values is:

```proto
message DomainNestedValueWireV1 {
  string schema_id = 1;
  SchemaVersionWireV1 schema_version = 2;
  repeated DomainPayloadFieldWireV1 fields = 3;
}

message DomainNestedListWireV1 {
  repeated DomainNestedValueWireV1 values = 1;
}
```

A nested value is accepted only when an explicit registered nested schema descriptor exists for
that `schema_id` and version. The descriptor fixes, exactly as for a top-level record, each nested
field number, semantic name, kind, optionality, and canonical list/order rule.

The wire format is therefore decided here without inventing semantic fields for nested domain types
whose authoritative field schema has not yet been implemented.

Normative rules:

- no reflection-derived fields;
- no JSON / arbitrary object serializer;
- no type-name-as-schema fallback;
- unknown nested schema/version fails Snapshot production and recovery;
- missing nested codec fails closed;
- nested fields are field-number ascending and duplicate-free;
- the registered nested semantic validator runs after decode;
- nested `OrderedNestedList` order is the order fixed by that nested schema owner.

The already-concrete participation policy rule is registered as:

```text
schema_id = domain.nested.participation-policy-rule
version   = 1.0
field 1   = priority : Int32, required
field 2   = rule_id  : Token, required
order     = (priority ascending, rule_id ASCII ascending)
```

`BodyRegionStateV1`, `PerceivedFactV1`, and governance rule AST content must each receive an
explicit nested schema descriptor/codec before actual material containing those values can enter a
production Snapshot. Their semantic fields are not guessed by this persistence amendment.

## 11. Decode-normalized semantic mapping

Recovery follows this order:

1. parse protobuf using the exact wire contract;
2. validate fixed lengths, tokens, schema versions, field numbers, oneof arms, ranges, ordering,
   duplicates, required/optional presence, and nested-schema availability;
3. map each field through the P4-05 descriptor into normalized semantic values;
4. run the existing generic/scalar/reference/domain payload validators;
5. construct the explicit domain-owned payload type through its registered production provider;
6. recompute that payload's canonical semantic digest;
7. reconstruct `DomainRecordEnvelopeV1<TPayload>` values;
8. reconstruct the actual `DomainPartitionStateV1<TPayload>`;
9. recompute `PartitionStateHeaderV1.CreateCanonical` from the restored material;
10. accept only if the recomputed header matches the repeated wire/frozen header including its
    canonical digest.

A protobuf byte digest is never substituted for step 6 or step 9.

Unknown record schema/version, unknown payload field, wrong field kind, unavailable nested schema,
or a semantic digest mismatch is a recovery failure, not a best-effort partial load.

## 12. Domain section logical content digest

For a Domain partition logical section, `logical_item_count` equals the partition header
`item_count`.

`logical_content_digest` is the partition header `canonical_digest` produced by the existing
`PartitionStateHeaderV1.CreateCanonical` semantic rule.

Consequently physical fragmentation, chunk boundaries, compression, protobuf field ordering
choices allowed by protobuf, and physical file layout do not change the logical section identity.

## 13. Domain fragment payload semantics

Every Domain `SnapshotSectionFragmentV1.fragment_payload` is exactly one serialized
`DomainPartitionSnapshotV1`.

Rules:

1. **Every fragment repeats the complete partition header.**
2. `records` contains only a contiguous slice of the partition's canonical record sequence.
3. Outer `item_count` equals the inner `records` count for that fragment.
4. For a non-empty fragment, outer `first_record_id` and `last_record_id` are present and exactly
   equal the first and last inner record IDs.
5. Across all fragments, records are globally strictly bytewise ascending; duplicate or overlapping
   ranges are invalid.
6. Every repeated header is semantically field-equal and equals the frozen WorldState partition
   header for the Snapshot cut.
7. Sum of outer fragment `item_count` values equals header `item_count` and logical section
   `logical_item_count`.
8. A non-empty partition uses the existing record-boundary 32 MiB target / 64 MiB hard maximum
   fragmentation rules.
9. An empty **actual** partition is represented by exactly one fragment with the full header,
   zero records, outer `item_count = 0`, and absent first/last record IDs. This rule is not
   permission to fabricate unmaterialized partitions as empty.
10. Reassembly is complete only after recomputing the restored partition canonical digest and
    matching section/header/WorldState authority.

Repeating the small header makes every fragment independently attributable to one exact frozen
partition and avoids special first-fragment recovery semantics.

## 14. Exact logical Snapshot manifest wire

```proto
message LogicalSnapshotSectionWireV1 {
  string section_id = 1;
  string schema_id = 2;
  SchemaVersionWireV1 schema_version = 3;
  uint64 logical_item_count = 4;
  bytes logical_content_digest = 5;
  bool required = 6;
}

message RequiredAddonWireV1 {
  string addon_id = 1;
  string metadata_schema_id = 2;
  SchemaVersionWireV1 metadata_schema_version = 3;
  bytes metadata_payload = 4;
  bytes metadata_semantic_digest = 5;
}

message LogicalSnapshotManifestWireV1 {
  uint32 persistence_schema_major = 1;
  uint32 persistence_schema_minor = 2;
  bytes world_id = 3;
  bytes snapshot_id = 4;
  uint64 snapshot_step = 5;
  uint64 history_anchor_sequence = 6;
  bytes history_anchor_digest = 7;
  bytes state_continuity_token = 8;
  bytes world_seed = 9;
  uint64 simulation_config_generation = 10;
  bytes simulation_config_digest = 11;
  uint64 master_generation = 12;
  repeated string required_domains = 13;
  repeated RequiredAddonWireV1 required_addons = 14;
  repeated LogicalSnapshotSectionWireV1 sections = 15;
  bytes snapshot_digest = 16;
}
```

Validation:

- persistence schema major/minor fit uint16 and major is non-zero;
- world/snapshot ids are exactly 16 non-zero bytes;
- history anchor digest, state continuity token, simulation config digest, section content digests,
  addon semantic digests, and snapshot digest are exactly 32 bytes;
- world seed is exactly 32 bytes;
- simulation config generation is non-zero;
- required domains are unique ASCII ascending StableTokens;
- required addons are unique and ASCII ascending by `addon_id`;
- sections are unique and ASCII ascending by `section_id`;
- standard v1 Snapshot has exactly the registered 103 required sections and every one has
  `required = true`.

### 14.1 Required addon metadata

The standard runtime currently permits an empty `required_addons` list.

A non-empty addon entry is authoritative compatibility metadata and is accepted only when a
registered addon metadata codec exists for `(addon_id, metadata_schema_id, version)`.
`metadata_payload` is that addon's exact registered binary wire; it is not JSON or arbitrary
serialization. Decode must normalize it and recompute `metadata_semantic_digest` before the addon
entry is accepted.

This same `RequiredAddonWireV1` is the canonical type for other Phase 4 persistence records that
reference required addon compatibility metadata.

## 15. Exact logical `SnapshotDigest` normalization

The Phase 1 label remains unchanged:

```text
mv.snapshot.v1
```

The normalized MV-DCBOR-v1 value for `LogicalSnapshotManifestWireV1` **excluding field 16** is the
following integer-key map:

```text
map(15) {
  0  => persistence_schema_major,
  1  => persistence_schema_minor,
  2  => world_id bytes,
  3  => snapshot_id bytes,
  4  => snapshot_step,
  5  => history_anchor_sequence,
  6  => history_anchor_digest bytes,
  7  => state_continuity_token bytes,
  8  => world_seed bytes,
  9  => simulation_config_generation,
  10 => simulation_config_digest bytes,
  11 => master_generation,
  12 => required_domains array in ASCII order,
  13 => required_addons array in addon_id ASCII order,
  14 => sections array in section_id ASCII order
}
```

Each normalized addon entry is:

```text
map(5) {
  0 => addon_id,
  1 => metadata_schema_id,
  2 => metadata_schema_major,
  3 => metadata_schema_minor,
  4 => metadata_semantic_digest bytes
}
```

The addon physical `metadata_payload` bytes are not hashed after their semantic digest has been
verified.

Each normalized section entry is:

```text
map(7) {
  0 => section_id,
  1 => schema_id,
  2 => schema_major,
  3 => schema_minor,
  4 => logical_item_count,
  5 => logical_content_digest bytes,
  6 => required
}
```

Then:

```text
SnapshotDigest = DomainHash("mv.snapshot.v1", normalized_manifest_without_snapshot_digest)
```

Decode must recompute and compare this value before the logical manifest can be recovery authority.

## 16. Physical manifest integrity digest

The existing outer message remains:

```proto
message PhysicalSnapshotManifestV1 {
  LogicalSnapshotManifestWireV1 logical = 1;
  repeated PhysicalSnapshotChunkDescriptorV1 chunks = 2;
  bytes physical_manifest_digest = 3;
}
```

For v1, `physical_manifest_digest` is defined as:

```text
SHA-256(
  deterministic protobuf serialization of PhysicalSnapshotManifestV1
  with physical_manifest_digest omitted
)
```

The `chunks` list is validated in `chunk_index` ascending canonical order before computing or
verifying the digest. The deterministic serialization requirement is only for this **physical
integrity digest** and does not make protobuf bytes a world semantic digest.

The actual `manifest.pb` contains the computed 32-byte field 3. Staging/recovery must parse the
manifest, recompute field 3 from the field-3-omitted deterministic form, validate the logical
`SnapshotDigest`, and validate the physical chunk mapping before a Snapshot is eligible.

## 17. Fail-closed compatibility rules

Production Snapshot creation/recovery must reject rather than approximate when any of these occur:

- missing one of the required 103 logical section providers/material authorities;
- standard partition identity/schema mismatch;
- fake/header-only material substituted for unavailable actual runtime material;
- unknown record schema/version or payload field number;
- wrong payload oneof arm for its P4-05 field kind;
- unavailable nested schema/codec;
- reflection/JSON/arbitrary serialization fallback;
- fragment order/range/count mismatch;
- section/header/material canonical digest mismatch;
- manifest logical or physical digest mismatch;
- unsupported required addon metadata;
- incomplete decode that would require regenerating recovery data from a digest.

## 18. Stage 2 implementation consequences

This document resolves the **wire-design blocker**. It does not check off Stage 2 implementation.
The implementation under PR #265 / Issue #240 must still provide, at minimum:

1. exact codecs for the common header, record, typed payload, nested registry, and manifest wires;
2. explicit standard payload provider bindings for actual runtime payload material;
3. exact-97 production provider/material coverage without fabricated empty partitions;
4. Domain fragment serialization and reassembly using section 13;
5. manifest serialization plus logical/physical semantic validation in staging and recovery;
6. restore-to-material followed by existing canonical partition digest recomputation;
7. negative tests for malformed field numbers/kinds/ordering, nested codec absence, fragment overlap,
   header mismatch, manifest tampering, chunk corruption, and fake material;
8. complete CI on the final PR head before any ready/merge transition.

No separate implementation Issue is created. #240 remains the sole INT-03 implementation tracker.

## 19. Design rationale

This contract deliberately separates three concerns:

- **semantic schema authority:** P4-05 descriptors and explicit nested descriptors;
- **physical persistence representation:** exact protobuf field numbers/types defined here;
- **world equality/integrity:** decode-normalized MV-DCBOR/domain hashes already owned by the Core
  semantic contracts.

A common typed numeric-field wire avoids duplicating 97 large protobuf messages while still fixing
every persisted top-level field number under its record schema version. Explicit nested registries
prevent persistence from inventing domain semantics. Repeating the partition header per fragment
keeps reassembly simple and independently verifiable. The result is compact, deterministic,
migratable, and fail-closed without treating protobuf layout or fabricated Snapshot material as
world authority.
