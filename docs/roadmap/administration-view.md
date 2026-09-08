# Administration View 実装ロードマップ

ImplementationWorkId: `ADMIN-01..ADMIN-04`  
Base branch: `administration-view`  
Upper roadmap: `/ROADMAP.md`

## 1. 実装baseline

Standard runtime profile:

```text
standalone Blazor WebAssembly net10.0
Gateway boundary: TLS WebSocket binary + Protocol Buffers
Authentication domain: Admin View dedicated domain
```

Administration View は General View Administrator の上位roleではなく、別 authn/authz domain の system operator UI とする。

Target component の internal object、Config file、production DLL へ直接依存しない。

## 2. Milestone mapping

| Global milestone | Work package | Dependency |
|---|---|---|
| M1 | `ADMIN-01` | `QA-01` protocol fixture |
| M2 | `ADMIN-02`, `ADMIN-03` | `ADMIN-01`; Gateway fixtureで先行可 |
| M3 | `ADMIN-04` | `ADMIN-03` |
| M6 | end-to-end management validation | `INT-*` |

## 3. Foundation

### ADMIN-01 — Admin View scaffold / Gateway protocol client

Scope:

- standalone Blazor WebAssembly shell
- binary WebSocket/protobuf client
- Admin View Config 1.0 loader
- auth/session lifecycle shell
- stable request/result presentation foundation

DoD gate:

- Gateway mockで単独build/test可能
- General View permission modelを流用しない

## 4. Observability / audit UI

### ADMIN-02 — Health / metrics / log / audit UI

Scope:

- management target catalog
- component health/status
- metrics dashboard
- structured log query/page
- audit query/export presentation
- correlation context display

Dependencies: `ADMIN-01`, `GW-07` fixture contract。

Rules:

- diagnostic log と security/management audit のauthorityを混同しない
- secret/redacted fieldをUI側で復元しない
- unavailable targetをdirect internal accessでfallbackしない

## 5. Config / command management

### ADMIN-03 — Config / operational command management

Scope:

- Config projection / editor
- ConfigGeneration / expected base generation
- normalized change set
- command catalog
- stable request/result tracking
- retry / stale generation UX

Dependencies: `ADMIN-01`, `GW-05/GW-06` fixture contract。

Rules:

- Config fileを直接編集しない
- invalid change setをpartial apply前提で扱わない
- generic Undoを提供しない。元へ戻す操作もnew change requestとする
- simulation-affecting changeのeffective StepをUI都合で上書きしない

## 6. High-impact / simulation management

### ADMIN-04 — High-impact / simulation Admin Operation

Scope:

- high-impact confirmation flow
- simulation Admin Operation request
- audit correlation
- session/revoke/failure state
- terminal effect result表示

Dependency: `ADMIN-03`。

Rules:

- Admin由来という理由だけでsimulation-affecting Operationを無条件最優先にしない
- authorization済みでもCore world invariantをoverrideしない
- confirmation tokenをOperationId代替にしない
- ACK / acceptedをterminal effect successとして表示しない

## 7. Administration View completion gate

Component-level completeには少なくとも次を要求する。

- Gateway mockでhealth/metrics/log/auditを検証可能
- Config stale generation / invalid set handling
- no generic Undo semantics
- Admin permission domain separation
- high-impact confirmation / audit correlation
- session revoke / unauthorized path
- stable Operation / command result tracking

Release完了は `INT-01..INT-03` の real Gateway/Core integration で判定する。
