# Alpha 1.1 full-step partition change-set authority

Status: **Complete / normative benchmark authority**

Tracking: #240, #265

## 1. Scope

This document fixes only the QA-04 / Alpha 1.1 benchmark authority needed to convert the already-authoritative Gate-1 typed mutation results into `PartitionCandidateV1` instances during Gate 2 full-Step integration.

It does **not** define a general production `PartitionChangeSetV1` wire/schema, a generic domain mutation journal, a cross-version partition diff format, or a reusable change-set digest contract for non-QA-04 workloads.

## 2. Existing authority reused

Implementation MUST reuse:

- `Qa04CanonicalOperationMutationBatchV1` and the six Gate-1 application authorities;
- the frozen `WorldStateV1` partition header as basis authority;
- the existing owner-specific `PartitionCandidateV1` factories;
- `SameStepOrderKey` canonical order;
- `Qa04CanonicalOperationBindingV1.ComputeImmutablePayloadDigest` / bound immutable payload digest authority;
- `HashSuite.DomainHash("mv.state-diagnostic.v1", ...)` for this benchmark-only transition receipt digest.

The implementation MUST NOT invent a second partition owner, basis revision, basis Step, target Step, or operation ordering rule.

## 3. Candidate coverage

For one QA-04 effective Step, only the six mutated authoritative target partitions produce a partition candidate:

```text
infrastructure.service_queue     owner=infrastructure_information
resident.behavior_state          owner=resident
physical.presence                owner=physical_built
society.market_transaction       owner=society_economy
governance.security_incident     owner=governance_security
environment.hazard               owner=environment
```

`participation.control_mode` is read-only support authority for the resident action application and MUST NOT receive a candidate unless it was independently mutated by an authorized path.

A no-op partition MUST NOT receive a candidate.

## 4. Basis / target authority

For every candidate:

```text
basis_revision = frozen WorldState partition header revision
basis_step     = frozen WorldState.Header.Step
target_step    = basis_step + 1
candidate_revision = basis_revision + 1
```

The batch `effective_step` MUST equal `target_step`.

Any basis mismatch, owner mismatch, missing standard partition header, revision overflow, Step overflow, or effective-Step drift MUST fail closed.

## 5. QA-04 transition receipt

`PartitionCandidateV1.ChangeSetDigest` for this Gate-2 benchmark is the digest of a deterministic transition receipt, not a generic serialization of `PartitionChangeSetV1`.

The receipt MUST be computed with:

```text
HashSuite.DomainHash("mv.state-diagnostic.v1", normalized_receipt)
```

where `normalized_receipt` is exactly the following DCBOR map:

```text
map(9) {
  0: "qa04.partition-change-set.v1",
  1: partition_id,
  2: owner_domain,
  3: basis_revision,
  4: basis_step,
  5: target_step,
  6: basis_partition_canonical_digest,
  7: post_item_count,
  8: ordered_changes
}
```

`basis_partition_canonical_digest` is copied from the frozen `WorldStateV1` header for that partition. `post_item_count` is taken from the actual typed post-mutation partition state returned by the Gate-1 handler composition.

`ordered_changes` is an array in canonical `SameStepOrderKey` order. Each entry is exactly:

```text
map(12) {
  0: operation_id,
  1: family_token,
  2: operation_kind,
  3: immutable_payload_digest,
  4: changed_record_id,
  5: record_schema_id,
  6: record_schema_major,
  7: record_schema_minor,
  8: result_revision,
  9: result_created_step,
  10: result_detail_level,
  11: mutation_mode
}
```

`mutation_mode` is exactly one of:

```text
create
revise
```

No other token is valid for this benchmark authority.

The changed record envelope fields above MUST come from the **actual result object returned by the Gate-1 handler**, not from a recomputed expected fixture. This ensures the candidate receipt binds the real typed mutation result.

