# Phase 4 active CrossDomainTransaction authority 監査

Status: 未解決 blocker の監査結果  
Tracking: #240  
Implementation: Draft PR #265  
Profile: `perf.reference.v1`

## 目的

QA-04 の `transaction.active-cross-domain` 10,000 件について、既存 runtime / persistence / history から authoritative active transaction state を lossless に materialize または再構成できるかを確認する。

この監査では、candidate を永続 authority として昇格させたり、history に存在しない participant/status を推測したりしない。

## QA-04 が固定済みの入力

`Qa04ReferenceScenariosV1` は次を固定している。

- active target = 10,000
- transaction creation cadence = 300 Steps
- 12 transaction kind と permille mix
- ordinal ごとの deterministic `transaction_id`
- 2 subject ids
- basis Step 0 の root `CausalityRefV1`

`Qa04ActiveTransactionDescriptorV1` は benchmark descriptor であり、それ自体は WorldState authority ではない。

## runtime candidate の authority 境界

`CrossDomainTransactionCandidateV1` は Step candidate 内の commit 前 container である。

実装上も次が明示されている。

```text
IsAuthoritative == false
```

candidate status は次だけを持つ。

```text
ASSEMBLING
READY_FOR_VALIDATION
VALID
INVALID
```

`COMMITTED` は candidate status に存在しない。

P4-01 でも、candidate phase は `COMMITTED` を持たず、commit 済み fact は history/event 側で表すと定義されている。

したがって、QA-04 10,000 件を `CrossDomainTransactionCandidateV1` の配列として Snapshot に保存することはできない。

## 現在の transition history が保持する情報

production SQLite transition path は `HistoryRecordMaterial` を `transition.committed.v1` として append する。

現行 `AlphaSingleGatewayRuntime` の normalized transition payload は次だけを保持する。

```text
{
  finalized_step_before_commit,
  terminal_operation_ids[]
}
```

`SqlitePersistenceStore.PersistTransitionCommitAsync` は history record と terminal Operation state を同一 SQLite transaction で commit し、state continuity token を更新する。

しかし、この transition history には active CrossDomainTransaction の次の semantic state が含まれない。

- `transaction_id`
- `transaction_kind`
- participant domain / partition
- participant intent ids
- required participant flag
- participant outcome
- candidate effect digest
- required invariant set / invariant result
- active lifecycle state
- active transaction の開始 Step / 更新 Step /終了条件

したがって、現行 `transition.committed.v1` の履歴だけから QA-04 の 10,000 active transaction authority を exact に再構成することはできない。

## event/history について確定していること

P4-01 は commit 済み transaction fact を history/event 側で表すという ownership boundary を固定している。

一方、現在の正本仕様・production persistence には、**active transaction lifecycle 全体**を lossless に再構成できる exact history/event schema と reconstruction recipe が存在しない。

「commit 済み fact が history/event 側」という規則だけを根拠に、active transaction 10,000 の現在状態を history から生成してはならない。

## blocker を閉じるために必要な決定

次のどちらかを正本仕様として固定する必要がある。

### A. persistent authority を持つ

103 Snapshot section の既存 owner のどこかに、active CrossDomainTransaction state を authoritative record として保持する。

その場合は少なくとも次を exact schema として定義する必要がある。

- stable transaction identity
- transaction kind
- current lifecycle state
- participant binding
- required/optional semantics
- current participant progress/outcome
- required invariants / validation state
- causality anchor
- created/updated/effective Step
- completion/retirement semantics
- canonical order / digest / recovery rule

ただし、どの existing section/partition を owner とするかは現時点で未決定であり、ここでは決めない。

### B. authoritative reconstructable recipe を持つ

active state 自体を Snapshot に保存せず、既存 authoritative Snapshot + versioned history/event records から lossless に再構成する。

この場合は少なくとも次が必要。

- transaction lifecycle event/history の exact schema
- start / participant progress / validation / completion を区別する record
- replay start anchor
- deterministic reconstruction order
- duplicate/missing/out-of-order rejection
- Snapshot cut 時点で active か terminal かを一意に判定する規則
- history compaction 後も reconstruction 可能であることの保証

現在の `transition.committed.v1` だけではこの条件を満たさない。

## 現在の結論

`transaction.active-cross-domain` 10,000 は引き続き **persistent/reconstruction authority 未定義**として扱う。

`Qa04ReferenceWorldDependencyContractV1` の failure code:

```text
qa04.material.cross-domain-transaction-authority-undefined
```

は維持する。

この blocker が閉じるまで、次を行ってはならない。

- `CrossDomainTransactionCandidateV1` を authoritative Snapshot material として保存する
- QA-04 descriptor だけを item_count 10,000 として計上する
- transition history に無い participant/status を reconstruction 時に補う
- synthetic active transaction state を release evidence として使用する

## Alpha 1.1 への影響

この blocker は canonical reference world の 8 initial-world class のうち 1 class を未 materialized のままにする。

したがって、現時点では引き続き:

```text
referenceWorldMaterialized=false
```

であり、PR #265 は Draft、#240 は open のままとする。
