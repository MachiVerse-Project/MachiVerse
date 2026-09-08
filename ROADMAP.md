# MachiVerse 実装ロードマップ

Status: Rebuilt from completed Phase 4 detailed design  
Tracking: Issue #61

## 1. 目的

本書は MachiVerse 全体の実装順序、横断マイルストーン、開始条件、完了条件を管理する上位ロードマップである。

旧 component 別 roadmap は「Phase 0 で設計を確定し、その後に実装順を決める」構成だったが、詳細設計 Phase 4 はすでに完了しており、production implementation へ移行可能な 38 standard work package まで確定している。

したがって本ロードマップでは、設計フェーズを再実行せず、確定済みの `ImplementationWorkId` と dependency DAG を実装マイルストーンへ再編する。

## 2. 正本と優先順位

実装契約の正本はロードマップではなく設計文書である。

主な参照先:

- `docs/design/phase4-completion-review.md`
- `docs/design/phase4-implementation-work-breakdown.md`
- `docs/design/phase4-test-acceptance.md`
- `docs/design/phase4-platform-runtime-profile.md`
- Phase 1〜4 の各 specification / completion review
- `docs/requirements/`

本書は「何を、どの順で実装するか」を管理し、schema、algorithm、Protocol、Config、Persistence、security、performance、acceptance semantics を再定義しない。

実装中に確定契約を変更する必要が生じた場合、implementation Issue 内で silent change せず design amendment Issue を作成する。

## 3. 現在地

詳細設計 Phase 4 completion review の判定:

- P4-01〜P4-09: Complete
- unresolved detailed-design blocker: 0
- standard implementation work package: 38
  - Simulation Core: 15
  - Gateway: 7
  - General View: 5
  - Administration View: 4
  - QA: 4
  - Integration: 3

現在は **M0 Contract Baseline Consolidation** とする。

## 4. 全体マイルストーン

### M0 — Contract Baseline Consolidation

目的: 詳細設計完了後の正本・ロードマップ・既存 Issue の前提を一致させる。

作業:

- Phase 4 completion review を現行実装 baseline として明示する
- 旧 architecture/protocol 文書に残る stale TBD を Phase 4 と整合させる
- repository / component roadmap を本構成へ移行する
- component roadmap Issue #35〜#38 を `SIM-*` / `GW-*` / `VIEW-*` / `ADMIN-*` trackerへ移行する
- `ImplementationWorkId` を保持した implementation Issue を依存順に起票可能な状態にする

Exit gate:

- 正本の優先順位に矛盾がない
- roadmap と `phase4-implementation-work-breakdown.md` が一致する
- Stage A の Issue を開始できる

### M1 — Foundation / Schema & Scaffold

対象 work package:

- `QA-01` Contract fixtures / schema golden vectors
- `SIM-01` Core project scaffold / deterministic primitives
- `GW-01` Gateway project scaffold / protocol-config foundation
- `VIEW-01` General View scaffold / Gateway protocol client
- `ADMIN-01` Admin View scaffold / Gateway protocol client

方針:

- 5 package は可能な範囲で並列開始する
- component 間 compiled DLL / shared DTO dependency を作らない
- protocol/schema fixture を component 独立 test の共通基準とする

Exit gate:

- 各 component が単独 build/test 可能
- schema golden fixture が利用可能
- deterministic primitive / protocol envelope / client shell の基礎が成立

### M2 — Common Runtime / State & Persistence

対象 work package:

- `SIM-02` Core Config coordinator
- `SIM-03` Persistence engine
- `SIM-04` WorldState / 97 partition registry
- `GW-02` Core protocol / confirmed cache / resync
- `VIEW-02` Confirmed state store / publication consumer
- `ADMIN-02` Health / metrics / log / audit UI（fixture-based parallel）
- `ADMIN-03` Config / operational command management（fixture-based parallel）

Exit gate:

- Core Config/Persistence/WorldState の基礎が成立
- Gateway が confirmed state / continuity / resync を fixture で検証可能
- View が FULL/DELTA と resync lifecycle を処理可能
- Admin management UI が protocol fixture で独立開発可能

### M3 — Runtime Spine / Auth / Operation Lifecycle

対象 work package:

- `SIM-05` Operation lifecycle / scheduling / dedup
- `SIM-06` StepCoordinator / deterministic merge / transaction engine base
- `GW-03` Peer / Master / custody / retry
- `GW-04` OIDC/BFF session / authentication
- `VIEW-03` Three.js scene projection / renderer
- `VIEW-04` Prediction / reconciliation / Operation controller
- `ADMIN-04` High-impact / simulation Admin Operation（fixture-based）

Exit gate:

- Operation が stable identity / scheduling / dedup contract に従う
- deterministic Step execution の spine が成立
- Gateway Master/custody/auth path が fixture で成立
- General View が confirmed state を 3D 表示し、non-authoritative prediction を分離できる

### M4 — Domain Parallel Implementation

対象 work package:

