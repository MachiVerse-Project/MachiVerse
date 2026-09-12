# Alpha 1.1 Participation control_mode authority audit

Status: Audit / normative proposal pending  
Tracking: #307  
Release tracking: #240  
Implementation PR: #265

## 1. Purpose

`perf.reference.v1` transaction genesisに残る最後のparticipant authorityである `participation.control_mode` について、既存schema/runtime semanticsとbenchmark initial population definitionの間にある未決定事項を明示し、explicit approval可能なdecision packageまで絞り込む。

本書はaudit / proposalであり、本書だけでは新しいpopulation/count/token/genesis値を正本化しない。

## 2. Current blocker surface

`Qa04ParticipationControlModeDependencyContractV1` のremaining blockerは3件:

1. canonical population authority
2. mode Token vocabulary
3. genesis control state

DetailRegionは #305 / #306 / #309 と #265 production proofで解消済み。direct canonical dependencyは現在この3件のみ。

Transaction participant authorityは現在7/8。`participation.control_mode`がactual authority化されれば8/8へ到達可能だが、normative decisionとproduction proof前にその状態へ更新してはならない。

## 3. Existing schema authority

`participation.control_mode` payload:

```text
resident_ref: Ref
binding_ref: Ref?
mode: Token
effective_from: Step
input_authority_generation: uint32
```

schema rule:

```text
one effective mode / resident
```

required secondary index:

```text
participation.control-by-resident
```

partitionはStandardDomainPartitionRegistry v1のauthoritative partitionであり、新partition追加は不要。

## 4. Existing runtime semantic authority

`ResidentControlModeV1`:

```text
Autonomous = 1
DiverControlAvailable = 2
DiverAbsentPolicy = 3
BoundResidentDeceased = 4
```

`ParticipationControlContextFactoryV1` semantics:

- active binding + available -> DiverControlAvailable
- active binding + unavailable -> DiverAbsentPolicy
- resident-deceased binding -> BoundResidentDeceased
- otherwise -> Autonomous

Phase 3 designも同じsemantic classesを `AUTONOMOUS`, `DIVER_CONTROL_AVAILABLE`, `DIVER_ABSENT_POLICY`, `BOUND_RESIDENT_DECEASED` として定義している。

enum/runtime semanticは既に存在するが、persistent StableToken serialization vocabularyは未定義。

## 5. Existing benchmark authority

Canonical Resident persistent identity:

```text
1,000,000
```

Resident identity recordsはactual `resident.identity_lifecycle` authorityとしてmaterialize済み。

`Qa04ReferenceLoadV1` の現行canonical record classes合計は:

```text
5,760,000 records
```

Resident DetailLevel distribution:

```text
D0 100,000
D1 300,000
D2 400,000
D3 200,000
-------------
   1,000,000
```

この1,000,000はResident population内部のdetail distributionであり、Resident persistent identityとは別の追加1,000,000 recordsではない。load accountingで二重計上しない。

一方、`phase4-performance-benchmark-profile.md` のinitial-world population表はParticipation control-mode record countを独立state classとして列挙していない。またbenchmarkはDiver population / active Participation binding populationを定義していない。

## 6. Transaction requirement

Canonical cross-domain transaction genesisはParticipation participantについてactual canonical record pool:

```text
partition = participation.control_mode
```

を要求する。poolがmissing/emptyの場合はfail closedする。

transaction materializerだけを見るとpool countは1以上なら構造的に成立する。しかしtransaction都合だけでworld-authority cardinalityを決めてはならない。

## 7. Population alternatives

### 7.1 Sparse transaction-only pool

例:

```text
1 record
10,000 records
```

これはtransaction genesisを少数recordで成立させられる一方、`one effective mode/resident`との関係、pool外Residentのcontrol authority、implicit Autonomous semanticsが未定義になる。

**採用候補にしない。**

### 7.2 Full per-Resident explicit authority

Schema-faithful candidate:

```text
population = canonical Resident persistent identity count = 1,000,000
one control-mode record per Resident
```

利点:

- `one effective mode/resident`を直接満たせる
- actual Resident Ref closureを完全に構成できる
- transaction participant poolをactual world authorityとして利用できる
- later binding/control changesのpersistent owner surfaceとして自然

ただしbenchmark initial population表に明示されていない追加1,000,000 authoritative recordsとなるため、load-impact approvalなしに採用不可。

**Audit recommendation: full per-Resident explicit authorityを採用候補とする。**

## 8. Quantified record-load impact

Full per-Resident candidateをinitial-world canonical recordsへ追加計上する場合:

```text
current canonical records      5,760,000
control_mode candidate        +1,000,000
----------------------------------------
candidate canonical records    6,760,000
```

したがってrecord-count impactは:

```text
+1,000,000 records
+17.36% vs current 5,760,000-record baseline
```

この **+17.36% はrecord cardinalityについてのみ確定する算術影響**である。

