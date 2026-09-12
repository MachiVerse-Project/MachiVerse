# Alpha 1.1 Participation control_mode 正本仕様

Status: Complete / normative benchmark authority

Tracking: #240（旧 #307 は統合済み）

Implementation PR: #265

## 1. 承認と適用範囲

[Issue #240 の2026-09-12承認決定](https://github.com/MachiVerse-Project/MachiVerse/issues/240#issuecomment-5644534723)により、#308 recommended packageを採択した。本書は `perf.reference.v1` の正本仕様である。文書統合の完了はproduction proofの成功を意味しない。

`participation.control_mode` は既存StandardDomainPartitionRegistry v1のauthoritative partitionを使用する。新partition/schemaは追加しない。

## 2. 承認済みbenchmark契約

```text
population:
  1,000,000 explicit participation.control_mode records
  exactly one per canonical Resident

benchmark accounting:
  canonical record total 5,760,000 -> 6,760,000
  +1,000,000 / +17.36% record cardinality

record identity:
  world_id       = perf.reference.v1 WorldId
  creation_step  = 0
  creator_domain = participation
  creator_entity = ZERO
  creation_kind  = perf.control-mode
  local_ordinal  = residentOrdinal

payload genesis:
  resident_ref               = corresponding actual resident.identity_lifecycle record
  binding_ref                = NONE
  mode                       = autonomous
  effective_from             = 0
  input_authority_generation = 0

exact mode token vocabulary:
  Autonomous            = autonomous
  DiverControlAvailable = diver-control-available
  DiverAbsentPolicy     = diver-absent-policy
  BoundResidentDeceased = bound-resident-deceased

envelope DetailLevel:
  mirror Qa04ReferenceLoadV1.ResidentDetailLevel(residentOrdinal)
  D0 100,000 / D1 300,000 / D2 400,000 / D3 200,000

proof/measurement:
  full 1,000,000 production materialization
  actual Resident Ref closure
  one effective mode/resident
  full production Snapshot encode/recovery/semantic rehash
  exact encoded byte count
  available encode/recovery time and memory metrics
  actual transaction participant pool binding
  fail-closed negative tests
  full current-head CI
```

Resident D0/D1/D2/D3はResident persistent identity 1,000,000の内訳であり、追加recordとして二重計上しない。control-modeは独立した1,000,000 recordsとしてbenchmark population表へ追加する。

`resident_ref` はactual Resident Refであり、Resident RecordIdをParticipation RecordIdへ流用しない。ZERO creatorとcanonical ordinalのidentity recipeを使用し、Residentとの関係はpayloadへ保持する。

`input_authority_generation=0` はbenchmark genesisでexternal control input authorityが未適用であることを表す。別state classのabsence-policy generationに関する0禁止条件は流用しない。

必須secondary indexは `participation.control-by-resident`、schema invariantは `one effective mode / resident`。既存runtime enumの意味を維持して上記exact StableTokenへ写像する。

## 3. Diver–Resident bindingの継続性

同じ承認コメントによる標準要件として、Residentに一度Diverを割り当てた後は通常処理で別Diverへ変更しない。

- disconnect / reconnect / logout / 長期不在ではbindingを解除・変更しない。
- 別Diverへの自動再割り当ては禁止する。
- 存命ResidentのDiver割り当て変更は管理者権限による明示的変更だけを許可する。通常のuser-level release/rebindでResident側のDiverを交代させない。
- Resident死亡時はbindingを終了可能とする。死亡後、同じDiverが参加規則に従って別の既存Residentへbindすることは可能とする。
- 管理者によるbinding authority変更ではgenerationを進め、旧generationの操作authorityをstaleとして拒否する。
- `1 Resident : at most 1 active Diver` と `1 Diver : at most 1 active Resident`、binding history保持を維持する。

benchmark genesisではDiver / active bindingを生成しない。binding lifecycleの標準要件をbenchmarkの架空bindingで実証した扱いにしない。

## 4. 本番実装・検証要件

実装では少なくとも以下を実証・実測する。

1. actual canonical Resident authorityをresolverへ登録
2. 承認済みcardinality（1,000,000）のcontrol-mode recordsをmaterialize
3. existing `participation.control_mode` schemaに対するproduction payload validation
4. exact enum -> Token mapping validation
5. one effective mode/resident invariant validation
6. actual Resident Ref closure
7. secondary index / resident uniqueness proof
8. full production Snapshot encode / recovery / semantic rehash
9. encoded Snapshot byte count
10. encode / recovery elapsed time（既存harnessが取得可能な範囲）
11. process / steady-memory metrics（既存harnessが取得可能な範囲）
12. current 5,760,000-record baselineと承認済み6,760,000-record runの比較
13. canonical transaction participant poolとしてactual recordsを使用
14. missing Resident / wrong partition / duplicate resident / invalid token / genesis driftのnegative tests
15. participant authority 7/8 -> 8/8判定
16. transaction creation parent blocker解除判定
17. direct dependency 3 -> 0判定
18. current-head full CI

Snapshot bytes / time / memoryは、このproduction measurement前には確定扱いしない。


## 5. 統合順序と判定境界

#308をdocumentationへ統合し、documentationからdevelopへのPR同期を完了した後に、既存#265へ実装する。既存typed payloadまたはcanonical adapterを通じ、production validation / Snapshot / recoveryに接続する。

production proof成功まではdirect dependencies=3、Transaction participant authority=7/8の実績値を更新しない。成功後にのみ3→0、7/8→8/8とTransaction creation parent blocker解除を判定する。

Infrastructure / Society-Governance accepted count、reference-world parent blockers、release flagsはこの文書承認だけでは変更しない。`referenceWorldMaterialized` と `authoritativeStepLoopAvailable` は各々の全gate成功までfalseを維持する。

record cardinalityの+17.36%からSnapshot bytes/time/memoryを外挿しない。測定値はproduction measurementによってのみ確定する。sparse synthetic pool、reduced/preflight evidenceによる代用は認めない。
