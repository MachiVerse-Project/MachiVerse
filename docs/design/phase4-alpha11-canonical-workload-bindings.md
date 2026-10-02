# Alpha 1.1 / INT-03 — canonical workload binding 監査

状態: 設計確定 / production implementation pending  
追跡: #240  
実装: Draft PR #265  
対象: `perf.reference.v1`

## 1. 現在の正本

以前この文書で列挙していた3つの未決定bindingはすべて設計済み。

Exact normative spec:

- `phase4-alpha11-canonical-workload-v1.md`

親index:

- `phase4-alpha11-normative-closure.md`

## 2. 確定した3 binding

### Operation authority binding

- 6 QA-04 family -> existing production OperationKind
- immutable payload selector
- admission basis / min lead / effective Step
- SameStepOrderKey phase/domain rank/priority/intent
- conflict scope digest
- descriptor payload digest == durable accepted payload digest

### Transaction creation binding

- 12 benchmark buckets -> existing17 transaction kinds
- `other-registered-transactions` -> remaining6 kinds exact round-robin
- required/any-of/optional participant rule
- participant owner partition / intent id / effect digest
- exact10k persistent active setとのturnover binding

### Detail transition binding

- 30k promotion /80k demotion = `EstimatedRecordCount` budget workload
- 89 cadence points in 27,000-Step run
- 1,424 unique canonical tile detail regions
- ConfigPolicy trigger
- existing hysteresis/budget/defer semantics
- conservation validation

## 3. machine-readable blocker

次はimplementation pendingのため維持する。

```text
qa04.workload.operation-authority-binding-undefined
qa04.workload.transaction-creation-binding-undefined
qa04.workload.detail-transition-request-binding-undefined
```

failure code名は過去の「undefined」状態を含むが、code renameは不要な互換性変更を避けるため、各implementationが完成するまで保持する。

Design completionだけで workload blockerを除去しない。
