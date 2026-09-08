# MachiVerse Alpha 1.0 Integration Runbook

## 目的

この文書は、`integration/1.0-alpha` で成立した最初のMachiVerse end-to-end runtimeについて、起動方法、再現可能な最小デモ、acceptance結果、既知の制約を記録する。

Alpha 1.0の合否はworld contentや見た目の完成度ではなく、次のauthoritative loopが実component間通信で一本通ることに置く。

```text
Simulation Core
  -> Gateway
  -> General View / Administration View
  -> protected request
  -> Gateway
  -> Simulation Core authoritative transition
  -> confirmed publication / terminal result
  -> Gateway
  -> View
```

View/Admin ViewからCoreへの直接接続、mock-only success、predictionのauthoritative扱いは使用しない。

## 対象component

- Simulation Core — .NET 10
- Gateway — .NET 10 ASP.NET Core / gRPC / WebSocket
- General View — .NET 10 Blazor WebAssembly / three.js
- Administration View — .NET 10 Blazor WebAssembly

## 前提

- .NET SDK `10.0.400`相当
- localhost上で4componentを起動できること
- `integration/1.0-alpha` branchを使用すること
- local Alpha runtimeではproduction OIDC/TLS profileではなく、明示的なloopback-only Alpha profileを使用する

## Release build

```bash
dotnet build src/MachiVerse.Simulation.Core/MachiVerse.Simulation.Core.csproj --configuration Release
dotnet build src/MachiVerse.Gateway/MachiVerse.Gateway.csproj --configuration Release
dotnet build src/MachiVerse.View/MachiVerse.View.csproj --configuration Release
dotnet build src/MachiVerse.Administration.View/MachiVerse.Administration.View.csproj --configuration Release
```

4componentすべてのRelease buildとcomponent-local smokeはAlpha integration CIで継続検証する。

## Local Alpha directory

world persistenceとGateway component persistenceをrestart前後で同じ場所へ向ける。

例:

```bash
export MACHIVERSE_ALPHA_ROOT="$PWD/.machiverse-alpha"
mkdir -p "$MACHIVERSE_ALPHA_ROOT/world" "$MACHIVERSE_ALPHA_ROOT/gateway"
```

`.machiverse-alpha`はlocal runtime data用であり、source正本ではない。

## 起動順序

### 1. Simulation Core

```bash
MACHIVERSE_ALPHA_LOCAL=1 \
MACHIVERSE_WORLD_ROOT="$MACHIVERSE_ALPHA_ROOT/world" \
Kestrel__Endpoints__Grpc__Url=http://127.0.0.1:5711 \
Kestrel__Endpoints__Grpc__Protocols=Http2 \
Kestrel__Endpoints__Health__Url=http://127.0.0.1:5710 \
Kestrel__Endpoints__Health__Protocols=Http1 \
dotnet run --project src/MachiVerse.Simulation.Core/MachiVerse.Simulation.Core.csproj \
  --configuration Release --no-build
```

確認:

```bash
curl --fail http://127.0.0.1:5710/healthz
```

### 2. Gateway

```bash
MACHIVERSE_ALPHA_LOCAL=1 \
MACHIVERSE_CORE_ENDPOINT=http://127.0.0.1:5711 \
MACHIVERSE_GATEWAY_DATA_ROOT="$MACHIVERSE_ALPHA_ROOT/gateway" \
ASPNETCORE_URLS=http://127.0.0.1:5520 \
dotnet run --project src/MachiVerse.Gateway/MachiVerse.Gateway.csproj \
  --configuration Release --no-build
```

確認:

```bash
curl --fail http://127.0.0.1:5520/healthz
```

Gateway healthでCore linkが`ready` / negotiated / `Synced`となることを確認する。

### 3. General View

```bash
ASPNETCORE_URLS=http://127.0.0.1:5750 \
dotnet run --project src/MachiVerse.View/MachiVerse.View.csproj \
  --configuration Release --no-build --no-launch-profile
```

ブラウザ:

```text
http://127.0.0.1:5750/
```

初回正常状態:

- Lifecycle: `Ready`
- initial confirmed publication: `FULL`
- confirmed basis: `0`（fresh Alpha worldの場合）
- Participation binding: `NONE`, generation `0`

### 4. Administration View

```bash
ASPNETCORE_URLS=http://127.0.0.1:5660 \
dotnet run --project src/MachiVerse.Administration.View/MachiVerse.Administration.View.csproj \
  --configuration Release --no-build --no-launch-profile
```

ブラウザ:

```text
http://127.0.0.1:5660/
```

初回正常状態:

- Lifecycle: `Ready`
- Admin session: `Active`
- Health projection: `Loaded`
- Config projection: `Loaded`
- fresh Gateway ConfigGeneration: `1`

## Alpha 1.0 Golden Demo

### A. General View login / confirmed world

1. General Viewを開く。
2. Gateway経由のGENERAL_VIEW sessionが`Active`になる。
3. Core由来のconfirmed `FULL` publicationを受信する。
4. rendererは`ConfirmedWorldStore`だけをworld authorityとして描画する。

### B. Diver binding Operation

1. `Open Diver Participation`を開く。
2. `Request Alpha binding`を実行する。
3. 同じ`StandardOperationV1` identityがView -> Gateway -> Coreを通る。
4. Coreがdurable lifecycleを`ACCEPTED -> SCHEDULED -> TERMINAL`へ進める。
5. default schedulingではeffective Step `2`になる。
6. authoritative worldはbasis `3`へ進む。
7. Gatewayは直前FULL tokenをbaseにしたconfirmed `DELTA`をViewへ送る。
8. bindingは`ACTIVE`, generation `1`, effective Step `2`となる。

