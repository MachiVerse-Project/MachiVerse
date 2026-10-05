# Alpha 1.1 ランタイム・リリースプロファイル補遺

状態: **Alpha 1.1の承認済み規範要件**。実装への適用完了とは区別する。
追跡: #556。実装: #548 / #554 / #558 / #559。Workflow: #555。

## 1. 適用範囲と優先順位

本補遺はAlpha 1.1のランタイムとGate 4のリリース証跡要件を定める。Alpha 1.1では、旧Phase 4文書にある30Hz性能条件・必須24h soakより本補遺を優先する。過去の意思決定・完了レビューは履歴として保持し、Config schema `1.0`の既定値は変更しない。

### 実行前条件

文書のみを統合したcheckoutでは、以下の変更は**未適用**である。旧実装のまま正式10Hz Step3や12h Step4を開始してはならない。

- #554の10Hz Config、実測deadline miss判定、5 digest検証、`run-step3` / `run-step4`をcandidateへ統合する。
- #559の12h manifest・全consumer・Step2計画識別子・Gateway周期契約をcandidateへ統合する。
- #555の対応workflowをdefault-branch authorityへ統合する。
- 同一candidate checkoutでConfig、manifestと全consumerのdigest、CLI、評価器、workflowの整合を検証する。

承認済み規範を先に`documentation`→`develop`へ統合し、その後で実装側を追従させる。実行手順は上記の適用確認後にのみ有効となる。PRの統合・通常CI成功は正式Step3/Step4 PASSを意味しない。

## 2. StepRateの正本

時間軸の正本は整数`SimulationStep`である。`StepRate`はSimulation Core Config所有の`SIMULATION + RUNTIME_SAFE`設定であり、effective Step、ConfigGeneration、ConfigDigest、replay/historyの契約を維持する。

互換性のためschema `1.0`の既定値は次のままとする。

```text
simulation.step-rate.numerator = 30
simulation.step-rate.denominator = 1
```

Alpha 1.1標準ランタイムはschema既定値を再定義せず、外部Configで次を明示する。

```text
simulation.step-rate.numerator = 10
simulation.step-rate.denominator = 1
```

#554適用前の同梱Configは10Hzへ変更済みとは扱わない。適用後は新規標準worldを10Hzで作成し、既存30Hz worldは永続化済みConfigを保持して起動する。起動時にConfig履歴を黙って移行しない。未知のdigestは拒否し、明示的な変更には既存のdurable Config変更契約を用いる。

## 3. Gate 4 Step 3 — 10 tick/s性能条件

正本workloadは`perf.reference.v1`を維持する。閾値変更をworkload削減・意味論省略の許可として扱わない。Core所有の外部`config/qa04-alpha11.json`を測定・Adapter・Runnerの共通正本とし、Config SHA-256を証跡へ保持する。

Alpha 1.1標準条件:

- target runtime rate: **10 tick/s**、処理deadline: **100 ms/tick**（StepRateから導出）
- p99 authoritative Step処理時間 **<=100ms**、deadline miss ratio **<=1%**
- pacing/waitは処理latencyから除外する
- worker **8 / 16**、各3独立run。worker数とphysical core数を混同せず、topology・affinity・production CPU実行を記録する
- accepted Operation loss = **0**、hidden solver iteration reduction = **false**
- 5種類すべてのdeterminism digestを欠損・不正形式・全ゼロ・不一致に対してfail-closedで検証する
- working-setの絶対上限を維持し、標準22GiB target / 28GiB hard guardを外部Configから取得する
- determinism、durability、COMMIT-before-publicationを維持する

deadline、許容miss率、測定件数、反復数、worker profile、メモリ上限は外部Configの値を正本とする。旧30Hzのp95 `33.333ms`、p99 `50ms`、60秒mean `30ms`は履歴・telemetryとして残せるが、Alpha 1.1 release blockerにはしない。#554適用前の評価器がこの新条件を実装済みとは扱わない。

## 4. Gate 4 Step 4 — 12h耐久条件

Alpha 1.1の必須TestCaseIdは`performance.soak.12h`、最低時間は**43,200 wall-clock seconds**である。Adapter報告時間とRunnerが測ったmonotonic経過時間の両方を検証し、小さい方を正本とする。旧24h実装を12h workflowで起動してはならない。

時間短縮以外の品質条件は緩和しない。

- Step3正式PASSと同一candidate commit・runtime Config
- 10 tick/s production pacing、memory bounded / plateau
- parallel verifier digest一致、post-warmup memory growth guard
- accepted Operation loss = 0、history/audit chain有効
- 回復不能なqueue deadlockなし、persistence/publication preflight PASS
- Snapshot / recovery continuity、runtime stability

24h以上は任意のextended endurance evidenceであり、Alpha 1.1の必須条件にはしない。

## 5. 証跡のfail-closed境界

短い`contract-smoke`、synthetic、reduced、preflight、通常CIをrelease evidenceへ昇格しない。`releaseEvidenceCapable=true`は正式Step3 PASSと完全な12h Step4証跡を検証した後にのみ判定する。旧Step3証跡はacceptance profile・Config digest・個別レポートの再検証によって拒否する。

## 6. QA-04 manifestの同期条件

#559で移行する12h manifestの予定SHA-256:

```text
4cdd020abcc8ce37a54944181ce718fb4ae6de8f562bf4f846d669dbdf155a06
```

文書のみのcheckoutでは上記を実在するcanonical digestとして扱わない。実行時はcheckoutのmanifest実体のSHA-256と、PerformanceHarness、RuntimeTarget、ReleaseEvidenceRunner、ReleaseAcceptance、workflowのbindingがすべて一致することを確認する。今後manifestが変更された場合も、実体と全consumerを同時に更新・検証する。
