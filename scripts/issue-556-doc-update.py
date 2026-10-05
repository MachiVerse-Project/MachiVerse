from pathlib import Path


def replace(path_s: str, old: str, new: str, count: int = 1) -> None:
    path = Path(path_s)
    text = path.read_text(encoding="utf-8")
    actual = text.count(old)
    if actual != count:
        raise SystemExit(f"{path}: expected {count} occurrence(s), found {actual}: {old[:80]!r}")
    path.write_text(text.replace(old, new), encoding="utf-8")


amendment = Path("docs/design/phase4-alpha11-runtime-release-profile.md")
if amendment.exists():
    raise SystemExit(f"{amendment} already exists")
amendment.write_text(
    """# Alpha 1.1 runtime / release profile amendment

Status: **Approved normative amendment for Alpha 1.1 release profile**  
Tracking: Issue #556  
Related implementation: #548 / #554 / #555 / #558 / #559

## 1. Scope and precedence

This amendment defines the current Alpha 1.1 runtime and Gate 4 release-evidence profile. It does not rewrite historical Phase 1–4 decisions or change Config schema `1.0` defaults in-place.

Within the Alpha 1.1 release-profile scope, this document overrides older Phase 4 text that describes 30Hz performance acceptance or a mandatory 24-hour soak. Historical completion/review documents remain records of the contract that existed when they were written.

## 2. StepRate authority

The authoritative time axis remains integer `SimulationStep`. `StepRate` remains owned by Simulation Core Config and remains `SIMULATION + RUNTIME_SAFE` with explicit effective Step, ConfigGeneration, ConfigDigest, and replay/history authority.

Config schema `1.0` defaults remain unchanged for compatibility:

```text
simulation.step-rate.numerator = 30
simulation.step-rate.denominator = 1
```

Alpha 1.1 standard runtime does **not** redefine that schema default. The Alpha 1.1 runtime profile explicitly selects:

```text
simulation.step-rate.numerator = 10
simulation.step-rate.denominator = 1
```

Therefore `30/1` is the schema default while `10/1` is the Alpha 1.1 standard runtime/release profile value.

## 3. Gate 4 Step 3 — Alpha 1.1 10 tick/s acceptance

The canonical workload identity remains `perf.reference.v1`; changing the release threshold does not authorize workload reduction or semantic shortcuts.

Alpha 1.1 formal Step 3:

- target runtime rate: **10 tick/s**;
- authoritative processing budget: **100 ms/tick**;
- hard latency boundary: **p99 authoritative Step processing time <= 100 ms**;
- hard deadline boundary: **deadline miss ratio <= 1%**;
- pacing/wait time is excluded from authoritative processing latency;
- release execution profiles: worker count **8** and **16**, three independent runs each;
- requested worker budget must be proven to reach production CPU execution rather than remaining metadata;
- accepted Operation loss must be zero;
- hidden solver-iteration reduction is forbidden;
- determinism/durability/COMMIT-before-publication requirements remain unchanged.

The historical 30Hz-oriented p95 `33.333 ms`, p99 `50 ms`, and 60-second mean `30 ms` values may remain as historical/reference telemetry, but they are not Alpha 1.1 release blockers.

## 4. Gate 4 Step 4 — 12-hour endurance acceptance

Alpha 1.1 hard endurance TestCaseId is:

```text
performance.soak.12h
```

Release evidence requires at least **43,200 wall-clock seconds**. Both adapter-reported duration and monotonic process elapsed time are checked; the smaller duration is authoritative for release eligibility.

The duration reduction is the only intentional relaxation. The following remain mandatory:

- parallel verifier digest match;
- post-warmup memory growth guard;
- accepted Operation loss = 0;
- history/audit chain validity;
- no unrecoverable queue deadlock;
- persistence/publication preflight PASS;
- same release-candidate commit as the preceding Step 3 evidence.

A run longer than 12 hours, including 24-hour endurance validation, may be retained as optional extended evidence but is not required for Alpha 1.1 hard release acceptance.

## 5. Fail-closed evidence boundary

Short `contract-smoke` or synthetic CI runs never become release evidence. `releaseEvidenceCapable=true` requires formal release execution and complete 12-hour duration evidence. A PR CI run is not a substitute for the wall-clock endurance run.

## 6. Canonical QA-04 binding

The Alpha 1.1 12-hour QA-04 manifest SHA-256 is:

```text
4cdd020abcc8ce37a54944181ce718fb4ae6de8f562bf4f846d669dbdf155a06
```

Current release tooling and validation workflows must bind this digest consistently.
""",
    encoding="utf-8",
)

