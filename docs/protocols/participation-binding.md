# Participation binding Protocol v1 corrective

Status: Normative corrective for Standard Protocol v1  
Tracking: #197  
Parents: `docs/design/phase3-participation-domain-design.md`, `docs/design/phase4-domain-operation-event-intent-catalog.md`, `docs/design/phase4-protocol-schema.md`, `docs/protocols/gateway-view.md`

## 1. Purpose

This document resolves the exact wire semantics for `participation.binding.create` without changing the Phase 3 Participation ownership model.

The Participation domain already requires an opaque stable `diver_ref`, one-to-one active binding invariants, and binding generation/history. The Phase 4 operation catalog already requires `diver ref` and `expected binding generation` in the create payload. This corrective makes those requirements explicit in Standard Protocol v1 wire messages.

The change is additive. Existing protobuf field numbers are not reused.

## 2. Identity ownership and trust chain

Gateway owns authentication/session truth and the account/session -> Diver identity mapping.

`diver_ref` is:

- a non-zero `Id128`;
- stable for the same Diver identity across reconnect/new General View sessions according to the existing Gateway/View participation contract;
- opaque outside the Gateway identity boundary;
- not a credential, bearer token, account identifier, or session secret.

For a GENERAL_VIEW session that has Diver participation authority, Gateway publishes the stable identity in `AuthSessionStateV1.diver_ref`.

The trust chain is:

```text
Gateway authenticated identity
  -> AuthSessionStateV1.diver_ref
  -> View immutable participation.binding.create payload
  -> Gateway session/diver_ref exact-match admission
  -> Core Participation validation
  -> confirmed ParticipationBindingViewV1.diver_ref
```

View may copy the Gateway-confirmed `diver_ref` into an immutable Operation payload. View must not choose or substitute another actor identity.

Gateway must reject a binding Operation when the payload `diver_ref` differs from the authenticated GENERAL_VIEW session `diver_ref`. UI visibility is not an authorization boundary.

Credentials/session secrets are never copied into the Operation payload or Core WorldState.

## 3. Auth session projection

Standard Protocol v1 adds field 7 to `AuthSessionStateV1`:

```proto
message AuthSessionStateV1 {
  bytes session_id = 1;
  AuthDomainWireV1 auth_domain = 2;
  string effective_role_set = 3;
  repeated string effective_permissions = 4;
  uint64 session_generation = 5;
  SessionWireStatusV1 status = 6;
  optional bytes diver_ref = 7;
}
```

Semantic rules:

- when present, `diver_ref` is non-zero `Id128`;
- GENERAL_VIEW ACTIVE sessions that expose Diver participation authority (`view.participation.bind` or `view.operation.diver`) require `diver_ref`;
- ADMIN_VIEW sessions do not use `diver_ref` and must omit it;
- reconnect/session replacement for the same Diver must preserve the same `diver_ref` unless identity itself is intentionally replaced by an explicit owner-controlled identity lifecycle operation.

## 4. Confirmed binding projection

Standard Protocol v1 adds fields 6 and 7 to `ParticipationBindingViewV1`:

```proto
message ParticipationBindingViewV1 {
  optional bytes binding_id = 1;
  optional bytes resident_id = 2;
  ParticipationBindingWireStatusV1 status = 3;
  optional uint64 effective_from_step = 4;
  optional string absence_policy_profile = 5;
  uint64 binding_generation = 6;
  optional bytes diver_ref = 7;
}
```

Generation semantics:

- confirmed `NONE` uses `binding_generation = 0`;
- first successful create transitions generation `0 -> 1`;
- every later successful binding lifecycle transition that replaces the current binding authority increments generation by exactly one;
- retry/dedup convergence of the same OperationId + immutable digest does not increment generation;
- a projection never decreases `binding_generation` within the same world continuity chain.

For `ACTIVE`:

- `binding_id` required, non-zero `Id128`;
- `resident_id` required, non-zero `Id128`;
- `diver_ref` required, non-zero `Id128`;
- `effective_from_step` required;
- `binding_generation >= 1`.

For `NONE`, `binding_id`, `resident_id`, `diver_ref`, and `effective_from_step` are absent and generation is zero.

`diver_ref` is part of the confirmed world binding projection so the authoritative actor association survives publication, persistence-derived recovery, and reconnect. A View must not treat an ACTIVE binding whose `diver_ref` differs from its current Gateway-confirmed session `diver_ref` as current control authority.

## 5. Binding create request payload

Standard Protocol v1 adds fields 5 and 6 to `ParticipationBindingRequestV1`:

```proto
message ParticipationBindingRequestV1 {
  bytes operation_id = 1;
  bytes immutable_payload_digest = 2;
  string preference_profile = 3;
  repeated string preference_tokens = 4;
  bytes diver_ref = 5;
  uint64 expected_binding_generation = 6;
}
```

Semantic rules:

- `operation_id`: non-zero `Id128`, exact match with outer `StandardOperationV1.operation_id`;
- `immutable_payload_digest`: `Hash256`, exact match with outer `StandardOperationV1.immutable_payload_digest`;
- `diver_ref`: non-zero `Id128`, exact match with authenticated Gateway session projection;
- `expected_binding_generation`: compare-and-set basis for the Diver's current confirmed binding;
- `preference_profile` and every `preference_tokens` entry use exact `StableToken` grammar `ASCII [a-z0-9][a-z0-9._/-]{0,63}`;
- `preference_tokens` is ASCII ascending and duplicate-free before hashing/submission.

For first binding create, `expected_binding_generation` must be `0`.

If the current authoritative generation differs, Gateway and/or Core rejects the new Operation with stable stale/conflict semantics; no silent overwrite is allowed.

## 6. Immutable Operation identity

For `participation.binding.create`, the immutable semantic digest includes at least:

```text
operation_kind
admission_basis_step
scheduling_policy_generation
requested_not_before_step presence/value
requested_deadline_step presence/value
payload_schema_id/version
diver_ref
expected_binding_generation
preference_profile
ordered preference_tokens
```

Candidate scheduling output, MessageId, CorrelationId, transport framing, protobuf wire bytes, and the self-referential OperationId/digest fields are excluded from semantic digest input.

OperationId is derived from the canonical immutable digest according to the existing Standard Operation identity rule.

Same OperationId + same immutable digest is retry/dedup convergence. Same OperationId + different digest is `protocol.operation-payload-mismatch`.

## 7. Core binding invariants

Gateway authorization is necessary but not sufficient. Core Participation still enforces:

```text
one resident_id -> at most one ACTIVE diver_ref
one diver_ref   -> at most one ACTIVE resident_id
```

A new `participation.binding.create` cannot overwrite an existing ACTIVE binding. A release/rebind must use its explicit operation kind and expected current generation.

#240の承認決定により、Residentに一度Diverを割り当てた後、通常のuser-level release/rebindで存命ResidentのDiverを交代させてはならない。存命中の変更は管理者権限による明示的変更に限定し、binding authority generationを進めて旧generationの操作をstaleとして拒否する。disconnect / reconnect / logout / 長期不在によるbinding解除・変更、および別Diverへの自動再割り当ては禁止する。Resident死亡時はbindingを終了可能とし、死亡後の同じDiverは参加規則に従い別の既存Residentへbindできる。既存の一対一制約とhistory保持は継続する。

## 8. Component ownership

The protocol docs and `.proto` files are the cross-component source of truth.

Simulation Core and General View must not compile the same hand-written C# codec/validation source file. Each component owns its implementation (or consumes generated protobuf types) and cross-component equality is verified with versioned golden vectors for canonical immutable identity.

No shared runtime DTO/codec assembly is introduced by this corrective.
