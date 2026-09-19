# Alpha 1.1 — QA-04 reference-world materialization 完了checkpoint

Status: **Accepted production authority checkpoint**  
Tracking: #240, #265  
Parent: `phase4-performance-benchmark-profile.md`

## 1. 目的と適用範囲

本書は、`perf.reference.v1` の初期reference worldについて、承認済みauthorityとproduction proofに基づくmaterialization完了状態を固定するcheckpointである。

本checkpointはQA-04 release gate全体の完了を意味しない。特に、authoritative full Step loop、exact-103 production evidence、worker 1/4/8/16のdeterminism反復、persistence/publication stress、24時間soakは別gateとして残る。

reference-world materialization完了条件は、次のすべてを満たすことである。

1. canonical reference-world classがすべてproduction materializerを持つ。
2. reference-world dependency blockerは、対応するexact production-path proof成功後にのみ解除する。
3. Society/GovernanceおよびInfrastructure/Informationがcanonical decomposition countと一致する。
4. runtime inspectionで`referenceWorldMaterialized=true`を返す場合でも、full Step loop未証明の間は`authoritativeStepLoopAvailable=false`、`releaseEvidenceCapable=false`を維持する。

基礎となるcanonical populationは`phase4-performance-benchmark-profile.md`を上位正本とする。本書はpopulationやbenchmark semanticsを再定義しない。

## 2. Canonical reference classの完了状態

`perf.reference.v1` の9 classはすべてproduction materializer availableである。

| class | canonical count | 状態 |
|---|---:|---|
| `resident.persistent-identity` | 1,000,000 | available |
| `participation.control_mode` | 1,000,000 | available |
| `physical.d0-presence` | 500,000 | available |
| `environment.d0-cell-cohort` | 1,000,000 | available |
| `environment.d1-aggregate` | 250,000 | available |
| `society-governance.active-record` | 2,000,000 | available |
| `infrastructure.active-record` | 500,000 | available |
| `spatial.hot-terrain-brick` | 500,000 | available |
| `transaction.active-cross-domain` | 10,000 | available |

`physical.d0-presence=500,000`および`transaction.active-cross-domain=10,000`は`phase4-performance-benchmark-profile.md`のexact countに従う。

#265 production implementationでは、`Qa04ReferenceWorldDependencyContractV1.Blockers`および対応failure-code setは空であり、`Qa04ReferenceWorldMaterialContractV1.AllProductionMaterializersAvailable=true`である。

## 3. Society/Governance material closure

Society/Governance decompositionはexact **2,000,000 / 2,000,000** accepted recordsで完了している。

最後のGovernance packageは**259,000**件であり、`phase4-alpha11-governance-remaining-authority.md`に固定したbenchmark authorityを使用する。既存の`governance.law_rule` nested Snapshot authorityを維持し、独自flat表現へ置換しない。

production proofで少なくとも次を確認済みである。

- exact 259,000 materialization;
- 全対象partitionのexact count;
- actual reference closure;
- RecordId uniqueness;
- production Snapshot encode / recovery;
- recovered semantic rehash;
- `governance.law_rule` nested value recovery;
- lineage source digest整合;
- drift / missing-ref / wrong-partition等のfail-closed negative proof。

旧blocker:

```text
qa04.material.society-governance-partition-mapping-undefined
```

はproduction proof後に解除済みであり、reference-world dependency contractへ再導入してはならない。

## 4. Infrastructure/Information material closure

Infrastructure/Information decompositionはexact **500,000 / 500,000** records、**16 / 16 slices**で完了している。

aggregate production proofは、個別に承認済みのauthorityを再定義せず合成する。

```text
topology                         120,100
network services + ServiceQueue  290,000
infrastructure.dependency         20,000
infrastructure.facility_service   15,000
information.delivery              20,000
information.media_distribution     5,000
information.record_store          10,000
address/failure/lineage tail      19,900
-----------------------------------------
total                            500,000
```

関連するauthority文書には、少なくとも以下を含む。

- `phase4-alpha11-infrastructure-remaining-authority-audit.md`
- `phase4-alpha11-infrastructure-service-queue-authority.md`
- `phase4-alpha11-facility-service-authority.md`
- `phase4-alpha11-information-delivery-authority.md`
- `phase4-alpha11-infrastructure-tail-authority.md`

aggregate proofでは、16 sliceのexact count、full descriptor coverage、cross-slice RecordId uniqueness、production Snapshot encode/recovery、recovered semantic identityを検証する。

accepted production evidence:

```text
workflow: INT-03 full Infrastructure topology validation
run:      34750371338
job:      103705927245
output:   infrastructure-aggregate-full-production-pass records=500000 recovered=500000 topology=120100 services_queue=290000 dependency=20000 facility=15000 delivery=20000 media=5000 record_store=10000 tail=19900 slices=16
```

このproduction proofと#265のdependency/material contract更新により、旧blocker:

```text
qa04.material.infrastructure-node-edge-authority-undefined
```

は解除済みであり、reference-world dependency contractへ再導入してはならない。

## 5. Runtime inspection境界

本書のruntime値は`documentation` branch単独の実装状態を表すものではなく、#265 production implementationのaccepted checkpointを記録する。

検証対象head:

```text
PR #265 head: 10839b1036c9490eafb9dab82a98b41a1fac7bd7
```

このheadの`Qa04ProcessTargetV1`では、`ReferenceWorldMaterialized()`が次の条件から算出される。

```text
reference-world dependency blocker count == 0
AND
all production materializers available == true
```

その結果、現在のruntime inspection境界はexactly次のとおりである。

```text
referenceWorldMaterialized       = true
authoritativeStepLoopAvailable   = false
releaseEvidenceCapable           = false
```

`referenceWorldMaterialized=true`はreference-world authority/materializer contractの完了のみを意味する。canonical full Step loop、exact-103 evidence、determinism matrix、stress、soakの完了を意味しない。

full Step loopが別途assembled/provenとなるまで、runtime/release evidenceは次のblockerを維持する。

```text
qa04.target.authoritative-step-loop-not-assembled
```

## 6. Fail-closed非回帰条件

次はrelease-contract regressionとして扱う。

- 解決済みreference-world blockerを根拠なく再導入する。
- Society/Governance active recordsを2,000,000以外として扱う。
- Infrastructure/Information active recordsを500,000以外として扱う。
- `physical.d0-presence`を500,000以外へ変更する。
- active CrossDomainTransaction targetを10,000以外へ変更する。
- accepted completion後にcanonical reference classをblockedへ戻す。
- reference world完了のみを根拠として`authoritativeStepLoopAvailable=true`にする。
- remaining release gate未証明のまま`releaseEvidenceCapable=true`にする。
- reduced / Resident-only Step-loop probeをcanonical full-reference-world release evidenceとして扱う。

## 7. 次のrelease-critical gate

reference-world materializationの次に閉じるべきrelease-critical gateは、**authoritative full Step-loop compositionとproduction proof**である。

少なくとも、completed reference worldをactual world stateへ組み上げ、production scheduler / Operation / Detail / domain execution / persistence boundaryを通してcanonical Stepを実行できることを証明する必要がある。

このgateが閉じるまでは、`authoritativeStepLoopAvailable=false`および`releaseEvidenceCapable=false`を維持する。
