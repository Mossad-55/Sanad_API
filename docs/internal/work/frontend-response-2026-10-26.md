# Sanad API → frontend — consolidated response (2026-10-26)

Status: `send-ready` (sanitized) — all 16 items answered and **final**; item 15's
length table (§3.1) is frozen on the final, gate-passed code: only the visit
closure reason publishes a raw `[StringLength(500)]` schema annotation; the
trimmed visit-1000 / support-2000 caps stay documented, not raw `maxLength`.
Prepared by the
backend worker for the
frontend team, delivered through the Owner / Mastermind. It covers **(1)** exact
money-action guidance and **(2)** the consolidated answer to the 16 reported
items.

Sanitization: this document contains **no credentials, no credential-file paths,
no internal host addresses, no private-handoff content**. Everything here is
either source-verified in the repository or a read-only observation on the
rehearsal environment.

Every claim below is **investigation evidence, not execution authorization**, and
every eligibility read goes stale the moment any action runs. No provider call,
no money movement and no state transition was performed to produce this document.

---

## 1. Money actions — exact guidance

### 1.1 Environment facts (read-only, verified 2026-10-26)

- The agreed rehearsal runs environment `Staging`, API revision
  `2bccdb3e5b59c65ba1cad91abd640bbb7fb92b81`; service active; database
  reachable; 9/9 migration heads applied.
- A **non-Production fixture mechanism** intercepts refund calls for **exactly
  seven synthetic fixture transaction ids** and returns scripted outcomes. Any
  other id passes straight through to the real payment provider.
- **"Seam enabled" is NOT blanket safety.** Exactly the seven ids below are
  intercepted; everything else reaches the real provider.

The seven intercepted ids (exact, ordinal match):

| # | Fixture transaction id | Scripted outcome |
|---|---|---|
| 1 | `demo-demo1-caregiver-bookings-seam-failed-refund-order-txn` | Rejected |
| 2 | `5359006666923497873` | Rejected |
| 3 | `2178478915071097933` | Rejected |
| 4 | `demo-demo1-finance-claims-submitted-capture` | Rejected |
| 5 | `demo-demo1-finance-claims-approved-capture` | Rejected |
| 6 | `demo-demo1-finance-claims-ambiguous-capture` | Unknown |
| 7 | `demo-demo1-finance-claims-failed-capture` | Rejected |

Scripted-outcome meaning: `Rejected` = provider refused, **no money moved**;
`Unknown` = outcome indeterminate (ambiguity marker); `Completed` = success.
These are scripted responses on the rehearsal — they are **not** evidence of
provider sandbox or production behaviour.

### 1.2 Routes, roles, request bodies (source-verified)

| Route (exact) | Authorized role | Request body |
|---|---|---|
| `POST /api/v1/admin/bookings/{bookingId}/refund` | `CaregiversAdmin` | **none** |
| `POST /api/v1/admin/bookings/{bookingId}/outcome` | `CaregiversAdmin` | `AdminRecordBookingOutcomeRequest(int OutcomeReason, string? Reason, int? ReasonCategory, int? ProviderLatenessMinutes, bool CustomerConsentedToLateness, bool ForceMajeure)` |
| `POST /api/v1/caregiver/bookings/{bookingId}/provider-outcome` | `CaregiverAccess` (assigned caregiver) | `CaregiverRecordProviderOutcomeRequest(int OutcomeReason, string? Reason, int? ReasonCategory)` |
| `POST /api/v1/admin/bookings/{bookingId}/refund/reconcile` | `CaregiversAdmin` | `AdminReconcileRefundAmbiguityRequest(string Evidence, bool RefundDidMove, Guid? ExpectedAttemptClaimId, string? VerifiedProviderReference)` |
| `POST /api/v1/admin/care-homes/bookings/{bookingId}/refund/retry` | `CareHomesOperationalAdmin` | **none** |
| `POST /api/v1/admin/care-homes/bookings/{bookingId}/refund/record-completed` | `CareHomesOperationalAdmin` | `RecordCompletedCareHomeRefundRequest(string Reference, string Reason)` |
| `POST /api/v1/admin/care-homes/bookings/{bookingId}/refund/reconcile` | `CareHomesOperationalAdmin` | `ReconcileCareHomeRefundRequest(Guid ExpectedAttemptClaimId, bool RefundDidMove, string? VerifiedProviderReference, string Evidence)` |
| `POST /api/v1/admin/finance/subscription-refunds/duplicate-charge/{claimId}/approve` | `RefundWorkflowAdmin` | `ApproveDuplicateChargeClaimRequest(string VerifiedBasis, string? DecisionNote, string? FamilyReason)` |
| `POST /api/v1/admin/finance/subscription-refunds/duplicate-charge/{claimId}/execute` | `SuperAdminOnly` | `ExecuteDuplicateChargeClaimRequest(Guid? ExpectedExecutionAttemptId)` |
| `POST /api/v1/admin/finance/subscription-refunds/duplicate-charge/{claimId}/reconcile` | `SuperAdminOnly` | `ReconcileDuplicateChargeClaimRequest(bool RefundDidMove, string Evidence, string? VerifiedProviderRefundId, string? MovedEvidenceReference, string? NotMovedProviderEvidenceReference, Guid? ExpectedExecutionAttemptId)` |