Snapshot bytes / encode time / recovery time / steady memoryが同率で増えることを意味しない。既存schema/validation contractだけから1 control-mode recordのexact serialized byte costは固定できないため、Snapshot byte impactは現時点で **TBD** とする。推測値をacceptance基準へ使用しない。

Hidden runtime stateとして別計上するとbenchmarkが実際のauthoritative material量を表さなくなるため、full案を採用するならcanonical totalも5,760,000 -> 6,760,000へ明示改定するのがaudit recommendation。

## 9. Genesis candidate — review only

Resident ordinal `r` 0..999,999 に対し:

```text
resident_ref = actual resident.identity_lifecycle record[r]
binding_ref = NONE
mode = Autonomous
effective_from = 0
```

根拠:

- benchmarkはDiver population / active binding populationを定義していない
- runtime factoryはbinding semantics上のotherwise caseをAutonomousとする
- synthetic binding / Diverをbenchmark都合で生成しない
- Step 0 genesisとしてeffective_from=0が最小の追加semantic

これはproposalであり、approval前に#265へ実装しない。

## 10. Record identity decision

### 10.1 Candidate A — Resident ordinal keyed

```text
DerivedIdentity.DeriveEntityId(
  worldId,
  creationStep=0,
  creatorDomain=participation,
  creatorEntityId=ZERO,
  creationKind=perf.control-mode,
  localOrdinal=residentOrdinal)
```

### 10.2 Existing QA-04 precedent

Production DetailRegion authorityは既に次の構造を採用している:

```text
creatorDomain = spatial
creatorEntityId = ZERO
creationKind = perf.detail-region
localOrdinal = tileIndex
```

つまり「owner domain + ZERO creator + benchmark-specific creation kind + canonical local ordinal」はactual domain-owned persistent authorityで使用済みのpattern。

またgeneric QA-04 record identityもStep 0 / ZERO creator / derivation token / local ordinalを使用する。

### 10.3 Candidate B — Resident identity keyed

Resident RecordIdをcreator inputへ含める別recipe。ただし`creatorEntityId`をsource relationとして使うsemanticを追加で正本化する必要がある。

**Audit recommendation: Candidate A。**

理由:

- existing production QA-04 authority precedentと同型
- Resident RecordIdをParticipation RecordIdとして再利用しない
- source relationはpayload `resident_ref`へ保持できる
- `creatorEntityId`へ新しいsource-relation semanticを持ち込まない

## 11. StableToken vocabulary decision

`StableToken` syntaxはlowercase ASCIIを基準とする `[a-z0-9][a-z0-9._/-]{0,63}`。

Phase 3/runtime semantic labelsを意味変更せずpersistent tokenへ落とすreview candidate:

```text
Autonomous            -> autonomous
DiverControlAvailable -> diver-control-available
DiverAbsentPolicy     -> diver-absent-policy
BoundResidentDeceased -> bound-resident-deceased
```

project-wide検索ではこれら4 tokenの既存persistent precedentは確認できない。そのため「既存tokenを発見した」のではなく、既存semantic classをStableToken grammarへcanonicalizeする**新しいnormative vocabulary proposal**として扱う。

**Audit recommendation: 上記4 tokenをexact vocabularyとして採用。**

## 12. input_authority_generation decision

Project-wide `input_authority_generation` 検索では、field schema / validator以外にinitial-value contractを確認できない。

Participation runtimeのabsence policy generationは0を拒否するが、これは別state classのcontractであり、control-mode `input_authority_generation`へ流用しない。

GenesisではDiver/binding/control-availability input自体が存在しないため、2案がある:

```text
A: 0 = external control input authority未適用
B: 1 = initial persistent control-mode authority generation
```

既存contractからA/Bを導出することはできない。

**Audit recommendation: A (`input_authority_generation = 0`)。**

理由:

- genesis candidateはbinding NONE / Autonomousで、external control inputをmaterializeしない
- field名は`input_authority_generation`であり、record revisionそのものではない
- 0禁止contractが存在しない
- 「初期persistent recordだから1」という別概念をfieldへ混ぜない

これはprecedentではなく、explicit approvalを要するsemantic proposal。

## 13. Envelope DetailLevel decision

### 13.1 Candidate A — Resident detail distribution mirror

対応ResidentのDetailLevelをcontrol-mode envelopeへmirrorする。

`Qa04ReferenceLoadV1.ResidentDetailLevel(residentOrdinal)` は既に:

```text
0 ..  99,999 -> D0
100,000 .. 399,999 -> D1
400,000 .. 799,999 -> D2
800,000 .. 999,999 -> D3
```

を固定している。新しい分布algorithmは不要。

### 13.2 Candidate B — all D0

全1,000,000 control-mode recordsをD0とする。

Mirror candidateとの差分:

```text
                  mirror       all-D0      all-D0 - mirror
D0                100,000     1,000,000         +900,000
D1                300,000             0         -300,000
D2                400,000             0         -400,000
D3                200,000             0         -200,000
```