replace(
    "docs/README.md",
    "- standard frequencyは30Hz。\n- 外部Configから変更可能。\n- Coreが30Hzへ追いつかなくてもprocessing delayだけを理由にStepをskipしない。",
    "- Config schema `1.0` のStepRate defaultは互換性のため `30/1` steps/secを維持する。\n- Alpha 1.1 standard runtime profileはCore Configで `10/1` steps/sec（10 tick/s）を明示選択する。\n- StepRateは外部Configから変更可能。\n- Coreがactive StepRateへ追いつかなくてもprocessing delayだけを理由にStepをskipしない。",
)
replace(
    "docs/README.md",
    "- 全世界を一律30Hzでhigh-detail updateすることは要求しない。",
    "- 全世界をactive StepRateで一律high-detail updateすることは要求しない。",
)

replace(
    "docs/architecture/configuration.md",
    "- シミュレーション計算頻度の標準値は **30Hz**。\n- 権威ある時間軸は整数ベースのSimulation Step。",
    "- Config schema `1.0` のStepRate defaultは互換性のため **30/1 steps/sec** を維持する。\n- Alpha 1.1 standard runtime profileはschema defaultを書き換えず、Core Configで **10/1 steps/sec（10 tick/s）** を明示選択する。\n- 権威ある時間軸は整数ベースのSimulation Step。",
)

replace(
    "docs/design/phase4-config-specification.md",
    "- rate generation wrap前にworld migration required。\n\n### 6.2 Worker/runtime",
    "- rate generation wrap前にworld migration required。\n\nSchema `1.0` の上表default `30/1` は互換性のため維持する。Alpha 1.1 standard runtime profileはdefault変更やschema migrationではなく、explicit Configとして `10/1` steps/sec（10 tick/s）を選択する。runtime apply時の `SIMULATION + RUNTIME_SAFE`、effective Step、ConfigGeneration / ConfigDigest / history契約は従来どおり適用する。\n\n### 6.2 Worker/runtime",
)

replace(
    "docs/design/phase4-config-standard-examples.md",
    "state-digest-every-steps = 1\n```\n\n## 3. Gateway",
    "state-digest-every-steps = 1\n```\n\n### 2.1 Alpha 1.1 standard runtime profile override\n\n上のTOMLはConfig schema `1.0` のdefault completion例であり、StepRate default `30/1` は互換性のため変更しない。Alpha 1.1 standard runtime / release profileでは、同じschemaに対して次を明示設定する。\n\n```toml\n[simulation.step-rate]\nnumerator = 10\ndenominator = 1\n```\n\nこれはschema defaultの変更ではない。runtimeで切り替える場合もSimulation-affecting Config changeとしてexplicit effective StepとConfig historyへ記録する。\n\n## 3. Gateway",
)

replace(
    "docs/design/README.md",
    "- `phase4-alpha11-society-governance-reference-authority.md` — `perf.reference.v1` Society/Governance benchmark genesisのToken vocabulary / actual Ref mapping / payload Step authority（#295）",
    "- `phase4-alpha11-society-governance-reference-authority.md` — `perf.reference.v1` Society/Governance benchmark genesisのToken vocabulary / actual Ref mapping / payload Step authority（#295）\n- `phase4-alpha11-runtime-release-profile.md` — Alpha 1.1 standard runtime `10/1`、Gate 4 Step 3 100ms/<=1% miss、Step 4 `performance.soak.12h` の現行release authority（#556）",
)
replace(
    "docs/design/README.md",
    "- 30Hz reference performance profile",
    "- Config schema `1.0` StepRate default `30/1` + Alpha 1.1 standard runtime/release profile `10/1`",
)

