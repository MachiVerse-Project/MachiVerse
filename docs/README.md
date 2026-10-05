# MachiVerse 設計ドキュメント

## このドキュメント群の目的

`docs` は、MachiVerseの確定要件、最上位architecture、世界simulation各領域、component責務、protocol contract、運用上の制約を追跡できるようにするための設計資料です。

MachiVerseはC#で開発する大規模なagent-based world simulatorです。世界を単なる都市や背景として扱わず、自然、住人、組織、経済、政治、技術、建造環境、情報、歴史等が因果的に相互作用する世界としてsimulationします。

## ドキュメントの優先関係

### 要件定義の正本

対話ベースの要件定義で確定したQ001〜Q279は、`requirements` 配下に決定記録として保存します。

- [要件定義の読み方](requirements/README.md)
- [Q001〜Q099](requirements/requirements-qa-001-099.md)
- [Q100〜Q199](requirements/requirements-qa-100-199.md)
- [Q200〜Q279](requirements/requirements-qa-200-279.md)

後続の質問で以前の決定を明示的に変更・補足した場合は後続要件を優先します。

### Architecture / Protocol

`architecture` は要件をcomponent責務・world subsystem・横断意味論へ具体化します。

`protocols` はcomponent間の具体的な通信契約の正本です。ただしprotocol設計も確定要件へ反してはなりません。

横断的な矛盾・古い記述の解消方針は [アーキテクチャ整合性監査](architecture/consistency-audit.md) を参照してください。

## 現在の最上位構成

MachiVerseの標準構成は次の4componentです。

1. **Simulation Core**
   - Authoritative World State、整数Simulation Step、world rule、deterministic update、save/replay/recoveryを担当する。
   - 標準構成では1つだけ存在する。
2. **Gateway**
   - Coreと外部Viewの接続境界。
   - authn/authz、cache、logical publication buffer、Operation aggregation、Master Gateway、retry/dedup、resyncを担当する。
   - Coreに対して1:Nで水平scale可能。
3. **General View**
   - 一般利用者がroleに応じてworldを参照・参加・操作するWeb UI。
   - Three.jsを用いてCoreのfull-3D worldを表示する。
4. **Admin View**
   - system operator向けの別UI。
   - component log/status/metrics、Config、operational command、Admin Operation等を扱う。

General View上のAdministratorとAdmin Viewは別のauth/authz domainです。

## Component分離原則

4componentは論理layerだけでなくcode/build/deploy/runtime単位まで独立させます。

- component間でproject参照を持たない。
- DLLやinternal typeを共有しない。
- shared DTO libraryをcommunication contractとして使用しない。
- component間のdirect method callを行わない。
- component間communicationはprotocolだけを通じて行う。
- 各componentを独立build・run可能にする。

## 標準構成のCore / Gateway

- Standard Simulation Coreは1つ。
- Core : Gateway = 1:N。
- 複数Gateway時はCoreがsafe candidateからMaster Gatewayをrandom選出する。
- General View由来Operationはlocal Gatewayでauthn/authz・aggregation・local conflict mediationを行い、Masterへ集約する。
- Masterは全Gateway分をdeterministic mergeし、final batchをCoreへ送る。
- Coreがauthoritative world stateとworld-state invariantに基づいて最終状態遷移を行う。
- stable Operation ID、Batch ID、Master generation、retry/dedup/idempotencyによりfailover・reconnectでduplicate applyを防ぐ。
- Master identityそのもののreplay再現性は標準要件ではないが、Master identityがworld outcomeを変えてはならない。

## Authoritative World Time

権威あるWorld Timeは整数ベースのSimulation Stepです。

- Config schema `1.0` のStepRate defaultは互換性のため `30/1` steps/secを維持する。
- Alpha 1.1 standard runtime profileはCore Configで `10/1` steps/sec（10 tick/s）を明示選択する。
- StepRateは外部Configから変更可能。
- Coreがactive StepRateへ追いつかなくてもprocessing delayだけを理由にStepをskipしない。
- network arrival timeをそのままOperation application timeにしない。
- Gateway/Masterがcandidate application timeを形成し、Coreがfinal valid Stepを決定する。

## Full 3D World

MachiVerseの3DはViewだけの要件ではありません。Simulation Coreのauthoritative spatial model自体をfull 3Dとします。

- cave
- tunnel
- basement
- mine working
- overhang
- cut
- same XYに異なるZで存在する複数surface/space

等を表現できる必要があります。

単一XYにつき単一Zしか持てないpure single-height mapをauthoritative terrainとしません。

Three.jsはGeneral Viewのrendering技術であり、Core world modelを置き換えるものではありません。

## World simulation対象

世界を構成する対象は、現在では未確定ではありません。Q001〜Q199を中心に多数の標準simulation領域が確定しています。

例として、次を相互に独立した背景値ではなく因果的に接続します。

