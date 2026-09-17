<p align="center">
  <img src="assets/images/social-preview/Social%20preview%20%28Cut%20and%20resize%2C%20comp%29.png" alt="MachiVerse" width="100%">
</p>

<p align="center">
  <img src="assets/images/icons/Icon%20%28Resize%20middle%29.png" alt="MachiVerse icon" width="112">
</p>

<h1 align="center">MachiVerse</h1>

<p align="center">
  <strong>世界を、状態の集合ではなく、因果と歴史を持つ動的なシステムとしてシミュレーションする。</strong>
</p>

<p align="center">
  <a href="https://github.com/MachiVerse-Project/MachiVerse/actions/workflows/m1-dotnet-validation.yml"><img alt="M1 .NET validation" src="https://github.com/MachiVerse-Project/MachiVerse/actions/workflows/m1-dotnet-validation.yml/badge.svg?branch=develop"></a>
  <img alt=".NET 10" src="https://img.shields.io/badge/.NET-10.0-512BD4">
  <a href="LICENSE"><img alt="Apache License 2.0" src="https://img.shields.io/badge/License-Apache%202.0-blue.svg"></a>
  <img alt="Status" src="https://img.shields.io/badge/Status-Alpha%201.0-orange">
</p>

MachiVerse は、C# / .NET 10 で開発しているエージェントベースの大規模世界シミュレーターです。

目標は、単に多数の機能やオブジェクトを配置することではありません。世界を構成する状態、因果、相互作用、時間、歴史、自然環境、社会的関係をつなぎ合わせ、**「なぜ現在の世界がこの状態になったのか」まで説明できる動的な世界**を成立させることです。

> [!IMPORTANT]
> 現在の `develop` には、Simulation Core / Gateway / General View / Administration View を実接続した **Alpha 1.0 の最初の再現可能な vertical slice** が入っています。設計だけの段階ではありません。

## 現在の状態 — Alpha 1.0

`INT-01 Single Gateway end-to-end` は完了済みです。

現在のローカルAlphaでは、次の authoritative loop が実コンポーネント間通信で成立しています。

```text
Simulation Core
      │
      │ gRPC / confirmed authority
      ▼
   Gateway
    │    │
    │    └──────────────┐
    ▼                   ▼
General View     Administration View
    │                   │
    │ Diver Operation   │ Config / health
    └──────────┬────────┘
               ▼
            Gateway
               │
               ▼
        Simulation Core
               │
               ├─ terminal result
               └─ confirmed FULL / DELTA
                          │
                          ▼
                       View
```

### すでに動くもの

- Simulation Core ↔ Gateway の実gRPC接続
- General View / Administration View の実ブラウザセッション
- confirmed world の `FULL` / `DELTA` publication
- continuity mismatch時のfail-closed + FULL resync
- Diver の最小 `participation.binding.create` Operation
- Coreでの `ACCEPTED -> SCHEDULED -> TERMINAL`
- authoritative WorldState更新とViewへの反映
- Viewのローカルprediction → confirmed state reconciliation
- Admin Viewからのhealth / Config read
- expected `ConfigGeneration` 付きConfig change
- stale generation rejectとaudit correlation
- Core / Gatewayの永続化、停止、再起動、recovery、resync
- wrong-domain / revoked / expired sessionのfail-closed

Alpha 1.0の詳細なacceptanceと再現手順は [`docs/alpha-1.0-integration.md`](docs/alpha-1.0-integration.md) を参照してください。

## Quick Start — Windows

前提:

- Windows
- [.NET SDK 10.0.400](global.json) 相当
- 最新の `develop`

```bat
git checkout develop
git pull
start-alpha.bat
```

`start-alpha.bat` は次を自動で行います。

1. .NET SDK確認
2. 4コンポーネントをRelease build
3. Simulation Core起動とhealth待機
4. Gateway起動とCore `Synced`待機
5. General View起動
6. Administration View起動
7. 2つのViewをブラウザでオープン

ビルド済みなら次回以降は:

```bat
start-alpha.bat --no-build
```

ローカルのworld / Gateway永続データは `.machiverse-alpha/` に保持されるため、restart / recoveryもそのまま試せます。

手動起動、Linux/macOS相当のコマンド、Golden Demo、既知制約は [`docs/alpha-1.0-integration.md`](docs/alpha-1.0-integration.md) にあります。

## コンポーネント

MachiVerse は4つの最上位コンポーネントを、独立した実行・ビルド・配布単位として扱います。