For this QA-04 workload all six current result envelopes are non-retired and lineage-free under their already-approved Gate-1 authorities. Those facts remain validated by the Gate-1 handlers and are not duplicated as new generic change-set fields here.

## 6. Family -> changed record binding

The changed record included in the receipt is exactly:

- `infrastructure-service-delivery`: the applied/created `infrastructure.service_queue` record returned by `Qa04InfrastructureServiceReserveApplicationV1`;
- `participation-control-resident-action`: the applied `resident.behavior_state` record returned by `Qa04ResidentActionApplicationV1`;
- `physical-item-movement-work`: the revised `physical.presence` record returned by `Qa04PhysicalMoveApplicationV1`;
- `society-market-payment-contract`: the created market-order record returned by `Qa04MarketOrderApplicationV1` inside `society.market_transaction`;
- `governance-security`: the created `governance.security_incident` record returned by `Qa04GovernanceIncidentApplicationV1`;
- `environment-spatial-admin-synthetic`: the created `environment.hazard` record returned by `Qa04EnvironmentHazardApplicationV1`.

The implementation MUST reject a family mapped to any other partition or result record.

## 7. Candidate construction

After the receipt digest is computed, implementation MUST call the existing owner-specific candidate factory for the corresponding partition. The candidate returned by that factory must satisfy:

```text
candidate.PartitionId      == target partition
candidate.OwnerDomain      == registered owner
candidate.BasisRevision    == frozen basis revision
candidate.CandidateRevision== basis revision + 1
candidate.BasisStep        == frozen basis Step
candidate.TargetStep       == effective Step
candidate.ChangeSetDigest  == QA-04 transition receipt digest
```

The benchmark integration MUST NOT directly construct foreign-owner candidates and MUST NOT bypass the existing owner/registry checks.

## 8. Domain output grouping

The six partition candidates are grouped into `DomainCandidateOutputV1.LocalPartitionCandidates` by registered owner domain. Domains without a local candidate still participate in the standard eight-domain execution coverage with an empty candidate list.

This grouping MUST NOT alter the canonical operation application order. Operation ordering is established before candidate grouping.

## 9. Proof requirements

Positive proof MUST demonstrate:

- exactly six candidates for one Step with one operation from every canonical family;
- exact target partition and owner for each candidate;
- basis revision/Step copied from frozen `WorldStateV1`;
- target Step equals the batch effective Step;
- transition receipt digest is deterministic across replay from the same frozen state/input;
- each receipt binds the actual Gate-1 result record identity/envelope and post item count;
- owner-specific candidate factories are used successfully;
- grouping yields standard eight-domain output coverage with six local partition candidates total.

Negative proof MUST reject at least:

- wrong/missing frozen partition basis;
- effective-Step drift;
- owner/partition mismatch;
- duplicate or non-canonical operation ordering;
- changed-record/family mismatch;
- tampered immutable payload digest/binding;
- post-item-count drift;
- candidate factory basis revision mismatch;
- attempt to emit a candidate for unchanged `participation.control_mode`.

## 10. Gate boundary

Completion of this authority and its implementation establishes only:

```text
Gate 2 Step 2: typed mutation result -> PartitionCandidateV1
```

It does **not** by itself establish:

- full eight-domain `StepCandidateV1` assembly;
- invariant/conflict completion;
- `StepStateApplicationV1.Prepare`;
- SQLite COMMIT;
- State(S+1) publish;
- post-commit semantic verification.

Therefore completion of this slice MUST NOT set:

```text
authoritativeStepLoopAvailable = true
releaseEvidenceCapable = true
```

## 11. Non-generalization

`qa04.partition-change-set.v1` and this transition-receipt digest are QA-04 / Alpha 1.1 benchmark-only authorities introduced because P4-01 intentionally leaves `PartitionChangeSetV1` domain-specific and the current runtime stores only a 32-byte change-set digest in `PartitionCandidateV1`.

They MUST NOT be reused as the generic production `PartitionChangeSetV1` schema or a public protocol/persistence contract without a separate normative migration.