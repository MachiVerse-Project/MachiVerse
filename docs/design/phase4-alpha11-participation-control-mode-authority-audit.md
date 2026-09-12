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

Transaction participant authorityは現在7/8。`participation.control_mode`がactual authority化されればparticipant owner coverageは8/8へ到達可能だが、production proof前にその状態へ更新してはならない。

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

一方、`phase4-performance-benchmark-profile.md`のinitial-world population表はResident / Physical / Environment / Society-Governance / Infrastructure / Terrain / active transactionを固定するが、Participation control-mode record countを独立state classとして列挙していない。

またbenchmarkはDiver population / active Participation binding populationを定義していない。

## 6. Transaction requirement

Canonical cross-domain transaction genesisはParticipation participantについて:

```text
partition = participation.control_mode
```

のactual canonical record poolを要求する。

poolがmissing/emptyの場合はfail closedする。

transaction materializerだけを見るとpool countは1以上なら構造的に成立する。しかしtransaction都合だけでpool cardinalityを決めてはならない。

## 7. Population alternatives

### 7.1 Sparse transaction-only pool

例:

```text
1 record
10,000 records
```

利点:

- transaction genesisを最小record追加で成立させられる。

問題:

- `one effective mode/resident` semanticsとの関係が未定義。
- poolに含まれないResidentのcontrol mode authorityが消える。
- implicit Autonomous semanticsはschemaに存在しない。
- transaction負荷都合のsynthetic populationをworld authorityへ逆輸入する。

結論:

**採用候補にしない。**

### 7.2 Full per-Resident explicit authority

Candidate:

```text
population = canonical Resident persistent identity count = 1,000,000
one control-mode record per Resident
```

利点:

- `one effective mode/resident`を直接満たす。
- actual Resident Ref closureを完全に構成できる。
- transaction participant poolが自然にactual world authorityとなる。
- later binding/control changesのpersistent owner surfaceとして整合する。

問題:

- benchmark initial population表に明示されていない追加1,000,000 authoritative recordsとなる。
- performance/memory/Snapshot負荷を増やす。
- benchmark profile amendmentとして扱う必要がある可能性が高い。

結論:

**schema-faithful candidateではあるが、load-impact approvalなしに採用不可。**

## 8. Full per-Resident candidate values — review only

以下はnormativeではない。

### 8.1 Resident mapping

Resident ordinal `r` 0..999,999 に対し:

```text
resident_ref = actual resident.identity_lifecycle record[r]
```

Resident RecordIdをParticipation RecordIdとして直接再利用しない。

### 8.2 Genesis semantic mode

benchmark Diver/binding populationが未定義であるためcandidateは:

```text
binding_ref = NONE
mode = Autonomous
effective_from = 0
```

とする。

### 8.3 Input authority generation

Candidate:

```text
input_authority_generation = 1
```

0をuninitializedとして扱う明示contractは現在ないが、initial persistent authority generationとして1を使う案。approval前に固定しない。

### 8.4 Token vocabulary candidate

```text
Autonomous            -> autonomous
DiverControlAvailable -> diver-control-available
DiverAbsentPolicy     -> diver-absent-policy
BoundResidentDeceased -> bound-resident-deceased
```

StableToken syntax内。enum mappingのexact vocabularyとしては未承認。

## 9. Record identity decision required

少なくとも次のcandidateが考えられる。

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

長所:
- benchmark ordinalから独立導出可能
- Resident RecordIdをidentityとして再利用しない

### B. Resident identity keyed

Resident RecordIdをcreator inputへ含める別recipe。

長所:
- source Residentとのidentity relationをdigest inputへ含められる

問題:
- `creatorEntityId` fieldのsemanticをsource relationとして使うことを別途正本化する必要がある

Audit recommendation:

Aの方が既存QA-04 ordinal-derived identity patternとの整合が高い。ただし未承認。

## 10. Envelope detail-level decision required

Full 1,000,000 candidateでは少なくとも2案がある。

### A. Resident detail distribution mirror

対応するResident descriptorのDetailLevelをcontrol-mode envelopeへmirror:

```text
D0 100,000
D1 300,000
D2 400,000
D3 200,000
```

### B. Control authority fixed entity detail

全control-mode recordsをD0として保持。

Audit:

- Aはbenchmark Resident detail distributionとの対応が明確。
- Bはcontrol-mode stateを常にentity-exact authorityとして扱う意味になる。
- schema/runtimeはどちらも自動決定していない。

明示decisionが必要。

## 11. Benchmark load accounting decision

Full per-Resident authorityを採用する場合、最低でも次を明記する必要がある。

1. 1,000,000 control-mode recordsは`perf.reference.v1` initial-world canonical record loadへ追加されるか。
2. 既存performance baseline countを変更したものとして扱うか、control/runtime authorityとして別計上するか。
3. steady memory / initialization / Snapshot sizeのacceptance interpretationを変更するか。
4. 24h soak / determinism matrixで常にfull control-mode Snapshot materialを含むか。

負荷増加を実装内部のhidden recordsとして扱わない。

## 12. Required production proof after normative decision

採用後:

1. actual canonical Resident authorityをresolverへ登録
2. decided cardinalityのcontrol-mode recordsをmaterialize
3. exact enum -> Token mapping validation
4. one effective mode/resident invariant validation
5. actual Resident Ref closure
6. full production Snapshot encode/recovery semantic rehash
7. canonical transaction participant poolとして使用
8. missing Resident / wrong partition / duplicate resident / invalid token / genesis driftのnegative tests
9. participant authority 7/8 -> 8/8
10. transaction creation parent blocker解除判定
11. current-head full CI

## 13. Non-goals / boundaries

- transaction成立だけを目的にsparse synthetic poolを作らない。
- Resident RecordIdをParticipation RecordIdとして直接再利用しない。
- benchmark Diverやactive bindingを勝手に生成しない。
- enum名をそのままpersistent Token vocabularyと仮定しない。
- load増加をreference profile外のhidden costにしない。
- approval前に#265へcandidate valuesを実装しない。