| Component | Responsibility | Alpha 1.0 |
| --- | --- | --- |
| **Simulation Core** | 世界シミュレーション、正本WorldState、Operation実行、永続化 | 実起動・mutation・recovery済み |
| **Gateway** | 外部接続、認証認可、confirmed cache、操作集約、publication | Core/View/Admin実接続済み |
| **General View** | 一般利用者向け参照・参加・操作UI | Blazor WASM + three.js、実browser E2E済み |
| **Administration View** | 監視、Config、運用UI | health / Config read-change実E2E済み |

コンポーネント間では内部型や実装DLLを通信契約として共有せず、versioned Protocol / schema を境界にします。

## MachiVerseが重視すること

### 狂気的なまでに世界をシミュレーションする

表面的な機能数ではなく、世界を構成する要素同士が因果でつながり、状態が時間とともに変わることを重視します。

### ダイバーは世界の外から操作する人ではなく、一人の住人

利用者ロール「ダイバー」が、世界のルール、時間、社会的関係、周囲の反応の中に存在する一住民として感じられる体験を目指します。

### 現在には歴史がある

都市、集落、住民、自然環境などを固定された完成物として扱わず、過去の状態と選択の結果として現在が形成され、現在の変化が未来に残る世界を志向します。

### 都市だけが世界ではない

複数の都市・集落・非都市地域が存在し、需要や相互作用、新たな居住地点の発生まで含めて世界を考えます。

### 人の営みと自然環境を切り離さない

地形、水域、植生などを背景ではなく、居住、生産、産業、交通その他の活動へ影響する世界状態として扱います。

### 決定論を守る

同一World Seed・同一設定・同一操作では同一結果へ収束することを基本契約とし、ログ、描画タイミング、telemetryなどの非意味論的情報をWorldStateへ混入させません。

## Alpha 1.0で意図的に未完了のもの

Alpha 1.0は「完成版MachiVerse」ではありません。次は別work packageです。

- `SIM-15` — Core observability / telemetry
- `QA-04` — performance / soak harness
- `INT-02` — multi-Gateway failover / resync / View churn
- `INT-03` — release acceptance / 24h soak
- production OIDC/TLS deployment acceptance
- production performance tuning
- 大規模Resident / economy / governance scenario
- polished UI / city / asset / world content

現在のlocal Alpha profileは **loopback-onlyの開発・integration用**であり、production security profileの代替ではありません。

## ドキュメント

- [Alpha 1.0 Integration Runbook](docs/alpha-1.0-integration.md)
- [設計ドキュメント一覧](docs/README.md)
- [全体アーキテクチャ](docs/architecture/overview.md)
- [世界シミュレーション設計](docs/architecture/world-simulation.md)
- [シミュレーションコア設計](docs/architecture/simulation-core.md)
- [ゲートウェイ設計](docs/architecture/gateway.md)
- [Protocol設計](docs/protocols/README.md)
- [Roadmap](ROADMAP.md)
- [開発ルール](AGENTS.md)
- [Contribution Guide](CONTRIBUTING.md)

## 開発フロー

常設ブランチ:

```text
main                  stable / release candidate
  ↑
develop               next integrated version
  ↑
├─ simulation
├─ gateway
├─ view
├─ administration-view
└─ documentation
```

通常作業は責任分野に対応する常設ブランチから作業ブランチを切り、Pull Requestで統合します。リポジトリ横断のintegration / hotfix / CI作業は [`AGENTS.md`](AGENTS.md) のルールに従います。

## Contributing

Issue / Pull Request / Discussion を歓迎します。

MachiVerseでは、確定済みのProtocol・決定論・コンポーネント境界・authoritative stateの意味を壊さずに拡張することを重視しています。参加前に [`CONTRIBUTING.md`](CONTRIBUTING.md) と [`AGENTS.md`](AGENTS.md) を確認してください。

## Links

- Website: https://machiverse.app
- GitHub Discussions: https://github.com/MachiVerse-Project/MachiVerse/discussions
- Roadmap: [`ROADMAP.md`](ROADMAP.md)

## License and rights

MachiVerseの**ソフトウェアコード**は、特に明記されていない限り [Apache License 2.0](LICENSE) のもとで提供されます。

キャラクター、イラスト、ロゴ、画像、3Dモデル、音声、音楽、動画その他のCreative Assetは、Apache-2.0の対象であると明示されていない限り [`RIGHTS.md`](RIGHTS.md) の **MachiVerse Rights, Fan Works & Asset Use Policy** に従います。

Creative Assetの二次創作、改変、収益化、再配布、派生プロジェクトでの利用、Contributionの扱いについても `RIGHTS.md` を参照してください。
