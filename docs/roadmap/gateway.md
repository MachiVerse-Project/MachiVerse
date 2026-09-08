# Gateway 実装ロードマップ

ImplementationWorkId: `GW-01..GW-07`  
Base branch: `gateway`  
Upper roadmap: `/ROADMAP.md`

## 1. 実装baseline

Standard runtime profile:

```text
.NET 10 LTS
C# 14
ASP.NET Core 10
Core/Gateway peer transport: gRPC bidirectional streaming
Browser boundary: TLS WebSocket binary + Protocol Buffers
Authentication: OIDC Authorization Code + PKCE S256 / Gateway BFF
```

Gateway は authoritative World State を所有しない。

主要責務:

- Core / peer / View / Admin protocol boundary
- confirmed cache / logical publication buffer / resync
- Master role / custody / retry / result routing
- General View / Admin View authn/authz domain
- external Operation admission / mediation
- management audit / observability

## 2. Milestone mapping

| Global milestone | Work package | Dependency |
|---|---|---|
| M1 | `GW-01` | `QA-01` fixture source |
| M2 | `GW-02` | `GW-01` |
| M3 | `GW-03`, `GW-04` | `GW-02`, then Master route |
| M4 | `GW-05`, `GW-07` | auth/session baseline |
| M5 | `GW-06` | cache + authorization |
| M6 | real Core/View/Admin integration | `INT-*` |

## 3. Foundation

### GW-01 — Gateway project scaffold / protocol-config foundation

Scope:

- executable / test project
- component-local protobuf generated types
- common envelope validator
- `config.gateway/1.0` loader
- lifecycle / dependency injection shell

DoD gate:

- other component production DLLなしでbuild/test可能
- protocol golden fixtureをdecode/validate可能

## 4. Core state / resync path

### GW-02 — Core protocol / confirmed cache / resync

Scope:

- Core gRPC client
- protocol negotiation
- confirmed derived state cache
- `basis_step` / `StateContinuityToken` validation
- FULL / DELTA apply
- resync coordinator
- scheduling policy view

Rules:

- old cacheをauthoritativeとしてblind reuseしない
- continuity mismatch時はnormal publication/admissionをgateする
- world-affecting Operation admissionにはconfirmed basisを要求する

Dependency: `GW-01`。Real Core integrationは`SIM-14`後、fixtureで先行する。

## 5. Peer / Master / custody spine

### GW-03 — Peer / Master / custody / retry

Scope:

- Gateway↔Gateway gRPC
- `GatewayLogicalId`
- peer heartbeat
- Core-authoritative `MasterGeneration` follow
- local batch / cross-Gateway merge path
- custody store
- retry / status convergence
- Master failover / stale generation handling

Custody:

```text
SOURCE_HELD
 -> MASTER_RECEIVED
 -> CORE_ACCEPTED
 -> TERMINAL
```

Rules:

- Master receipt ACKをCore durable acceptanceと同一視しない
- retry/failoverでOperationId / immutable digestを変更しない
- arrival/thread timingをsemantic merge orderにしない

Dependencies: `GW-01`, `GW-02`。

## 6. Authentication / session

### GW-04 — OIDC/BFF session / authentication

Scope:

- OIDC Authorization Code + PKCE S256
- Gateway confidential BFF
- Secure / HttpOnly opaque session cookie
- Master login proxy / finalization
- session generation / revoke / reconnect
- allowed Origin validation

Dependency: `GW-03` のMaster login routing。

Security gate:

- access/refresh tokenをbrowser JavaScriptへ露出しない
- old MasterGeneration auth authorityをcurrent化しない
- General View / Admin View auth domainを混同しない

## 7. Authorization / audit

### GW-05 — Authorization / View + Admin boundaries

Scope:

- General View role→permission enforcement
- Admin View explicit permission set
- TLS WebSocket envelope/session validation
- OperationKind / target / category authorization
- unauthorized downstream forwarding防止

Dependency: `GW-04`。

### GW-07 — Gateway observability / management audit

Scope:

- structured log / metrics / traces
- audit append-only chain / retention
- Admin audit query / export
- protected Admin actionのaudit fail-closed path

Dependency: `GW-05`。`GW-06`と並行可能な範囲あり。

## 8. Publication / result path completion

### GW-06 — Publication / result routing / backpressure

Scope:

- logical publication buffer
- confirmed state coalesce
- subscriber / permission filter
- Operation result router
- slow consumer handling
- View/Admin protocol payload routing
- queue separation / backpressure

Dependencies: `GW-02`, `GW-05`。

Rules:

- publication freshnessはlossy/coalesce可能だがOperation custody/resultを同じlossy policyへ載せない
- slow ViewがCore Operation pathを無制限にblockしない
- Gateway buffer/cache stateがworld outcomeを変えない

## 9. Gateway completion gate

Component-level completeには少なくとも次を要求する。

- Core confirmed state / FULL-DELTA / resync fixture pass
- Master failover / stale generation / custody convergence
- OIDC/session negative test corpus
- View/Admin authorization domain separation
- unauthorized Operation downstream forwarding 0
- publication / result / slow-client backpressure test
- audit / redaction / observability contract

Release完了は `INT-01`、`INT-02`、`INT-03` と `QA-04` で横断判定する。
