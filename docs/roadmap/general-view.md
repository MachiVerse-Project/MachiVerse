# General View 実装ロードマップ

ImplementationWorkId: `VIEW-01..VIEW-05`  
Base branch: `view`  
Upper roadmap: `/ROADMAP.md`

## 1. 実装baseline

Standard runtime / presentation profile:

```text
standalone Blazor WebAssembly net10.0
Gateway boundary: TLS WebSocket binary + Protocol Buffers
3D renderer: Three.js THREE.WebGPURenderer
Preferred backend: WebGPU
Compatibility backend: WebGL 2 through WebGPURenderer fallback
Custom material/shader: TSL / node-material first
```

General View は authoritative World State を所有しない。

confirmed state、presentation interpolation/prediction、user input、Diver participation を明確に分離する。

## 2. Milestone mapping

| Global milestone | Work package | Dependency |
|---|---|---|
| M1 | `VIEW-01` | `QA-01` protocol fixture |
| M2 | `VIEW-02` | `VIEW-01` |
| M3 | `VIEW-03`, `VIEW-04` | `VIEW-02` |
| M5 | `VIEW-05` | `VIEW-04`, Gateway authz fixture |
| M6 | end-to-end validation | `INT-*` |

## 3. Foundation

### VIEW-01 — General View scaffold / Gateway protocol client

Scope:

- standalone Blazor WebAssembly shell
- binary WebSocket client
- protobuf decode/encode boundary
- General View Config 1.0 loader
- lifecycle / reconnect shell
- JavaScript / Three.js interop boundary skeleton

DoD gate:

- real Gatewayなしでfixture接続を検証可能
- protocol payloadをpresentation modelへ直接流し込まずboundary mappingを持つ

## 4. Confirmed state pipeline

### VIEW-02 — Confirmed state store / publication consumer

Scope:

- FULL / DELTA publication consume
- `basis_step` / `StateContinuityToken`
- delta base validation
- atomic confirmed state swap
- reconnect / resync lifecycle
- syncing / resyncing user-visible state

Rules:

- stale local cacheをauthoritative-looking stateとしてblind reuseしない
- inconsistent sequenceをnormal confirmed stateへ昇格しない
- prediction/interpolation stateにconfirmed tokenを付与しない

Dependency: `VIEW-01`。

## 5. Presentation and interaction spine

### VIEW-03 — Three.js scene projection / renderer

Scope:

- SceneProjection model
- Three.js scene lifecycle
- full 3D terrain / built / presence projection
- render LOD / culling
- `THREE.WebGPURenderer`
- WebGPU-first / WebGL2 automatic fallback
- TSL / node-material based custom material
- device loss / renderer reinitialize path

Dependency: `VIEW-02`。Asset fixtureで並列開発可能。

Acceptance emphasis:

- render backend差でconfirmed state / Operation result / Core StateDiagnosticを変更しない
- camera / FPS / GPU能力をworld simulation入力にしない
- standard rendererを`THREE.WebGLRenderer`へsilent変更しない

### VIEW-04 — Prediction / reconciliation / Operation controller

Scope:

- local interpolation / short prediction
- immediate user feedback
- stable Operation request
- pending / retry / result state
- authoritative reconcile / correction

Dependency: `VIEW-02`。

Rules:

- predictionをauthoritative mutationにしない
- retryでOperationIdを再発行しない
- candidate/requested Stepをauthoritative effective Stepとして表示しない

`VIEW-03` と `VIEW-04` は `VIEW-02` 後に並列化可能。

## 6. Participation UX

### VIEW-05 — Participation UX

Scope:

- Diver participation / resident preference
- existing resident binding projection
- absence policy UI
- reconnect state
- controlled resident death / rebinding state
- role/permissionに応じたUI action availability

Dependencies: `VIEW-04`, `GW-05` protocol/authz contract。

Rules:

- Diver joinのため専用residentを自動生成しない
- UI表示制御だけでauthorizationを完結させない
- disconnectでresidentをworldからremoveしない
- reconnectでDiver identityを再発行しない

## 7. General View completion gate

Component-level completeには少なくとも次を要求する。

- FULL/DELTA continuity / resync client tests
- WebGPU initialization / backend acceptance
- WebGL2 fallback through `WebGPURenderer`
- TSL/node-material path
- device loss recovery
- prediction-not-authority test
- Operation retry identity preservation
- participation binding/reconnect/death UX contract
- Gateway mockで独立 build/test

Release完了は `INT-01..INT-03` と performance / soak acceptance で判定する。
