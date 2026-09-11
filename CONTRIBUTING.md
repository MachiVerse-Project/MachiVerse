# コントリビューションガイド

MachiVerse への関心と協力ありがとうございます。

このリポジトリでは、設計上の確定事項と提案・未確定事項を明確に分けながら開発を進めます。

## 基本方針

- リポジトリの基準言語は日本語です。
- 実装前に関連する設計書と `AGENTS.md` を確認してください。
- 未承認の仕様を推測して確定事項として実装しないでください。
- コンポーネント間でコード、DLL、内部型、共有DTOライブラリを通信契約として共有しないでください。
- コンポーネント間通信は `docs/protocols/` の設計書を契約として扱ってください。
- 調整可能な数値は各コンポーネントが所有する外部Configから変更可能にしてください。

## ライセンスとCreative Asset Contribution

MachiVerseでは、ソフトウェアコードとCreative Assetを異なる権利条件で管理しています。詳細は [`RIGHTS.md`](RIGHTS.md) を確認してください。

### コードContribution

Apache-2.0の対象として指定されているコード領域へ意図的に提出されるコードContributionは、別途合意がある場合を除き、Apache License 2.0の条件で提出されます。

Contributorは、提出するコードについて必要な権利を有していることを確認してください。

### Creative Asset Contribution

画像、イラスト、ロゴ、テクスチャ、3Dモデル、音声、音楽、動画、キャラクター設定その他のCreative Assetは、**通常のApache-2.0コードContributionフローでは受け付けません**。

Creative Assetを提出したい場合は、Pull Requestを作成する前に、適用されるAsset-specific terms、Contributor Agreement、またはその他の個別合意をMachiVerse Projectの公式窓口を通じて確認してください。

Creative Assetを含むPull Requestを例外的に提出する場合は、少なくとも次を明確にする必要があります。

- 提出者が当該Assetを提出するために必要な権利を有していること
- 適用されるAsset-specific termsまたは個別合意が特定されていること
- 当該Assetが、単にApache-2.0のリポジトリへ投稿されたことだけを理由として通常のコードContributionとして扱われるものではないことについて、事前の明示的な整理があること

上記が整理されていないCreative Assetを通常のコードPull Requestへ含めないでください。

## 相談・報告先の使い分け

内容に応じて、次の入口を使い分けてください。

- **Issue / 不具合報告**: 再現可能な不具合、期待動作との差異、追跡可能な修正対象
- **Issue / 機能・設計提案**: 実装候補として具体化され、影響範囲や関連設計を整理できる改善・設計変更
- **GitHub Discussions / Ideas**: まだ実装対象として整理されていないアイデアや改善案
- **GitHub Discussions / Q&A**: 使い方、設計意図、開発方針などに関する質問
- **GitHub Discussions / Design**: 実装前に設計上の選択肢、責務境界、Protocol、互換性等を議論したい内容
- **GitHub Discussions / Show and tell**: MachiVerseを使った試作、検証、可視化、連携例などの共有
- **Security**: 脆弱性や、公開すると危険な情報を含む報告。公開IssueやDiscussionには投稿せず `SECURITY.md` の手順に従ってください。

Discussionの内容だけを根拠として未承認仕様を確定しません。合意して実装対象になった内容は、必要に応じてIssue、ROADMAP、`docs/architecture/`、`docs/protocols/` などの正本へ反映します。

一般的な相談窓口については `SUPPORT.md` も参照してください。

## ブランチ運用

常設ブランチは以下です。

- `main`: 安定版・リリース可能な状態
- `develop`: 次期版の統合ブランチ
- `simulation`: Simulation Coreの統合ブランチ
- `gateway`: Gatewayの統合ブランチ
- `view`: Viewの統合ブランチ
- `administration-view`: Administration Viewの統合ブランチ

通常の作業は、対象コンポーネントの常設ブランチから作業ブランチを作成してください。

例:

- `feature/simulation-world-time`
- `fix/gateway-master-reselection`
- `refactor/simulation-threading`
- `docs/core-concurrency`

`main`、`develop`、各コンポーネント常設ブランチへの通常作業の直接コミットは行いません。

## Pull Request

Pull Requestでは、少なくとも以下を説明してください。

- 変更の目的
- 変更内容
- 影響するコンポーネント
- 関連する設計書・プロトコル
- 未解決事項または意図的に変更していない事項
- 実装変更の場合は確認・テスト方法

1つのPull Requestへ無関係な変更をまとめないでください。

Creative Assetを含む場合は、通常のコードPRとして扱わず、上記「ライセンスとCreative Asset Contribution」の条件を満たしていることを確認してください。

## プロトコル変更

コンポーネント間プロトコルを変更する場合は、実装より先に対象のプロトコル設計書を更新してください。

同一Major内のMinor更新は後方互換性を維持し、後方互換性を維持できない変更はMajor更新として扱います。

## Issue

Issueは、追跡・実装・修正の対象として具体化できる内容に使用してください。Issue作成時は用意されているフォームを使用し、対象コンポーネント、再現手順、関連する設計書・Protocol等を分かる範囲で記載してください。

アイデア段階の相談や、結論を出す前のオープンな設計議論はGitHub Discussionsを使用してください。

セキュリティ上の問題や公開すると危険な情報は、通常の公開Issueへ投稿しないでください。詳細は `SECURITY.md` を確認してください。

## 設計ドキュメント

システム構成、責務、通信境界、シミュレーション要件等を変更する場合は、実装だけでなく関連設計書も更新してください。

主要な設計資料は `docs/README.md` から確認できます。
