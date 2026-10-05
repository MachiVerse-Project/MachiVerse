# 外部Config設計

## 1. 目的

MachiVerse では、システム動作を調整する各種数値・設定をソースコードへ運用固定値として埋め込まず、責任コンポーネントが所有する外部Configから供給する。

この方針はSimulation Core、Gateway、General View、Admin Viewの全コンポーネントに適用する。

Config意味論は `config-semantics.md`、Phase 1の詳細契約は `../design/phase1-config-contract.md` を正本とする。

## 2. 基本原則

- 調整可能な数値・しきい値・時間・件数・容量・挙動選択等は外部Configから供給する。
- ソースコード内へ運用値・性能調整値・シミュレーション条件を直接固定しない。
- 数学的定数、配列インデックス等、設定値として意味を持たない実装上の数値は対象外とできる。
- 各コンポーネントは、自身が責任を持つ設定を自身のConfigで所有する。
- 他コンポーネントのConfigファイルを直接参照しない。
- コンポーネント間でConfigファイルを共有しない。
- 他コンポーネントへ影響する必要情報は、owner componentが標準プロトコルのeffective informationとして配布する。
- Config fileのfilesystem更新時刻やwatch eventをworld outcomeの入力にしない。

## 3. 標準Config document

operator-editable Config fileは UTF-8 TOML 1.0 とする。

必須metadata:

```toml
[meta]
format = "machiverse-config"
schema_version = "1.0"
component = "simulation-core"
```

- `meta.format` は `machiverse-config` 固定。
- `schema_version` は `major.minor`。
- `component` はowner componentを表すstable token。
- unknown fieldは黙って無視しない。
- secret materialは標準Config documentへ格納せず、必要ならsecret referenceを保持する。

Config pathはprocess launch/deploymentからowner component自身が解決する。Phase 1では全component共通の絶対pathは固定しない。

## 4. 現時点で確定している基準値

### 4.1 Simulation Core

- Config schema `1.0` のStepRate defaultは互換性のため **30/1 steps/sec** を維持する。
- Alpha 1.1 standard runtime profileはschema defaultを書き換えず、Core Configで **10/1 steps/sec（10 tick/s）** を明示選択する。
- 権威ある時間軸は整数ベースのSimulation Step。
- StepRateはCore Configから変更可能で、simulation-affecting runtime changeとしてexplicit effective Stepを持つ。
- Coreはマルチスレッド実行を前提とし、使用可能スレッド数は **1〜16**。
- 実使用スレッド数はCore Configから変更可能で、world outcomeを変えないOPERATIONAL設定として扱う。

### 4.2 Gateway

- 外部公開の揺らぎを吸収する論理的な遅延バッファの標準値は **約1秒**。
- この値はGateway Configから変更可能。
- 遅延バッファはcacheとは別責務であり、OperationやAdmin操作を一律1秒遅らせる意味ではない。

## 5. Configの所有責任

各設定値は、その値を使用して動作を決定する責任コンポーネントが所有する。

| 設定例 | 所有コンポーネント |
|---|---|
| Simulation Step進行頻度 | Simulation Core |
| Core使用スレッド数 | Simulation Core |
| シミュレーション詳細度・世界生成条件 | 原則Simulation Core |
| Gateway公開遅延バッファ | Gateway |
| Gateway cache関連値 | Gateway |
| Gateway timeout・retry・流量制御 | Gateway |
| General View表示更新・表示のみの調整値 | General View |
| Admin View表示・監視UIの調整値 | Admin View |

この表は網羅ではない。所有責任は「どのコンポーネントがその設定により自身の動作を決めるか」で判断する。

## 6. Config field schema

各fieldはowner component schemaで少なくとも次を定義する。

- stable path
- value type / integer bit width / signedness
- unit
- required / default
- range / cross-field constraint
- impact
- mutability
- protocol exposure有無

既定値の正本はcurrent schemaとし、実装内部に意味上別のhidden defaultを持たない。

## 7. Configの分類

### 7.1 世界結果への影響

- `SIMULATION`: 世界状態、因果、Operation result、乱数、detail等へ影響し得る。
- `OPERATIONAL`: 性能、監視、timeout、retry、buffer、logging等。world outcomeは変えない。
- `PRESENTATION`: UI、表示、非権威な補間等のみ。

SIMULATION fieldは決定論・保存・リプレイの再現条件に含める。

### 7.2 変更可能性

- `RUNTIME_SAFE`
- `RESTART_REQUIRED`
- `WORLD_REGENERATION_REQUIRED`

impactとmutabilityは独立に分類する。

## 8. Config schema version / migration

Config schema versionは `major.minor` とする。

- 同一majorの古いminorは定義済みdeterministic migration chainでcurrentへ移行する。
- 新しいminorを古いreaderが黙って読み飛ばさない。
- major mismatchはexplicit migrationが完全成功しない限り拒否する。
- migrationはwall clock、network、filesystem enumeration、randomへ依存しない。
- 情報損失を伴う変換をsilentに行わない。

## 9. 起動時検証

startup時はConfig全体をparse、migration、default completion、validation、normalizationしてから起動する。

