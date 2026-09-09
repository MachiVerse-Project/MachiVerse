# MachiVerse Brand Assets

このディレクトリには、MachiVerse の公式ロゴ画像を配置しています。

広報資料、Web ページ、SNS、プレゼンテーション、ドキュメントなどで MachiVerse を表現するときは、可能な限りここにある公式アセットをそのまま使用してください。

## まずどれを使うか

特別な理由がなければ、**`MachiVerse_Official_Logo_Primary.png` を標準ロゴとして使用**してください。

用途に応じて、次のように選びます。

| 用途 | 推奨アセット |
| --- | --- |
| 通常のロゴ表示 | `MachiVerse_Official_Logo_Primary.png` |
| アイコン、アバター、正方形に近い領域 | `MachiVerse_Official_Logo_Primary_Symbol.png` |
| MachiVerse の名称を中心に見せたい場合 | `MachiVerse_Official_Logo_Primary_Wordmark.png` |
| Primary とは異なるトーンが必要な場合 | 対応する `Dark` バリエーション |

`Dark` バリエーションを使用するときは、配置先の背景とのコントラストと視認性を確認してください。

## 収録アセット

### Primary

MachiVerse の標準バリエーションです。通常はこちらを優先してください。

| ファイル | 内容 |
| --- | --- |
| [`MachiVerse_Official_Logo_Primary.png`](./MachiVerse_Official_Logo_Primary.png) | シンボルとワードマークを組み合わせた標準ロゴ |
| [`MachiVerse_Official_Logo_Primary_Symbol.png`](./MachiVerse_Official_Logo_Primary_Symbol.png) | シンボルのみ |
| [`MachiVerse_Official_Logo_Primary_Wordmark.png`](./MachiVerse_Official_Logo_Primary_Wordmark.png) | ワードマークのみ |

### Dark

Primary とは異なるトーンで使用するためのバリエーションです。使用時は背景との組み合わせを確認し、ロゴが十分に判別できる状態を保ってください。

| ファイル | 内容 |
| --- | --- |
| [`MachiVerse_Official_Logo_Dark.png`](./MachiVerse_Official_Logo_Dark.png) | シンボルとワードマークを組み合わせた Dark ロゴ |
| [`MachiVerse_Official_Logo_Dark_Symbol.png`](./MachiVerse_Official_Logo_Dark_Symbol.png) | Dark バリエーションのシンボルのみ |
| [`MachiVerse_Official_Logo_Dark_Wordmark.png`](./MachiVerse_Official_Logo_Dark_Wordmark.png) | Dark バリエーションのワードマークのみ |

## ロゴの構成

このディレクトリでは、ロゴを次の3種類に分けています。

- **Logo**: シンボルとワードマークを組み合わせたもの。最も基本的な表現です。
- **Symbol**: シンボルのみ。アイコンや小さな表示領域など、ワードマークを含めにくい用途に使用します。
- **Wordmark**: MachiVerse の文字表現のみ。名称を中心に見せたい用途に使用します。

フルロゴから手作業でシンボルやワードマークを切り出すのではなく、用途に対応した公式ファイルを使用してください。

## 使用上のガイドライン

ロゴの一貫性を保つため、次の点を守ってください。

- アスペクト比を維持し、縦横を個別に引き伸ばさない。
- 色を任意に変更しない。
- シンボルやワードマークの形状、配置、間隔を変更しない。
- 影、縁取り、グラデーションなどの効果を追加しない。
- ロゴの一部を切り取って別のロゴを作らない。
- 背景と十分なコントラストを確保する。
- 周囲に余白を設け、他の文字や図形を密着させない。
- Symbol や Wordmark が必要な場合は、用意されている専用アセットを使用する。

サイズを変更する場合も、元画像の比率を維持してください。小さく表示すると細部が判別しにくくなる場合は、フルロゴではなく Symbol など、用途に適したアセットを選択してください。

## ファイル名の規則

ロゴアセットは、原則として次の形式で命名します。

```text
MachiVerse_Official_Logo_<Variant>[_<Element>].png
```

現在使用している値は次のとおりです。

- `<Variant>`
  - `Primary`
  - `Dark`
- `<Element>`
  - 省略: シンボル + ワードマークのフルロゴ
  - `Symbol`: シンボルのみ
  - `Wordmark`: ワードマークのみ

新しい公式バリエーションを追加するときは、この命名規則に合わせ、この README の収録アセット一覧と用途説明も同時に更新してください。

## 削除済み・旧アセットについて

`MachiVerse_Official_Logo_Light.png` は `promotion` ブランチから削除されています。

新しい文書や実装からこのファイルへの参照を追加しないでください。既存の参照を見つけた場合は、用途を確認したうえで現行アセットへ置き換えてください。

## 利用条件について

この README は、MachiVerse のブランドアセットを一貫して扱うための表示・運用ガイドです。ロゴや名称に関する権利・利用許諾を、この文書によって新たに定義または追加するものではありません。

外部で利用する場合は、リポジトリのライセンスおよびプロジェクトが別途定める方針も確認してください。