- terrain、climate、weather、water、ocean、geology、ecosystem
- resident lifecycle、health、knowledge、skill、memory、emotion、goal、daily activity
- family、relationship、organization、education、work
- agriculture、resource、energy、manufacturing、logistics
- market、currency、credit、tax、public finance、enterprise、household
- law、crime、justice、administration、politics、diplomacy、military
- building、interior、construction、infrastructure、transport
- information、media、communication、maps、public records
- accident、disaster、emergency response、pollution、maintenance
- history、culture、language、religion、arts、social events

個別の詳細は `architecture` 配下の各domain設計書を参照してください。

## World-scale detail

- defaultでは世界規模で可能な限り個体・物品・建物等の存在、persistent ID、重要stateを保持する方向とする。
- 全世界をactive StepRateで一律high-detail updateすることは要求しない。
- remote / low-importance対象ではupdate frequency・detailを下げられる。
- Entity identityと重要因果を失わずdetail promotion/demotion、aggregation、archiveを行う。

## Determinism

同じWorld Seed、simulation-affecting Config、accepted Operation集合・順序・application Stepからは同じlogical world outcomeを得ます。

world outcomeをprocessing speed、OS scheduling、thread completion order、Gateway count、Master identity、network race等へ依存させません。

Coreは最大16 thread、実使用1〜16をConfigで設定可能です。thread countが変わっても同じ再現条件ならworld outcomeを変えません。

異なるCPU/OS/runtime間のすべてのfloating-point operationをbit-identicalにすることは標準要件ではありません。

## 外部Config原則

- 調整可能な数値・threshold・frequency・timeout・capacity等は外部Config化する。
- 各componentが自身のConfig fileを所有する。
- component間でConfig fileを共有・直接参照しない。
- 他componentへ必要な設定・状態はCoreに近い責任componentがprotocolで配布する。
- startup Configに不整合があれば起動しない。
- simulation-affecting runtime changeはsafe Simulation Stepでatomicに適用しhistoryへ記録する。
- old Configでnew fieldが欠ける場合はdefaultを適用し、そのfieldをConfig fileへ追加する。

詳細は [外部Config設計](architecture/configuration.md) と [Config意味論](architecture/config-semantics.md) を参照してください。

## Addon原則

Addonはcomponent単位で設定できます。

標準protocolへaddon固有function payload、addon command、generic extension data areaを載せません。

一方、addon install状況、identity、version、required/provided Capability等、connection safety/compatibility判定に必要なmeta informationは標準protocolで交換できます。

Addon固有のcross-component data communicationが必要な場合は、protocol extension framework addon等とadditional protocolを別途成立させる方向です。具体APIは未確定です。

Addon構成に不整合があれば重大度に関係なく対象componentを起動しません。saved worldが依存するaddon不整合もexplicit migrationが完全成功しない限り起動拒否します。

## Diver

Diverは参加時にnew residentを生成しません。

- existing normal residentへbindする。
- broad preferenceはrequestできるがmatchingを保証しない。
- 原則1residentにつき1Diver。
- disconnectを理由に別Diverへcontrolを移さない。
- reconnectしても同じDiver identityを使う。
- disconnected residentはworld内で存在・行動を続け、Diverはabsent behavior priorityを事前設定可能。

## Save / Replay / Recovery

- defaultはSnapshot＋Operation/Event history＋high-precision replay方向。
- replayはvideoではなくCoreによるdeterministic recalculation。
- saveはspecific Simulation Stepのconsistent stateとして取得する。
- live saveが高負荷・困難ならsafe boundaryで一時停止してよい。
- corrupt/incompatible saveをpartial loadしてworldを起動しない。
- deterministic migration不能ならold format worldは起動拒否する。
- restore後もsame world identity、Entity ID、Simulation Step、Operation historyを維持する。

## 主なドキュメント入口

### Requirements

- [要件定義](requirements/README.md)

### Cross-cutting architecture

- [全体architecture](architecture/overview.md)
- [整合性監査](architecture/consistency-audit.md)
- [Simulation Core](architecture/simulation-core.md)
- [Gateway](architecture/gateway.md)
- [General View](architecture/view.md)
- [Admin View](architecture/admin-view.md)
- [External Config](architecture/configuration.md)
- [Deterministic update](architecture/deterministic-update-execution.md)
- [Random / ID / numerics](architecture/deterministic-random-id-numerics.md)
- [Protocol compatibility / Capability](architecture/protocol-compatibility-capability.md)
- [Addon boundary](architecture/addon-boundary-safety.md)
- [Persistence / replay](architecture/persistence-replay-recovery.md)

### Protocols

- [Protocol common policy](protocols/README.md)
- [Core ↔ Gateway](protocols/core-gateway.md)
- [Gateway ↔ Gateway](protocols/gateway-gateway.md)
- [Gateway ↔ General View](protocols/gateway-view.md)
- [Gateway ↔ Admin View](protocols/gateway-admin-view.md)

## 更新方針

Architecture、responsibility、protocol semantics、world rule、Config semantics等の確定要件を変更する場合、implementationだけを変更せず関連documentも同時に更新します。

新しい決定が過去の記述を置き換える場合は、`docs/requirements` に決定を残し、横断documentと関係する個別documentから古い矛盾記述を除去します。
