# QA-03 Adversarial Fixtures

This directory is the version-controlled source for `QA-03` crash / malformed-input / security-negative harness cases.

## Authority boundary

The harness does **not** define production protocol, Config, persistence, authentication, TLS, law-AST, snapshot, history, or audit semantics. Those remain owned by `docs/design/`, `docs/protocols/`, and the responsible component.

The corpus provides deterministic hostile inputs and expected *disposition classes* only. Component-local adapters decide the exact canonical reason code and must still enforce their own contract.

Production component DLLs/internal types are not referenced by the harness.

## Commands

```text
dotnet run --project tools/MachiVerse.AdversarialHarness -- verify
dotnet run --project tools/MachiVerse.AdversarialHarness -- materialize <output-directory>
```

`verify` checks:

- exact persistence crash matrix coverage: 6 write stages x 5 injection points = 30 cases;
- canonical manifest ordering and duplicate rejection;
- all P4-08 malformed-input target classes:
  - protobuf envelope/payload;
  - WebSocket frame/message assembly;
  - StableToken parser;
  - Config TOML/parser/schema;
  - Rule/law AST;
  - Snapshot manifest/chunk metadata;
  - history payload decoder;
  - log/audit query filter;
- deterministic case identities and SHA-256 corpus digest;
- auth/session/internal-mTLS/audit negative-case traceability;
- synthetic-only security fixtures;
- absence of private-key / bearer-token / session-cookie-like secret material;
- machine-readable external target adapter contract.

`materialize` writes deterministic JSON artifacts for component-local or integration runners. Generated output is not the contract source and should not be hand-edited.

## JSONL target adapter

A component-local runner may consume materialized mutation cases and emit one JSON object per line.

Request fields:

```text
caseId
category
declaredLength
expectedDisposition
payloadBase64
```

Result fields:

```text
caseId
diagnosticDigest
outcome
reasonCode
```

The adapter must preserve `caseId`. It must not convert a harness `reject-or-explicit-failure` expectation into permission to accept malformed input. Exact rejection semantics come from the component's canonical contract.

## Crash matrix semantics

Before durable commit, the previous authoritative state must remain authoritative and no partial durable success may be exposed.

After durable commit but before response/publication, recovery must retain the durable fact and converge without duplicate application or false failure that would encourage a new logical identity.

A process crash by itself is never a PASS criterion.

## Secret policy

Do not add real OAuth/OIDC tokens, browser session handles, passwords, private keys, production certificates, client secrets, authorization codes, or personal credentials to this corpus. mTLS fixtures are metadata-only unless a future test-specific synthetic certificate fixture is explicitly approved and generated for testing.