replace(
    "docs/design/phase4-performance-benchmark-profile.md",
    "`phase4-config-standard-examples.md` Simulation Core defaultを使用する。",
    "`phase4-config-standard-examples.md` Simulation Core schema `1.0` defaultをbaselineとして使用する。Alpha 1.1 release executionではschema default自体を変更せず、`phase4-alpha11-runtime-release-profile.md` に従ってStepRate `10/1`を明示選択する。",
)
replace(
    "docs/design/phase4-performance-benchmark-profile.md",
    "Worker 1/4/8 are scaling/determinism data and need not each achieve 30Hz, unless separately claimed by product profile。\n\n## 17. Determinism pass criteria",
    "Worker 1/4/8 are scaling/determinism data and need not each achieve 30Hz, unless separately claimed by product profile。\n\n### 16.1 Alpha 1.1 release acceptance overlay\n\n上記30Hz-oriented performance valuesはPhase 4 reference/historyとして保持する。Alpha 1.1 formal Gate 4 Step 3では同じcanonical workload identity `perf.reference.v1`を維持したまま、release blockerを次へ更新する。\n\n- standard runtime StepRate: `10/1` steps/sec（10 tick/s）。\n- release execution worker profile: 8 / 16、各3 independent process runs。\n- p99 authoritative Step processing time <=100ms。\n- deadline miss ratio <=1%。\n- pacing/waitはprocessing latencyへ含めない。\n- accepted Operation loss = 0。\n- hidden solver iteration reduction禁止。\n- production CPU worker evidence、determinism、durability、COMMIT-before-publicationを維持する。\n\n旧p95 33.333ms / p99 50ms / 60-second mean 30msはAlpha 1.1ではtelemetryでありrelease blockerではない。\n\n## 17. Determinism pass criteria",
)

old_perf = """## 32. Performance acceptance

Use `phase4-performance-benchmark-profile.md`。

Release performance profile requires:

- 16-worker p95 Step <=33.333ms reference node。
- p99 <=50ms。
- Core memory never >28GiB guard。
- SQLite commit p95 <=4ms/p99<=8ms。
- Snapshot COW barrier p95 <=5ms。
- no accepted Operation loss。
- publication limits/slow-client isolation pass。

Performance failure does not authorize semantic shortcut; release profile fails。
"""
new_perf = """## 32. Performance acceptance

Use `phase4-performance-benchmark-profile.md` and the Alpha 1.1 override in `phase4-alpha11-runtime-release-profile.md`。

Alpha 1.1 release performance profile requires:

- standard runtime StepRate = 10/1 steps/sec（10 tick/s）。
- worker 8 / 16, three independent process runs each。
- p99 authoritative Step processing time <=100ms。
- deadline miss ratio <=1%。
- pacing/wait excluded from authoritative processing latency。
- production CPU worker budget evidence valid。
- Core memory / persistence / Snapshot / publication guards remain valid through the target reports and QA-04 subprofiles。
- no accepted Operation loss。
- no hidden solver iteration reduction。
- deterministic final-state evidence一致。

Historical 30Hz-oriented p95/p99/rolling-mean values remain reference telemetry but are not Alpha 1.1 release blockers。

Performance failure does not authorize semantic shortcut; release profile fails。
"""
replace("docs/design/phase4-test-acceptance.md", old_perf, new_perf)
replace("docs/design/phase4-test-acceptance.md", "TestCaseId = performance.soak.24h", "TestCaseId = performance.soak.12h")
replace(
    "docs/design/phase4-test-acceptance.md",
    "- 24 wall-clock hours continuous standard simulation load。",
    "- 12 wall-clock hours（43,200 seconds minimum）continuous Alpha 1.1 standard simulation load。",
)
replace(
    "docs/design/phase4-test-acceptance.md",
    "| release candidate | all suites + full perf.reference.v1 + 24h soak |",
    "| release candidate | all suites + Alpha 1.1 Step3 perf.reference.v1 + 12h soak |",
)

