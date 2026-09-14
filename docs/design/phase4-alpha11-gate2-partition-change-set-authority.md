# Alpha 1.1 / QA-04 Gate 2 partition change-set authority

Status: normative benchmark-only authority for Issue #240 / `perf.reference.v1` Gate 2.

## Scope

This document defines the minimum deterministic authority required to bridge the six already-authoritative QA-04 Operation mutation handlers into the existing `PartitionCandidateV1` boundary.

It does **not** define a general Phase-4 `PartitionChangeSetV1` wire/schema for arbitrary simulation workloads. It is restricted to the canonical `perf.reference.v1` workload used by Alpha 1.1 / INT-03 release evidence.

The six mutation families and target partitions are the Gate-1 authorities already fixed by their family-specific documents and implementations:

- `infrastructure-service-delivery` -> `infrastructure.service_queue`
- `participation-control-resident-action` -> `resident.behavior_state`
- `physical-item-movement-work` -> `physical.presence`
- `society-market-payment-contract` -> `society.market_transaction`
- `governance-security` -> `governance.security_incident`
- `environment-spatial-admin-synthetic` -> `environment.hazard`

`participation.control_mode` is read authority for the Resident handler and is not a mutation target of this Gate-2 bridge.

## Basis and target Step

For a mutation batch whose scheduled Operations have `effective_step = T`:

- the authoritative basis WorldState MUST be `State(T-1)`;
- each emitted `PartitionCandidateV1.basis_step = T-1`;
- each emitted `PartitionCandidateV1.target_step = T`;
- the basis partition header MUST come from the frozen authoritative `WorldStateV1` at `State(T-1)`;
- the candidate partition revision MUST be `basis_revision + 1`;
- a partition with no actual mutation MUST NOT emit a candidate.

A mismatch between the scheduled effective Step, WorldState basis Step, partition basis header, or candidate target Step is fail-closed.

## Candidate partition semantic digest

The post-mutation typed partition MUST be recomputed with the existing `PartitionStateHeaderV1.CreateCanonical` authority:

- `revision = basis_revision + 1`;
- `basis_step = T`;
- `detail_level = basis partition header.detail_level`;
- payload digest = the existing canonical payload digest for that partition payload (`CanonicalDigest()` / existing production equivalent).

The resulting `PartitionStateHeaderV1.CanonicalDigest` is the **candidate partition semantic digest**. No new payload codec, JSON/reflection encoding, or alternate record serialization is introduced by Gate 2.

The recomputed candidate header MUST preserve the standard partition identity and owner and MUST have the actual post-mutation item count.

## QA-04 partition change-set commitment

Because P4-01 leaves the domain-specific `PartitionChangeSetV1` layout open, QA-04 uses a bounded transition commitment instead of inventing a general change-set schema.

For each actually mutated target partition, compute `change_set_digest` with the existing `HashSuite.DomainHash("mv.state-diagnostic.v1", ...)` over this exact canonical map:

```text
{
  0: partition_id ASCII text,
  1: owner_domain ASCII text,
  2: basis_revision uint64,
  3: candidate_revision uint64,
  4: basis_step uint64,
  5: target_step uint64,
  6: basis_partition_canonical_digest bytes32,
  7: candidate_partition_canonical_digest bytes32,
  8: ordered_operation_ids array<bytes16>
}
```

`ordered_operation_ids` MUST contain exactly the Operations that actually mutated that partition in the batch, in canonical `SameStepOrderKey` ascending order. Duplicate OperationIds are invalid.

The transition commitment therefore binds the candidate to:

1. the exact authoritative pre-state partition revision and digest;
2. the exact authoritative post-mutation partition revision and semantic digest;
3. the exact State transition `T-1 -> T`;
4. the exact canonical Operations responsible for the mutation.

The resulting 32-byte digest is passed unchanged to the existing owner-specific `PartitionCandidateV1` factory. `PartitionCandidateV1.CandidateDigest` remains owned by the existing runtime model and is not redefined here.

## Owner-specific candidate factories

The Gate-2 bridge MUST create candidates through existing owner validation paths:

- Infrastructure: `InfrastructureInformationPartitionCandidateFactoryV1`
- Resident: `ResidentParticipationPartitionCandidateFactoryV1.CreateResident`
- Physical/Built: `PhysicalBuiltPartitionCandidateFactoryV1`
- Society/Economy: `SocietyEconomyPartitionCandidateFactoryV1`
- Governance/Security: `GovernanceSecurityPartitionCandidateFactoryV1`
- Environment: `DomainOwnedPartitionCandidateFactoryV1.CreateEnvironment`

The bridge MUST NOT construct a candidate for a foreign-owned partition through another domain owner.

## Fail-closed requirements

The bridge MUST reject at least:

- WorldId mismatch;
- basis WorldState Step not equal to `effective_step - 1`;
- missing target partition in the basis WorldState;
- target partition owner/schema mismatch;
- basis header revision/digest mismatch against the frozen WorldState;
- candidate canonical header identity/owner mismatch;
- candidate revision other than `basis_revision + 1`;
- candidate header basis Step other than `effective_step`;
- empty OperationId list for an emitted candidate;
- duplicate OperationIds;
- non-canonical Operation order;
- Operation family mapped to a partition other than its Gate-1 target;
- a post-state whose canonical digest cannot be computed through existing production payload authority.

## Evidence boundary

Passing this bridge proves only:

`canonical scheduled Operations -> six typed mutations -> six owner-valid PartitionCandidateV1 values`.

It does **not** by itself prove a full authoritative Step. In particular, it does not yet prove:

- complete eight-domain `DomainCandidateOutputV1` assembly;
- `StepCandidateV1.Build` integration;
- invariant barrier acceptance;
- `StepStateApplicationV1.Prepare` integration;
- durable SQLite COMMIT;
- State(S+1) publish-after-COMMIT;
- exact-103 Snapshot/recovery/replay;
- release evidence capability.

Therefore `authoritativeStepLoopAvailable` and `releaseEvidenceCapable` remain `false` after this authority is implemented and tested.
