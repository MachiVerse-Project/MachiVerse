# MachiVerse contract fixtures

`QA-01` の component-independent fixture sourceです。production component DLLやgenerated assemblyを契約正本として共有せず、`docs/protocols/schema/*.proto` と確定設計から検証可能な入力・golden valueを提供します。

## v1 contents

- StableToken valid/invalid vectors
- SHA-256 base vectors used by the common hash suite
- canonical `ProtocolVersionV1` protobuf fixture
- canonical schema source manifest
- TestCaseId registry seed
- persistence fixture seed generator

検証:

```text
dotnet run --project tools/MachiVerse.ContractFixtures -- verify
```

Persistence engine向けseed manifest生成:

```text
dotnet run --project tools/MachiVerse.ContractFixtures -- generate-persistence-seed artifacts/persistence-fixture-seed.json
```

`SIM-03` がphysical persistence formatを実装するまでは、このseed manifestをSQLite/Snapshot binary fixtureそのものとして扱いません。