replace("docs/release-acceptance.md", "the real `performance.soak.24h` result.", "the real `performance.soak.12h` result.")
replace(
    "docs/release-acceptance.md",
    "A PR workflow is never a substitute for 24 wall-clock hours of soak evidence.",
    "A PR workflow is never a substitute for the required 12 wall-clock hours of soak evidence.",
)
replace("docs/release-acceptance.md", "- 24-hour soak report reference and report digest;", "- 12-hour soak report reference and report digest;")
replace(
    "docs/release-acceptance.md",
    "## 24-hour soak evidence\n\n`performance.soak.24h` must report at least `86400` wall-clock seconds and satisfy all pinned guards:",
    "## 12-hour soak evidence\n\n`performance.soak.12h` must report at least `43200` wall-clock seconds and satisfy all pinned guards:",
)
replace(
    "docs/release-acceptance.md",
    "The soak evidence must also bind the exact candidate commit and immutable report digest. A shorter run is always `INCOMPLETE` even when every observed metric is otherwise healthy.",
    "The soak evidence must also bind the exact candidate commit and immutable report digest. A run shorter than 43,200 seconds is always `INCOMPLETE` even when every observed metric is otherwise healthy. Runs longer than 12 hours, including 24-hour endurance runs, are optional extended evidence rather than an Alpha 1.1 hard requirement.",
)
replace(
    "docs/release-acceptance.md",
    "- `docs/design/phase4-performance-benchmark-profile.md`",
    "- `docs/design/phase4-performance-benchmark-profile.md`\n- `docs/design/phase4-alpha11-runtime-release-profile.md`",
)