> Frontend note (item 2 — money matrix): the care-home retry path is
> `/api/v1/admin/care-homes/bookings/{bookingId}/refund/retry` — the frontend's
> `/refund/retry` is **correct**; an earlier backend table that showed a bare
> `/refund` was wrong and is corrected here.

### 1.3 Current demo state + expected result (read-only, 2026-10-26)

Effective transaction id rule for the families admin refund: the **first
succeeded** `families.payment_transactions.PaymobTransactionId` for the booking,
falling back to the booking's stored id. For the care-home retry: the booking's
own stored `PaymobTransactionId`.

| Demo fixture (exact id) | Current state (read-only) | Effective id | Expected result |
|---|---|---|---|
| Family demo booking `01a17542-3659-7158-9c52-83bc1325f916` | Status 8 CancelledByCaregiver; succeeded payment transaction `01a17542-3665-7b69-9eaa-99d5d285291f` (amount 575.00); ambiguous=false; in-flight=false; attempt version 2; released attempt claim `c00b5b8f-6cba-4f6d-be7c-86d8d332e870`; award 100 minor units | `demo-demo1-caregiver-bookings-seam-failed-refund-order-txn` (allowlisted #1) | **Eligible** → `POST .../refund` (no body) → scripted `Rejected`; **no money moved**, booking returns to failed-refund and stays retryable |
| Care-home demo booking `01a17542-2fc6-75bb-a082-ccb0b4c143f0` | Status 7 RefundPending; PaymentStatus 2; RefundStatus 1 Pending; amount 4025.00; ambiguous=false; no claim | `5359006666923497873` (allowlisted #2) | **Eligible** → `POST .../refund/retry` (no body) → scripted `Rejected` → refused (expected `502 Paymob.RefundRejected`); RefundStatus → Failed; still retryable |
| Care-home demo booking `01a17542-308a-73a5-b5a1-d604ffa57ad8` | Status 7 RefundPending; PaymentStatus 2; RefundStatus 3 Failed (reason: scripted refund rejection, no provider call); amount 4025.00; ambiguous=false; no claim | `2178478915071097933` (allowlisted #3) | **Eligible** → `POST .../refund/retry` (no body) → scripted `Rejected` (expected `502`); stays Failed; retryable |
| Duplicate-charge claim `01a17542-3bc9-7033-a39a-1233d0ffa7af` | state 3 (approved), execution_state 2; capture `demo-demo1-finance-claims-approved-capture` | capture allowlisted #5 | **Eligible** → `POST .../execute` → scripted `Rejected`; execution attempt fails (`Paymob.RefundRejected`), attempt-counted, retryable; no money moved |
| Family demo booking — reconcile `01a17542-3659-7158-9c52-83bc1325f916` | ambiguous=false, in-flight=false | — | **NOT executable** — state guard refuses (no unconfirmed/interrupted attempt) |
| Care-home demo bookings — reconcile (both) | ambiguous=false | — | **NOT executable** — state guard refuses before any mutation |
| `POST .../outcome` and caregiver `.../provider-outcome` on `01a17542-3659-7158-9c52-83bc1325f916` | booking is CancelledByCaregiver, not Accepted | — | **NOT executable** — accepted-booking guard refuses before any provider path |
| `record-completed` (both care-home rows) | manual record, DB-only | — | Only meaningful with a verified real-world refund; not a test action |
| Duplicate-charge claim `01a1731a-433e-7f99-9e08-76b16e0e1f4f` (state 1) | **not** allowlisted | — | **Must stay excluded** — if approved/executed it would reach the **real** provider |

### 1.4 The reconcile question — correction of an internal contradiction

The internal brief contained both an "ambiguous fixture exists" note and a
"no reconcile is eligible" note. Inspected against the live persisted rows and
the source handlers, the correct statement is:

- **Booking-level reconciles are NOT eligible** on any demo row:
  `POST /api/v1/admin/bookings/{bookingId}/refund/reconcile` and
  `POST /api/v1/admin/care-homes/bookings/{bookingId}/refund/reconcile` are both
  refused by the state guard (no ambiguity marker, no in-flight attempt).
- **The duplicate-charge reconcile IS currently eligible in principle**: claim
  `01a17542-3ce7-7fc6-b3dc-a322eb2a351e` is in an ambiguous execution state
  (state 3, execution_state 4, recorded gateway error `Paymob.RefundOutcomeUnknown`,
  execution attempt `01a17542-3cf9-7377-a805-bee4b9ad6ded`).

So a blanket "all reconciles are ineligible" is **wrong**; it holds only for the
booking-level surfaces. Corrected and recorded here.

Either way: **every reconcile — including the DB-only ones — alters money
records.** None is authorized for frontend testing. A truthful reconcile needs a
known-no-money fixture, `refundDidMove` stating a verified fact, and the
provider's own reference when money moved. **Never submit a fabricated provider
id or reference.**

---

## 2. Bounded frontend test protocol (evidence-ready — requires coordinated execution)

**This section is a proposed protocol, not a blanket "safe host" authorization.**
The Owner authorizes development/release work, not arbitrary financial test
writes. Run this only in a coordinated window with the Owner/operator; a
separate release worker handles commit/push/deploy after review.

**Preconditions (all required before any action)**

1. Confirm the rehearsal revision is still `2bccdb3e5b59c65ba1cad91abd640bbb7fb92b81`
   and environment `Staging` (`GET /api/v1/deployment/info`, anonymous).
2. Confirm the seven-id allowlist above is unchanged.
3. Capture a pre-action read-back of the target rows below.
4. Confirm you are about to hit **one of the eligible fixtures in §1.3** — never
   any id outside the seven.

**Eligible actions (all move no money; each changes only its own fixture state)**

| Action | Route | Expected scripted result | Expected state delta |
|---|---|---|---|
| A1 | `POST /api/v1/admin/bookings/01a17542-3659-7158-9c52-83bc1325f916/refund` | `Rejected` (definitive refusal; expected `502 Paymob.RefundRejected`) | booking back to failed-refund; refund attempt claim released; still retryable |
| A2 | `POST /api/v1/admin/care-homes/bookings/01a17542-2fc6-75bb-a082-ccb0b4c143f0/refund/retry` | `Rejected` (expected `502`) | RefundStatus Pending(1) → Failed(3); no claim left held |
| A3 | `POST /api/v1/admin/care-homes/bookings/01a17542-308a-73a5-b5a1-d604ffa57ad8/refund/retry` | `Rejected` (expected `502`) | RefundStatus remains Failed(3) |
| A4 | `POST /api/v1/admin/finance/subscription-refunds/duplicate-charge/01a17542-3bc9-7033-a39a-1233d0ffa7af/execute` | `Rejected` (`Paymob.RefundRejected`) | execution attempt recorded, attempt count increments, claim retryable; read the claim view for the exact HTTP code (not asserted here) |

After each action: read back the same rows and record HTTP status + state delta.
Eligibility is single-use — re-read before repeating.

**Disallowed / do not trigger**

- Any id outside the seven-id allowlist (reaches the real provider).
- `POST .../refund/reconcile` (families) and `POST .../refund/reconcile`
  (care-homes) on any demo row — guard refusal, and DB-only reconciles still
  alter money records.
- `POST .../duplicate-charge/{claimId}/reconcile` — even though the ambiguous
  claim is a genuine target, it books money records; owner-authorized operator
  action only, with truthful evidence.
- `record-completed` without a verified real-world refund.
- `outcome` / `provider-outcome` on the CancelledByCaregiver demo booking.
- Any fabricated provider transaction id, refund id or evidence reference.
- `POST .../duplicate-charge/01a1731a-433e-7f99-9e08-76b16e0e1f4f/{approve|execute}`
  (non-allowlisted; would reach the real provider).

---

## 3. Consolidated 16-item answer

**Item numbering is the original report's numbering — authoritative.** Every
one of the 16 items is answered below; no item is left as a "parent maps
number" placeholder.

| Item | Status / answer |
|---|---|
| 1 (FAQ list 500) | **Fixed locally** — the `HelpFaqId` ordering defect (`IComparable`); HelpCenter tests **23/23 pass**. **Awaiting release.** |
| 2 (safe money matrix) | **Answered** — exact fixtures and eligibility are in §1–§2; **no action was run**. The ambiguous duplicate-charge reconcile is **eligible in principle (DB-only)**; the booking-level reconciles are **ineligible**. Investigation evidence only — no execution authorization. |
| 3 (credentials) | **Owner-private transfer, pending.** No secret, credential-file path or private-handoff content appears in any backend document — and none should be pasted into tickets/chat. |
| 4 (public legal route) | **Answered with live read-only evidence (see `frontend-evidence-public-2026-10-26.md`):** the DEMO placeholder page is **NOT at `/`** (earlier phrasing corrected; `/` is `404` on loopback and public — nginx proxies straight to Kestrel, no static page). The bilingual **"DEMO — not legal text"** placeholder is the body of the **public legal route**: `/api/v1/public/legal/1`, `/2`, `/PrivacyPolicy`, `/TermsAndConditions` → **200**; `/api/v1/public/legal/privacy-policy\|privacy\|terms\|0\|3` → **400** `Cms.Content.InvalidDocumentType`; `/api/v1/public/legal` (no segment) → **404**; `/api/v1/legal/1` → **404**; `/api/v1/legal/terms`, `/api/v1/legal/privacy-policy` → **401** (authenticated surface). So the frontend's `404` is almost certainly a **path/segment mismatch** (missing `documentType`, or using `privacy-policy`/`terms` instead of `1`/`2`, or a non-existent root path). Send: exact route + environment + UTC timestamp + raw status/headers/body. The **legal owner + real content and date remain external** — the live text is a demo placeholder, an external/legal call, not a code change here. |
| 5 (admin elderly/caregiver name+ID lookup) | **Implemented** — new admin name+ID lookup routes, **names only**, Support/SuperAdmin; documented in docs + Postman; **16 tests passed** in the prior gate. **Final gate + release pending.** |
| 6 (notification fresh text / producers) | **Answered.** `ChatNewMessage` is **bilingual** (`UiLanguage`; Arabic default) — the earlier English-only claim is **retracted**; language is chosen **at creation** from the persisted recipient `ui_language` and is **not retroactive**. Source-level evidence only — **no new notification/chat/outbox events were created** to produce it. |
| 7 (refund policy v2) | **Owner decision (accepted 2026-10-26): policy v2 stays DORMANT** — no activation, no date, no code task. |
| 8 (released mobile claim submission) | **Confirmed released** (`claimSubmission = true`). The confirmation itself is **external/pending**. |
| 9 (care-home listing subscription) | **No care-home listing-subscription API exists**; the current docs are already corrected to say so. |
| 10 (missing elderly fixtures) | **DONE (data task).** Exactly **9 rows** added on the existing demo elderly, Owner-approved, **zero-delta replay, no outbox rows**; exact ids/dates in §4 (and the brief's section F). **No API read-back.** |
| 11 (Chat) | **Answered with persist-state evidence (see `frontend-evidence-recon-2026-10-26.md`):** exactly **6 conversations DB-wide, 4 demo (27 messages)**. The empty-list cause is concrete persisted state: thread `01a17542-4062-7e0c-b356-a70fe178eb67` is `HiddenForParticipantOne=true` for `demo1.family.owner` (hidden from its list; visible to demo1.carehome.owner1); a block row exists (`demo1.family.owner2` blocked `demo1.caregiver.3`, 2026-10-24); **no threads exist for any `r2.*` account** — an r2 login legitimately sees an empty list. The 6 orphan caregivers appear in **no** chat row. To close the report send the exact account + request used. |
| 12 (care-record source docs/comments) | **Corrected** in the source docs/comments: numeric `1/2/3` name-or-number filters. **No API behaviour change.** |
| 13 (legacy caregiver orphans) | **Owner decision (accepted 2026-10-26): the 6 legacy caregiver orphans stay untouched — no deletion.** Exact identity (read-only recon, `frontend-evidence-recon-2026-10-26.md`): 14 caregiver rows total, 6 orphans — `01a06152-dbc4-775e-9769-2108ba12acea`, `01a0761b-8da1-7ee9-9dd6-514b6e4eb2ee`, `01a0761f-f18d-7d47-a15f-7ccb85e0f69d`, `01a07f9f-5dc5-7bb6-b761-d20093e4dd40`, `01a080f8-c541-76d3-ba16-ca64fd8d91a9`, `01a0a5ec-a8b0-78f7-81c1-46a03528f47c` — each referencing a dangling `user_id` with **no** `identity.users` row (dangling ids `01a04327-a1fa-7cd3-81b9-da49837e0edf`, `01a0761a-0368-7e59-92ae-182a792e88e1`, `01a0432a-4b7e-7bef-ab39-341908b44680`, `01a07f9c-21a8-7b33-880d-19fc211cfa66`, `01a080f5-1b9a-7f76-97bb-1ab369c47150`, `01a0a5eb-b7b2-77cf-a7ff-a6a1db06898e`), created 2026-09-02…2026-09-15; uniform reason: no matching identity row, so no name/phone/email is recoverable; absent from `user_accounts` and from every chat row. |
| 14 (notification preferences) | **Doc complete.** Role/category mapping is source-derived; self-service auth is `NormalAccess` (`GET`/`PUT /api/v1/account/notification-preferences`), per the recent provided guidance. **No placeholder.** |
| 15 (length limits) | **Final — Owner decision (accepted 2026-10-26): no new numeric caps this phase; ship as-is.** Only the raw closure-500 publishes a schema annotation; the trimmed visit-1000 / support-2000 caps stay documented; booking reject/cancel reason, room-type descriptions and maintenance reason stay unannotated/unbounded. OpenAPI contract tests **gate-passed green** (actual schema metadata). |
| 16 (complaint-review by refund case) | **Existing optional resolver already covers it** — **no additional frontend work needed.** |

Every original item is answered above; the earlier "parent maps number"
placeholders are **removed**. **Additional discovered fixes, named separately
and NOT renumbered into the 16:** the eligible-visit-residents EF query fix and
the caregiver-picker EF query fix.

**Not** claimed anywhere in this document: any money action executed, any
credentials delivered, any live authenticated endpoint read-back. The
caregiver-picker EF fix is source/test-verified only (**no live read-back**).

### 3.1 Item 15 — length limits (FINAL, frozen 2026-10-26)

The field-lengths correction is complete and gate-passed; these limits are final:

| Field surface | Limit / behaviour | Basis |
|---|---|---|
| Visit reject / cancel reason | 1000, **trimmed** | domain guard — documented, NOT a schema annotation |
| Visit closure reason | 500, **raw** (measured before trimming) | domain guard **+ the one published `[StringLength(500)]` request-schema annotation** |
| Support ticket reply body | 2000, **trimmed** | domain guard — documented, NOT a schema annotation |
| Booking reject / cancel reason | non-blank, **unbounded text** | no length attribute |
| Room-type description | DB column **4000** only | EF column |
| Maintenance reason | DB column **1000** only | EF column |

Schema policy (final): only **raw-JSON-measured** domain caps are published as
request-schema annotations, because OpenAPI request schemas are generated from
the request record types and a raw `[StringLength]` validates the **raw** text.
The visit decision reason (the approve path ignores it entirely) and the
cancellation reason, and the support reply body, are capped on the **trimmed**
text — a raw `maxLength` would falsely reject previously accepted
whitespace-padded values and padded values above the trimmed cap on approve
(rejected only on reject). Those caps therefore stay **documented, not
annotated**. The closure reason alone is measured on the raw value before
trimming (`CareHomeVisitSettings.Validate`), so its `[StringLength(500)]`
annotation is exact and is the only `maxLength` the OpenAPI schema emits among
these five surfaces. This preserves both the trim compatibility and the
approve-ignored-reason acceptance in shipped schemas; covered by
`RequestLengthBoundOpenApiContractTests` (actual OpenAPI metadata, **green
14/14** after the `[property:]` targeting fix detailed below) and
`SupportTicketReplyTrimBoundaryTests`/trimmed-cap tests. **Owner decision
(accepted 2026-10-26): NO new numeric caps this phase** — the tables above ship
as-is; none invented.

Implementation note (found and fixed at the release gate): the record's
positional parameter needed the **`[property:]` target modifier** — without it
the attribute stays on the constructor parameter and the OpenAPI generator
emits no `maxLength` (the focused contract test initially failed exactly there
and now passes against real generated metadata).

---

## 4. Fixture facts — elderly-ops demo set (read-only, verified by the operator's host-side access)

Provisioned under the Owner-approved fixture order; exactly **9 rows across 7
tables**, zero notification/outbox rows, and a byte-stable idempotent replay.
Verified by host-side read-only database access; **no authenticated API
read-back was performed and no login/session was created.** This is fixture
identity/state evidence, not an API-behaviour proof.

- Owner identity user: `01a17542-12d2-7bd4-bd85-59d13a5bd98f`
  (`demo1.family.owner@test.sanad.local`).
- Family: `01a17542-1983-7ba5-9b29-30b5980ddad7`; owns exactly one elderly.
- **Elderly id (primary frontend filter key): `01a17542-1a17-7c54-9e18-a6f52372813e`**
  ("Demo Grandfather [demo1:dependent]"; tz Africa/Cairo).
- SOS `01a17896-160c-7241-b9ff-4b6008820afd` (Open) + history
  `01a17896-160d-7305-85f5-b5f4aaecffd2`.
- Help request `01a17896-1712-7241-aa56-ca75dfb17599` (Pending) + history
  `01a17896-1713-7a34-9442-cce8edb37f3f`.
- Check-in `80ca5cb2-8855-4faf-890a-3d74616fcde4` — **2026-10-19**, answer **no**, 10:00 local.
- Check-in `459e67c3-fe0c-418e-bcab-6c377e450720` — **2026-10-20**, answer **yes**, 09:30 local.
- Medication `01a17896-17d8-7f67-8a73-0e3e5cb07cc1` — Completed, **2026-09-06 → 2026-09-26**, 08:00 daily dose (course over).
- Dose log `01a17896-1877-7782-ad14-905c7737af05` — **2026-09-11 Taken**.
- Dose log `01a17896-18b3-738f-aa8d-b8d20a2c39f1` — **2026-09-12 Skipped**.

Frontend filters: `elderlyId = 01a17542-1a17-7c54-9e18-a6f52372813e`; check-in
dates 2026-10-19 (false) / 2026-10-20 (true); one Open SOS; one Pending help
request; one Completed medication course whose logs split Taken/Skipped.

---

## 5. Evidence, limitations and rules honored

- **Source review (this pass):** all money routes re-read in the actual
  controllers — roles/policies and request-DTO field lists in §1.2 are taken from
  the current source, not from an earlier summary.
- **Live read-only state (this pass):** deployment info (revision/environment/
  heads/DB reachable), the seven-id allowlist, the three booking fixtures, the
  claim captures and the duplicate-charge claim rows — all read-only SELECTs via
  the operator's established SSH access.
- **No API auth, login, retry or provider call was made**; no database write, no
  runtime change, no new fixture transition. The only API read was the anonymous
  deployment-info identity check over loopback.
- **Limitations:** outcomes here are scripted for the seven armed ids on the
  rehearsal only; they do not prove provider sandbox or production behaviour.
  Eligibility goes stale after any action. Exact HTTP codes for the
  duplicate-charge `execute` refusal are to be read back, not assumed.
- **Rules honored:** no commit/push/deploy; no provider calls; no secrets,
  credential paths or private-handoff content; unrelated untracked work
  preserved; fixes remain in-tree and **not deployed**.