- 型、範囲、相互制約、依存、addon、Capability等を検証する。
- 不整合がある場合、そのcomponent/worldを起動しない。
- 無効値を黙って丸める、未知項目を読み飛ばす、部分的にdefaultへ置換して続行することを一般原則としない。
- compatible old Configで不足fieldにschema defaultがある場合のみdefault completionする。

## 10. Configの後方互換とwrite-back

Q214により、古いcompatible Configでcurrent schemaに追加されたfieldが欠けている場合、そのfieldのschema defaultを採用してConfig fileへ追記する。

- default completion / migration後のcomplete Configをatomic replaceで書き戻す。
- durable flush後にactive fileをreplaceする。
- 書き戻し後に再読込し、schema versionとConfigDigestを検証する。
- required write-backに失敗した場合は起動しない。
- 単純なfield不足と、意味変更・型変更・互換不能構造を区別する。

## 11. `ConfigGeneration` / `ConfigDigest`

有効Configはatomic revisionとして管理する。

```text
ConfigGeneration := uint64
```

- startupで成立した最初のEffectiveConfigをgeneration 1とする。
- successful runtime changeで1増加する。
- rejected / no-opでは増加しない。

EffectiveConfigはPhase 1 deterministic encoding/hash契約に従い、`MV-DCBOR-v1` + SHA-256 domain label `mv.config.v1` の256bit `ConfigDigest`を持つ。

## 12. 実行中変更

- Config fileが変更された瞬間に各処理が個別に新値を読み始める方式にはしない。
- filesystem watchは変更候補の通知に利用できるが、effective Configを暗黙変更しない。
- runtime activationはexplicit Config change actionとする。
- change setはstable OperationIdとexpected base ConfigGenerationを持つ。
- candidate Config全体を検証し、一項目でも不整合ならchange set全体を拒否する。
- 以前の有効Configを維持し、部分適用しない。
- `RESTART_REQUIRED` / `WORLD_REGENERATION_REQUIRED`をruntime applyしない。

## 13. Simulation影響Configのruntime apply

`SIMULATION + RUNTIME_SAFE` changeはexplicit SimulationStepを持つ。

`effective_step = S` の場合、new Configは `State(S) -> State(S+1)` transition開始前にatomicに有効化する。

- transition途中でold/new値を混在させない。
- past finalized Stepへ遡及しない。
- same-Step複数changeはdeterministic ordering規則に従う。
- network arrival timingを順序根拠にしない。
- apply generation / digest / effective Step / changed valuesを履歴へ保存する。

OPERATIONAL / PRESENTATION changeはcomponent-defined safe pointでatomicに切り替える。

## 14. Config変更履歴

simulation-affecting Config changeはreplay可能な履歴を持つ。

最低限:

- ConfigGeneration
- effective SimulationStep
- OperationId
- ConfigDigest
- normalized changed values

初期Configもeffective Step 0のinitial entryとして扱う。

Admin Viewから元の値へ戻す場合も一般Undoは行わず、新しい変更Operationとして履歴化する。

## 15. 保存・リプレイ・復旧

- saveはactive simulation ConfigGeneration / ConfigDigest / Config history continuation pointを保持する。
- restoreはsaved simulation-affecting Config/historyをcontinuationの正本とする。
- current local Configのsimulation値を過去World Stateへsilentに混入させない。
- runtime-safe差分を使いたい場合はrestore後に新規Config changeとして明示適用する。
- world-regeneration-required差分は既存WorldIdへそのまま適用しない。
- operational/presentation Configはcurrent component値を利用できるがworld outcomeを変えてはならない。

## 16. コンポーネント間に影響する設定

Q213により、他コンポーネントへ影響するからといって共通Config fileを作らない。

例えばCoreが定義するSimulation Stepの意味や、接続相手が知る必要のある有効設定状態は、必要な範囲をCore所有protocolのeffective informationとしてGatewayへ伝える。

recipientはCore Config fileを直接読む必要も権限も持たない。

## 17. 数値Configの代表例

- 実行頻度
- 使用スレッド数
- 時間間隔
- 遅延時間
- timeout
- cache保持時間
- buffer量
- 件数上限・size上限
- retry回数・間隔
- 流量制御値
- 詳細度のしきい値・更新頻度
- 保存間隔・保持量
- 監視間隔・保持期間
- その他、運用・性能・シミュレーション挙動を調整する値

## 18. 関連設計書

- [Phase 1 Config詳細契約](../design/phase1-config-contract.md)
- [Config意味論](config-semantics.md)
- [Simulation Core並列実行設計](core-concurrency.md)
- [決定論的更新実行](deterministic-update-execution.md)
- [保存・リプレイ・復旧](persistence-replay-recovery.md)
- [Admin操作・安全性](admin-operation-safety.md)

## 19. Component固有設計へ残す事項

Phase 1で共通のfile/schema/apply/history契約は確定した。各component詳細設計では次を定義する。

- concrete Config path / deployment default
- concrete field key / type / unit / default / constraint
- fieldごとのimpact / mutability
- component-specific cross-field validator
- protocolへ公開するeffective information
- addon固有Config schema