old_cmds = """The runner has three commands:

```bash
dotnet run --project tools/MachiVerse.ReleaseEvidenceRunner --configuration Release -- verify

dotnet run --project tools/MachiVerse.ReleaseEvidenceRunner --configuration Release -- \\
  run <contract-smoke|release> <source-commit> <adapter-executable> <plan-directory> <output-directory>

dotnet run --project tools/MachiVerse.ReleaseEvidenceRunner --configuration Release -- \\
  apply <qa04-evidence-fragment.json> <base-evidence.json> <output-evidence.json>
```
"""
new_cmds = """The runner supports the staged Alpha 1.1 release path plus bounded contract validation:

```bash
dotnet run --project tools/MachiVerse.ReleaseEvidenceRunner --configuration Release -- verify

dotnet run --project tools/MachiVerse.ReleaseEvidenceRunner --configuration Release -- \\
  run-step3 <contract-smoke|release> <source-commit> <adapter-executable> <plan-directory> <output-directory>

dotnet run --project tools/MachiVerse.ReleaseEvidenceRunner --configuration Release -- \\
  run-step4 <contract-smoke|release> <source-commit> <adapter-executable> <plan-directory> <step3-evidence.json> <output-directory>

dotnet run --project tools/MachiVerse.ReleaseEvidenceRunner --configuration Release -- \\
  run <contract-smoke|release> <source-commit> <adapter-executable> <plan-directory> <output-directory>

dotnet run --project tools/MachiVerse.ReleaseEvidenceRunner --configuration Release -- \\
  apply <qa04-evidence-fragment.json> <base-evidence.json> <output-evidence.json>
```

Formal Alpha 1.1 Gate 4 uses `run-step3` followed by `run-step4` on the same candidate commit. The monolithic `run` path remains available for contract/compatibility use but does not replace the staged release authority.
"""
replace("docs/release-evidence-runner.md", old_cmds, new_cmds)
replace(
    "docs/release-evidence-runner.md",
    "`5de8301439ca57080eefa599da284f9271b29366c791bcb9c2f85ddbfa041423`",
    "`4cdd020abcc8ce37a54944181ce718fb4ae6de8f562bf4f846d669dbdf155a06`",
)
replace(
    "docs/release-evidence-runner.md",
    "The runner calculates the canonical acceptance boundaries itself, including worker-16 median p95, p99, 60-second mean, memory guard, SQLite p95/p99, snapshot COW p95, accepted Operation loss, hidden solver reduction, and final-state digest equality across all 12 runs. A target-provided `passed=true` cannot override a runner-detected failure.",
    "For formal Alpha 1.1 Step 3, `run-step3` selects worker 8 and 16 from the canonical matrix, three runs each, and applies the 10 tick/s release gate: p99 authoritative processing <=100ms and deadline miss ratio <=1%. Pacing/wait time is excluded from processing latency. Accepted Operation loss, hidden solver reduction, production CPU worker evidence, and deterministic digest checks remain fail-closed. Historical 30Hz-oriented p95/p99/rolling-mean values remain telemetry rather than Alpha 1.1 release blockers. A target-provided `passed=true` cannot override a runner-detected failure.",
)
replace("docs/release-evidence-runner.md", "## 24-hour soak anti-shortcut rule", "## 12-hour soak anti-shortcut rule")
replace(
    "docs/release-evidence-runner.md",
    "`performance.soak.24h` has two independent duration checks in `release` mode:",
    "`performance.soak.12h` has two independent duration checks in `release` mode:",
)
replace(
    "docs/release-evidence-runner.md",
    "1. the adapter report must claim at least 86400 seconds;\n2. the adapter process itself must remain running for at least 86400 monotonic elapsed seconds as measured by the evidence runner.",
    "1. the adapter report must claim at least 43200 seconds;\n2. the adapter process itself must remain running for at least 43200 monotonic elapsed seconds as measured by the evidence runner.",
)
replace(
    "docs/release-evidence-runner.md",
    "Therefore an adapter cannot satisfy the release gate by immediately returning a fabricated `duration_seconds = 86400` report.",
    "Therefore an adapter cannot satisfy the release gate by immediately returning a fabricated `duration_seconds = 43200` report.",
)
replace(
    "docs/release-evidence-runner.md",
    "Run this on a dedicated release host whose process/job lifetime permits a continuous 24-hour execution; ordinary PR CI is only for contract validation.",
    "Run this on a dedicated release host whose process/job lifetime permits a continuous 12-hour execution; ordinary PR CI is only for contract validation. A longer 24-hour run may be retained as optional extended endurance evidence.",
)
replace(
    "docs/release-evidence-runner.md",
    """dotnet run --project tools/MachiVerse.ReleaseEvidenceRunner --configuration Release -- \\
  run release \"$candidate\" /path/to/real-assembled-runtime-adapter \\
  artifacts/qa04-plan artifacts/qa04-release

dotnet run --project tools/MachiVerse.ReleaseEvidenceRunner --configuration Release -- \\
  apply artifacts/qa04-release/qa04-evidence-fragment.json \\
  artifacts/release/base-evidence.json artifacts/release/evidence.json""",
    """dotnet run --project tools/MachiVerse.ReleaseEvidenceRunner --configuration Release -- \\
  run-step3 release \"$candidate\" /path/to/real-assembled-runtime-adapter \\
  artifacts/qa04-plan artifacts/gate4-step3

dotnet run --project tools/MachiVerse.ReleaseEvidenceRunner --configuration Release -- \\
  run-step4 release \"$candidate\" /path/to/real-assembled-runtime-adapter \\
  artifacts/qa04-plan artifacts/gate4-step3/gate4-step3-benchmark-evidence.json artifacts/qa04-release

dotnet run --project tools/MachiVerse.ReleaseEvidenceRunner --configuration Release -- \\
  apply artifacts/qa04-release/qa04-evidence-fragment.json \\
  artifacts/release/base-evidence.json artifacts/release/evidence.json""",
)

replace("docs/roadmap/quality-integration.md", "- 24h soak orchestration", "- 12h soak orchestration")
replace("docs/roadmap/quality-integration.md", "- 24h soak", "- 12h soak")

replace("docs/design/phase4-alpha11-market-order-application-authority.md", "or 24h soak.", "or 12h soak.")
replace("docs/design/phase4-alpha11-physical-move-application-authority.md", "or 24h soak.", "or 12h soak.")
replace(
    "docs/design/phase4-alpha11-reference-world-materialization-completion.md",
    "persistence/publication stress、24時間soakは別gateとして残る。",
    "persistence/publication stress、12時間soakは別gateとして残る。",
)

for path_s in [
    "docs/design/phase4-alpha11-runtime-release-profile.md",
    "docs/release-acceptance.md",
    "docs/release-evidence-runner.md",
    "docs/design/phase4-test-acceptance.md",
]:
    text = Path(path_s).read_text(encoding="utf-8")
    if "performance.soak.12h" not in text:
        raise SystemExit(f"{path_s}: expected 12h contract reference")
