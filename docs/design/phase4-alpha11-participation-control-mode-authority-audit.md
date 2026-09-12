# Alpha 1.1 Participation control_mode normative benchmark authority

Status: Complete / normative benchmark authority  
Release tracking: #240  
Implementation PR: #265  
Approval record: #240 issue comment `5644534723` / 2026-09-12

## 1. Purpose

`perf.reference.v1` の `participation.control_mode` について、canonical initial-world population、RecordId、persistent StableToken vocabulary、genesis state、DetailLevel、production proof requirements を正本化する。

本書の decision package は #240 で明示承認済みであり、以後 proposal ではなく Alpha 1.1 / INT-03 の normative benchmark authority として扱う。

## 2. Normative population

Canonical Resident persistent identity は 1,000,000 records とする。

`participation.control_mode` は canonical Resident 1件につき exactly one record を持つ。

```text
population = 1,000,000
resident coverage = Resident ordinal 0..999,999 exactly once
one effective mode / resident
```

Sparse transaction-only poolは採用しない。Resident populationの一部だけにcontrol authorityを付与したり、pool外Residentをimplicit Autonomousとして扱ったりしてはならない。

## 3. Benchmark accounting

従来のcanonical record total:

```text
5,760,000
```

`participation.control_mode` を追加した canonical total:

```text
5,760,000 + 1,000,000 = 6,760,000
```

Record cardinality impact:

```text
+1,000,000
+17.36%
```

+17.36% は record cardinality の算術影響のみを表す。Snapshot bytes / encode time / recovery time / memory を同率増加として外挿してはならず、production measurementで実測する。

## 4. Existing schema authority

Partition:

```text
participation.control_mode
```

Payload:

```text
resident_ref: Ref
binding_ref: Ref?
mode: Token
effective_from: Step
input_authority_generation: uint32
```

Required invariant:

```text
one effective mode / resident
```

Required secondary index:

```text
participation.control-by-resident
```

StandardDomainPartitionRegistry v1 の既存partition/schemaを使用し、新partitionを追加しない。

## 5. Record identity

Resident ordinal `r` 0..999,999 に対する canonical RecordId は次で導出する。

```text
DerivedIdentity.DeriveEntityId(
  worldId,
  creationStep=0,
  creatorDomain=participation,
  creatorEntityId=ZERO,
  creationKind=perf.control-mode,
  localOrdinal=residentOrdinal)
```

Canonical fields:

```text
world_id       = perf.reference.v1 WorldId
creation_step  = 0
creator_domain = participation
creator_entity = ZERO
creation_kind  = perf.control-mode
local_ordinal  = residentOrdinal
```

Resident RecordIdをParticipation RecordIdとして直接再利用しない。Residentとの関係はpayload `resident_ref`で保持する。

このidentity patternは既存production DetailRegion authorityの `owner domain + ZERO creator + benchmark creation kind + canonical ordinal` と同型とする。

## 6. Genesis payload

Resident ordinal `r` に対し、genesis payload は次で固定する。

```text
resident_ref               = corresponding actual resident.identity_lifecycle record[r]
binding_ref                = NONE
mode                       = autonomous
effective_from             = 0
input_authority_generation = 0
```

Benchmark genesisではDiver population / active Participation bindingをsynthetic生成しない。

`input_authority_generation = 0` は external control input authority が未適用であることを表す。別state classのgeneration contractを流用しない。

## 7. Exact StableToken vocabulary

Persistent `mode` Token vocabulary は以下を exact value とする。

```text
Autonomous            -> autonomous
DiverControlAvailable -> diver-control-available
DiverAbsentPolicy     -> diver-absent-policy
BoundResidentDeceased -> bound-resident-deceased
```

Enum名や表示名から暗黙変換せず、persistent representationでは上記4 tokenのみを使用する。

## 8. Runtime semantic correspondence

Existing runtime semantic classes:

```text
ResidentControlModeV1.Autonomous
ResidentControlModeV1.DiverControlAvailable
ResidentControlModeV1.DiverAbsentPolicy
ResidentControlModeV1.BoundResidentDeceased
```

Existing control-context semantics:

