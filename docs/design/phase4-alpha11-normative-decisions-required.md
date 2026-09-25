# Alpha 1.1 / INT-03 — 残り正本判断

Status: Resolved design audit / implementation pending  
Tracking: #240  
Implementation: Draft PR #265

## 目的

この文書は、Alpha 1.1で以前「正本判断待ち」として実装を停止していた項目の監査履歴を示す。

**未決定事項はすべて設計済み。** 現在のnormative entry pointは:

- `phase4-alpha11-normative-closure.md`

各scopeのexact spec:

- `phase4-alpha11-reference-world-decomposition.md`
- `phase4-alpha11-physical-occupancy-v2.md`
- `phase4-alpha11-market-transaction-v2.md`
- `phase4-alpha11-infrastructure-network-v2.md`
- `phase4-alpha11-terrain-canonical-generation.md`
- `phase4-alpha11-body-region-state-v1.md`
- `phase4-alpha11-core-effect-custody-v2.md`
- `phase4-alpha11-canonical-workload-v1.md`

## 解消した設計待ち

- Physical collision-shape v2 exact arms / migration / benchmark material
- Environment D0/D1 exact partition split / D1 aggregation / genesis rules
- Society/Governance 2M exact decomposition
- MarketState/order/fact v2 schema / migration / benchmark market
- Infrastructure network/node/edge v2 / full500k decomposition
- Terrain coordinate/SDF/material/root/connectivity/revision/lineage
- `BodyRegionStateV1` exact nested schema/vocabulary
- active CrossDomainTransaction owner/lifecycle/SQLite/Snapshot/history/recovery/turnover
- canonical Operation scheduler/payload binding
- transaction creation kind/participant binding
- detail transition trigger/region/budget/defer/conservation binding

## 重要: blockerはまだ実装待ち

`Qa04ReferenceWorldDependencyContractV1` の9 failure codesと `Qa04CanonicalWorkloadDependencyContractV1` の3 failure codesは、**設計不足を示す名前を持つものも含め、互換性のため実装完了まで維持する**。

設計文書が存在するだけでは blockerを外さない。各codeは対応production implementation + negative validation + Snapshot/recoveryまたはruntime authority proofがgreenになったcommitで除去する。

したがって現時点でも:

```text
referenceWorldMaterialized=false
authoritativeStepLoopAvailable=false
PR #265 = Draft
```

を維持する。