all-D0はcontrol-mode stateを常にentity-exact authorityとして扱う新しいbenchmark/semantic choiceになる。

**Audit recommendation: Candidate A / Resident DetailLevel mirror。**

理由:

- canonical Resident distributionをそのまま利用できる
- ungrounded all-D0 policyを追加しない
- Resident-control correspondenceがordinalごとに明示的

この分布差からmemory/time差は推定しない。

## 14. Persistent payload implementation surface audit

Develop tree / Participation domain / code searchでは、schema descriptor `participation.control_mode` は存在する一方、`ParticipationControlModePayloadV1` というtyped persistent payload modelは確認できない。

現状確認できるもの:

- Phase 4 payload schema descriptor
- `DomainPayloadValidation` schema signature
- secondary index `participation.control-by-resident`
- runtime `ParticipationControlContextV1`

したがってproduction implementationでは、既存generic domain-payload/Snapshot infrastructureへ接続できる**typed payload representationまたはequivalent canonical adapter**の追加要否をimplementation時に確定する。

これは新partition/schemaを作る意味ではない。StandardDomainPartitionRegistry v1と既存 `participation.control_mode` schemaをそのまま使用する。

Production proof checklistで存在しないclass名を事前にnormative化しない。

## 15. Recommended decision package — review only

#307を一括decisionできるaudit recommendation:

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

このpackageは**推奨案であって未承認**。#307のexplicit normative approval後にdocumentation -> develop sync -> #265 implementationの順で進める。

## 16. Required production proof and measurement after normative decision

採用後は少なくとも以下を実証・実測する。

1. actual canonical Resident authorityをresolverへ登録
2. decided cardinality（recommended packageでは1,000,000）のcontrol-mode recordsをmaterialize
3. existing `participation.control_mode` schemaに対するproduction payload validation
4. exact enum -> Token mapping validation
5. one effective mode/resident invariant validation
6. actual Resident Ref closure
7. secondary index / resident uniqueness proof
8. full production Snapshot encode / recovery / semantic rehash
9. encoded Snapshot byte count
10. encode / recovery elapsed time（既存harnessが取得可能な範囲）
11. process / steady-memory metrics（既存harnessが取得可能な範囲）
12. current 5,760,000-record baselineとcandidate 6,760,000-record runの比較
13. canonical transaction participant poolとしてactual recordsを使用
14. missing Resident / wrong partition / duplicate resident / invalid token / genesis driftのnegative tests
15. participant authority 7/8 -> 8/8判定
16. transaction creation parent blocker解除判定
17. direct dependency 3 -> 0判定
18. current-head full CI

Snapshot bytes / time / memoryは、このproduction measurement前には確定扱いしない。

## 17. Decision checklist

明示approval対象:

1. full 1,000,000 population
2. benchmark total 5,760,000 -> 6,760,000
3. ordinal-keyed RecordId recipe
4. exact StableToken vocabulary
5. Autonomous / binding NONE / effective_from=0 genesis
6. input_authority_generation=0
7. Resident DetailLevel mirror
8. full Snapshot/recovery/performance evidence inclusion

## 18. Approval boundary

このdocumentをmergeしただけではnormative approvalとみなさない。

Approvalは#307でrecommended decision packageを明示採択し、本document statusを`Complete / normative benchmark authority`へ変更したcommitをdocumentationへmergeすることで成立させる。その後developへ同期し、初めて#265 implementationを開始する。

Review時のapproval phrase例:

```text
#307 の recommended decision package で確定して進めて
```

## 19. Decision readiness

Audit上、追加の既存contract探索で自動的に解消できるsemantic decisionは残っていない。

- population / load accounting: trade-offを定量化済み
- identity: existing QA-04 production precedentを確認済み
- token: grammarと既存semantic classを確認済み、exact vocabularyは新規decision
- genesis: runtime fallback semanticsとbenchmark absence-of-bindingを確認済み
- input generation:既存initial-value contract不在を確認済み
- DetailLevel: mirror/all-D0差分を定量化済み
- implementation surface:既存partition/schema維持、typed payload/adapter要否をimplementation concernとして分離済み

したがって次のgateは追加監査ではなく、#307 recommended decision packageのexplicit normative approval。

## 20. Non-goals / boundaries

- transaction成立だけを目的にsparse synthetic poolを作らない
- Resident RecordIdをParticipation RecordIdとして直接再利用しない
- benchmark Diverやactive bindingを勝手に生成しない
- enum名をpersistent Token vocabularyと暗黙変換しない
- benchmark load増加をreference profile外のhidden costにしない
- record count +17.36%からSnapshot bytes/time/memory増分を外挿しない
- absence-policy generation contractをcontrol-mode generationへ流用しない
- new partition/schemaを追加しない
- approval前に#265へcandidate valuesを実装しない