- `SIM-07` Spatial / Environment
- `SIM-08` Physical / Built
- `SIM-09` Resident / Participation
- `SIM-10` Society / Economy
- `SIM-11` Governance / Security
- `SIM-12` Infrastructure / Information
- `SIM-15` Core observability / telemetry
- `GW-05` Authorization / View + Admin boundaries
- `GW-07` Gateway observability / management audit

方針:

- `SIM-07..SIM-12` は stable DomainRuntime API を前提に最大限並列化する
- 未merge dependency は fixture contract を使用して先行可能
- Gateway security/audit は domain 実装と並行して進める

Exit gate:

- 8 domain / 97 authoritative partition の minimum implementation が揃う
- component security / observability baseline が成立

### M5 — Cross-domain / Protocol / Verification Completion

対象 work package:

- `SIM-13` Cross-domain transactions / detail transitions
- `SIM-14` Core protocol boundary / publication projection
- `GW-06` Publication / result routing / backpressure
- `VIEW-05` Participation UX
- `QA-02` Determinism / replay harness
- `QA-03` Crash / fuzz / security harness

Exit gate:

- 17 cross-domain transaction semantics が実装される
- Core↔Gateway publication / operation / status / resync が production implementation で接続可能
- Gateway↔View/Admin delivery path が完成
- worker 1/4/8/16、restart、retry、malformed/security corpus を検証可能

### M6 — Integration / Reliability / Release Acceptance

対象 work package:

- `INT-01` Single Gateway end-to-end
- `INT-02` Multi-Gateway failover / resync
- `QA-04` Performance / soak harness
- `INT-03` Release acceptance

主要 scenario:

```text
INT-01: Core + 1 Gateway + General View + Admin View
INT-02: Core + 4 Gateway + View churn
INT-03: full release acceptance
```

Exit gate:

- login / publication / Diver participation / Admin management / save-recovery が E2E で成立
- Master failover / stale generation / custody convergence / resync が成立
- `perf.reference.v1` と 24h soak を含む non-waivable acceptance を通過
- ReleaseAcceptanceRecordV1 を生成可能

## 5. 依存関係の骨格

```text
QA-01
 ├─ SIM-01 -> SIM-02/03/04 -> SIM-05/06 -> SIM-07..12 -> SIM-13 -> SIM-14
 ├─ GW-01  -> GW-02 -> GW-03 -> GW-04 -> GW-05 -> GW-06
 │                                      └────────────-> GW-07
 ├─ VIEW-01 -> VIEW-02 -> VIEW-03
 │                     └-> VIEW-04 -> VIEW-05
 └─ ADMIN-01 -> ADMIN-02
               └-------> ADMIN-03 -> ADMIN-04

SIM-06 -> QA-02
SIM-03 + GW-04 -> QA-03
SIM-13 + SIM-14 + GW-06 + QA-02 -> QA-04

component minimum flows -> INT-01 -> INT-02 -> INT-03
```

細部の dependency は `docs/design/phase4-implementation-work-breakdown.md` を正本とする。

## 6. Component roadmap

- `docs/roadmap/simulation-core.md`
- `docs/roadmap/gateway.md`
- `docs/roadmap/general-view.md`
- `docs/roadmap/administration-view.md`
- `docs/roadmap/quality-integration.md`

GitHub上のcomponent tracker:

- #35 Simulation Core implementation
- #36 Gateway implementation
- #37 General View implementation
- #38 Administration View implementation

## 7. Branch / PR 原則

- Simulation Core implementation: `simulation` から作業 branch
- Gateway implementation: `gateway` から作業 branch
- General View implementation: `view` から作業 branch
- Administration View implementation: `administration-view` から作業 branch
- repository 共通 docs / protocol: `documentation` から `docs/*` branch
- cross-component integration: 各 component PR を責任 branchへ統合後、`develop` 上で検証

複数 component の production implementation を同一 feature branch へまとめない。

## 8. Issue 運用

各 implementation Issue は `ImplementationWorkId` を title/body に保持する。

最低限:

- Work ID
- target component / base branch
- authoritative design docs
- scope / out of scope
- dependencies
- acceptance TestCaseId
- Definition of Done

日付ベースの進捗より dependency gate を優先し、未成立の依存を「予定日が来た」という理由で迂回しない。

## 9. 旧 roadmap からの移行

Issue #35〜#38 は当初、詳細設計 Phase 4 完了前の「Phase 0 設計確定」を追跡するために作られた。

現在はこれらをcloseせず、各componentのimplementation trackerとして継続利用する。

- #35: `SIM-01..SIM-15`
- #36: `GW-01..GW-07`
- #37: `VIEW-01..VIEW-05`
- #38: `ADMIN-01..ADMIN-04`

旧Phase 0 checklistを新しいimplementation scopeとして再解釈しない。Phase 4ですでに確定済みの内容は再設計せず、stale architecture/protocol表現の同期だけをM0のbaseline normalizationとして扱う。

Phase番号ベースで別系列のroadmap Issueを増やさず、実装作業は `ImplementationWorkId` 単位で追跡する。