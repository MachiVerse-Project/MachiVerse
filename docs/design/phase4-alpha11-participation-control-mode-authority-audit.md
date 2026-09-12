# Alpha 1.1 Participation control_mode authority audit

Status: Audit / normative proposal pending  
Tracking: #307  
Release tracking: #240  
Implementation PR: #265

## 1. Purpose

`perf.reference.v1` transaction genesisに残る最後のparticipant authorityである `participation.control_mode` について、既存schema/runtime semanticsとbenchmark initial population definitionの間にある未決定事項を明示する。

本書はauditであり、新しいpopulation/count/token/genesis値を正本化しない。

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

enum semanticsは既に存在するが、persistent StableToken serialization vocabularyは未定義。

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

## 9. Full per-Resident candidate values — review only

以下はnormativeではない。

### 9.1 Resident mapping

Resident ordinal `r` 0..999,999 に対し:

```text
resident_ref = actual resident.identity_lifecycle record[r]
```

Resident RecordIdをParticipation RecordIdとして直接再利用しない。

### 9.2 Genesis semantic mode

benchmark Diver/binding populationが未定義であるためcandidateは:

```text
binding_ref = NONE
mode = Autonomous
effective_from = 0
```

### 9.3 Input authority generation

Candidate:

```text
input_authority_generation = 1
```

0をuninitializedとして扱う明示contractは現在ない。initial persistent authority generationとして1を使う案だが、approval前に固定しない。

### 9.4 Token vocabulary candidate

```text
Autonomous            -> autonomous
DiverControlAvailable -> diver-control-available
DiverAbsentPolicy     -> diver-absent-policy
BoundResidentDeceased -> bound-resident-deceased
```

StableToken syntax内だが、exact persistent vocabularyとしては未承認。

## 10. Record identity decision required

### A. Resident ordinal keyed

```text
DerivedIdentity.DeriveEntityId(
  worldId,
  creationStep=0,
  creatorDomain=participation,
  creatorEntityId=ZERO,
  creationKind=perf.control-mode,
  localOrdinal=residentOrdinal)
```

既存QA-04 ordinal-derived identity patternとの整合が高く、Resident RecordIdをidentityとして再利用しない。

### B. Resident identity keyed

Resident RecordIdをcreator inputへ含める別recipe。ただし`creatorEntityId`をsource relationとして使うsemanticを別途正本化する必要がある。

Audit recommendationはAだが、未承認。

## 11. Envelope DetailLevel decision required

### A. Resident detail distribution mirror

対応ResidentのDetailLevelをcontrol-mode envelopeへmirrorする案。

`Qa04ReferenceLoadV1.ResidentDetailLevel(residentOrdinal)` は既に:

```text
0 ..  99,999 -> D0
100,000 .. 399,999 -> D1
400,000 .. 799,999 -> D2
800,000 .. 999,999 -> D3
```

を固定している。したがってAは新しい分布algorithmを発明せず、既存canonical functionから機械的に導出可能。

### B. Control authority fixed entity detail

全control-mode recordsをD0として保持する案。

Aのmechanical feasibilityは確認できたが、schema/runtimeはA/Bのどちらも自動決定していない。**Aが実装可能であることはAのsemantic approvalを意味しない。**

## 12. Benchmark load accounting decision

Full per-Resident authorityを採用する場合、少なくとも次を明記する必要がある。

1. 1,000,000 control-mode recordsを`perf.reference.v1` initial-world canonical loadへ追加するか
2. canonical totalを5,760,000 -> 6,760,000へ改定するか
3. steady memory / initialization / Snapshot sizeのacceptance interpretationをどう扱うか
4. 24h soak / determinism matrixでfull control-mode materialを常時含めるか
5. Snapshot byte/time/memoryの実測結果をどのacceptance gateへ反映するか

負荷増加をimplementation内部のhidden recordsとして扱わない。

## 13. Required production proof and measurement after normative decision

採用後は少なくとも以下を実証・実測する。

1. actual canonical Resident authorityをresolverへ登録
2. decided cardinality（full案なら1,000,000）のcontrol-mode recordsをmaterialize
3. production `ParticipationControlModePayloadV1` validation
4. exact enum -> Token mapping validation
5. one effective mode/resident invariant validation
6. actual Resident Ref closure
7. full production Snapshot encode / recovery / semantic rehash
8. encoded Snapshot byte count
9. encode / recovery elapsed time（既存harnessが取得可能な範囲）
10. process / steady-memory metrics（既存harnessが取得可能な範囲）
11. current 5,760,000-record baselineとcandidate 6,760,000-record runの比較
12. canonical transaction participant poolとしてactual recordsを使用
13. missing Resident / wrong partition / duplicate resident / invalid token / genesis driftのnegative tests
14. participant authority 7/8 -> 8/8判定
15. transaction creation parent blocker解除判定
16. current-head full CI

Snapshot bytes / time / memoryは、このproduction measurement前には確定扱いしない。

## 14. Decision checklist

明示approvalが必要:

1. full 1,000,000 population adoption
2. benchmark total 5,760,000 -> 6,760,000 amendment
3. RecordId recipe
4. StableToken vocabulary
5. Autonomous / binding NONE genesis
6. input_authority_generation initial value
7. envelope DetailLevel policy
8. full Snapshot/recovery/performance evidence inclusion

## 15. Non-goals / boundaries

- transaction成立だけを目的にsparse synthetic poolを作らない
- Resident RecordIdをParticipation RecordIdとして直接再利用しない
- benchmark Diverやactive bindingを勝手に生成しない
- enum名をそのままpersistent Token vocabularyと仮定しない
- benchmark load増加をreference profile外のhidden costにしない
- record count +17.36%からSnapshot bytes/time/memory増分を外挿しない
- approval前に#265へcandidate valuesを実装しない
