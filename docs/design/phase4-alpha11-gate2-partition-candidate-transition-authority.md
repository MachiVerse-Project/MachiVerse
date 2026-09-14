# Alpha 1.1 / QA-04 Gate 2 partition-candidate transition authority

## Scope

This document defines the **benchmark-only assembly boundary** used by Issue #240 / PR #265 to convert the already-authoritative QA-04 Gate 1 typed mutation result into ordinary runtime `PartitionCandidateV1` plus `StepPartitionStateMaterialV1` values.

It introduces no new partition-change-set hash contract. The bridge MUST reuse the existing Step runtime authority in `StepPartitionStateMaterialV1.ComputeChangeSetDigest` and the existing owner-specific candidate factories.

It does not change the standard domain registry and does not by itself make the full authoritative Step loop available.

## Preconditions

For basis Step `S` and target/effective Step `T`:

- `T = S + 1`;
- the basis `WorldStateV1` is the authoritative state at `S`;
- `Qa04CanonicalOperationMutationBatchV1` has produced the typed post-state for `T` from the canonical scheduled Operations;
- each candidate uses the basis partition header already present in `WorldStateV1`;
- `basis_revision != ulong.MaxValue`;
- the resulting/candidate revision is exactly `basis_revision + 1`;
- the candidate target Step is exactly `T`.

## Candidate partitions

A six-family QA-04 mutation batch produces exactly one candidate/material pair for each mutated partition:

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

## Canonical resulting partition state

For the five standard v1 typed states:

- `infrastructure.service_queue`;
- `resident.behavior_state`;
- `physical.presence`;
- `governance.security_incident`;
- `environment.hazard`;

the bridge MUST derive the resulting `PartitionStateHeaderV1` from the actual typed post-state with `PartitionStateHeaderV1.CreateCanonical`, using:

- `revision = basis_revision + 1`;
- `basisStep = T`;
- `detailLevel = basis partition header detail level`;
- each payload type's existing canonical digest authority (`CanonicalDigest()` / the underlying standard domain payload canonical digest).

For `society.market_transaction`, the Gate 1 actual mutation state is the approved explicit v2 materialization target. The bridge MUST use `SocietyMarketTransactionSnapshotAuthorityV2.CreateCanonical`, verify that authority, and use its resulting header. The v2 partition identity preserves the registered partition id/owner/partition schema while using the approved v2 record schema; the bridge does not flip `StandardDomainPartitionRegistry` away from v1.

The resulting header construction MUST retain the existing lifecycle checks, including rejection of records with `created_step > T` or `retired_step > T`.

## Existing transition/change-set authority

The candidate `changeSetDigest` MUST be exactly:

```text
StepPartitionStateMaterialV1.ComputeChangeSetDigest(
    basis_header,
    resulting_header,
    basis_world_step = S,
    target_world_step = T)
```

No QA-04-specific replacement hash domain is introduced.

The existing runtime authority binds, under `mv.step-partition-change-set.v1`, the basis/resulting partition identity, revisions, partition basis Steps, world basis/target Steps, basis/resulting canonical partition digests, resulting detail level, and resulting item count. The Gate 2 bridge MUST consume this authority unchanged so that `StepStateApplicationV1.Prepare` can later validate the exact same candidate/material pair.

## Candidate construction

For each of the six partitions, the bridge MUST:

1. resolve the basis header from `basisState.Partitions`;
2. verify basis partition id, owner, and partition schema against the actual typed resulting-state identity;
3. derive and verify the canonical resulting header as defined above;
4. compute `changeSetDigest` using `StepPartitionStateMaterialV1.ComputeChangeSetDigest`;
5. call the existing owner-specific candidate factory with the basis `WorldStateV1`, exact partition id, and computed digest;
6. create `StepPartitionStateMaterialV1` from the exact resulting header;
7. verify candidate partition/owner, basis revision, candidate revision, basis Step, target Step, and digest against the basis/resulting headers.

The returned six candidate/material pairs MUST be unique by partition id and exposed in canonical partition-id ordinal order.

## Owner-specific factories

The bridge MUST route candidate construction through the existing factories:

- Infrastructure -> `InfrastructureInformationPartitionCandidateFactoryV1.Create`;
- Resident -> `ResidentParticipationPartitionCandidateFactoryV1.CreateResident`;
- Physical/Built -> `PhysicalBuiltPartitionCandidateFactoryV1.Create`;
- Society/Economy -> `SocietyEconomyPartitionCandidateFactoryV1.Create`;
- Governance/Security -> `GovernanceSecurityPartitionCandidateFactoryV1.Create`;
- Environment -> `DomainOwnedPartitionCandidateFactoryV1.CreateEnvironment`.

## Fail-closed conditions

The bridge MUST reject at least:

- non-canonical QA-04 world id;
- mutation effective Step other than `basisState.Header.Step + 1`;
- missing basis partition;
- basis revision overflow;
- resulting typed-state partition identity/owner/partition-schema drift;
- record lifecycle Step beyond target Step;
- canonical payload/resulting-header derivation failure;
- wrong Market v2 identity/schema/authority contract;
- duplicate or missing candidate partition;
- candidate owner/revision/Step drift from the owner factory result;
- candidate `changeSetDigest` differing from `StepPartitionStateMaterialV1.ComputeChangeSetDigest`.

## Non-generalization boundary

This authority is limited to assembling the six QA-04 Gate 1 mutation targets into existing generic Step runtime candidate/material authority. It does not define:

- a new general `PartitionChangeSetV1` representation;
- a generic delta encoding for arbitrary partitions;
- a new standard partition or record schema;
- an automatic v1 -> v2 registry migration for `society.market_transaction`;
- domain-output assembly, `StepCandidateV1` construction, COMMIT, durable terminal Operation transition, or State(S+1) publication semantics.

Until later Gate 2 stages connect these candidate/material pairs through all eight domain outputs, `StepCandidateV1`, durable SQLite COMMIT, and State(S+1) publication, `authoritativeStepLoopAvailable` remains `false`. `releaseEvidenceCapable` also remains `false`.