# Phase 4 Standard Domain Payload Canonical Digest v1

Status: Normative / INT-03 Stage 2
Tracking: Issue #240
Parent contracts:

- `phase1-determinism-ordering-random.md`
- `phase4-domain-state-registry.md`
- `phase4-domain-payload-schema.md`
- `phase4-domain-partition-snapshot-wire-blocker.md`

## 1. Purpose

The 97 standard authoritative Domain partitions already have exact P4-05 field names, field kinds,
optionality, canonical collection order, record schema id/version, and Snapshot field ordinals.
`PartitionStateHeaderV1.CreateCanonical` also requires a 32-byte canonical semantic payload digest
for every record. What was not yet fixed was one implementation-independent normalization for that
payload digest.

Stage 2 must not use protobuf bytes, CLR object layout, reflection, JSON, dictionary iteration order,
or one ad-hoc hash label per runtime payload class as the standard authority. This amendment fixes a
single semantic digest contract for all standard Domain record payload schema v1 values.

## 2. Hash domain

For every standard partition payload:

```text
StandardDomainPayloadDigestV1 = DomainHash(
  "mv.domain-payload.v1",
  normalized_standard_domain_payload
)
```

`DomainHash` and `MV-DCBOR-v1` are exactly the Phase 1 definitions. The label is fixed and is not a
Config value, domain value, or persisted field.

## 3. Top-level normalized value

The normalized MV-DCBOR value is the map:

```text
{
  0: partition_id,
  1: record_schema_id,
  2: record_schema_major,
  3: record_schema_minor,
  4: present_fields
}
```

where:

- `partition_id` is the canonical standard `StableToken`;
- `record_schema_id/version` are taken from `StandardDomainPayloadSchemaRegistry` and must equal the
  standard partition registry record schema;
- `present_fields` is an array in ascending 1-based P4-05 descriptor ordinal;
- every required field is present exactly once;
- an absent optional field contributes no entry;
- a present zero/false/empty value contributes an entry and is therefore distinct from absence.

Each present field entry is:

```text
[field_number, normalized_value]
```

No field name or CLR type name is duplicated into the digest. The selected record schema version and
field ordinal already bind those semantics normatively.

## 4. Exact value normalization

| P4-05 kind | MV-DCBOR semantic value |
|---|---|
| `Ref` | `[partition_id, record_id_bytes16]` |
| `RefList` | array of normalized `Ref` values in P4-05 canonical order |
| `Id128` | 16-byte byte string |
| `Token` | ASCII text |
| `TokenList` | array of ASCII text in canonical token order |
| `OrderedTokenList` | array of ASCII text in schema semantic order |
| `Ratio`, `UInt8`, `UInt16`, `UInt32`, `Step`, `UInt64` | CBOR unsigned integer |
| `Int32`, `Int64`, `Length`, `Mass`, `Volume`, `Temperature`, `Pressure`, `Power`, `Energy`, `Money` | CBOR integer |
| `Bool` | CBOR boolean |
| `Digest` | exactly 32-byte byte string |
| `Vec3` | `[x, y, z]`, each CBOR integer |
| `Quat` | `[x, y, z, w]`, each CBOR integer |
| ordered token maps | array of `[token, value]` pairs in strict ASCII key order |
| `OrderedNestedList` | array of normalized nested values in the registered owner order |
| `RuleAst` | one normalized registered nested value |

P4-05 scalar range and canonical collection validation runs before hashing. Reference existence is a
separate whole-Snapshot semantic check: a structurally valid `Ref` is normalized as above, while the
all-97 actual-record resolver proves that the referenced authoritative record exists and has the
expected record schema before recovery is accepted.

## 5. Nested value normalization

A nested value is accepted only through `DomainNestedSnapshotCodecRegistryV1`. Its normalized value
is:

```text
{
  0: nested_schema_id,
  1: nested_schema_major,
  2: nested_schema_minor,
  3: present_fields
}
```

Nested `present_fields` uses the same `[field_number, normalized_value]` rule against the registered
nested descriptor. Unknown schema/version, wrong runtime type, missing required field, wrong field
kind, non-canonical ordered list, or unavailable nested codec fails closed.

The currently registered production nested schema is:

```text
domain.nested.participation-policy-rule / 1.0
1 = priority : Int32
2 = rule_id  : Token
```

with list order `(priority ascending, rule_id ASCII ascending)`.

Recursive nested values are not inferred. A recursive/nested field kind remains unavailable until
its domain owner registers the explicit nested schema/codec required by the Stage 2 wire contract.

## 6. Relationship to Snapshot protobuf

The digest is semantic and independent of protobuf serialization. Production may encode a payload
as `DomainPayloadWireV1`, decode it, normalize it through P4-05, and compute the same digest before
and after persistence.

The following are forbidden as `StandardDomainPayloadDigestV1` inputs:

- raw `DomainPayloadWireV1` bytes;
- raw `DomainRecordSnapshotV1` bytes;
- JSON;
- reflection-derived property names/order;
- CLR type names;
- dictionary/hash-map iteration order;
- compression/chunk boundaries.

## 7. Recovery acceptance

For each restored record:

1. decode the exact persistence wire;
2. normalize and validate against P4-05 and the registered nested codecs;
3. validate references against the all-97 actual-record/schema resolver;
4. compute `StandardDomainPayloadDigestV1`;
5. reconstruct `DomainRecordEnvelopeV1` and the actual partition material;
6. recompute `PartitionStateHeaderV1.CreateCanonical`;
7. require the restored partition header/digest to equal the frozen Snapshot authority exactly.

A mismatch rejects that Snapshot candidate. There is no protobuf-byte fallback and no best-effort
reconstruction.

## 8. Migration rule

For standard record schema v1.0, this digest normalization is fixed. Changing a field ordinal, field
kind, optionality, collection semantics, nested schema, or normalization meaning requires the
corresponding record/nested schema compatibility and persistence migration decision; it must not be
changed silently under the same schema version.