- active binding + available -> DiverControlAvailable
- active binding + unavailable -> DiverAbsentPolicy
- resident-deceased binding -> BoundResidentDeceased
- otherwise -> Autonomous

Persistent Token vocabularyはこのsemantic distinctionを保持する。

## 9. Envelope DetailLevel

各 `participation.control_mode` record の envelope DetailLevel は対応Residentのcanonical DetailLevelをmirrorする。

`Qa04ReferenceLoadV1.ResidentDetailLevel(residentOrdinal)`:

```text
0 ..  99,999       -> D0
100,000 .. 399,999 -> D1
400,000 .. 799,999 -> D2
800,000 .. 999,999 -> D3
```

Distribution:

```text
D0 100,000
D1 300,000
D2 400,000
D3 200,000
-------------
   1,000,000
```

All-D0 policyは採用しない。

## 10. Production implementation boundary

#265 implementationでは既存 `participation.control_mode` schema/validator/indexを維持する。

必要に応じてtyped persistent payload representationまたはequivalent canonical adapterを追加してよいが、新partitionや別schemaを作成してはならない。

Production materializerはactual canonical Resident authorityを参照し、1,000,000件すべてについてRef closureを成立させる。

## 11. Required production proof

Production accepted と判定する前に少なくとも以下を満たす。

1. canonical Resident authority 1,000,000件をresolverへ登録
2. `participation.control_mode` 1,000,000件をproduction pathでmaterialize
3. actual Resident Ref closure
4. Resident ordinal 0..999,999 のexact coverage
5. exactly one control-mode / Resident
6. canonical RecordId recipe validation
7. exact StableToken mapping validation
8. `binding_ref = NONE`
9. `mode = autonomous`
10. `effective_from = 0`
11. `input_authority_generation = 0`
12. Resident DetailLevel mirror
13. secondary index / resident uniqueness proof
14. full production Snapshot encode
15. full recovery
16. semantic rehash一致
17. actual transaction participant pool binding
18. fail-closed negative tests
19. encoded Snapshot byte count measurement
20. available encode/recovery elapsed-time measurement
21. available process / steady-memory measurement
22. current-head full CI

Negative proofには少なくとも missing Resident / wrong partition / duplicate resident / invalid token / genesis drift を含める。

## 12. Accounting after production proof

Normative approvalだけではaccepted count / release flagsを変更しない。

上記production proof成功後にのみ以下を判定する。

```text
Participation direct canonical dependencies: 3 -> 0
Transaction participant authority:            7 / 8 -> 8 / 8
```

Transaction creation parent blockerは実際のparticipant authority proofに基づいて解除可否を判定する。

`referenceWorldMaterialized` / `authoritativeStepLoopAvailable` はこのpackage単独では変更しない。

## 13. Non-goals / boundaries

- sparse synthetic control-mode poolを作らない
- Resident RecordIdをParticipation RecordIdとして再利用しない
- benchmark Diverやactive bindingを勝手に生成しない
- enum名をpersistent Tokenへ暗黙変換しない
- +17.36% record countからSnapshot bytes/time/memoryを外挿しない
- absence-policy generation contractをcontrol-mode generationへ流用しない
- new partition/schemaを追加しない
- production proof前にaccepted countやrelease flagsを更新しない

## 14. Approval provenance

本packageは #240 の 2026-09-12 承認決定で明示採択済み。

承認対象:

```text
population:
  canonical Resident 1,000,000にexactly one participation.control_mode

record identity:
  participation + ZERO creator + perf.control-mode + resident ordinal

payload genesis:
  resident_ref               = corresponding actual Resident
  binding_ref                = NONE
  mode                       = autonomous
  effective_from             = 0
  input_authority_generation = 0

exact mode tokens:
  autonomous
  diver-control-available
  diver-absent-policy
  bound-resident-deceased

envelope:
  Resident DetailLevel mirror

proof:
  full 1M production materialization
  Snapshot / recovery / semantic rehash
  negative proof
  byte/time/memory measurement
  transaction participant binding
```

このdecisionを再度approval待ちへ戻してはならない。変更が必要な場合は新たなnormative decisionとして #240 で明示管理する。
