# Alpha 1.1 / INT-03 — Governance 残存 benchmark authority

Status: **Normative benchmark authority**  
Tracking: #240  
Implementation: Draft PR #265

## 1. 目的と適用範囲

本書は `perf.reference.v1` の Society/Governance 2,000,000-record decomposition において、未 materialize の Governance **259,000 records** を QA-04 canonical reference world へ落とし込むための benchmark authority を固定する。

対象は次の 10 partition である。

| partition | global ordinal | count |
|---|---:|---:|
| `governance.law_rule` | `1,606,000..1,635,999` | 30,000 |
| `governance.tax_fiscal` | `1,701,000..1,750,999` | 50,000 |
| `governance.diplomacy` | `1,821,000..1,830,999` | 10,000 |
| `governance.security_incident` | `1,831,000..1,875,999` | 45,000 |
| `governance.investigation` | `1,876,000..1,905,999` | 30,000 |
| `governance.judicial_case` | `1,906,000..1,930,999` | 25,000 |
| `governance.enforcement` | `1,931,000..1,960,999` | 30,000 |
| `governance.military_authority` | `1,961,000..1,970,999` | 10,000 |
| `governance.border_control` | `1,971,000..1,980,999` | 10,000 |
| `governance.lineage` | `1,981,000..1,999,999` | 19,000 |

件数、slice、owner domain、D2 detail level、specialized identity の有無は `Qa04SocietyGovernanceReferenceDecompositionV1` を上位 authority とする。現行 decomposition では specialized identity を使う Society/Governance partition は `society.market_transaction` のみであり、本書対象の Governance 10 partition はすべて既存 descriptor identity を使用する。

本書は QA-04 benchmark 専用である。現実世界の法制度、税制、外交、安全保障、司法、軍事、国境管理の一般モデルを規定せず、既存 GovernanceSecurity domain schema の意味を変更しない。

## 2. 上流 authority と記法

WorldId は `perf.reference.v1`、genesis Step は `0` とする。

本書では、production materialization 済みの実 record を canonical local ordinal 順に次のように表す。

```text
P[p] = governance.polity[p]                p = 0..999
I[i] = governance.institution[i]           i = 0..4,999
J[j] = governance.jurisdiction[j]          j = 0..9,999
T[t] = governance.territorial_claim[t]     t = 0..9,999
C[c] = governance.effective_control[c]     c = 0..19,999
A[a] = governance.public_authority[a]      a = 0..24,999
O[o] = society.organization[o]             o = 0..9,999
S[s] = canonical TileScope[s]              s = 0..4,095
B[b] = built.structure[b]                  b = 0..14,999
```

`S[s]` は Governance territorial foundation が実際に参照する既存 canonical TileScope pool、`B[b]` は FacilityService authority で production materialize 済みの benchmark facility BuiltStructure pool である。

新規対象 record は次のように表す。

```text
L[l]  = governance.law_rule[l]
TF[f] = governance.tax_fiscal[f]
D[d]  = governance.diplomacy[d]
SI[s] = governance.security_incident[s]
IV[i] = governance.investigation[i]
JC[j] = governance.judicial_case[j]
EN[e] = governance.enforcement[e]
MA[m] = governance.military_authority[m]
BC[b] = governance.border_control[b]
GL[g] = governance.lineage[g]
```

参照は descriptor だけから合成してはならず、production proof では上記 upstream の実 record が存在し、partition/schema が一致することを検証する。RefList は既存 canonical ref comparator で sort + unique した結果を encode する。

全対象 record の envelope は次を満たす。

```text
revision      = 1
created_step  = 0
retired_step  = NONE
detail_level  = descriptor D2
lineage_ref   = NONE
```

RecordId は対象 slice の既存 QA-04 descriptor identity を使用する。Governance domain 内部に別の semantic identity/digest が存在しても、QA-04 RecordId をそれへ置換してはならない。

## 3. `governance.law_rule` 30,000

local ordinal `l = 0..29,999` について次を使用する。

```text
jurisdiction_ref = J[l % 10,000]
priority         = floor(l / 10,000)        // 0, 1, 2
specificity      = 1
effective_from   = 0
effective_until  = NONE
predicate_ast    = FactEquals(perf.subject-class, perf.reference-subject)
effect_ast       = Permit(perf.reference-permit)
status           = active
```

各 jurisdiction には priority 0 / 1 / 2 の exactly 3 rules が割り当てられる。

`predicate_ast` と `effect_ast` は flat payload や独自 byte 列へ置き換えてはならない。必ず既存の以下を使用する。

- `GovernanceLawPredicateNestedValueV1`
- codec id `governance.law.predicate`, schema version `1`
- `GovernanceLawEffectNestedValueV1`
- codec id `governance.law.effect`, schema version `1`
- `GovernanceLawAstCodecV1` の canonical encode/decode

