# Phase 4 canonical domain-partition Snapshot wire blocker

Status: **BLOCKING Stage 2 production completion**  
Tracking: Issue #240  
Scope: INT-03 canonical Snapshot / recovery

## Purpose

Stage 2 requires the 97 authoritative domain partitions to be serialized from actual
`DomainPartitionStateV1<TPayload>` material and restored without inventing persistence semantics.
The current Phase 4 documents fix the semantic payload registry and the outer Snapshot framing,
but several inner protobuf contracts referenced by the persistence specification are not defined
with exact field numbers/types.

This document records those gaps so implementation remains fail-closed. It does not define a new
wire format.

## Already exact and reusable

The following production contracts already exist and must be reused:

- exact 97 `StandardDomainPartitionRegistry` identities and ownership;
- exact P4-05 payload field registry / scalar-family validation;
- `DomainRecordEnvelopeV1<TPayload>` semantic envelope;
- `PartitionStateHeaderV1.CreateCanonical` semantic partition digest;
- exact 103 logical section registry;
- `SnapshotSectionFragmentV1` protobuf wire;
- canonical fragment/chunk ordering and 32 MiB target / 64 MiB hard maximum;
- `MVCHNK01` physical chunk framing;
- stored-payload and semantic chunk logical-digest verification;
- Snapshot staging / atomic rename / SQLite catalog commit boundary;
- Core six-section exact wire and semantic verifiers.

## Missing exact contracts

### 1. `PartitionStateHeaderWireV1`

`phase4-persistence-specification.md` references this message from
`DomainPartitionSnapshotV1.header`, but no exact proto definition exists in the repository.
At minimum the implementation needs an authoritative decision for the representation and field
numbers that bind:

- partition/schema identity;
- revision;
- basis Step;
- detail level;
- item count;
- canonical digest.

The implementation must not infer field numbers from the C# class layout.

### 2. `DomainRecordSnapshotV1`

`phase4-persistence-specification.md` references this message from
`DomainPartitionSnapshotV1.records`, but no exact proto definition exists in the repository.
The persistence wire must losslessly represent the existing envelope semantics:

- record id;
- record schema/version;
- record revision;
- created Step;
- optional retired Step;
- detail level;
- optional lineage ref;
- domain-owned payload.

The canonical record order is already fixed as PartitionRecordId bytewise ascending.

### 3. Domain-owned payload binary wire

P4-05 fixes semantic fields and scalar families for all 97 partitions, but does not assign an
exact protobuf field-number/type mapping for those payloads. Several fields also reference nested
semantic values such as `PolicyRuleV1`, `BodyRegionStateV1`, `PerceivedFactV1`, other
`OrderedNestedList` values, and `RuleAst` without a persistence wire definition.

A production decoder cannot reconstruct authoritative payload values until this mapping is fixed.
Using reflection, JSON, arbitrary object serialization, or a benchmark-only payload wire would
create a new persistence schema and is prohibited.

### 4. `LogicalSnapshotManifestWireV1`

`PhysicalSnapshotManifestV1` references `LogicalSnapshotManifestWireV1`, but that message is not
specified as an exact proto. The prose model also contains `required_addon_metadata`, while the
current C# `LogicalSnapshotManifest` model does not expose that field.

`manifest.pb` therefore has validation models but no normative exact serializer/decoder that can
be used as production recovery authority. Writing opaque fixture bytes is not sufficient.

### 5. Domain fragment payload semantics

The outer `SnapshotSectionFragmentV1` wire and record-boundary splitting are exact. The inner
`DomainPartitionSnapshotV1` contract does not yet say whether each fragment repeats a complete
partition header, carries a fragment header plus record slice, or uses another exact normalized
form. This must be fixed before a multi-fragment partition serializer is implemented.

## Implemented while blocked

Stage 2 may safely implement infrastructure that does not decide the missing wire:

- actual partition material/header cryptographic binding;
- exact-97 material and provider coverage checks;
- fail-closed provider seam;
- production Zstd compression/decompression according to Core Config;
- tests proving header-only/fake/missing material cannot enter the production provider path.

These pieces must not be described as completion of the 103-section production Snapshot path.

## Required resolution under #240

Before Stage 2 can be marked complete, #240 must receive an exact normative amendment containing:

1. protobuf definitions for `PartitionStateHeaderWireV1` and `DomainRecordSnapshotV1`;
2. an exact generic or per-schema payload wire mapping that covers all P4-05 field kinds and nested values;
3. exact `LogicalSnapshotManifestWireV1` / required-addon metadata representation;
4. exact domain-partition fragment payload semantics;
5. decode-normalized semantic mapping proving restored material recomputes the existing canonical
   partition digest rather than hashing protobuf bytes.

No separate implementation Issue is required; progress and resolution remain tracked by #240 and
its implementation PRs.
