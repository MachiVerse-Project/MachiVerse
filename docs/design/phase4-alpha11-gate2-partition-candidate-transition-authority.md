# Alpha 1.1 / QA-04 Gate 2 partition-candidate transition authority

## Scope

This document defines the **benchmark-only** bridge used by Issue #240 / PR #265 to convert the already-authoritative QA-04 Gate 1 typed mutation result into runtime `PartitionCandidateV1` values.

It does **not** define a general `PartitionChangeSetV1` wire/schema, does not change the standard domain registry, and does not by itself make the full authoritative Step loop available.

## Preconditions

For basis Step `S` and target/effective Step `T`:

- `T = S + 1`;
- the basis `WorldStateV1` is the authoritative state at `S`;
- `Qa04CanonicalOperationMutationBatchV1` has produced the typed post-state for `T` from the canonical scheduled Operations;
- each candidate uses the basis partition header already present in `WorldStateV1`;
- `basis_revision != ulong.MaxValue`;
- the candidate revision is exactly `basis_revision + 1`;
- the candidate target Step is exactly `T`.

## Candidate partitions

A six-family QA-04 mutation batch produces exactly one candidate for each mutated partition:

| Operation family | Candidate partition | Owner |
|---|---|---|
| `infrastructure-service-delivery` | `infrastructure.service_queue` | `infrastructure_information` |
| `participation-control-resident-action` | `resident.behavior_state` | `resident` |
| `physical-item-movement-work` | `physical.presence` | `physical_built` |
| `society-market-payment-contract` | `society.market_transaction` | `society_economy` |
| `governance-security` | `governance.security_incident` | `governance_security` |
| `environment-spatial-admin-synthetic` | `environment.hazard` | `environment` |

`participation.control_mode` remains read-only input authority for the Resident handler and MUST NOT emit a candidate from this bridge.

Candidates MUST be constructed through the existing owner-specific candidate factories. The bridge MUST NOT bypass owner validation with a direct `PartitionCandidateV1` construction.

## Canonical post-state digest

The bridge MUST compute a canonical post-state `PartitionStateHeaderV1` for each mutated typed partition using `PartitionStateHeaderV1.CreateCanonical` with:

- `revision = basis_revision + 1`;
- `basisStep = T`;
- `detailLevel = basis partition header detail level`;
- the actual Gate 2 typed post-state records.

For these five v1 partitions:

- `infrastructure.service_queue`;
- `resident.behavior_state`;
- `physical.presence`;
- `governance.security_incident`;
- `environment.hazard`;

payload digests MUST be produced by the existing `StandardDomainPayloadCanonicalDigestV1` authority from each payload's standard field mapping. Reference validation remains fail-closed.

For `society.market_transaction`, the Gate 1 actual mutation state is the explicit v2 materialization target. Its payload digest MUST use `SocietyMarketTransactionPayloadCanonicalDigestV2.Compute`. The v2 partition identity preserves the registered partition identity and partition schema while using the approved v2 record schema; this bridge does not flip `StandardDomainPartitionRegistry` away from v1.

The post-state header MUST reject any record with `created_step > T` or `retired_step > T`, as required by `PartitionStateHeaderV1.CreateCanonical`.

## QA-04 transition digest

The runtime `PartitionCandidateV1` currently carries only a 32-byte `changeSetDigest`, while the general domain-specific `PartitionChangeSetV1` representation is not concretely defined by the current implementation. For QA-04 Gate 2 only, the candidate `changeSetDigest` is therefore the following deterministic transition binding.

Hash domain:

```text
mv.qa04.partition-change-set.v1
```

Normalized DCBOR payload:

```text
map(7) {
  0: partition_id                 ; ASCII StableToken
  1: basis_revision               ; uint64
  2: basis_step                   ; uint64 S
  3: basis_partition_digest       ; bytes32, WorldState header canonical_digest
  4: candidate_revision           ; uint64 = basis_revision + 1
  5: target_step                  ; uint64 T = S + 1
  6: post_partition_digest        ; bytes32, canonical post-state header digest
}
```

The digest is:

```text
HashSuite.DomainHash("mv.qa04.partition-change-set.v1", normalized_payload)
```

This value binds the exact authoritative basis partition to the exact canonical typed post-state without inventing a general change-set schema.

## Candidate construction

For each of the six partitions, the bridge MUST:

1. resolve the basis header from `basisState.Partitions`;
2. verify basis owner/partition identity against the existing registry/factory authority;
3. compute the canonical post-state header as defined above;
4. compute `changeSetDigest` from the QA-04 transition digest;
5. call the existing owner-specific `PartitionCandidateV1` factory with the basis `WorldStateV1` and computed digest;
6. verify the returned candidate has the expected partition, owner, basis revision, candidate revision, basis Step, target Step, and change-set digest.

The returned six candidates MUST be unique by partition id and presented in canonical partition-id ordinal ordering when exposed as a batch result.

## Fail-closed conditions

The bridge MUST reject at least:

- non-canonical QA-04 world id;
- mutation effective Step other than `basisState.Header.Step + 1`;
- missing basis partition;
- basis revision overflow;
- post-state partition identity drift;
- post-state owner drift;
- record lifecycle Step beyond target Step;
- payload/reference canonical-digest validation failure;
- wrong Market v2 identity/schema contract;
- duplicate or missing candidate partition;
- candidate owner/revision/Step drift from the owner factory result;
- any 32-byte digest length violation.

## Non-generalization boundary

This authority is limited to the six QA-04 Gate 1 mutation targets and the Issue #240 Gate 2 full-Step assembly. It does not define:

- the general domain `PartitionChangeSetV1` representation;
- a generic delta encoding for arbitrary partitions;
- a new standard partition or record schema;
- an automatic v1 -> v2 registry migration for `society.market_transaction`;
- COMMIT, durable terminal Operation transition, or State(S+1) publication semantics.

Until the later Gate 2 stages connect candidates through all eight domain outputs, `StepCandidateV1`, durable SQLite COMMIT, and State(S+1) publication, `authoritativeStepLoopAvailable` remains `false`. `releaseEvidenceCapable` also remains `false`.