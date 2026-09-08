# Roadmap Index

本ディレクトリは、MachiVerse の component 別実装ロードマップと QA / integration ロードマップを管理する。

上位ロードマップはリポジトリルートの `ROADMAP.md`。

## 正本関係

ロードマップは設計契約の正本ではない。

実装内容の意味、schema、algorithm、Protocol、Config、Persistence、security、performance、acceptance criteria は `docs/design/`、`docs/protocols/`、`docs/architecture/`、`docs/requirements/` の確定文書へ従う。

特に実装順序・work package dependency は次を正本とする。

- `docs/design/phase4-completion-review.md`
- `docs/design/phase4-implementation-work-breakdown.md`
- `docs/design/phase4-test-acceptance.md`
- `docs/design/phase4-platform-runtime-profile.md`

## Component roadmap

| Area | Roadmap | ImplementationWorkId |
|---|---|---|
| Simulation Core | `simulation-core.md` | `SIM-01..SIM-15` |
| Gateway | `gateway.md` | `GW-01..GW-07` |
| General View | `general-view.md` | `VIEW-01..VIEW-05` |
| Administration View | `administration-view.md` | `ADMIN-01..ADMIN-04` |
| QA / Integration | `quality-integration.md` | `QA-01..QA-04`, `INT-01..INT-03` |

## 進捗状態

各 work package は原則として次の状態で追跡する。

```text
NOT_READY
READY
IN_PROGRESS
BLOCKED
REVIEW
COMPLETE
```

- `NOT_READY`: dependency gate 未成立
- `READY`: dependency を満たし着手可能
- `IN_PROGRESS`: implementation branch / PR で作業中
- `BLOCKED`: 新たな blocker が明示されている
- `REVIEW`: Definition of Done を満たすため review / acceptance 中
- `COMPLETE`: target responsibility branch へ統合済みかつ required acceptance を通過

## 設計変更の扱い

実装中に確定契約の変更が必要になった場合:

1. implementation Issue 内で silent change しない。
2. design amendment Issue を作る。
3. affected stable schema/token/version を更新する。
4. migration / compatibility impact を評価する。
5. affected acceptance test を更新する。
6. dependent implementation Issue へ反映する。

## Integration gate

各 component の個別進捗だけでは release readiness を判定しない。

横断的な完成判定は `quality-integration.md` の `INT-01`、`INT-02`、`INT-03` を使用する。
