# Alpha 1.1 / INT-03 — `BodyRegionStateV1` nested schema bindings

Status: Decided normative design / implementation pending  
Tracking: #240  
Implementation: Draft PR #265

## 1. 現在の正本

以前未決定だった `BodyRegionStateV1` のfield/ordinal/unit/optionality/vocabulary/Ref semanticsはすべて確定した。

Exact spec:

- `phase4-alpha11-body-region-state-v1.md`

Parent:

- `phase4-alpha11-normative-closure.md`

## 2. 確定したschema

```text
domain.resident.body-region-state /1.0
```

Required fields:

```text
region_token
integrity_ppm
function_capacity_ppm
pain_ppm
injury_load_ppm
disease_load_ppm
impairment_ppm
recovery_ppm
```

All ppm are uint32 0..1,000,000。nested Refなし。

Region vocabulary:

```text
body.arm.left
body.arm.right
body.head
body.leg.left
body.leg.right
body.systemic
body.torso
```

Parent listはASCII token ascending。

## 3. implementation blocker

Exact schemaはもう未決定ではないが、codec/materializer/recovery未実装のため互換failure code:

```text
qa04.material.body-region-state-schema-undefined
```

はimplementation完成まで維持する。

設計完了だけで reference-world blockerを減らさない。
