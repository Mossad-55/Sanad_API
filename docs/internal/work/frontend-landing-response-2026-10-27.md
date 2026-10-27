# Backend response — frontend and landing follow-up

Date: 2026-10-27

This response separates backend responsibilities from frontend, mobile, content
and infrastructure responsibilities. Existing delivered functionality is not
rebuilt. Please use the existing APIs and report reproducible failures rather
than treating missing content or undecided navigation as backend defects.

## 1. Backend work — delivered in this release

### 1.1 Lookup permissions — delivered

The admin name+ID lookup policies now match the agreed screen-to-role matrix
exactly:

| Lookup | Route | Allowed roles |
|---|---|---|
| Caregiver lookup | `GET /api/v1/admin/caregivers/lookup` | Super Admin, Finance Admin |
| Elderly lookup | `GET /api/v1/admin/elderly/lookup` | Super Admin, Support Admin |
| Content Admin | either route | not allowed (`403`) |

- Support Admin's prior access to the caregiver lookup is **removed
  explicitly**; the caregiver review/ratings surfaces keep their existing
  policy unchanged. The change is scoped to the lookup route only.
- Responses remain name-and-ID only. No phone matching or contact-data
  expansion exists or is planned: the caregiver route searches only the
  Arabic/English full-name columns, and the elderly route never returns a
  contact or medical value.
- The deployed policy is proven by a real registered-policy role matrix plus
  runtime HTTP authorization tests (401/403 per role for both routes) and the
  published payload shape is contract-locked to `id` + the two bilingual
  names only.