Snapshot recovery 後は decode -> re-encode が byte-exact で一致しなければならない。

`GovernanceRuleIdentityV1` 等の semantic digest/token は domain 内の rule semantic identity として利用可能だが、本 partition の QA-04 RecordId authority ではない。

## 4. `governance.tax_fiscal` 50,000

local ordinal `f = 0..49,999` について次を使用する。

```text
polity_ref      = P[f % 1,000]
tax_kind        = perf.tax-policy
tax_base_token  = perf.reference-tax-base
rate_ppm        = 100,000 + 10,000 * (f % 5)
claim_amount    = NONE
debtor_ref      = NONE
due_step        = NONE
status          = active
```

`rate_ppm` は 100,000 / 110,000 / 120,000 / 130,000 / 140,000 の 5 値のみを取る。

これは genesis 時点の deterministic tax-policy fixture であり、実際の未払い税債権が発生済みであることを意味しない。したがって claim/debtor/due は明示的に NONE とする。

## 5. `governance.diplomacy` 10,000

local ordinal `d = 0..9,999` について、

```text
p0     = d % 1,000
offset = 1 + floor(d / 1,000)              // 1..10
p1     = (p0 + offset) % 1,000
```

とし、次を使用する。

```text
party_refs      = canonical_sort_unique([P[p0], P[p1]])
relation_kind   = perf.diplomatic-relation
status          = active
effective_from  = 0
effective_until = NONE
instrument_refs = [L[d % 30,000]]
terms_digest    = SHA-256(UTF8("perf.reference.v1/governance/diplomacy/terms/v1"))
```

`p0 != p1` は必須である。`terms_digest` の期待値は hex で次の 32 bytes とする。

```text
9ff4bf863b8803c1a7d0c7f765802e590e8ea17b3db2fed8b17e95e0cea4d4d8
```

この固定 digest は benchmark の共通 genesis terms marker であり、一般の treaty/terms 内容をモデル化したものではない。

## 6. `governance.security_incident` 45,000

local ordinal `s = 0..44,999` について次を使用する。

```text
incident_kind   = perf.security-incident
subject_refs    = [O[s % 10,000]]
scope_ref       = S[s % 4,096]
occurred_step   = 0
fact_event_refs = []
status          = recognized
severity_ppm    = 100,000 + 100,000 * (s % 9)
```

`severity_ppm` は 100,000..900,000 の 9 段階のみを取る。空の `fact_event_refs` は「QA-04 genesis state の入力として incident がすでに存在する」境界を示し、存在しない過去 event を捏造しないための明示的 authority である。

## 7. `governance.investigation` 30,000

local ordinal `i = 0..29,999` について次を使用する。

```text
incident_ref      = SI[i % 45,000]
authority_ref     = A[i % 25,000]
investigator_refs = [I[i % 5,000]]
evidence_refs     = []
suspect_refs      = [O[i % 10,000]]
status            = open
opened_step       = 0
closed_step       = NONE
```

空の `evidence_refs` は evidence が不要という一般規則ではない。QA-04 genesis fixture が過去 evidence record を生成しないことを固定する。

## 8. `governance.judicial_case` 25,000

local ordinal `j = 0..24,999` について次を使用する。

```text
case_kind            = perf.reference-case
jurisdiction_ref     = J[j % 10,000]
party_refs           = canonical_sort_unique([O[j % 10,000], O[(j + 1) % 10,000]])
evidence_refs        = []
charge_or_claim_refs = [L[j % 30,000]]
status               = open
opened_step          = 0
decision_ref         = NONE
```

2 parties は必ず distinct でなければならない。`decision_ref = NONE` は genesis 時点で未決であることを示す。

## 9. `governance.enforcement` 30,000

local ordinal `e = 0..29,999` について次を使用する。

```text
authority_ref      = A[e % 25,000]
order_kind         = perf.reference-enforcement-order
subject_refs       = [O[e % 10,000]]
target_refs        = [O[(e + 1) % 10,000]]
status             = issued
issued_step        = 0
effective_step     = 0
outcome_event_refs = []
```

`subject_refs[0] != target_refs[0]` は必須である。空の `outcome_event_refs` は enforcement order が physical/world state を直接変更したことにしないための境界である。実際の結果は別 domain の intent/event authority に従う。

## 10. `governance.military_authority` 10,000

local ordinal `m = 0..9,999` について次を使用する。

```text
polity_ref           = P[m % 1,000]
unit_or_org_ref      = O[m % 10,000]
command_ref          = A[m % 25,000]
mission_token        = perf.reference-mission
objective_refs       = [J[m % 10,000]]
authority_scope_refs = [J[m % 10,000]]
status               = active
issued_step          = 0
```

