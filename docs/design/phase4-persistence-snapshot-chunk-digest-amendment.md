# Phase 4 persistence amendment: fragmented chunk logical payload digest

Status: Complete / P4-04 amendment  
Tracking: Issue #250  
Parent: `phase4-persistence-specification.md`, `phase4-persistence-snapshot-fragment-amendment.md`

## 1. Purpose

The v1.0 physical chunk header already requires `logical_payload_digest` to bind the decoded semantic payload rather than raw protobuf serialization bytes. After the fragment parent-wire amendment, the decoded `SnapshotChunkPayloadV1` semantic value is an ordered sequence of `SnapshotSectionFragmentV1` values. This document fixes the normalization used for that physical integrity digest.

This digest is not Snapshot logical identity. `LogicalSnapshotSection.logical_content_digest` and `SnapshotDigest` remain independent of protobuf serialization, compression, physical fragmentation and chunk placement.

## 2. Canonical digest

Standard v1.0 uses:

```text
SnapshotChunkLogicalPayloadDigestV1 =
  DomainHash("mv.snapshot-chunk-payload.v1", normalized_fragment_array)
```

`normalized_fragment_array` preserves the canonical fragment order required by the fragment parent-wire amendment.

Each decoded fragment is mapped to MV-DCBOR-v1 as a map with exactly seven unsigned integer keys:

```text
0 => section_id                         ASCII StableToken
1 => fragment_index                    uint32
2 => fragment_count                    uint32
3 => first_record_id                   [] or [bytes16]
4 => last_record_id                    [] or [bytes16]
5 => item_count                        uint64
6 => fragment_payload                  bytes
```

Optional record ids use zero-or-one-element arrays so absence is distinct from any byte value.

## 3. Validation order

For one `MVCHNK01` file:

1. validate framing/version/flags/lengths;
2. SHA-256 validate the stored bytes at offset 96..EOF against `stored_payload_digest`;
3. decompress according to the header codec and require the exact `uncompressed_length`;
4. protobuf-decode `SnapshotChunkPayloadV1` and validate canonical fragment ordering;
5. normalize the decoded fragment semantic values as section 2;
6. recompute `SnapshotChunkLogicalPayloadDigestV1` and compare with the header `logical_payload_digest`;
7. only after all chunks pass may cross-chunk section reassembly and schema-owner semantic verification run.

A missing compression decoder is a fail-closed condition. Implementations must not treat stored compressed bytes as decoded payload.

## 4. Authority boundary

The chunk logical payload digest is a physical integrity binding for one decoded chunk. It may change when fragment boundaries or chunk placement change.

It MUST NOT be used as:

- `LogicalSnapshotSection.logical_content_digest`;
- `SnapshotDigest`;
- partition canonical state digest;
- a replacement for schema-owner semantic verification after reassembly.

The schema owner remains responsible for decoding `fragment_payload` and recomputing the logical section item count and semantic digest.

## 5. Acceptance

- protobuf encodings that decode to the same ordered fragment semantic values produce the same chunk logical payload digest;
- raw protobuf byte hashing is not the standard logical payload digest;
- stored-byte tamper fails before semantic reassembly;
- decoded fragment semantic tamper fails the chunk logical payload digest;
- section semantic tamper that preserves a valid physical chunk still fails schema-owner verification;
- no synthetic or fixture verifier is release evidence.

Refs #16 #240 #248 #249 #250
