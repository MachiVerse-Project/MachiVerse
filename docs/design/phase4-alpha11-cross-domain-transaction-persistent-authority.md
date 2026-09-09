# Alpha 1.1: CrossDomainTransaction persistent authority 境界

Status: In Progress / Alpha 1.1 INT-03  
Tracking: Issue #240  
Implementation: Draft PR #265

## 1. 目的

`perf.reference.v1` が要求する steady active `CrossDomainTransaction` 10,000件を、単なる `StepCandidate` 内の一時 candidate ではなく、Snapshot / history / recovery を跨いで lossless に維持できる authoritative state として実装するための既存正本と未決定点を分離する。

未定義事項を推測して persistence schema、lifecycle、snapshot section を新設しない。

## 2. 既に正本で固定されているもの

### 2.1 Runtime transaction candidate

`CrossDomainTransactionCandidateV1` は次を保持する。

- `WorldId`
- `TransactionId`
- `TransactionKind`
- `BasisStep`
- root causality
- ordered subject refs
- ordered participant candidates
- invariant results
- candidate status
- diagnostic digest / failure code

ただし `IsAuthoritative == false` である。

`TransactionCandidateStatusV1` は candidate phase の状態だけを表し、`COMMITTED` を持たない。P4-01 も committed fact は history/event 側で表すと明記している。

### 2.2 Transaction kind registry

standard `CrossDomainTransactionKindRegistryV1` は17 kindを固定済み。

participant domain、required/optional/conditional participant、required invariant registry も既に存在する。

### 2.3 Benchmark descriptor

`Qa04ReferenceScenariosV1` は以下を固定済み。

```text
ActiveCrossDomainTransactionTarget = 10,000
CrossDomainTransactionCreationEverySteps = 300
```

初期10,000 descriptorについて deterministic `TransactionId` と2 subject identityを生成できる。

transaction mix 12 bucket / 1000 permille も固定済み。ただし `other-registered-transactions` の production allocation は workload blockerとして別に残る。

### 2.4 Detail guard

runtime token:

```text
detail.guard.active-transaction
```

は固定済み。

これは active transaction に関係する region/detail authority を性能都合で demote しないための意味論を示すが、persistent active transaction からどの detail region/domainへ guard を付与・解除するかの authority binding は未定義。

## 3. 現行 persistence で不足しているもの

### 3.1 `transition.committed.v1`

P4-04 `TransitionCommittedRecordWireV1` は次を保持する。

- effective/resulting Step
- Config generation/digest
- applied Operation IDs / outcomes
- previous/resulting StateContinuityToken
- StateDiagnostic hash
- partition digests

しかし transaction-level の以下は保持しない。

- `transaction_id`
- transaction kind
- lifecycle transition
- active/terminal state
- participant/subject/causality closure

そのため `transition.committed.v1` の存在だけから active transaction set を lossless reconstruction できるとは扱わない。

### 3.2 Standard history registry

P4-04 initial standard history record typeは次の10種であり、CrossDomainTransaction 専用 persistent lifecycle recordはない。

```text
world.genesis.v1
operation.accepted.v1
operation.scheduled.v1
operation.terminal.v1
config.changed.v1
transition.committed.v1
snapshot.committed.v1
persistence.migrated.v1
master.generation.changed.v1
world.pause.changed.v1
```

新しい history type を追加するのか、既存 transition payload をversion migrationするのか、別 authoritative stateから replayするのかは正本未定義。

### 3.3 Snapshot registry

standard Snapshot は6 Core section + 97 Domain section = exact 103 section。

現状、active CrossDomainTransaction 専用 section はない。

既存6 Core sectionのどこかへ所有させるのか、Domain ownerへ正規化するのか、Snapshot schema migrationで新しい authority representation を導入するのかは未決定。

## 4. 8 subdependencies

`Qa04CrossDomainTransactionPersistentAuthorityDependencyContractV1` は top-level world blocker:

```text
transaction.active-cross-domain.persistent-authority
qa04.material.cross-domain-transaction-authority-undefined
```

1件の下位診断契約として、次を固定する。

1. `AuthorityOwner`
   - active set の唯一の authoritative owner / state location
   - `qa04.cross-domain-transaction.authority-owner-undefined`
2. `BenchmarkTurnoverBinding`
   - initial 10,000 と 300-Step creation mix に対する completion/retirement/replenishment rule
   - `qa04.cross-domain-transaction.benchmark-turnover-binding-undefined`
3. `DetailGuardBinding`
   - active transaction -> guarded detail region/domain の付与/解除 authority
   - `qa04.cross-domain-transaction.detail-guard-binding-undefined`
4. `HistoryCommitBinding`
   - transaction lifecycle transition と participant effect を同じ durable commit factへ結ぶ規則
   - `qa04.cross-domain-transaction.history-commit-binding-undefined`
5. `LifecycleSemantics`
   - active の exact definition、start/terminal state、Step boundary、completion/cancel/failure semantics
   - `qa04.cross-domain-transaction.lifecycle-semantics-undefined`
6. `RecoveryReconstruction`
   - Snapshot anchor + history replay から同一 active set / lifecycle / guard stateへ戻る exact recipe
   - `qa04.cross-domain-transaction.recovery-reconstruction-undefined`
7. `SnapshotAuthority`
   - exact-103 cutで active state を保持する authority representation / section ownership
   - `qa04.cross-domain-transaction.snapshot-authority-undefined`
8. `StateSchema`
   - persistent record の exact fields / version / ordering / Ref closure / canonical digest
   - `qa04.cross-domain-transaction.state-schema-undefined`

## 5. workload blockerとの分離

次の workload blocker は別問題である。

```text
workload.transaction.creation-binding
qa04.workload.transaction-creation-binding-undefined
```

persistent authority blockerは「作成された transaction をStepを跨いでどう authority として保持するか」を扱う。

workload creation blockerは「benchmark transaction descriptor を production participant candidate / invariant / schedulingへどう組み立てるか」を扱う。

両者を統合して blocker 数を減らしてはならない。

## 6. 現時点で実装してはいけないもの

正本決定前に次を行わない。

- `CrossDomainTransactionCandidateV1.IsAuthoritative` を true にする
- candidate objectをそのまま Snapshot authority にする
- `transition.committed.v1` の未定義 field を追加する
- exact-103 section数を独断で104以上へ変更する
- `operation_state` を transaction lifecycle store とみなす
- Domainの contract/obligation/handoff recordを、根拠なく1つのglobal transaction stateへ合成する
- benchmarkの300-Step creationを「全既存transactionを入れ替える」等と解釈する

## 7. blocker 解消条件

この top-level blocker を外せるのは少なくとも以下が全部揃った時だけとする。

- versioned persistent active-transaction schema
- unique authoritative owner / storage location
- exact active lifecycle semantics
- transaction stateを含む consistent Snapshot cut
- transaction lifecycleをparticipant effectsとatomicに結ぶ durable history/commit contract
- restart/recoveryで同一 active setを semantic rehashできること
- active transaction detail guardの再構成
- initial 10,000 + steady turnover の canonical benchmark materialization
- production workload transaction creation bindingとの接続

## 8. 現在の判定

`CrossDomainTransactionCandidateV1`、17 kind registry、invariant validation、same-Step atomicity、benchmark descriptorは利用できる。

しかし persistent active-set authority は存在しないため:

```text
qa04.material.cross-domain-transaction-authority-undefined
```

を維持する。

`referenceWorldMaterialized` は引き続き `false` とする。