これは command/mission authority の deterministic fixture であり、戦闘、damage、占領、移動等の結果を直接生成しない。

## 11. `governance.border_control` 10,000

local ordinal `b = 0..9,999` について次を使用する。

```text
jurisdiction_ref   = J[b]
boundary_ref       = S[b % 4,096]
checkpoint_refs    = [B[b % 15,000]]
movement_rule_refs = [L[b % 30,000]]
status             = active
capacity_per_step  = 1 + (b % 1,000)
```

### 11.1 QA-04 boundary anchor の限定定義

Governance 設計上、`boundary_ref` は spatial boundary/interface を参照する。Spatial domain には `spatial.boundary_topology` が正規 partition として存在するが、Issue #240 の現在の QA-04 accepted materialization には、この Governance 10,000件が安全に参照できる production-proven `spatial.boundary_topology` pool は含まれていない。

このため本 benchmark に限り、既存 production-proven `TileScope` を **QA-04 boundary anchor** として `boundary_ref` に使用する。

この限定 authority は次を意味しない。

- `TileScope` を一般用途の `spatial.boundary_topology` record と同一視しない。
- Governance schema の `boundary_ref` の一般意味を変更しない。
- 将来の production world で boundary topology を省略してよいことを意味しない。

将来 QA-04 自体が production-proven `spatial.boundary_topology` pool を正本化する場合、この benchmark mapping の変更は schema evolution / benchmark revision として明示的に扱い、同一 `perf.reference.v1` の意味を黙って差し替えてはならない。

`checkpoint_refs` は FacilityService authority の実 BuiltStructure を参照し、`movement_rule_refs` は本書の実 LawRule を参照する。

## 12. `governance.lineage` 19,000

local ordinal `g = 0..18,999` について次を使用する。

```text
subject_ref       = L[g]
predecessor_refs  = []
succession_kind   = perf.genesis
effective_step    = 0
causality_digest  = L[g].CanonicalDigest()
```

空の predecessor は benchmark genesis boundary を表す。`causality_digest` は参照先 LawRule の **実 canonical payload digest** でなければならず、nested predicate/effect を既存 codec で canonical capture した後の payload と一致しなければならない。subject payload が drift した場合は fail closed とする。

## 13. 決定性・Snapshot・fail-closed 要件

本 package を accepted と数える前に、少なくとも次を production proof する。

1. 10 partition を exact count `30k / 50k / 10k / 45k / 30k / 25k / 30k / 10k / 10k / 19k` で materialize する。
2. 全 record が現行 decomposition の global/local ordinal、descriptor RecordId、D2 envelope に一致する。
3. `P/I/J/A/O/S/B` は descriptor-only resolver ではなく production materialized actual record を参照する。
4. LawRule nested predicate/effect は既存 Governance nested Snapshot codec を使用し、capture/recovery/re-encode が byte-exact である。
5. LawRule の各 jurisdiction が exactly 3 rules、priority `0/1/2` を持つことを証明する。
6. Diplomacy party は distinct、canonical sort + unique 済みであり、固定 terms digest が一致する。
7. Incident severity、TaxFiscal rate、Border capacity の範囲・式を exact に検証する。
8. Investigation/Judicial/Enforcement/Military/Border の cross-partition ref が存在し、期待 partition/schema に一致する。
9. Judicial party と Enforcement subject/target の distinct 条件を検証する。
10. Border boundary anchor が実 canonical TileScope、checkpoint が実 BuiltStructure、movement rule が実 LawRule であることを検証する。
11. GovernanceLineage subject と actual canonical LawRule payload digest の一致を検証する。
12. RecordId uniqueness、RefList canonical ordering/uniqueness、optional/list/token/scalar/envelope の完全一致を検証する。
13. production Snapshot encode -> recovery -> semantic rehash を全10 partitionで実施する。
14. missing/wrong-partition ref、ordinal relation drift、nested codec drift、digest drift、duplicate identity/ref、population drift、status/scalar/list drift は fail closed とする。
15. unrelated release gate と Infrastructure network-topology blocker を勝手に解消扱いしない。
16. current-head CI を実行する。

## 14. Release boundary

本書の正本化だけでは materialization 完了とは数えない。production proof 完了時のみ次へ遷移する。

```text
Governance accepted            141,000 ->   400,000 /   400,000
Governance remaining           259,000 ->         0
Society/Governance accepted  1,741,000 -> 2,000,000 / 2,000,000
Society/Governance remaining   259,000 ->         0
```

これにより Society/Governance active-record partition mapping blocker は閉じられる。ただし reference-world 全体は独立した Infrastructure network-topology node/edge target/schema compatibility blocker が残る限り完了ではない。

`referenceWorldMaterialized` および `authoritativeStepLoopAvailable` は、それぞれの独立 gate を満たすまで `false` のままとする。