local predictionはpresentation-onlyであり、Core/Gatewayへ送信されない。terminal成功後はconfirmed basis到着を待ち、basis 3のauthoritative publicationで`prediction.confirmed-authoritative`として除去される。

### C. Administration Config change

Administration Viewの`Open Config / operational command management`からlocal draftを作る。

Alphaでruntime-safeとして開いている値の例:

```text
peer.heartbeat-interval-ms
```

fresh runtimeで:

```text
expected ConfigGeneration = 1
new value = 1200
```

をsubmitすると:

```text
ConfigGeneration 1 -> 2
result = config.change.applied
```

となる。

同じ旧generation draftを新Operationとして再submitすると`config.generation-stale`でrejectされる。protected requestはauditへrequested/applied/rejectedとして記録される。

### D. Save / restart / recovery

Diver bindingとConfigGeneration 2が成立した状態で、General/Admin Viewのweb serverは残してCoreとGatewayを停止してよい。

1. Gateway停止
2. Core停止
3. 同じ`MACHIVERSE_WORLD_ROOT`でCore再起動
4. 同じ`MACHIVERSE_GATEWAY_DATA_ROOT`でGateway再起動
5. GatewayがCoreへ再接続し、confirmed basis `3`へFULL resyncする
6. General Viewをreloadする
7. Administration Viewをreloadする

期待結果:

- General Viewは新接続のため`FULL`から復帰する
- recovered FULL basisは`3`
- recovered basis-3 continuity tokenはrestart前のauthoritative basis-3 tokenと同一
- bindingは同じDiver actor / generation `1` / effective Step `2`
- ConfigGenerationは`2`
- `peer.heartbeat-interval-ms`は`1200`
- normal restart recoveryでcontinuity mismatchを発生させない

この複合経路は`Alpha combined restart validation`で自動検証する。

## Fail-closed acceptance

Alpha integration CIでは次も検証済みである。

### Session

- General View/Admin Viewのwrong auth domainを拒否
- connected General View sessionの`REVOKED` / `EXPIRED`
- terminal session generation increment
- terminal後のprotected OperationはCoreへforwardしない
- correlated terminal rejected Operation resultを返す
- View lifecycleはReadyのまま残らずDegradedへ移る

### Publication continuity

正常経路:

```text
FULL basis 0
  -> real binding mutation
  -> DELTA basis 3
  -> restart/new connection
  -> FULL basis 3
```

continuity mismatch経路はproduction Gatewayにfake authorityを実装せず、`tests/MachiVerse.Alpha.ViewFaultProxy`のtest-owned transportでのみ注入する。

```text
valid FULL
  -> test transport corrupts one DELTA base token
  -> View rejects DELTA
  -> ConfirmedWorldStore authority clear
  -> renderer clears old confirmed-looking scene
  -> Participation RefreshRequired / mutation unavailable
  -> force-FULL request
  -> real Gateway FULL
  -> Ready recovery
```

## CI acceptance gates

Alpha integration branchのPRでは以下を継続して緑に保つ。

- Alpha integration PR validation
- Alpha View link validation
- Alpha General View browser validation
- Alpha Administration View browser validation
- Alpha Administration Config change validation
- Alpha Diver binding validation
- Alpha session fail-closed validation
- Alpha View continuity fault validation
- Alpha combined restart validation

## Alpha 1.0で意図的に未完了のもの

次はこの最初のINT-01 Alpha vertical sliceの合否条件ではない。

- SIM-15 Core observability / telemetry完成
- QA-04 performance / soak harness完成
- INT-02 multi-Gateway failover/resync/churn acceptance
- INT-03 release acceptance
- 24h soak
- production performance tuning
- production OIDC/TLS deployment profileの最終acceptance
- 完成版UI/asset/world content
- 大規模Resident/economy/governance scenario
- polished city visual fidelity

local Alpha Gateway/View bridgeは開発・integration用であり、production security profileの代替ではない。

## 現在のAlpha制約

- local Alpha profileはloopback-onlyを前提とする。
- General Viewのlocal Alpha session control acceptanceでは曖昧なterminal targetを避けるためactive General View sessionを1接続に制限する。
- Alphaで公開するworld-affecting View Operationは現在`participation.binding.create`の最小sliceのみ。
- Admin operational command catalogは標準descriptorがないcommandをfail-closedで公開しない。
- Config changeはAlpha runtime-safeとして明示した非secret項目だけを対象とする。
- test-owned continuity fault proxyはacceptance専用でありproduction起動構成へ含めない。
- world visual contentの完成度はAlpha 1.0のacceptance基準ではない。

## Acceptance evidence

主要integration slice:

- PR #195 — Administration Config change
- PR #196 — real Diver binding / actor identity / persistence recovery
- PR #201 — confirmed Gateway -> View DELTA
- PR #202 — continuity mismatch fail-closed / force-FULL recovery
- PR #203 — browser FULL -> DELTA -> restart FULL continuity
- PR #205 — revoke / expired / unauthorized fail-closed
- PR #207 — prediction -> authoritative confirmed reconciliation
- PR #209 — test-owned bad-base injection / stale scene clearing / real FULL recovery
- PR #211 — combined Operation / Config / publication restart acceptance

Tracking:

- #178 — Alpha 1.0 initial integration / INT-01
- #69 — QA / Integration roadmap

## 次の段階

INT-01 completion後は、`integration/1.0-alpha`を`develop`へ戻せる状態にし、その後の主対象を次へ移す。

- SIM-15
- QA-04
- INT-02
- INT-03

Alpha 1.0は「完成版MachiVerse」ではなく、Core-authoritative loopが4component間で実際に成立する最初の再現可能な縦断版として扱う。
