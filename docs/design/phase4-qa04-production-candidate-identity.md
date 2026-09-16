# Phase 4 QA-04 production Step CandidateId contract

Status: Canonical QA-04 release-evidence convention  
Tracking: #240, #368  
Implementation: Draft PR #265

## 1. Scope

This contract defines only the deterministic `StepCandidateV1.CandidateId` sequence used by the canonical `perf.reference.v1` production run.

It does not define a general CandidateId policy for arbitrary Simulation Core callers and does not change Step scheduling, domain execution, Operation identity, retry policy, or persistence semantics.

## 2. Canonical identity tuple

For every canonical QA-04 workload transition:

```text
basis_step  = authoritative State(S).Header.Step
target_step = basis_step + 1
```

The CandidateId authority tuple is exactly:

```text
(WorldId, basis_step, target_step)
```

No wall clock, process identity, worker count, thread identity, run/repetition ordinal, machine identity, retry count, random iteration order, or performance measurement state may participate in this identity.

The tuple therefore produces the same CandidateId for the same canonical transition under workers `1`, `4`, `8`, and `16`, across all repetitions and retries.

## 3. Domain-separated derivation

CandidateId is derived with the existing deterministic hash primitives:

```text
for nonce = 0, 1, 2, ...:
    digest = DomainHash("mv.qa04-step-candidate.v1", DCBOR({
        0: WorldId bytes,
        1: basis_step,
        2: target_step,
        3: nonce
    }))
    candidate_id = Trunc128(digest)
    if candidate_id != ZERO:
        return candidate_id
```

The `nonce` exists only for the impossible-but-defined ZERO truncation path. It is not a retry ordinal and must begin at zero for every derivation.

If `basis_step == ulong.MaxValue`, derivation fails closed before computing `target_step`.

If the nonce space is exhausted without producing a non-ZERO value, derivation fails closed.

## 4. Retry and replay identity

Retrying or replaying the same canonical transition from the same authoritative `State(S)` MUST reuse the CandidateId derived from the same `(WorldId, S, S+1)` tuple.

A retry must not mint a new CandidateId from attempt count, elapsed time, process restart, or worker topology.

The CandidateId is therefore stable across:

- process restart;
- deterministic replay;
- workers `1/4/8/16`;
- the three release-evidence repetitions for each worker count.

## 5. Collision fail-closed rule

Within one `perf.reference.v1` run, every distinct `(WorldId, basis_step, target_step)` tuple MUST map to a distinct CandidateId.

The release-evidence loop must retain the CandidateIds it has derived for the run and fail closed if a newly derived CandidateId is already bound to a different tuple.

A collision must not be repaired by adding run-local entropy or by advancing the nonce. Nonce advancement is reserved only for the ZERO result of the same tuple.

This keeps the identity function pure and replayable and makes any 128-bit collision visible instead of silently remapping committed history.

## 6. QA-04 State relation

Under the canonical QA-04 measurement State convention:

```text
State(1)           production initialization basis / not sampled
transition #1      basis State(1), target State(2)
...
transition #27000  basis State(27000), target State(27001)
```

The canonical production run therefore derives exactly 27,000 CandidateIds, one for each workload transition from `State(1)` through final `State(27001)`.

## 7. Determinism evidence requirement

The workers `1/4/8/16 x 3` determinism matrix must verify that every run produces the identical ordered CandidateId sequence in addition to the required final State/history/provenance digests.

CandidateId equality is necessary because committed transition history contains CandidateId. Equal final world semantics with a different CandidateId history is not sufficient release evidence.
