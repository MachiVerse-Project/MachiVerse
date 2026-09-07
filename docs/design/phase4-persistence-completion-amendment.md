# 詳細設計 Phase 4: Persistence Completion Status Amendment

Status: Complete / P4-04 completion normalization  
Tracking: Issue #131  
Parent: `phase4-persistence-specification.md`  
Completion authority: `phase4-persistence-completion-review.md`, `phase4-completion-review.md`

## 1. 目的

`phase4-persistence-specification.md` は P4-04 作業途中に作成されたため、先頭 Status が `In Progress` のまま残り、section 37 `P4-04 acceptance status` に後続成果物で既に解消された「未確定」項目が残存している。

本amendmentは、その作業途中表記を Phase 4 completion後の解釈へ正規化する。Persistence semantic contract、schema、format、migration ruleを新規変更するものではない。

## 2. 正本優先順位

`phase4-completion-review.md` section 2 の規則に従い、Phase 4個別文書の作業途中Status/残作業記述とcompletion判定が競合する場合は completion review を優先する。

P4-04については次をcompletion authorityとする。

1. `phase4-completion-review.md`
2. `phase4-persistence-completion-review.md`
3. `phase4-persistence-record-catalog.md`
4. `phase4-persistence-specification.md` の確定済み本文
5. `phase4-persistence-specification.md` の作業途中Status/残作業記述

## 3. `phase4-persistence-specification.md` Statusの正規化

Phase 4 completion後のeffective statusは次とする。

```text
Status: Complete / P4-04
```

ファイル先頭に残る `Status: In Progress / P4-04` は履歴上の作業途中表記であり、現在のP4-04 completion状態を表さない。

## 4. Section 37の旧「未確定」一覧

`phase4-persistence-specification.md` section 37 に残る次の項目は、後続Phase 4成果物によりすべて解消済みである。

| 旧未確定項目 | Completion source | Effective result |
|---|---|---|
| exact history payload schemas | `phase4-persistence-record-catalog.md` sections 4–11 | fixed |
| `SameStepOrderKey` binary DB encoding | `phase4-persistence-record-catalog.md` section 3 | fixed 55-byte encoding |
| Snapshot chunk target sizing/splitting | `phase4-persistence-record-catalog.md` sections 13–14 | 32 MiB target / 64 MiB max |
| historical replay retention default | `phase4-persistence-record-catalog.md` section 16 | full logical history retention / State(0) floor |
| compaction transaction/anchor policy | `phase4-persistence-record-catalog.md` sections 16–17 | v1.0 semantic compaction disabled; physical maintenance only |
| backup/export bundle format | `phase4-persistence-record-catalog.md` sections 18–22 | `MachiVerseWorldExportV1` fixed |
| performance budget cross-review | Phase 4 P4-06 artifacts / `phase4-completion-review.md` | complete |

したがって section 37 の旧「未確定」一覧を、現行implementation contractの未解決事項として扱ってはならない。

## 5. Portable export/import effective contract

Standard portable export physical boundaryは `phase4-persistence-record-catalog.md` sections 18–22 を正本とし、少なくとも次を固定する。

```text
MachiVerseWorldExportV1/
  export-manifest.pb
  snapshot/
    manifest.pb
    chunks/...
  history/
    00000000.mvlog
    00000001.mvlog
    ...
```

History segment framingは `MVLOG001` v1.0。

Exportはcommitted Snapshot + required history rangeのconsistent bundleとし、Importはexisting active generationを直接上書きせず、新しいPersistenceGeneration stagingへ検証loadして成功後のみactivateする。

protobuf physical bytesをsemantic digest authorityにしない既存規則は維持する。

## 6. Implementation interpretation

Implementationは次を守る。

- `backup/export bundle format` を未確定としてformat-neutral standardへ後退させない。
- `MachiVerseWorldExportV1` / `MVLOG001` をPhase 4 completed contractとして実装する。
- semantic digestはschema-normalized valueから計算し、protobuf serialized bytesをauthorityにしない。
- existing active persistence generationをin-place importで上書きしない。
- contract変更が必要なら `phase4-completion-review.md` section 20 に従い別design amendmentを先行する。

## 7. Completion decision

P4-04はComplete。

Portable export/importを含む unresolved persistence detailed-design blockerは **0件**。

本amendmentは `phase4-persistence-specification.md` の作業途中Status/section 37残作業記述をsupersedeし、Record Catalog / Persistence Completion Review / Phase 4 Completion Reviewと同じcompletion状態へ正規化する。
