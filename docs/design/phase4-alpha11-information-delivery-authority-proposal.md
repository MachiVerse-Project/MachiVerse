# Alpha 1.1 InformationDelivery authority proposal

Status: **Approved / adopted by #240; retained as review history**

Tracking: #240

Approval: explicit project-owner approval on 2026-09-12; recorded in #240

Normative authority: `phase4-alpha11-information-delivery-authority.md`

Implementation PR: #265

## Review history

This file records the decision-ready review package that was approved in #240. The normative source after approval is `phase4-alpha11-information-delivery-authority.md`.

Approved scope:

- exactly 20,000 `information.delivery` records;
- local ordinal `d = 0..19,999`;
- existing QA-04 Infrastructure descriptor identity, slice start `445,100`, non-specialized identity;
- `content_ref = actual InformationClaim[d]`;
- `sender_ref = InformationClaim[d].claimant_ref`;
- `recipient_refs = [canonical Resident[d + 1]]`;
- `channel_ref = actual CommunicationService[d mod 10,000]`;
- `eligible_step = 0`;
- `delivered_step = NONE`;
- `priority = 0`;
- `status = queued`;
- `content_digest` copied byte-for-byte from the referenced InformationClaim authoritative digest;
- descriptor DetailLevel envelope, revision 1, created_step 0, retired_step NONE, lineage_ref NONE;
- full 20,000-record production materialization, payload validation, Snapshot/recovery semantic rehash, lifecycle compatibility, negative proof, and current-head CI required before accepted accounting changes.

This approval is benchmark-only. It does not define general recipient selection, routing, priority, delivery lifecycle, or channel ownership semantics.

The following remain explicitly unapproved and unchanged by this decision:

- `infrastructure.facility_service` and upstream Built facility identity authority;
- `information.media_distribution`;
- `information.record_store`;
- `information.address_place_index`;
- `infrastructure.failure_recovery`;
- `infrastructure.lineage`.

Only after #265 production proof succeeds may Infrastructure accounting move from `430,100 / 500,000` accepted and `69,900` remaining to `450,100 / 500,000` accepted and `49,900` remaining. Operation authority remains `5 / 6` until `facility_service` is separately resolved and production-proven.