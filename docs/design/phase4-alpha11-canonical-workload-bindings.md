# Alpha 1.1 / INT-03 — canonical workload binding 監査

状態: 未決定 binding の監査済み / production binding 未完了  
追跡: #240  
実装: Draft PR #265  
対象: `perf.reference.v1`

## 1. 目的

`Qa04ReferenceLoadV1` / `Qa04ReferenceScenariosV1` には、canonical workload の件数・identity・mix・cadence が既に決定論的に定義されている。

一方、それらの descriptor を production runtime の scheduler、Domain Operation、CrossDomainTransaction、detail transition authority へ接続するには、まだ正本で固定されていない binding がある。

本書はその不足だけを整理する。未定義の `SameStepOrderKey`、domain payload、transaction participant material、detail transition semantics を新しく定義しない。

machine-readable mirror は `Qa04CanonicalWorkloadDependencyContractV1` とし、現在の workload blocker は **3件**。これは canonical reference-world の `Qa04ReferenceWorldDependencyContractV1` **9件とは別契約**であり、件数を合算して world blocker と呼ばない。

## 2. 既に固定されている workload input

### 2.1 Operation injection

`Qa04ReferenceLoadV1` は次を固定済み。

- steady: 5,000 Operation / Step
- burst: 900 Steps ごとに +50,000
- 6 operation families と permille share
- `(profile, step, family, ordinal)` から deterministic `OperationId`
- 同じ context から deterministic 32-byte payload digest
- steady Step は 5,000、burst Step は 55,000 descriptors

これらは smoke で count、ID uniqueness、repeatability、payload digest repeatability を検証済み。

### 2.2 CrossDomainTransaction scenario

`Qa04ReferenceScenariosV1` は次を固定済み。

- active target = 10,000
- creation cadence = 300 Steps
- 12 benchmark transaction buckets と permille mix
- deterministic transaction identity / subject identities

production `CrossDomainTransactionKindRegistryV1` は17 standard kindsを持つ。

benchmark bucket のうち `other-registered-transactions` を除く11種は、`transaction.<benchmark-kind>` として現在の production registry に存在する。`other-registered-transactions` は単一 production kind ではなく、どの残り registered kinds へどう配分するかが未固定。

なお active transaction の durable persistence/reconstruction authority は reference-world 側の独立 blockerであり、本書の creation binding と混同しない。

### 2.3 Detail transition scenario

`Qa04ReferenceScenariosV1` は300 Stepsごとに次を固定済み。

- promotion: 6 regions / 30,000 candidate records
- demotion: 10 regions / 80,000 candidate records
- deterministic region ID
- deterministic candidate ID

production runtime には `DetailTransitionRequestV1` / `DetailTransitionCandidateV1` / trigger authority が存在する。

## 3. Workload blocker 1 — Operation authority binding

failure code:

`qa04.workload.operation-authority-binding-undefined`

### 固定済み

- `OperationId`
- payload digest
- injection Step
- family token
- family ordinal
- steady / burst count
- Phase 1 の一般的 `SameStepOrderKey` 構造

`SameStepOrderKey` は次を要求する。

- phase
- deterministic domain rank
- conflict scope digest
- semantic priority
- intent id

Phase 1 では external input phase や default semantic priority の一般規則はあるが、QA-04 の6 family を production semantics へ写像する family-specific rule は固定されていない。

### 決定が必要

各 QA-04 operation family について最低限:

- authoritative Domain / Operation kind
- production payload schema と descriptor からの lossless materialization rule
- logical conflict scope
- resulting deterministic domain rank source
- semantic priority を default 0 とするか、別の正本値を持つか
- `SameStepOrderKey` の intent/source identity derivation
- scheduler admission / effective Step rule
- domain runtime が生成すべき mutation / transaction / result semantics

`OperationId` の大小を business priority や conflict-scope 代用にしない。

## 4. Workload blocker 2 — Transaction creation binding

failure code:

`qa04.workload.transaction-creation-binding-undefined`

### 固定済み

- creation cadence
- active target
- benchmark transaction mix
- deterministic descriptor identity
- 11 benchmark kindsに対応する production registered kind

### 決定が必要

- benchmark kind token -> production `transaction.*` token の正式 mapping
- `other-registered-transactions` を残り registered kindsへ配分する exact rule
- required / optional / any-of participant domains から actual participant materialを作る rule
- per-domain prepare/material payload
- invariant set / decision material
- creation Step と basis/effective Step の exact relation

active lifecycle の persistent authority が別途決まるまでは、creation binding だけ実装しても `referenceWorldMaterialized=true` や release evidence を許可しない。

## 5. Workload blocker 3 — Detail transition request binding

failure code:

`qa04.workload.detail-transition-request-binding-undefined`

### 固定済み

- cadence
- promotion/demotion region count
- candidate record count
- region/candidate identity
- production `DetailTransitionRequestV1` の required fields

### 決定が必要

QA-04 batch から `DetailTransitionRequestV1` へ写像するための:

- `DomainToken`
- `CurrentLevel`
- `TargetLevel`
- `RequiredEffectiveStep`
- `SemanticPriority`
- `TriggerSource`
- `TriggerId`
- `TriggerObservedStep`
- region ごとの `EstimatedRecordCount` の exact allocation
- promotion/demotion 対象 record set と conservation material

`promotion` / `demotion` という batch token だけからこれらを推測しない。

## 6. reference-world blocker との関係

workload 3 blocker は world 9 blockerを置換しない。

特に次の workload は、対応する canonical world/schema が先に完成しない限り production 実行できない。

- collision load -> Physical `RecordSchema`
- environment load -> Environment `PartitionMapping`
- market load -> Market `RecordSchema` + Society/Governance decomposition
- infrastructure load -> Infrastructure `RecordSchema` / decomposition
- transaction lifecycle -> CrossDomainTransaction `PersistentAuthority`

したがって現在は:

```text
reference world dependency blockers = 9
canonical workload dependency blockers = 3
referenceWorldMaterialized = false
authoritativeStepLoopAvailable = false
```

を維持する。

## 7. 実装可能になる条件

world/schema側の依存と本書の3 bindingが正本化された後に、次の順で production loopへ接続する。

1. canonical Operation materialization / scheduling
2. operation family -> Domain runtime path
3. transaction creation/candidate path
4. detail transition request/candidate path
5. environment / collision / market / infrastructure scenario binding
6. full authoritative Step loop
7. 9,000 warm-up + 18,000 measurement × workers 1/4/8/16 × 3 runs

この順序は実装依存を表すものであり、未定義 semantics の提案ではない。