- Documentation states the role permissions, the matched name fields, search
  validation (case-insensitive literal substring; `%`, `_`, `\` match
  themselves, never wildcards; no maximum length) and exact pagination
  (`page` ≥ 1; `pageSize` default 20 when 0/absent, clamped 1–100; response
  envelope `items/page/pageSize/totalCount/totalPages`): see
  `docs/admin/caregivers-review.md`, `docs/admin/families.md`,
  `docs/auth/claims-and-policies.md`.

### 1.2 Notification verification — verified, no current defect

We verified the current notification producers against the reported concerns,
using producer source and isolated producer tests — **no fresh live events
were claimed or required**:

- Current producers write readable, human copy and resolve the recipient's
  **persisted account language** (Arabic default, English for accounts saved
  as English). Each reported producer (care-home onboarding/review, care-home
  booking decisions, chat messages) is covered by tests asserting the exact
  title/body per language from the producer's own copy source.
- No raw routing IDs appear in displayed copy: ids ride only on the structured
  destination pair (`DestinationEntityKind`/`DestinationEntityId`), and tests
  assert the human text is id-free.
- Language resolution follows the persisted account value with a defined
  fallback; producers differ in fallback behavior for unknown stored values
  (a source-level fact, all current users are on the default). There is no
  current defect to fix.
- The historical inbox rows you supplied predate the current producers; their
  language at creation is unknown and they are **not rewritten**. If a fresh,
  timestamped discrepancy appears after account-language synchronization
  (your header switch must save via the existing account-language endpoint),
  send the sanitized evidence and we will investigate the current producer.

### 1.3 Money-action matrix — delivered as a standalone document

The action/fixture/expected-result matrix is now an accessible standalone
handoff: [frontend-money-actions-2026-10-27.md](frontend-money-actions-2026-10-27.md).
It routes exact-eligible fixture IDs, per-role policy requirements, and
stateful preconditions — with the capture date and the requirement to re-read
state before acting. It also states honestly what is excluded (all
reconciliation routes) and why.

Separately, the minimal endpoint-to-Paymob test-account verification
(**maximum two test records**) is now **executed and closed** (both records
used; see §1.5). No pre-existing eligible refund capture existed, so the only
legitimate path was a new funded TEST care-home booking through the real
checkout flow.

Do not treat the matrix as permission to execute any further listed action; do
not fabricate provider references or duplicate-charge claims — existing real
captures are valid single sales, not duplicates.

### 1.5 Care-home refund completion — delivered, runtime-proven

The refund-completion gap reported after the first test booking (provider
settled the refund while the app stayed `Initiated`) is fixed and proven
end-to-end on the rehearsal:

- Refund completion no longer depends on callback (re)delivery: once the
  refund call returns the provider transaction id and it is durably persisted,
  the processor reads that exact transaction back over the authenticated
  merchant channel and completes only on a settled, cross-correlated refund.
  The signed callback remains the fallback, and a readback refusal never
  repeats the refund request.
- The reviewer-flagged parent-payment-path concurrency re-read was corrected so
  a genuinely applied refunded-parent callback is acknowledged via the
  booking's recorded payment id, without weakening the child-reference identity
  check.
- Final runtime proof (2026-10-27, rehearsal `Staging`): one real TEST booking
  (11,500.00 EGP, `refundPolicyVersion` 1) paid through the documented
  frictionless TEST card and the normal HMAC callback, then cancelled by the
  family before check-in. The cancel response itself returned
  `refundStatus Completed` with `refundCompletedOnUtc` set; the outbound
  readback `GET /api/acceptance/transactions/<refund-id>` is recorded in the
  service journal within that same request, and exactly one `RefundCompleted`
  outbox event was written. Provider readback confirms a settled refund child
  correlated to our capture and order. Full evidence: release receipt in
  [frontend-landing-follow-up-2026-10-27.md](frontend-landing-follow-up-2026-10-27.md).

### 1.4 Documentation/contract corrections — delivered

- The `200**` formatting typo in `docs/app/public/site-settings.md` is fixed.
- `docs/app/public/landing.md` now states explicitly that the **public landing
  form uses `POST /api/v1/partner-contact` (anonymous, required
  `Idempotency-Key`)**, while `POST /api/v1/support/contact` is a separate
  authenticated ticket surface for signed-in app users — a public form calling
  it gets `401` by design.
- `docs/app/public/partner-contact.md` documents the retry semantics exactly
  as implemented: a retry after an uncertain outcome resends the **same
  `Idempotency-Key` with the same content** and replays the stored receipt
  (same `contactId`, no second row, no duplicate inbox entry, and it never
  consumes the 5-per-hour budget or returns `429`); a genuinely new submission
  gets a new key; do not re-key the same unchanged submission to bypass a
  `409`. The runtime idempotency policy is unchanged and no new frontend
  architecture is required.

## 2. Frontend and landing actions — yours to own and complete

### A1. Deployment URLs and origins

Production domains and hosting are undecided. Final CORS/HTTPS setup is
deferred to deployment planning; no guessed origins will be installed now.
Pin local development ports to the agreed values rather than relying on
automatic port changes. Same-origin portal deployments do not require
cross-origin API permissions.

### A2. Contact form

The supplied request used the documented anonymous endpoint and expected
headers; no CORS failure is reported. Align retry behavior with the
documented idempotency semantics (above). Do not automatically bypass a
conflict by issuing a new key for the same unchanged submission — a genuinely
new submission is different from a retry. If an actual request fails, provide
the sanitized request, UTC timestamp, response and trace ID where available.

### A3. Proxy/IP handling

Trusted-proxy configuration and verification of visitor-IP rate limiting are
infrastructure-readiness items; they will be configured against the actual
deployment topology later, not guessed now. Do not add client-supplied IP
headers as a workaround.

### A4. Navbar and `/choose-type`

Button labels, destinations and unavailable-supplier-card behavior belong to
frontend/product decisions; obtain product-owner approval and maintain them in
frontend configuration. No new backend CMS schema is planned for these
controls. Proposed domains must not be presented as verified deployed
destinations.

### A5. Testimonials and counts

Do not ship unapproved hard-coded testimonials or misleading labels; resolve
content approval with the product owner. A caregiver count must not be
presented as a supplier count; rehearsal statistics include test data and are
not launch marketing claims. No supplier-count feature is planned in this
phase.

### A6. Publication and seed script

Your team owns committing, validating and running its seed/publication script
through the existing APIs; the backend will not run the script or populate
marketing content. Preserve existing published content and use staging-only
material accordingly. Authorized content/admin users own publishing content
and selecting FAQ/testimonial references, subject to implemented permissions.
Do not distribute admin tokens in code, reports or logs; obtain access through
the approved private process. Prevent demo placeholders from being presented
as approved launch legal text — legal text, its author/approver and
publication date belong to the product/legal owner; clearly marked test
content may be used through the existing legal publishing workflow for
integration testing only. If a publishing API fails, report the specific
route, authorized role, sanitized request, response and trace ID — that is a
backend issue we can investigate; unentered or unapproved content is not.

### A7. Lookup integration

The delivered matrix in §1.1 is the implemented behavior. After deployment,
test the intended role on each screen and report concrete mismatches. No
phone matching is required.

### A8. Language settings

Your header switch currently changes browser-local UI language only;
notifications use the persisted account language. If product behavior
requires synchronization, save changes through the existing account-language
endpoint. Do not treat an old English notification as proof that today's
producer is broken; share fresh, timestamped evidence if a discrepancy remains
after synchronization.

### A9. Money-action testing

Continue holding execution until §1.3's matrix and exact preconditions have
been verified against the live target. Then your team owns testing its screens
and returning results, within the explicitly agreed set only. Do not execute
actions outside that set or fabricate provider references/evidence.

### Test accounts

No additional broad backend account-provisioning task is planned. Create and
configure your own test accounts using supported registration and authorized
admin workflows, then run role tests. Privileged roles must not be
self-assigned. Report a specific missing or failing account-management
capability if it blocks you.

## 3. Mobile team action

The backend claim-submission contract and fail-closed behavior are already
implemented. Obtain the released-build confirmation directly from the mobile
team: platform, app version, build number and distribution channel;
confirmation that duplicate-charge submissions include `claimSubmission: true`;
whether older supported builds omit it and the upgrade/retirement approach if
so. Provide sanitized evidence only. Backend will not restore the legacy money
path or act as an intermediary for a confirmation only the mobile team can
supply.

## 4. Deferred infrastructure and product work

Outside this follow-up: final production/staging domains, certificates and
origin restrictions; trusted-proxy/client-IP configuration against the final
hosting topology; production provisioning/cutover to new infrastructure,
database, service-provider configuration and Paymob account; launch legal and
marketing content approval; draft preview and instant revalidation; supplier
functionality or statistics.

The intended clean production baseline is reference/lookups data and the Super
Admin account, not demonstration fixtures — a future cutover plan, not an
authorization to reset the rehearsal database or flip its environment.
Existing defaults remain: 60-second ISR acceptable; draft preview deferred; no
new length caps in this phase; the rehearsal stays Staging with its configured
seam intact.

## 5. Delivery record

- Approved scope: Owner-approved follow-up of 2026-10-27 (this document's §1).
- Implemented changes: §1.1–§1.4 as described — caregiver-lookup policy change
  (lookup-scoped only), lookup role/contract tests, notification producer
  verification tests, doc corrections and the standalone money-action matrix.
- Tests and verification: strict Release build **0 warnings / 0 errors**;
  focused gates — lookup contract/role-matrix/runtime tests, shared
  caregiver-review policy tests, notification producer tests
  (readable/care-home-booking/chat), partner-contact +
  idempotency tests — **146 passed / 0 failed / 0 skipped**; architecture
  checks 1/1; deployment verified per the release receipt in the execution
  brief. Skipped/not-run: no full-suite rerun this phase (focused set per
  contract); no live notification events triggered; no provider calls.
- Commit/push/deployed revision: the lookup/notification/matrix/documentation
  work shipped in `cf065d4b` (deployed to the rehearsal and verified:
  verifier exit 0, 9/9 heads, smoke 200). The refund-completion fix (§1.5)
  shipped separately in `b302a3fc`, deployed to the same rehearsal and
  verified the same way (revision match, 9/9 heads / 0 pending, smoke 200,
  configuration fingerprints unchanged). Exact receipts in
  `frontend-landing-follow-up-2026-10-27.md`.
- Provider verification: **executed and closed** — the maximum two TEST
  care-home records were used; the second proved the app's own `Completed`
  refund reconciliation end-to-end (§1.5). Test mode only; live merchant keys
  and HTTPS webhook delivery for production remain to be verified separately.
- Handoff attachments and outstanding team actions: §2, §3 as listed.

Please complete your team's actions and return concrete test results. Backend
owns backend defects and the agreed delivery scope — not unrelated frontend
behavior, content authoring or undecided product requirements.
