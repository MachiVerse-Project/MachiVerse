# Phase 4 P4-04 Amendment: Snapshot Fragment Parent Wire

Status: Complete / implementation clarification  
Tracking: Issue #248  
Parent: `phase4-persistence-specification.md`  
Record catalog: `phase4-persistence-record-catalog.md`

## 1. Purpose

P4-04 main specification section 22 predates the completed record-boundary fragmentation contract in the persistence record catalog. The main specification describes `SnapshotChunkPayloadV1` as directly carrying `SnapshotSectionPayloadV1`, while the completed record catalog later requires `SnapshotSectionFragmentV1` with fragment index/count, record ranges, item count, and schema-owned fragment payload.

Standard persistence format v1.0 requires one exact physical chunk logical-payload encoding. This amendment removes the ambiguity without changing logical Snapshot semantics.

## 2. Canonical v1.0 chunk logical payload

The standard physical chunk logical payload is:

```proto
message SnapshotChunkPayloadV1 {
  repeated SnapshotSectionFragmentV1 fragments = 1;
}

message SnapshotSectionFragmentV1 {
  string section_id = 1;
  uint32 fragment_index = 2;
  uint32 fragment_count = 3;
  optional bytes first_record_id = 4;
  optional bytes last_record_id = 5;
  uint64 item_count = 6;
  bytes fragment_payload = 7;
}
```

The earlier `repeated SnapshotSectionPayloadV1 sections = 1` representation in `phase4-persistence-specification.md` section 22 is superseded for the standard v1.0 physical chunk profile. It is not a second accepted canonical encoding.

## 3. Canonical ordering

Within one `SnapshotChunkPayloadV1`:

1. `section_id` is ASCII bytewise ascending.
2. fragments belonging to the same section are contiguous.
3. within a section, `fragment_index` is exactly ascending.
4. the complete snapshot reassembly still requires each section to contain exactly `0..fragment_count-1` across its physical chunks.
5. first/last record ranges must not overlap and remain canonical record-id order.

Chunk boundaries do not reset a section's fragment index.

## 4. `fragment_payload` authority

`fragment_payload` is owned by the section schema implementation. This amendment does not create a generic record payload schema for domains or Core recovery sections.

The following remain authoritative:

- section semantic schema and normalization rules;
- logical item count;
- logical content digest;
- domain record ordering;
- Core recovery-section ordering;
- section-specific decode/validation.

Physical protobuf bytes, fragment boundaries, chunk boundaries, and compression are not logical digest authority.

## 5. Logical digest invariance

`LogicalSnapshotSection.logical_content_digest` is computed from schema-normalized semantic section content exactly as before.

`SnapshotDigest` remains derived from the normalized logical manifest and therefore excludes:

- protobuf serialization choices;
- fragment boundaries;
- physical chunk boundaries;
- compression;
- stored payload digest.

Refragmenting otherwise identical semantic content must not change the logical section digest or SnapshotDigest.

## 6. Validation

A chunk/reassembled snapshot is invalid if any of the following occurs:

- unknown/malformed section id;
- non-canonical section ordering;
- non-contiguous same-section fragments inside a chunk;
- fragment index gap/duplicate/reorder;
- inconsistent `fragment_count` for one section;
- overlapping/reversed record ranges;
- fragment item-count sum differs from logical section item count;
- reassembled semantic digest differs from the logical section digest;
- one schema-owned item exceeds the 64 MiB hard maximum;
- required 103-section set is incomplete.

Use the existing P4-04 persistence error classes, especially `persistence.snapshot-fragment-invalid`, `persistence.snapshot-section-missing`, `persistence.snapshot-item-too-large`, and `persistence.snapshot-digest-mismatch`.

## 7. Scope boundary

This amendment fixes only the parent wire relationship needed to carry P4-04 fragments. It does not claim that schema-owner payload serializers for all six Core recovery sections and all 97 domain partitions are already implemented.

QA-04 release evidence remains unavailable until the actual reference-world section payload providers, complete 103-section serialization, reassembly verification, and normal running-snapshot drain are assembled.

Refs #16 #240 #247 #248
